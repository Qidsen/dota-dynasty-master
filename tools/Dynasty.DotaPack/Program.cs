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
Abilities.Result heroAbilities = Abilities.Convert(data.Constants("abilities").AsObject(), data.Constants("hero_abilities").AsObject(), abilityMap, data.GameUnits());

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
        ["itemBuild"] = Builds.For(data, h["id"]!.GetValue<int>(), itemIds, shopData, items.Ids),
    });
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
    },
    ["heroRules"] = new JsonObject
    {
        ["xpForLevel"] = new JsonArray(xpLevels.Select(x => (JsonNode)JsonValue.Create(x!.GetValue<int>())!).ToArray()),
        ["xpShareRadius"] = 1500,
        ["heroKillXpBase"] = 100,
        ["heroKillXpPerLevel"] = 0,
        ["heroKillXpPercentOfVictim"] = 13,
    },
    ["heroes"] = packHeroes,
    ["unitTypes"] = new JsonArray(items.UnitTypes.Concat(heroAbilities.UnitTypes).Select(u => u!.DeepClone()).ToArray()),
    ["abilities"] = new JsonArray(items.Abilities.Concat(heroAbilities.Abilities).Select(a => a!.DeepClone()).ToArray()),
    ["items"] = items.Items,
};

string json = pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
RulesetContent applied = RulesetPack.Apply(BaseRuleset.Create(), json);   // the pack must load
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, json);
string report = Path.ChangeExtension(output, ".coverage.txt");
File.WriteAllLines(report, new[]
{
    "Dota pack " + Patch + ": what is not carried over yet (docs/DOTA_PACK.md)", "",
    $"hero abilities: {heroAbilities.Mapped} of {heroAbilities.Total} mapped", "",
}.Concat(heroAbilities.Coverage).Append("").Concat(items.Coverage));
Console.WriteLine($"{output}: {applied.Heroes.Count} heroes, abilities {heroAbilities.Mapped}/{heroAbilities.Total}, {applied.Items.Count} items ({items.Coverage.Count} with gaps, {report}), " +
                  $"levels 1-{applied.HeroRules!.MaxLevel}, fingerprint {applied.ToDescriptor().Fingerprint}");
