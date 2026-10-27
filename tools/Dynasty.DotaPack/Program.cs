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
DotaMap dotaMap = DotaMap.Load(Path.Combine("packs", "cache", "oldgrowth-" + DotaMap.Commit[..8]));
foreach (string warning in dotaMap.Warnings)
    Console.WriteLine("map: " + warning);

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
        // Lane aggro (Liquipedia «Lane Creeps» and «Buildings», 7.41): an attack order on an enemy hero draws the enemy lane creeps
        // within 500 of the attacker for 2.3 s, once per 3 s per hero; creeps spawned before 5:00 cannot be drawn until within
        // 1500 of their tier 1 tower; a tower turns on a hero within 500 of it attacking a hero, and weighs threats within 700.
        ["aggro"] = new JsonObject
        {
            ["drawRadius"] = 500, ["forced"] = 2.3, ["cooldown"] = 3, ["lockedUntil"] = 300, ["unlockRadius"] = 1500, ["unlockStructureType"] = "tower.t1",
            ["towerProtectRadius"] = 500, ["towerThreatRadius"] = 700,
        },
    },
    ["heroes"] = packHeroes,
    // Dota's map of 7.41f (D-053, DotaMap.cs).
    ["map"] = dotaMap.Map,
    ["runes"] = Runes(dotaMap.PowerSpots, dotaMap.BountySpots),
    // Roshan (game file npc_dota_roshan; Liquipedia «Roshan»; Valve 7.38 respawn 8–11 min; D-051 growth): the top pit by day, the
    // bottom one by night (Valve 7.38, 7.41), changing at every change of day and night from the start (the project owner); every hero of the killing team 135 gold, the last hitter 200–290 more, 400 XP to the
    // killing team's heroes around; every minute from the horn, alive or dead, +130 health, +0.4 armor (already at 0:00),
    // +7 damage, +20 XP.
    ["boss"] = new JsonObject
    {
        ["position"] = new JsonObject { ["x"] = dotaMap.RoshanTop.X, ["y"] = dotaMap.RoshanTop.Y },
        ["nightPosition"] = new JsonObject { ["x"] = dotaMap.RoshanBottom.X, ["y"] = dotaMap.RoshanBottom.Y },
        // Roshan idles in his pit and fights only those who attack him (Fandom «Roshan»: «only attacking when aggroed»).
        ["id"] = "roshan", ["creep"] = "roshan", ["firstSpawn"] = 0, ["respawnMin"] = 480, ["respawnMax"] = 660, ["provokeRadius"] = 0,
        ["teamGold"] = 135, ["killerGold"] = new JsonArray(200, 290), ["xp"] = 400,
        ["growth"] = new JsonObject { ["interval"] = 60, ["health"] = 130, ["armor"] = 0.4, ["damage"] = 7, ["xp"] = 20, ["armorFromStart"] = true },
        // The Aegis (game file item_aegis: reincarnate_time 5, disappear_time 300; Fandom «Aegis of the Immortal» 2024-11: unused or
        // taken back by Roshan's respawn, 20% of max health and mana per second for 5 s): to the killing team's richest hero (B-149).
        ["aegis"] = new JsonObject
        {
            ["key"] = "aegis", ["duration"] = 300, ["revives"] = true, ["reviveDelay"] = 5, ["consumedOnRevive"] = true, ["dispellable"] = false,
            ["expireModifier"] = new JsonObject { ["key"] = "aegis_regen", ["duration"] = 5, ["healthRegenMaxPercent"] = 20, ["manaRegenMaxPercent"] = 20 },
        },
        // Roshan's spells (game file roshan_slam, roshan_revengeroar; Valve 7.35, 7.39): Slam while he fights — every 10 s, 0.47 s
        // cast, 350, 70 + 10 a minute magical damage (creeps take +100%), 50% slow for 4 s; Roar of Retribution when the team
        // that last killed him hits him below 80% — every 60 s, 0.5 s windup, 600, 50 + 10 a minute magical damage, the units
        // take 25% more damage for 12 s and are disarmed for 2 s; cast even stunned or silenced, strongly dispels him (7.39).
        // From the third death Cheese and a Refresher Shard (Valve 7.37–7.38, D-051); the Banner from the second is not carried over (B-152).
        ["drops"] = new JsonArray(new JsonObject { ["item"] = "cheese", ["fromDeath"] = 3 }, new JsonObject { ["item"] = "refresher_shard", ["fromDeath"] = 3 }),
        ["abilities"] = new JsonArray(
            new JsonObject
            {
                ["id"] = "roshan_slam", ["trigger"] = "engaged", ["cooldown"] = 10, ["castPoint"] = 0.47, ["radius"] = 350, ["damageType"] = "magical",
                ["damage"] = 70, ["damagePerInterval"] = 10, ["creepDamagePercent"] = 200,
                ["modifiers"] = new JsonArray(new JsonObject { ["key"] = "roshan_slam_slow", ["duration"] = 4, ["moveSpeedPercent"] = -50 }),
            },
            new JsonObject
            {
                ["id"] = "roshan_revengeroar", ["trigger"] = "hitByLastKillersBelowHealth", ["cooldown"] = 60, ["castPoint"] = 0.5, ["radius"] = 600,
                ["damageType"] = "magical", ["damage"] = 50, ["damagePerInterval"] = 10, ["belowHealthPercent"] = 80, ["ignoresDisables"] = true, ["dispelsSelf"] = true,
                ["modifiers"] = new JsonArray(
                    new JsonObject { ["key"] = "roshan_roar_disarm", ["duration"] = 2, ["flags"] = new JsonArray("disarmed") },
                    new JsonObject { ["key"] = "roshan_roar_amplify", ["duration"] = 12, ["incomingDamagePercent"] = 25 }),
            }),
    },
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
        // without limit. The flagbearer follows the melee creep; siege creeps get nothing (only +16 damage as super creeps).
        ["upgrades"] = new JsonObject
        {
            ["interval"] = 450, ["maxStatCycles"] = 30,
            ["steps"] = new JsonArray(
                UpgradeStep("melee", 12, 1, 1, 0), UpgradeStep("ranged", 12, 2, 3, 8), FlagbearerStep(),
                UpgradeStep("melee_super", 19, 2, 1.5m, 0), UpgradeStep("ranged_super", 18, 3, 6, 0),
                UpgradeStep("melee_mega", 0, 0, 1.5m, 0), UpgradeStep("ranged_mega", 0, 0, 6, 0)),
        },
        // Liquipedia «Lane Creeps» (7.41f): a lane's melee barracks makes the enemy's melee creeps there super, its ranged barracks the
        // ranged and siege creeps (siege: the game file's upgraded siege creep, +16 damage); all six down: mega creeps on every lane
        // (they grow only in gold); flagbearers stay as they are.
        ["superUpgrades"] = new JsonArray(CreepUpgrade("melee", "melee_super"), CreepUpgrade("ranged", "ranged_super"), CreepUpgrade("siege", "siege_super")),
        ["megaUpgrades"] = new JsonArray(CreepUpgrade("melee", "melee_mega"), CreepUpgrade("ranged", "ranged_mega"), CreepUpgrade("siege", "siege_mega")),
    },
    // Lane creeps and structures from the game's npc_units.txt, replacing the base rules' ones of the same id; the barracks and
    // effigy types are new (the base map has one barracks type, unused on the Dota map).
    ["unitTypes"] = new JsonArray(items.UnitTypes.Concat(heroAbilities.UnitTypes).Select(u => u!.DeepClone())
        .Concat(new (string Id, string Game)[]
            {
                ("melee", "npc_dota_creep_goodguys_melee"), ("ranged", "npc_dota_creep_goodguys_ranged"), ("siege", "npc_dota_goodguys_siege"),
                ("melee_super", "npc_dota_creep_goodguys_melee_upgraded"), ("ranged_super", "npc_dota_creep_goodguys_ranged_upgraded"),
                ("siege_super", "npc_dota_goodguys_siege_upgraded"), ("melee_mega", "npc_dota_creep_goodguys_melee_upgraded_mega"),
                ("ranged_mega", "npc_dota_creep_goodguys_ranged_upgraded_mega"), ("siege_mega", "npc_dota_goodguys_siege_upgraded_mega"),
                ("flagbearer", "npc_dota_creep_goodguys_flagbearer"), ("roshan", "npc_dota_roshan"),
            }.Concat(Jungle.Units.Select(u => (Id: u, Game: "npc_dota_" + u))).Select(c => (JsonNode)Roshan(Flagbearer(Abilities.GameUnit(c.Id, c.Game, gameUnits)))))
        .ToArray()),
    ["structureTypes"] = new JsonArray(new (string Id, string Game)[]
        {
            ("tower.t1", "npc_dota_goodguys_tower1_mid"), ("tower.t2", "npc_dota_goodguys_tower2_mid"), ("tower.t3", "npc_dota_goodguys_tower3_mid"),
            ("tower.t4", "npc_dota_goodguys_tower4"), ("ancient", "npc_dota_goodguys_fort"), ("barracks.melee", "npc_dota_goodguys_melee_rax_mid"),
            ("barracks.ranged", "npc_dota_goodguys_range_rax_mid"), ("effigy", "npc_dota_goodguys_fillers"),
        }.Select(s => (JsonNode)StructureRules(Abilities.GameUnit(s.Id, s.Game, gameUnits))).Append(Fountain()).ToArray()),
    ["abilities"] = new JsonArray(items.Abilities.Concat(heroAbilities.Abilities).Select(a => a!.DeepClone()).Concat(RoshanDropAbilities()).ToArray()),
    ["items"] = new JsonArray(items.Items.Select(i => i!.DeepClone()).Concat(RoshanDropItems()).ToArray()),
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
        "barracks.melee" => (155, 90, 135, false, 100, 0),
        "barracks.ranged" => (90, 90, 135, false, 100, 0),   // last hit 112 on average as the melee one
        // Effigy buildings: 68 gold (Liquipedia «Buildings», 7.41f), no last-hit gold given; denied as a barracks (B-156).
        "effigy" => (68, 0, 0, true, 100, 0),
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
    if (kind == "barracks.melee")
        structure["upgradesCreeps"] = new JsonArray("melee");
    if (kind == "barracks.ranged")
        structure["upgradesCreeps"] = new JsonArray("ranged", "siege");
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
    if (id is "tower.t1" or "tower.t2" or "barracks.melee")
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

// The flagbearer (game file: flagbearer_creep_aura_effect; Liquipedia «Lane Creeps», 7.40–7.41): XP 57 in the unit file plus
// the aura ability's bonus_xp 3 = 60, as in Valve's 7.41 notes ("57 → 60"); Inspiration Aura: allied creeps (not heroes,
// illusions or creep-heroes) within 700 get +3 health regeneration (0.5 s linger) and 4% magic resistance per upgrade cycle,
// at most 15; killed by an enemy, 10 gold + 100% of its bounty to the killing player and to the enemy heroes within 1500.
static JsonObject Flagbearer(JsonObject unit)
{
    if (unit["id"]!.GetValue<string>() != "flagbearer")
        return unit;
    if (unit["bounty"] is JsonObject bounty)
        bounty["xp"] = bounty["xp"]!.GetValue<decimal>() + 3;
    unit["areaBounty"] = new JsonObject { ["gold"] = 10, ["bountyPercent"] = 100, ["radius"] = 1500 };
    unit["modifiers"] = new JsonArray(new JsonObject
    {
        ["key"] = "flagbearer_inspiration",
        ["aura"] = new JsonObject
        {
            ["radius"] = 700, ["team"] = "ally", ["creepsOnly"] = true,
            ["modifier"] = new JsonObject { ["key"] = "flagbearer_inspiration_regen", ["duration"] = 0.5, ["healthRegen"] = 3, ["unique"] = true },
        },
    });
    return unit;
}

// The flagbearer upgrades as a melee creep, and its aura's magic resistance by 4% per cycle (at most 15).
static JsonObject FlagbearerStep()
{
    JsonObject step = UpgradeStep("flagbearer", 12, 1, 1, 0);
    step["auraMagicResist"] = 4;
    step["auraMaxCycles"] = 15;
    step["auraRadius"] = 700;
    return step;
}

static JsonObject CreepUpgrade(string from, string to) => new() { ["from"] = from, ["to"] = to };

static JsonObject UpgradeStep(string creep, int health, int damage, decimal gold, int xp) =>
    new() { ["creep"] = creep, ["health"] = health, ["damage"] = damage, ["gold"] = gold, ["xp"] = xp };

// Roshan's Bash (game file roshan_bash: 15%, +50 physical damage added to the attack, 1.65 s stun; pseudo-random like every bash).
// Spell Block (roshan_spell_block: cooldown 15, status_resistance 25), Devotion (roshan_devotion: slow_resistance 25) and illusions
// dealing 80% less (Fandom «Roshan», Strength of the Immortal; not in the file).
static JsonObject Roshan(JsonObject unit)
{
    if (unit["id"]!.GetValue<string>() != "roshan")
        return unit;
    unit.Remove("bounty");   // the boss's rewards are its own (the "boss" section)
    unit["modifiers"] = new JsonArray(new JsonObject
    {
        ["key"] = "roshan_immortal", ["statusResistance"] = 25, ["slowResistance"] = 25, ["spellBlockCooldown"] = 15, ["illusionDamageTaken"] = -80,
        ["dispellable"] = false,
    }, new JsonObject
    {
        ["key"] = "roshan_bash", ["onHitChance"] = 15, ["pseudoRandom"] = true,
        ["onHit"] = new JsonArray(
            new JsonObject { ["kind"] = "modifier", ["modifiers"] = new JsonArray(new JsonObject { ["key"] = "roshan_bash_stun", ["duration"] = 1.65, ["flags"] = new JsonArray("stunned") }) },
            new JsonObject { ["kind"] = "damage", ["damageType"] = "physical", ["amounts"] = new JsonArray(50) }),
    });
    return unit;
}

// Roshan's drops (game file items.txt: item_cheese — cooldown 40, health_restore 2500, mana_restore 1500, one charge;
// item_refresher_shard — cooldown 180, 200 mana, one charge, refreshes the cooldowns). Not sold in the shop, no gold value here.
static JsonNode[] RoshanDropAbilities() => new JsonNode[]
{
    new JsonObject
    {
        ["id"] = "item_cheese", ["name"] = "Cheese", ["targeting"] = "noTarget", ["targetTeam"] = "ally", ["castRange"] = 0, ["castPoint"] = 0,
        ["cooldown"] = new JsonArray(40), ["requiredLevels"] = new JsonArray(1),
        ["effects"] = new JsonArray(new JsonObject { ["kind"] = "heal", ["amounts"] = new JsonArray(2500) },
            new JsonObject { ["kind"] = "restoreMana", ["amounts"] = new JsonArray(1500) }),
    },
    new JsonObject
    {
        ["id"] = "item_refresher_shard", ["name"] = "Refresher Shard", ["targeting"] = "noTarget", ["targetTeam"] = "ally", ["castRange"] = 0, ["castPoint"] = 0,
        ["cooldown"] = new JsonArray(180), ["manaCost"] = new JsonArray(200), ["requiredLevels"] = new JsonArray(1),
        ["effects"] = new JsonArray(new JsonObject { ["kind"] = "refreshCooldowns" }),
    },
};

static JsonNode[] RoshanDropItems() => new JsonNode[]
{
    new JsonObject { ["id"] = "cheese", ["name"] = "Cheese", ["cost"] = 0, ["active"] = "item_cheese", ["charges"] = 1, ["inShop"] = false },
    new JsonObject { ["id"] = "refresher_shard", ["name"] = "Refresher Shard", ["cost"] = 0, ["active"] = "item_refresher_shard", ["charges"] = 1, ["inShop"] = false },
};

// Runes (Fandom «Runes» 2025-09 and the runes' pages; Valve 7.35–7.41 for what changed since: Amplify 80% + 15% spell amp,
// Arcane 25%, Haste always 22 s and Invisibility without damage reduction since 7.40, bounty gold by spawn time since 7.40;
// D-051: bounty 40 + 6 per 4 minutes). The power spots are the base map's river spots; the base map has no bounty spots,
// so one in each team's jungle, mirrored, until the Dota map (B-131).
static JsonObject Runes((int X, int Y)[] power, (int X, int Y)[] bounty)
{
    JsonObject Spot(int x, int y) => new() { ["x"] = x, ["y"] = y };
    JsonArray Spots(params (int X, int Y)[] spots) => new(spots.Select(s => (JsonNode)Spot(s.X, s.Y)).ToArray());
    JsonObject Modifier(string key, double duration, JsonObject fields)
    {
        fields["key"] = key;
        fields["duration"] = duration;
        return fields;
    }

    JsonObject IllusionFor(string kind, int incoming) => new()
    {
        ["kind"] = kind,
        ["effects"] = new JsonArray(new JsonObject
        {
            ["kind"] = "illusion", ["counts"] = new JsonArray(2), ["duration"] = 75,
            ["modifiers"] = new JsonArray(new JsonObject { ["key"] = "rune_illusion", ["incomingDamagePercent"] = incoming, ["attackDamagePercent"] = -65 }),
        }),
    };
    return new JsonObject
    {
        ["types"] = new JsonArray(
            new JsonObject { ["id"] = "bounty", ["teamGold"] = 40, ["teamGoldPerInterval"] = 6, ["teamGoldInterval"] = 240 },
            new JsonObject { ["id"] = "water", ["effects"] = new JsonArray(new JsonObject { ["kind"] = "heal", ["amounts"] = new JsonArray(40) },
                new JsonObject { ["kind"] = "restoreMana", ["amounts"] = new JsonArray(80) }) },
            new JsonObject { ["id"] = "amplify_damage", ["modifier"] = Modifier("rune_amplify_damage", 45, new JsonObject { ["baseDamagePercent"] = 80, ["spellAmplification"] = 15 }) },
            new JsonObject { ["id"] = "arcane", ["modifier"] = Modifier("rune_arcane", 50, new JsonObject { ["cooldownReduction"] = 25, ["manaCostReduction"] = 30 }) },
            new JsonObject { ["id"] = "haste", ["modifier"] = Modifier("rune_haste", 22, new JsonObject { ["fixedMoveSpeed"] = 550 }) },
            // Illusion: a basic dispel on the hero, 2 illusions for 75 s dealing 35% and taking 200% (melee) or 300% (ranged).
            new JsonObject { ["id"] = "illusion", ["effects"] = new JsonArray(new JsonObject { ["kind"] = "dispel" }, IllusionFor("ifMelee", 100), IllusionFor("ifRanged", 200)) },
            // Invisibility: after a 2 s fade, invisible for 45 s until it attacks or casts.
            new JsonObject { ["id"] = "invisibility", ["modifier"] = Modifier("rune_invisibility_fade", 2, new JsonObject
            {
                ["expireModifier"] = Modifier("rune_invisibility", 45, new JsonObject { ["flags"] = new JsonArray("invisible"), ["breakOnAction"] = true }),
            }) },
            // Regeneration: 6% of health and mana a second for 30 s, 1% for 3 s after a player's damage, gone when full.
            new JsonObject { ["id"] = "regeneration", ["modifier"] = Modifier("rune_regeneration", 30, new JsonObject
            {
                ["healthRegenMaxPercent"] = 6, ["manaRegenMaxPercent"] = 6, ["damagedRegenMaxPercent"] = 1, ["damagedRegen"] = 3, ["endsWhenFull"] = true,
            }) },
            new JsonObject { ["id"] = "shield", ["modifier"] = Modifier("rune_shield", 75, new JsonObject { ["barrierMaxHealthPercent"] = 50 }) }),
        ["schedules"] = new JsonArray(
            new JsonObject { ["id"] = "bounty_first", ["spots"] = Spots(power.Concat(bounty).ToArray()), ["first"] = 0, ["types"] = new JsonArray("bounty"), ["stay"] = true },
            new JsonObject { ["id"] = "bounty", ["spots"] = Spots(bounty), ["first"] = 240, ["interval"] = 240, ["types"] = new JsonArray("bounty"), ["stay"] = true },
            new JsonObject { ["id"] = "water", ["spots"] = Spots(power), ["first"] = 120, ["interval"] = 120, ["until"] = 240, ["types"] = new JsonArray("water") },
            new JsonObject
            {
                ["id"] = "power", ["spots"] = Spots(power), ["first"] = 360, ["interval"] = 120, ["placement"] = "oneRandomSpot", ["noRepeat"] = true,
                ["types"] = new JsonArray("amplify_damage", "arcane", "haste", "illusion", "invisibility", "regeneration", "shield"),
            }),
    };
}
