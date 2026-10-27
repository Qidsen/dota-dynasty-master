using System.Globalization;
using System.Text.Json.Nodes;

/// <summary>
/// Talents, Aghanim's Scepter and Aghanim's Shard (docs/DOTA_PACK.md, wave 3). dotaconstants only has the
/// talents' names, so the values come from the game's own files at the pinned build: a hero file links an
/// ability value to a talent, "special_bonus_scepter" or "special_bonus_shard" with an operation, and
/// npc_abilities.txt holds the generic talents' values. The forms of the operations were checked against the
/// talents' texts in dotaconstants: "+n", "-n" and a bare "n" add ("+35" = "+35 Mist Coil Damage", Medusa's "1" =
/// "+1 Volley"), "=n" sets ("=25" on 0 = "+25% Pure Damage Lifesteal"), "xn" multiplies ("x3" = "3x Treant HP"),
/// "n%" raises by a percent ("50%" = "+50% Serpent Wards Max HP"); several values are per ability level.
/// Facets were removed in 7.41: "special_bonus_facet_*" links are leftovers and ignored.
/// </summary>
static class Talents
{
    /// <summary>Hero levels of the four talent tiers (dotaconstants numbers them 1–4).</summary>
    public static readonly int[] TierLevels = { 10, 15, 20, 25 };

    /// <summary>Generic talent (name without its number) → the modifier fields its value goes to.</summary>
    private static readonly Dictionary<string, string[]> GenericFields = new()
    {
        ["special_bonus_attack_damage"] = new[] { "damage" },
        ["special_bonus_attack_base_damage"] = new[] { "damage" },   // base damage taken as bonus damage (simplification)
        ["special_bonus_attack_speed"] = new[] { "attackSpeed" },
        ["special_bonus_hp"] = new[] { "maxHealth" },
        ["special_bonus_mp"] = new[] { "maxMana" },
        ["special_bonus_hp_regen"] = new[] { "healthRegen" },
        ["special_bonus_mp_regen"] = new[] { "manaRegen" },
        ["special_bonus_strength"] = new[] { "strength" },
        ["special_bonus_agility"] = new[] { "agility" },
        ["special_bonus_intelligence"] = new[] { "intelligence" },
        ["special_bonus_all_stats"] = new[] { "strength", "agility", "intelligence" },
        ["special_bonus_armor"] = new[] { "armor" },
        ["special_bonus_magic_resistance"] = new[] { "magicResist" },
        ["special_bonus_movement_speed"] = new[] { "moveSpeed" },
        ["special_bonus_movement_speed_percentage"] = new[] { "moveSpeedPercent" },
        ["special_bonus_attack_range"] = new[] { "attackRange" },
        ["special_bonus_lifesteal"] = new[] { "lifesteal" },
        ["special_bonus_evasion"] = new[] { "evasion" },
    };

    /// <summary>Engine-level ability fields in the game's values → dotaconstants' keys.</summary>
    private static readonly Dictionary<string, string> SpecialKeys = new()
    {
        ["AbilityCastRange"] = "abilitycastrange",
        ["AbilityCastPoint"] = "abilitycastpoint",
        ["AbilityDuration"] = "abilityduration",
        ["AbilityChannelTime"] = "abilitychanneltime",
        ["AbilityCharges"] = "abilitycharges",
        ["AbilityChargeRestoreTime"] = "abilitychargerestoretime",
        ["AbilityDamage"] = "abilitydamage",
    };

    /// <summary>One ability value's links: its base value in the game file and the upgrades' operations, in file order.</summary>
    public sealed class Link
    {
        public string? Base { get; init; }
        public List<(string Upgrade, string Operation)> Operations { get; } = new();
    }

    /// <summary>The links of every ability in a hero file: ability → value key → link.</summary>
    public static Dictionary<string, Dictionary<string, Link>> Links(JsonObject heroFile)
    {
        var result = new Dictionary<string, Dictionary<string, Link>>(StringComparer.Ordinal);
        foreach (var (abilityKey, ability) in Blocks(heroFile))
        {
            if (ability["AbilityValues"] is not JsonObject values)
                continue;
            var links = new Dictionary<string, Link>(StringComparer.Ordinal);
            foreach (var (valueKey, node) in values)
            {
                if (node is not JsonObject block)
                    continue;
                var link = new Link { Base = block["value"] is JsonValue v ? v.GetValue<string>() : null };
                foreach (var (key, op) in block)
                {
                    string? upgrade = Upgrade(key);
                    if (upgrade != null && op is JsonValue text)
                        link.Operations.Add((upgrade, text.GetValue<string>()));
                }

                if (link.Operations.Count > 0)
                    links[valueKey] = link;
            }

            if (links.Count > 0)
                result[abilityKey] = links;
        }

        return result;
    }

    /// <summary>The upgrade a link key stands for: a talent, "scepter" or "shard"; null for anything else (facets included).</summary>
    private static string? Upgrade(string key) =>
        key == "special_bonus_scepter" ? "scepter"
        : key == "special_bonus_shard" ? "shard"
        : key.StartsWith("special_bonus_facet", StringComparison.Ordinal) ? null
        : key.StartsWith("special_bonus_", StringComparison.Ordinal) ? key
        : null;

    /// <summary>Every block that has ability values, at any depth (abilities sit inside the hero block).</summary>
    private static IEnumerable<(string Key, JsonObject Block)> Blocks(JsonObject node)
    {
        foreach (var (key, child) in node)
        {
            if (child is not JsonObject block)
                continue;
            if (block.ContainsKey("AbilityValues"))
                yield return (key, block);
            else
                foreach (var nested in Blocks(block))
                    yield return nested;
        }
    }

    /// <summary>
    /// The ability's data as the upgrades make it: each linked value of dotaconstants' data (cooldown, mana cost,
    /// attributes) after the operations of the upgrades in <paramref name="upgrades"/>, in the game file's order.
    /// </summary>
    public static JsonObject Apply(JsonObject source, Dictionary<string, Link> links, ICollection<string> upgrades)
    {
        var result = source.DeepClone().AsObject();
        var attributes = result["attrib"] as JsonArray ?? new JsonArray();
        result["attrib"] = attributes;
        foreach (var (valueKey, link) in links)
        {
            var operations = link.Operations.Where(o => upgrades.Contains(o.Upgrade)).ToList();
            if (operations.Count == 0)
                continue;

            string field = valueKey == "AbilityCooldown" ? "cd" : valueKey == "AbilityManaCost" ? "mc" : "";
            string key = SpecialKeys.GetValueOrDefault(valueKey, valueKey);
            JsonObject? attribute = field.Length > 0 ? null : attributes.OfType<JsonObject>().FirstOrDefault(a => a["key"]!.GetValue<string>() == key);
            JsonNode? current = field.Length > 0 ? result[field] : attribute?["value"];
            // dotaconstants' value, or the game file's own when dotaconstants has none or a non-number ("undefined").
            decimal[]? data = null;
            if (current != null && Abilities.HasNumbers(current))
            {
                try
                {
                    data = Abilities.NumbersOf(current);
                }
                catch (FormatException)
                {
                }
            }

            decimal[] values = data ?? (link.Base != null ? Split(link.Base).Select(Number).ToArray() : new decimal[0]);
            if (values.Length == 0)
                values = new[] { 0m };
            foreach (var (_, operation) in operations)
            {
                if (Split(operation).Length > 0)
                    values = Operate(values, Split(operation));
            }

            var list = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(Math.Round(v, 4))!).ToArray());
            if (field.Length > 0)
                result[field] = list;
            else if (attribute != null)
                attribute["value"] = list;
            else
                attributes.Add(new JsonObject { ["key"] = key, ["value"] = list });
        }

        return result;
    }

    private static string[] Split(string text) => text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private static decimal Number(string text) => decimal.Parse(text.TrimStart('+').TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Applies an operation per level (an operation with fewer values repeats its last one).</summary>
    private static decimal[] Operate(decimal[] values, string[] operations)
    {
        int levels = Math.Max(values.Length, operations.Length);
        var result = new decimal[levels];
        for (int i = 0; i < levels; i++)
        {
            decimal value = values[Math.Min(i, values.Length - 1)];
            string op = operations[Math.Min(i, operations.Length - 1)];
            result[i] = op.StartsWith('=') ? Number(op[1..])
                : op.StartsWith('x') || op.StartsWith('X') ? value * Number(op[1..])
                : op.EndsWith('%') ? value * (1 + Number(op) / 100)
                : value + Number(op);
        }

        return result;
    }

    /// <summary>
    /// The pack talents of a hero, in tiers: the talent taken first in a tier is the one picked more often in the
    /// statistics (<paramref name="picks"/>), then the one Valve's bot build takes, then dotaconstants' order.
    /// A generic talent gets its stat bonus from npc_abilities.txt; others work through ability variants.
    /// </summary>
    public static JsonArray For(string hero, JsonArray? dotaTalents, JsonObject abilitiesData, JsonObject gameAbilities, JsonObject? heroFile,
        IReadOnlyDictionary<string, int> picks, List<string> coverage)
    {
        var result = new JsonArray();
        if (dotaTalents == null)
            return result;
        var botBuild = new HashSet<string>(StringComparer.Ordinal);
        if (heroFile != null && FindBlock(heroFile, "Build") is JsonObject build)
            foreach (var (_, name) in build)
                if (name is JsonValue v)
                    botBuild.Add(v.GetValue<string>());

        var unsupported = new List<string>();
        // Only the eight talents of the four tiers: dotaconstants also lists a few other abilities after them (Rubick's hidden
        // slots, Ringmaster's souvenirs) as "levels" 5–7.
        foreach (var tier in dotaTalents.OfType<JsonObject>()
                     .Where(t => t["name"]!.GetValue<string>().StartsWith("special_bonus_", StringComparison.Ordinal) && t["level"]!.GetValue<int>() is >= 1 and <= 4)
                     .GroupBy(t => t["level"]!.GetValue<int>()).OrderBy(g => g.Key))
        {
            int level = TierLevels[tier.Key - 1];
            var talents = tier.Select((t, index) => (Name: t["name"]!.GetValue<string>(), Index: index))
                .OrderByDescending(t => picks.GetValueOrDefault(t.Name))
                .ThenByDescending(t => botBuild.Contains(t.Name))
                .ThenBy(t => t.Index)
                .Select(t => t.Name);
            foreach (string name in talents)
            {
                var talent = new JsonObject
                {
                    ["id"] = name,
                    ["name"] = abilitiesData[name]?["dname"]?.GetValue<string>() ?? name,
                    ["level"] = level,
                };
                if (!name.Contains("_unique_"))
                {
                    JsonObject? bonus = Bonus(name, gameAbilities);
                    if (bonus != null)
                        talent["bonus"] = bonus;
                    else
                        unsupported.Add(name + " (" + talent["name"]!.GetValue<string>() + ")");
                }

                result.Add(talent);
            }
        }

        if (unsupported.Count > 0)
            coverage.Add($"hero {hero}: generic talents not carried over: {string.Join(", ", unsupported)}");
        return result;
    }

    private static JsonObject? FindBlock(JsonObject node, string key)
    {
        foreach (var (k, child) in node)
        {
            if (child is not JsonObject block)
                continue;
            if (k == key)
                return block;
            if (FindBlock(block, key) is JsonObject found)
                return found;
        }

        return null;
    }

    /// <summary>A generic talent's stat bonus: the stat from its name, the value from the game's npc_abilities.txt.</summary>
    private static JsonObject? Bonus(string name, JsonObject gameAbilities)
    {
        string stat = System.Text.RegularExpressions.Regex.Replace(name, @"_\d+$", "");
        if (!GenericFields.TryGetValue(stat, out string[]? fields))
            return null;
        JsonNode? value = gameAbilities[name]?["AbilityValues"]?["value"];
        string? text = value is JsonObject block ? block["value"]?.GetValue<string>() : (value as JsonValue)?.GetValue<string>();
        if (text == null)
            return null;
        var bonus = new JsonObject { ["key"] = "talent_" + name };
        foreach (string field in fields)
            bonus[field] = JsonValue.Create(Number(Split(text)[0]));
        return bonus;
    }
}
