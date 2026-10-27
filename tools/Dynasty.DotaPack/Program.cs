using System.Text.Json;
using System.Text.Json.Nodes;
using Dynasty.Content;
using Dynasty.MatchSimulation.Rules;

// Builds the Dota 2 ruleset pack (decision D-048, docs/DOTA_PACK.md) from odota/dotaconstants at a pinned
// commit, with hero item builds from OpenDota. The pack and its coverage report are written locally
// (default packs/dota-7.41f.json, git-ignored) and are never committed.
//   dotnet run --project tools/Dynasty.DotaPack [-- out.json [cacheDir]]

const string Patch = "7.41f";
string output = args.Length > 0 ? args[0] : Path.Combine("packs", "dota-" + Patch + ".json");
var data = new DotaData(args.Length > 1 ? args[1] : Path.Combine("packs", "cache", "dotaconstants-" + DotaData.Commit[..8]));

JsonObject heroes = data.Constants("heroes").AsObject();
JsonArray xpLevels = data.Constants("xp_level").AsArray();
JsonObject shopData = data.Constants("items").AsObject();
JsonObject itemIds = data.Constants("item_ids").AsObject();
JsonObject Map(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)),
    documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
Items.Result items = Items.Convert(shopData, data.ShopItemNames(), Map("items.json"), data.GameItems());
JsonObject abilityMap = Map("abilities.json");
JsonObject abilitiesData = data.Constants("abilities").AsObject();
JsonObject heroAbilitiesData = data.Constants("hero_abilities").AsObject();
var linksByHero = new Dictionary<string, Dictionary<string, Dictionary<string, Talents.Link>>?>();
Dictionary<string, Dictionary<string, Talents.Link>>? LinksOf(string hero)
{
    if (!linksByHero.TryGetValue(hero, out var links))
        linksByHero[hero] = links = data.GameHero("npc_dota_hero_" + hero) is JsonObject file ? Talents.Links(file) : null;
    return links;
}

var gameUnits = data.GameUnits();
Abilities.Result heroAbilities = Abilities.Convert(abilitiesData, heroAbilitiesData, abilityMap, gameUnits, LinksOf);

// Which talent of a tier the AI takes first: the one picked more in pro matches of the latest patch (OpenDota explorer)
// when the hero has at least MinProGames there, otherwise in Divine–Immortal public matches (STRATZ, recent weeks:
// all 7.41f), otherwise Valve's bot build. 7.41f came out on 2026-09-15 (STRATZ gameVersions: asOfDateTime 1789430400).
const int MinProGames = 20;
const string Since = "extract(epoch from timestamp '2026-09-15')";
JsonObject abilityIds = data.Constants("ability_ids").AsObject();
var proPicks = new Dictionary<int, Dictionary<string, int>>();
foreach (JsonNode? row in data.Explorer("talents_7.41f",
             "SELECT pm.hero_id, u.ab, count(*) n FROM player_matches pm JOIN matches m USING(match_id), unnest(pm.ability_upgrades_arr) u(ab) " +
             $"WHERE m.start_time >= {Since} AND m.leagueid > 0 GROUP BY 1, 2"))
{
    int hero = row!["hero_id"]!.GetValue<int>();
    string? name = abilityIds[row["ab"]!.ToString()]?.GetValue<string>();
    if (name != null)
        (proPicks.TryGetValue(hero, out var picks) ? picks : proPicks[hero] = new Dictionary<string, int>())[name] = int.Parse(row["n"]!.ToString());
}

var proGames = data.Explorer("games_7.41f",
        "SELECT pm.hero_id, count(*) n FROM player_matches pm JOIN matches m USING(match_id) " +
        $"WHERE m.start_time >= {Since} AND m.leagueid > 0 AND pm.ability_upgrades_arr IS NOT NULL GROUP BY 1")
    .ToDictionary(r => r!["hero_id"]!.GetValue<int>(), r => int.Parse(r!["n"]!.ToString()));
string tokenFile = Path.Combine("packs", "stratz.token");
var highPicks = new Dictionary<int, Dictionary<string, int>>();
foreach (var chunk in heroes.Select(h => h.Value!["id"]!.GetValue<int>()).Chunk(25))
{
    string query = "{ heroStats { " + string.Join(" ", chunk.Select(id => $"h{id}: talent(heroId: {id}, bracketBasicIds: [DIVINE_IMMORTAL]) {{ abilityId matchCount }}")) + " } }";
    if (data.Stratz("talents_divine_immortal_" + chunk[0], query, tokenFile)?["heroStats"] is not JsonObject stats)
        break;
    foreach (int id in chunk)
    {
        var picks = highPicks[id] = new Dictionary<string, int>();
        foreach (JsonNode? row in stats["h" + id] as JsonArray ?? new JsonArray())
            if (abilityIds[row!["abilityId"]!.ToString()]?.GetValue<string>() is string name)
                picks[name] = row["matchCount"]!.GetValue<int>();
    }
}

JsonObject gameAbilities = data.GameAbilities();
var talentCoverage = new List<string>();
var talentSources = new List<string>();

var packHeroes = new JsonArray();
foreach (var (_, node) in heroes.OrderBy(h => h.Value!["id"]!.GetValue<int>()))
{
    JsonObject h = node!.AsObject();
    string id = h["name"]!.GetValue<string>().Replace("npc_dota_hero_", "");
    string primary = h["primary_attr"]!.GetValue<string>() switch
    {
        "str" => "strength",
        "agi" => "agility",
        "int" => "intelligence",
        "all" => "universal",
        var other => throw new InvalidDataException(id + ": unknown primary attribute " + other),
    };
    packHeroes.Add(new JsonObject
    {
        ["id"] = id,
        ["name"] = h["localized_name"]!.GetValue<string>(),
        ["roles"] = new JsonArray(h["roles"]!.AsArray().Select(r => (JsonNode)JsonValue.Create(r!.GetValue<string>().ToLowerInvariant())!).ToArray()),
        ["attackType"] = h["attack_type"]!.GetValue<string>() == "Melee" ? "melee" : "ranged",
        ["primaryAttribute"] = primary,
        ["strength"] = DotaData.Number(h["base_str"]),
        ["agility"] = DotaData.Number(h["base_agi"]),
        ["intelligence"] = DotaData.Number(h["base_int"]),
        ["strengthGain"] = DotaData.Number(h["str_gain"]),
        ["agilityGain"] = DotaData.Number(h["agi_gain"]),
        ["intelligenceGain"] = DotaData.Number(h["int_gain"]),
        ["baseHealth"] = DotaData.Number(h["base_health"]),
        ["baseHealthRegen"] = DotaData.Number(h["base_health_regen"] ?? 0),
        ["baseMana"] = DotaData.Number(h["base_mana"]),
        ["baseManaRegen"] = DotaData.Number(h["base_mana_regen"] ?? 0),
        ["baseArmor"] = DotaData.Number(h["base_armor"]),
        ["magicResist"] = DotaData.Number(h["base_mr"]),
        ["attackDamageMin"] = DotaData.Number(h["base_attack_min"]),
        ["attackDamageMax"] = DotaData.Number(h["base_attack_max"]),
        ["attackRange"] = DotaData.Number(h["attack_range"]),
        ["projectileSpeed"] = DotaData.Number(h["projectile_speed"]),
        ["baseAttackTime"] = DotaData.Number(h["attack_rate"]),
        ["baseAttackSpeed"] = DotaData.Number(h["base_attack_time"]),   // dotaconstants names the base attack speed "base_attack_time"
        ["attackPoint"] = DotaData.Number(h["attack_point"]),
        ["moveSpeed"] = DotaData.Number(h["move_speed"]),
        ["dayVision"] = DotaData.Number(h["day_vision"]),
        ["nightVision"] = DotaData.Number(h["night_vision"]),
        ["abilities"] = heroAbilities.HeroAbilities.TryGetValue(id, out JsonArray? own) ? own.DeepClone() : new JsonArray(),
        ["talents"] = Talents.For(id, heroAbilitiesData[h["name"]!.GetValue<string>()]?["talents"] as JsonArray, abilitiesData, gameAbilities,
            data.GameHero(h["name"]!.GetValue<string>()), TalentPicks(h["id"]!.GetValue<int>(), id), talentCoverage),
        ["itemBuild"] = Builds.For(data, h["id"]!.GetValue<int>(), itemIds, shopData, items.Ids),
    });
}

IReadOnlyDictionary<string, int> TalentPicks(int heroId, string hero)
{
    if (proGames.GetValueOrDefault(heroId) >= MinProGames && proPicks.TryGetValue(heroId, out var pro))
    {
        talentSources.Add(hero + ": pro " + proGames[heroId]);
        return pro;
    }

    if (highPicks.TryGetValue(heroId, out var high) && high.Count > 0)
    {
        talentSources.Add(hero + ": divine-immortal");
        return high;
    }

    talentSources.Add(hero + ": bot build");
    return new Dictionary<string, int>();
}

var pack = new JsonObject
{
    ["format"] = RulesetPack.Format,
    ["formatVersion"] = RulesetPack.FormatVersion,
    ["id"] = "dota",
    ["version"] = 7416,
    ["name"] = "Dota 2 " + Patch,
    ["provenance"] = new JsonObject
    {
        ["source"] = "https://github.com/odota/dotaconstants", ["commit"] = DotaData.Commit, ["patch"] = Patch,
        ["builds"] = "OpenDota /heroes/{id}/itemPopularity, " + DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["gameFiles"] = "https://github.com/SteamDatabase/GameTracking-Dota2 " + DotaData.GameFilesCommit,
        ["talentPicks"] = $"pro matches since 7.41f (OpenDota explorer, at least {MinProGames} games), else Divine-Immortal (STRATZ), else Valve's bot build: " + string.Join("; ", talentSources),
    },
    ["heroRules"] = new JsonObject
    {
        // dotaconstants' table, except level 30: 64400 (8000 from level 29, Liquipedia «Experience»; dotaconstants has 63900) —
        // Valve's last word is 7.32 «Changed XP required per level» without numbers; decided by the project owner (D-051).
        ["xpForLevel"] = new JsonArray(xpLevels.Select((x, i) => (JsonNode)JsonValue.Create(i == 29 ? 64400 : x!.GetValue<int>())!).ToArray()),
        ["xpShareRadius"] = 1500,
        ["heroKillXpBase"] = 100,
        ["heroKillXpPerLevel"] = 0,
        ["heroKillXpPercentOfVictim"] = 13,
        // Since 7.40 (patch notes; Liquipedia «Talents», «Attributes»; confirmed by the project owner): talents use their own
        // points at these levels; a skill point nothing can use goes to the +2 all attributes bonus, 7 levels.
        ["talentPointLevels"] = new JsonArray(10, 15, 20, 25, 27, 28, 29, 30),
        ["attributeBonusLevels"] = 7,
        ["attributeBonusPerLevel"] = 2,
        // Dota's economy in 7.41f (docs/DOTA_RULES_AUDIT.md; decisions D-051): Liquipedia «Gold» (changes up to 7.40: assist formula),
        // «Experience»; Valve patch notes 7.22 (death loss), 7.24 (respawn), 7.29 (buyback), 7.31 (kill bounty, streaks), 7.38 (bounty
        // runes); dota2.fandom.com «Death» (respawn table); none of these changed in 7.41–7.41f.
        ["economy"] = new JsonObject
        {
            ["killGoldBase"] = 125, ["killGoldPerLevel"] = 8, ["firstBloodGold"] = 135,
            ["streakGold"] = new JsonArray(60, 100, 150, 210, 280, 360, 450, 550),
            ["streakXpPerLevel"] = new JsonArray(13.75m, 20m, 28.75m, 40m, 53.75m, 70m, 88.75m, 110m),   // (1.25x² − 2.5x + 10) for streaks 3…10+
            ["assistGoldBase"] = 15, ["assistGoldPool"] = 50, ["assistGoldNetWorthPermille"] = 37, ["assistRadius"] = 1500,
            ["killCreditWindow"] = 20,   // D-051 (Valve 7.22 said 30; the wikis say 20)
            ["deathGoldLossNetWorthDivisor"] = 40, ["buybackBaseGold"] = 200, ["buybackNetWorthDivisor"] = 13, ["buybackRespawnPenalty"] = 25,
            ["respawn"] = new JsonArray(12, 15, 18, 21, 24, 26, 28, 30, 32, 34, 36, 44, 46, 48, 50, 52, 54, 65, 70, 75, 80, 85, 90, 95, 100),
            ["neutralRespawnMin"] = 26,
            ["periodicGold"] = new JsonArray(new (int Minute, decimal PerMinute)[]
                {
                    (0, 90m), (12, 94.8m), (30, 99.8m), (45, 105.5m), (62, 112.8m), (87, 120.5m), (112, 129m), (140, 139m), (175, 150.5m),
                }.Select(s => (JsonNode)new JsonObject { ["from"] = s.Minute * 60, ["perMinute"] = s.PerMinute }).ToArray()),
            ["heroXpCapLevel"] = 25, ["deniedCreepXpPercent"] = 50,
        },
    },
    // Dota's combat rules in 7.41f (docs/DOTA_RULES_AUDIT.md): magic resistance multiplies, evasion stacks diminishingly, critical strikes
    // roll from the strongest multiplier (dota2.fandom «Magic resistance», «Critical strike»; Liquipedia «Evasion»); attack speed 20…700
    // (fandom «Attack speed»); illusions 60% and summons 90% against buildings (Liquipedia «Attack Damage», D-051).
    ["combat"] = new JsonObject
    {
        ["dotaStacking"] = true, ["maxAttackSpeed"] = 700, ["illusionBuildingPercent"] = 60, ["summonBuildingPercent"] = 90,
        // Glyph of Fortification (Liquipedia «Buildings», 7.41f): cooldown 300 s, first usable at 3:00, buildings 7 s, lane creeps
        // 3 s (Fandom 2024: 4 — older), the cooldown reset to 1 s by the first tier 1, tier 2 and melee barracks lost.
        ["glyph"] = new JsonObject { ["cooldown"] = 300, ["initialCooldown"] = 180, ["buildingDuration"] = 7, ["creepDuration"] = 3, ["resetDelay"] = 1 },
    },
    ["heroes"] = packHeroes,
    // Lane waves (Liquipedia «Lane Creeps», updated for 7.41): every 30 s from 0:00, 3 melee and 1 ranged; a siege creep from the
    // 11th wave every 10th; a flagbearer from the 5th wave every 2nd in place of a melee creep; +1 melee at 15:00, 30:00 and
    // 45:00, +1 ranged at 40:00, +1 siege at 30:00 (7.41: was 35:00) and at 60:00 (new in 7.41).
    ["waves"] = new JsonObject
    {
        ["firstSpawn"] = 0, ["interval"] = 30,
        ["composition"] = new JsonArray(
            Wave("melee", 3), Wave("ranged", 1), Wave("siege", 1, firstWave: 11, every: 10),
            Wave("flagbearer", 1, firstWave: 5, every: 2, replaces: "melee"),
            Wave("melee", 1, from: 15 * 60), Wave("melee", 1, from: 30 * 60), Wave("melee", 1, from: 45 * 60),
            Wave("ranged", 1, from: 40 * 60),
            Wave("siege", 1, firstWave: 11, every: 10, from: 30 * 60), Wave("siege", 1, firstWave: 11, every: 10, from: 60 * 60)),
        // Every 7:30 (Liquipedia «Lane Creeps», 7.41; Valve 7.39–7.40 for the gold): health and damage up to 30 times, gold and XP
        // without limit. The flagbearer follows the melee creep; siege creeps get nothing (their +16 damage after the lane's
        // ranged barracks falls waits for the second barracks with the Dota map, B-131).
        ["upgrades"] = new JsonObject
        {
            ["interval"] = 450, ["maxStatCycles"] = 30,
            ["steps"] = new JsonArray(
                UpgradeStep("melee", 12, 1, 1, 0), UpgradeStep("ranged", 12, 2, 3, 8), UpgradeStep("flagbearer", 12, 1, 1, 0),
                UpgradeStep("melee_super", 19, 2, 1.5m, 0), UpgradeStep("ranged_super", 18, 3, 6, 0)),
        },
    },
    // Lane creeps and structures from the game's npc_units.txt, replacing the base rules' ones of the same id (the map is still the base one:
    // one barracks per lane, so it gets the melee barracks; B-131).
    ["unitTypes"] = new JsonArray(items.UnitTypes.Concat(heroAbilities.UnitTypes).Select(u => u!.DeepClone())
        .Concat(new (string Id, string Game)[]
            {
                ("melee", "npc_dota_creep_goodguys_melee"), ("ranged", "npc_dota_creep_goodguys_ranged"), ("siege", "npc_dota_goodguys_siege"),
                ("melee_super", "npc_dota_creep_goodguys_melee_upgraded"), ("ranged_super", "npc_dota_creep_goodguys_ranged_upgraded"),
                ("flagbearer", "npc_dota_creep_goodguys_flagbearer"),
            }.Select(c => (JsonNode)Flagbearer(Abilities.GameUnit(c.Id, c.Game, gameUnits))))
        .ToArray()),
    ["structureTypes"] = new JsonArray(new (string Id, string Game)[]
        {
            ("tower.t1", "npc_dota_goodguys_tower1_mid"), ("tower.t2", "npc_dota_goodguys_tower2_mid"), ("tower.t3", "npc_dota_goodguys_tower3_mid"),
            ("tower.t4", "npc_dota_goodguys_tower4"), ("ancient", "npc_dota_goodguys_fort"), ("barracks", "npc_dota_goodguys_melee_rax_mid"),
        }.Select(s => (JsonNode)StructureRules(Abilities.GameUnit(s.Id, s.Game, gameUnits))).Append(Fountain()).ToArray()),
    ["abilities"] = new JsonArray(items.Abilities.Concat(heroAbilities.Abilities).Select(a => a!.DeepClone()).ToArray()),
    ["items"] = items.Items,
};

string json = pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output + ".unchecked", json);   // kept for inspection if the check below fails
RulesetContent applied = RulesetPack.Apply(BaseRuleset.Create(), json);   // the pack must load
File.WriteAllText(output, json);
File.Delete(output + ".unchecked");
string report = Path.ChangeExtension(output, ".coverage.txt");
File.WriteAllLines(report, new[]
{
    "Dota pack " + Patch + ": what is not carried over yet (docs/DOTA_PACK.md)", "",
    $"hero abilities: {heroAbilities.Mapped} of {heroAbilities.Total} mapped", "",
}.Concat(heroAbilities.Coverage).Concat(talentCoverage).Append("").Concat(items.Coverage));
Console.WriteLine($"{output}: {applied.Heroes.Count} heroes, abilities {heroAbilities.Mapped}/{heroAbilities.Total}, {applied.Items.Count} items ({items.Coverage.Count} with gaps, {report}), " +
                  $"levels 1-{applied.HeroRules!.MaxLevel}, fingerprint {applied.ToDescriptor().Fingerprint}");

// Dota's structure gold and denying (Liquipedia «Buildings» as of 7.41f: team bounty, average last hit; Valve 7.27 «±10»; D-051): the game's unit
// file has no bounty for buildings (set by the game's code). A building below 10% of its health can be denied, the ancient
// never; a denied tower gives both teams half its team bounty, a denied barracks still gives its enemies the full one.
static JsonObject StructureRules(JsonObject structure)
{
    (int Team, int LastHitMin, int LastHitMax, bool LastHitterGetsTeamGold, int DeniedEnemy, int DeniedOwn)? bounty = structure["id"]!.GetValue<string>() switch
    {
        "tower.t1" => (90, 110, 130, true, 50, 50),
        "tower.t2" => (110, 130, 150, true, 50, 50),
        "tower.t3" => (125, 150, 170, true, 50, 50),
        "tower.t4" => (145, 170, 190, true, 50, 50),
        "barracks" => (155, 90, 135, false, 100, 0),   // the melee barracks (one per lane until the map is carried over)
        _ => null,
    };
    // Backdoor Protection (Liquipedia «Buildings», 7.41f; D-051): tier 2 towers by their own 900 radius, the buildings in the base
    // by 4000 around the ancient (the game file's backdoor_protection_in_base says 900 — the wikis: the ancient's radius
    // governs them); off while an enemy lane creep is inside and 15 s (file: activation_time) + 0.5 s linger after;
    // −50% damage, −75% from illusions and summons; heals the enemy damage at 180 HP/s (file: 90, wikis and Valve 7.32: 180).
    string kind = structure["id"]!.GetValue<string>();
    if (kind != "tower.t1")
        structure["backdoor"] = new JsonObject
        {
            ["radius"] = kind == "tower.t2" ? 900 : 4000, ["aroundAncient"] = kind != "tower.t2",
            ["damageReduction"] = 50, ["summonDamageReduction"] = 75, ["healPerSecond"] = 180, ["reactivation"] = 15.5,
        };
    if (bounty is not { } b)
        return structure;
    // Tower Protection (Liquipedia «Buildings», 7.41f): allied heroes within 900 get +3/5/5/5 armor and +1/3/3/3 regeneration
    // (T1–T4), lingering 0.5 s; the same aura does not stack. Tier 4: +4 armor per standing barracks (Valve 7.40).
    string id = structure["id"]!.GetValue<string>();
    if (id.StartsWith("tower.", StringComparison.Ordinal))
    {
        bool t1 = id == "tower.t1";
        structure["modifiers"] = new JsonArray(new JsonObject
        {
            ["key"] = "tower_protection",
            ["aura"] = new JsonObject
            {
                ["radius"] = 900, ["team"] = "ally", ["heroesOnly"] = true,
                ["modifier"] = new JsonObject { ["key"] = "tower_protection_aura", ["duration"] = 0.5, ["armor"] = t1 ? 3 : 5, ["healthRegen"] = t1 ? 1 : 3, ["unique"] = true },
            },
        });
    }

    if (id == "tower.t4")
        structure["armorPerStandingBarracks"] = 4;
    // Glyph: towers of tier 2 and up strike 5 more enemies per attack; the first tier 1, tier 2 and melee barracks lost reset it.
    if (id is "tower.t2" or "tower.t3" or "tower.t4")
        structure["glyphExtraTargets"] = 5;
    if (id is "tower.t1" or "tower.t2" or "barracks")
        structure["resetsGlyphOnFirstLoss"] = true;
    structure["denyHealthPercent"] = 10;
    structure["bounty"] = new JsonObject
    {
        ["teamGold"] = b.Team,
        ["lastHitGold"] = new JsonArray(b.LastHitMin, b.LastHitMax),
        ["lastHitterGetsTeamGold"] = b.LastHitterGetsTeamGold,
        ["deniedEnemyGoldPercent"] = b.DeniedEnemy,
        ["deniedOwnGoldPercent"] = b.DeniedOwn,
    };
    return structure;
}

// The fountain is not in the game's unit file (set by the map and the game's code): Liquipedia «Buildings» (7.41f) and Fandom
// «Fountain» (2024): 290–310 damage every 0.15 s, range and acquisition 1200, projectile 1400, Reinforced, bound radius 144,
// vision 1800 day and night, True Sight 1200, invulnerable; Valve 7.27: +3 damage per hit on the same target (the stack is
// added before the hit, 12 s); Valve 7.35: 25% splash within 250. Not carried over: the 25% accuracy against evasion, and the
// tower-like protection of heroes it does not have (B-145).
static JsonObject Fountain() => new()
{
    ["id"] = "fountain", ["health"] = 1, ["armor"] = 0, ["attackDamageMin"] = 290, ["attackDamageMax"] = 310, ["baseAttackTime"] = 0.15,
    ["attackPoint"] = 0, ["attackRange"] = 1200, ["acquisitionRange"] = 1200, ["projectileSpeed"] = 1400, ["visionRange"] = 1800,
    ["nightVision"] = 1800, ["trueSight"] = 1200, ["radius"] = 144, ["attackClass"] = "reinforced",
    ["modifiers"] = new JsonArray(new JsonObject
    {
        ["key"] = "fountain_damage", ["splash"] = 25, ["splashRadius"] = 250,
        ["onHit"] = new JsonArray(
            new JsonObject { ["kind"] = "modifier", ["modifiers"] = new JsonArray(new JsonObject { ["key"] = "fountain_damage_stack", ["duration"] = 12, ["stacking"] = true, ["maxStacks"] = 1000 }) },
            new JsonObject { ["kind"] = "damage", ["damageType"] = "physical", ["scaling"] = "perTargetStack", ["stackKey"] = "fountain_damage_stack", ["amounts"] = new JsonArray(3) }),
    }),
};

static JsonObject Wave(string creep, int count, int firstWave = 1, int every = 1, int from = 0, string? replaces = null)
{
    var entry = new JsonObject { ["creep"] = creep, ["count"] = count };
    if (firstWave != 1)
        entry["firstWave"] = firstWave;
    if (every != 1)
        entry["everyWaves"] = every;
    if (from != 0)
        entry["from"] = from;
    if (replaces != null)
        entry["replaces"] = replaces;
    return entry;
}

// The flagbearer's XP: BountyXP 57 in the unit file plus its aura ability's bonus_xp 3 = 60, as in Valve's 7.41 notes ("57 → 60").
static JsonObject Flagbearer(JsonObject unit)
{
    if (unit["id"]!.GetValue<string>() == "flagbearer" && unit["bounty"] is JsonObject bounty)
        bounty["xp"] = bounty["xp"]!.GetValue<decimal>() + 3;
    return unit;
}

static JsonObject UpgradeStep(string creep, int health, int damage, decimal gold, int xp) =>
    new() { ["creep"] = creep, ["health"] = health, ["damage"] = damage, ["gold"] = gold, ["xp"] = xp };
