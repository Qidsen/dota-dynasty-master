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

Abilities.Result heroAbilities = Abilities.Convert(abilitiesData, heroAbilitiesData, abilityMap, data.GameUnits(), LinksOf);

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
        ["xpForLevel"] = new JsonArray(xpLevels.Select(x => (JsonNode)JsonValue.Create(x!.GetValue<int>())!).ToArray()),
        ["xpShareRadius"] = 1500,
        ["heroKillXpBase"] = 100,
        ["heroKillXpPerLevel"] = 0,
        ["heroKillXpPercentOfVictim"] = 13,
        // Since 7.40 (patch notes; Liquipedia «Talents», «Attributes»; confirmed by the project owner): talents use their own
        // points at these levels; a skill point nothing can use goes to the +2 all attributes bonus, 7 levels.
        ["talentPointLevels"] = new JsonArray(10, 15, 20, 25, 27, 28, 29, 30),
        ["attributeBonusLevels"] = 7,
        ["attributeBonusPerLevel"] = 2,
    },
    ["heroes"] = packHeroes,
    ["unitTypes"] = new JsonArray(items.UnitTypes.Concat(heroAbilities.UnitTypes).Select(u => u!.DeepClone()).ToArray()),
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
