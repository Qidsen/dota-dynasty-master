using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dynasty.Content;
using Dynasty.MatchSimulation.Rules;

// Builds the Dota 2 ruleset pack (decision D-048, docs/DOTA_PACK.md) from odota/dotaconstants at a pinned
// commit. The pack is written locally (default packs/dota-7.41f.json, git-ignored) and is never committed.
//   dotnet run --project tools/Dynasty.DotaPack [-- out.json [cacheDir]]

const string Commit = "5c0242df9057848fa2036ca3fafe9f2bd4508163";   // dotaconstants, 2026-10-12, verified against patch 7.41f
const string Patch = "7.41f";
string output = args.Length > 0 ? args[0] : Path.Combine("packs", "dota-" + Patch + ".json");
string cache = args.Length > 1 ? args[1] : Path.Combine("packs", "cache", "dotaconstants-" + Commit[..8]);
Directory.CreateDirectory(cache);

JsonNode Load(string name)
{
    string file = Path.Combine(cache, name + ".json");
    if (!File.Exists(file))
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dota-dynasty-pack/1.0");
        string url = $"https://raw.githubusercontent.com/odota/dotaconstants/{Commit}/build/{name}.json";
        Console.WriteLine("download " + url);
        File.WriteAllText(file, http.GetStringAsync(url).GetAwaiter().GetResult());
    }

    return JsonNode.Parse(File.ReadAllText(file))!;
}

JsonNode Number(JsonNode? value) => value is JsonValue v && v.TryGetValue(out string? s)
    ? JsonValue.Create(decimal.Parse(s, CultureInfo.InvariantCulture))!
    : JsonValue.Create(value!.GetValue<decimal>())!;

JsonObject heroes = Load("heroes").AsObject();
JsonArray xpLevels = Load("xp_level").AsArray();

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
        ["strength"] = Number(h["base_str"]),
        ["agility"] = Number(h["base_agi"]),
        ["intelligence"] = Number(h["base_int"]),
        ["strengthGain"] = Number(h["str_gain"]),
        ["agilityGain"] = Number(h["agi_gain"]),
        ["intelligenceGain"] = Number(h["int_gain"]),
        ["baseHealth"] = Number(h["base_health"]),
        ["baseHealthRegen"] = Number(h["base_health_regen"] ?? 0),
        ["baseMana"] = Number(h["base_mana"]),
        ["baseManaRegen"] = Number(h["base_mana_regen"] ?? 0),
        ["baseArmor"] = Number(h["base_armor"]),
        ["magicResist"] = Number(h["base_mr"]),
        ["attackDamageMin"] = Number(h["base_attack_min"]),
        ["attackDamageMax"] = Number(h["base_attack_max"]),
        ["attackRange"] = Number(h["attack_range"]),
        ["projectileSpeed"] = Number(h["projectile_speed"]),
        ["baseAttackTime"] = Number(h["attack_rate"]),
        ["baseAttackSpeed"] = Number(h["base_attack_time"]),   // dotaconstants names the base attack speed "base_attack_time"
        ["attackPoint"] = Number(h["attack_point"]),
        ["moveSpeed"] = Number(h["move_speed"]),
        ["dayVision"] = Number(h["day_vision"]),
        ["nightVision"] = Number(h["night_vision"]),
    });
}

var pack = new JsonObject
{
    ["format"] = RulesetPack.Format,
    ["formatVersion"] = RulesetPack.FormatVersion,
    ["id"] = "dota",
    ["version"] = 7416,
    ["name"] = "Dota 2 " + Patch,
    ["provenance"] = new JsonObject { ["source"] = "https://github.com/odota/dotaconstants", ["commit"] = Commit, ["patch"] = Patch },
    ["heroRules"] = new JsonObject
    {
        ["xpForLevel"] = new JsonArray(xpLevels.Select(x => (JsonNode)JsonValue.Create(x!.GetValue<int>())!).ToArray()),
        ["xpShareRadius"] = 1500,
        ["heroKillXpBase"] = 100,
        ["heroKillXpPerLevel"] = 0,
        ["heroKillXpPercentOfVictim"] = 13,
    },
    ["heroes"] = packHeroes,
};

string json = pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
RulesetContent applied = RulesetPack.Apply(BaseRuleset.Create(), json);   // the pack must load
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, json);
Console.WriteLine($"{output}: {applied.Heroes.Count} heroes, levels 1-{applied.HeroRules!.MaxLevel}, fingerprint {applied.ToDescriptor().Fingerprint}");
