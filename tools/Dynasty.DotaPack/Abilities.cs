using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Hero abilities of dotaconstants as pack abilities, through a hand-written map (abilities.json next to the
/// generator): the map gives each ability's structure in the engine's effect tree and refers to the data's
/// values by key (<c>"$damage"</c>, <c>"-$slow"</c>), so the numbers come from the data and follow patches.
/// Cooldown, mana cost, cast range and cast point come from the data unless the map overrides them. An
/// ability the map does not know is left out and listed in the coverage report, like a simplified one.
/// </summary>
static class Abilities
{
    private static readonly int[] BasicLevels = { 1, 3, 5, 7 };
    private static readonly int[] UltimateLevels = { 6, 12, 18 };

    public sealed record Result(JsonArray Abilities, JsonArray UnitTypes, Dictionary<string, JsonArray> HeroAbilities, List<string> Coverage, int Mapped, int Total);

    public static Result Convert(JsonObject data, JsonObject heroAbilities, JsonObject map, Dictionary<string, Dictionary<string, string>> gameUnits)
    {
        foreach (var (key, entry) in map)
            if (entry is JsonObject template)
                RejectHandTypedNumbers(key, template, "");
        var abilities = new JsonArray();
        var unitTypes = new JsonArray();
        var byHero = new Dictionary<string, JsonArray>();
        var coverage = new List<string>();
        int mapped = 0, total = 0;
        foreach (var (heroKey, node) in heroAbilities.OrderBy(h => h.Key, StringComparer.Ordinal))
        {
            string hero = heroKey.Replace("npc_dota_hero_", "");
            var visible = node!["abilities"]!.AsArray().Select(a => a!.GetValue<string>())
                .Where(a => a != "generic_hidden" && data[a] is JsonObject d && !Behaviors(d).Contains("Hidden") && d["is_innate"] == null)
                .ToList();
            var innates = node["abilities"]!.AsArray().Select(a => a!.GetValue<string>())
                .Where(a => data[a] is JsonObject d && d["is_innate"] is JsonValue innate && innate.GetValue<bool>())
                .ToList();
            var list = new JsonArray();
            foreach (string key in innates)
            {
                total++;
                if (map[key] is not JsonObject entry)
                    continue;   // innates are listed in the report only when mapped or missing as such below
                JsonObject ability = Build(key, data[key]!.AsObject(), entry, ultimate: false);
                ability["innate"] = true;
                ability["requiredLevels"] = new JsonArray(1);
                abilities.Add(ability);
                list.Add(key);
                mapped++;
                if (entry["status"]?.GetValue<string>() == "simplified")
                    coverage.Add($"hero {hero}: simplified: {key} ({entry["note"]?.GetValue<string>()})");
            }

            var missing = new List<string>();
            var simplified = new List<string>();
            for (int slot = 0; slot < visible.Count; slot++)
            {
                string key = visible[slot];
                total++;
                if (map[key] is not JsonObject entry)
                {
                    missing.Add(key);
                    continue;
                }

                bool ultimate = slot == visible.Count - 1 && visible.Count >= 4;
                abilities.Add(Build(key, data[key]!.AsObject(), entry, ultimate));
                foreach (JsonNode? unit in entry["unitTypes"] as JsonArray ?? new JsonArray())
                    unitTypes.Add(ResolveUnit(unit!.AsObject(), data[key]!.AsObject(), gameUnits));
                list.Add(key);
                mapped++;
                if (entry["status"]?.GetValue<string>() == "simplified")
                    simplified.Add(key + " (" + entry["note"]?.GetValue<string>() + ")");
            }

            byHero[hero] = list;
            if (missing.Count > 0)
                coverage.Add($"hero {hero}: abilities missing: {string.Join(", ", missing)}");
            if (simplified.Count > 0)
                coverage.Add($"hero {hero}: simplified: {string.Join("; ", simplified)}");
        }

        return new Result(abilities, unitTypes, byHero, coverage, mapped, total);
    }

    /// <summary>
    /// The map refers to the data for every number (docs/DOTA_PACK.md). Allowed literals are the engine's own
    /// conventions: an aura's modifier lasts 1 s and is refreshed by the aura, 20000 and more means "global",
    /// and summon counts.
    /// </summary>
    private static void RejectHandTypedNumbers(string key, JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (field, child) in obj)
                    RejectHandTypedNumbers(key, child, path + "." + field);
                break;
            case JsonArray list:
                for (int i = 0; i < list.Count; i++)
                    RejectHandTypedNumbers(key, list[i], path + "[" + i + "]");
                break;
            case JsonValue v when v.TryGetValue(out decimal number):
                bool allowed = (path.EndsWith(".aura.modifier.duration") && number == 1) || number >= 20000 || path.Contains(".counts[");
                if (!allowed)
                    throw new InvalidDataException($"abilities.json: {key}{path} = {number} is typed by hand; refer to the data (\"$key\")");
                break;
        }
    }

    private static IReadOnlyList<string> Behaviors(JsonObject ability) => ability["behavior"] switch
    {
        JsonArray list => list.Select(b => b!.GetValue<string>()).ToList(),
        JsonValue one when one.TryGetValue(out string? b) => new[] { b },
        _ => Array.Empty<string>(),
    };

    private static Dictionary<string, JsonNode?> ValuesOf(JsonObject source)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (JsonNode? attribute in source["attrib"]?.AsArray() ?? new JsonArray())
            values[attribute!["key"]!.GetValue<string>()] = attribute["value"];
        if (source["dmg"] is JsonNode dmg)
            values["dmg"] = dmg;   // some abilities keep their damage outside the attributes (Storm Hammer)
        return values;
    }

    /// <summary>
    /// A unit type a summon of this ability creates. <c>"gameUnit"</c> names the unit in the game's npc_units.txt,
    /// which gives the base stats; the map's fields (usually the ability's values) override them. Per-level values
    /// take the top level (simplification).
    /// </summary>
    private static JsonObject ResolveUnit(JsonObject template, JsonObject source, Dictionary<string, Dictionary<string, string>> gameUnits)
    {
        Dictionary<string, JsonNode?> values = ValuesOf(source);
        var unit = new JsonObject();
        if (template["gameUnit"]?.GetValue<string>() is string name)
        {
            if (!gameUnits.TryGetValue(name, out Dictionary<string, string>? game))
                throw new InvalidDataException("no game unit \"" + name + "\"");
            unit["id"] = template["id"]!.DeepClone();
            foreach (var (field, value) in GameUnitStats(game))
                unit[field] = value;
        }

        foreach (var (field, node) in template)
        {
            if (field == "gameUnit")
                continue;
            unit[field] = node is JsonValue v && v.TryGetValue(out string? s) && IsReference(s) ? Scalar(node, values, 1, fallback: null)
                : node is JsonObject nested ? ResolveAt(nested, values, int.MaxValue)   // the bounty: references at the top level
                : node?.DeepClone();
        }

        return unit;
    }

    /// <summary>A game unit's stats in the pack's unit fields; the attack only for units that attack, the bounty when it has one.</summary>
    private static IEnumerable<(string Field, JsonNode Value)> GameUnitStats(Dictionary<string, string> game)
    {
        decimal? Number(string key) => game.TryGetValue(key, out string? text) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;
        IEnumerable<(string, string)> fields = new[]
        {
            ("health", "StatusHealth"), ("armor", "ArmorPhysical"), ("magicResist", "MagicalResistance"), ("moveSpeed", "MovementSpeed"),
            ("visionRange", "VisionDaytimeRange"), ("nightVision", "VisionNighttimeRange"),
        };
        if (game.GetValueOrDefault("AttackCapabilities") != "DOTA_UNIT_CAP_NO_ATTACK")
            fields = fields.Concat(new[]
            {
                ("attackDamageMin", "AttackDamageMin"), ("attackDamageMax", "AttackDamageMax"), ("baseAttackTime", "AttackRate"),
                ("attackPoint", "AttackAnimationPoint"), ("attackRange", "AttackRange"), ("acquisitionRange", "AttackAcquisitionRange"),
                ("projectileSpeed", "ProjectileSpeed"),
            });
        foreach (var (field, key) in fields)
            if (Number(key) is decimal value && (value != 0 || field is "health" or "armor" or "moveSpeed" or "visionRange"))
                yield return (field, JsonValue.Create(value)!);
        if (game.GetValueOrDefault("CombatClassAttack") is "DOTA_COMBAT_CLASS_ATTACK_BASIC" or "DOTA_COMBAT_CLASS_ATTACK_PIERCE")
            yield return ("attackClass", game["CombatClassAttack"] == "DOTA_COMBAT_CLASS_ATTACK_BASIC" ? "basic" : "piercing");
        if (Number("BountyGoldMin") is decimal goldMin && Number("BountyGoldMax") is decimal goldMax && Number("BountyXP") is decimal xp && goldMax + xp > 0)
            yield return ("bounty", new JsonObject { ["goldMin"] = goldMin, ["goldMax"] = goldMax, ["xp"] = xp });
    }

    private static JsonObject Build(string key, JsonObject source, JsonObject entry, bool ultimate)
    {
        Dictionary<string, JsonNode?> values = ValuesOf(source);

        int levels = new[] { Count(source["cd"]), Count(source["mc"]) }.Concat(values.Values.Select(Count)).Max();
        if (levels <= 1)
            levels = Count(source["cd"]) == 1 && Count(source["mc"]) <= 1 && values.Values.All(v => Count(v) <= 1) ? (ultimate ? 3 : 4) : levels;
        int[] required = (ultimate ? UltimateLevels : BasicLevels).Take(levels).ToArray();
        levels = required.Length;

        var ability = new JsonObject
        {
            ["id"] = key,
            ["name"] = source["dname"]?.GetValue<string>() ?? key,
            ["targeting"] = entry["targeting"]!.DeepClone(),
            ["targetTeam"] = entry["targetTeam"]?.DeepClone() ?? "enemy",
            ["castRange"] = Scalar(entry["castRange"] ?? JsonValue.Create("$abilitycastrange"), values, levels, fallback: 0),
            ["castPoint"] = Scalar(entry["castPoint"] ?? JsonValue.Create("$abilitycastpoint"), values, levels, fallback: 0),
            ["requiredLevels"] = new JsonArray(required.Select(l => (JsonNode)l).ToArray()),
        };
        string targeting = entry["targeting"]!.GetValue<string>();
        if (targeting != "passive")
        {
            ability["cooldown"] = PerLevel(entry["cooldown"] ?? source["cd"], values, levels, fallback: 0);
            ability["manaCost"] = PerLevel(entry["manaCost"] ?? source["mc"], values, levels, fallback: 0);
        }
        else
        {
            ability["cooldown"] = new JsonArray(0);
        }

        if (entry["targetsStructures"] is JsonNode structures)
            ability["targetsStructures"] = structures.DeepClone();
        ability["effects"] = new JsonArray((entry["effects"] as JsonArray ?? new JsonArray()).Select(e => (JsonNode)ResolveEffect(e!.AsObject(), values, levels)).ToArray());
        if (entry["passive"] is JsonObject passive)
            ability["passive"] = Modifiers(passive, values, levels);
        return ability;
    }

    private static JsonObject ResolveEffect(JsonObject effect, Dictionary<string, JsonNode?> values, int levels)
    {
        var resolved = new JsonObject();
        foreach (var (field, node) in effect)
        {
            resolved[field] = field switch
            {
                "amounts" or "counts" => PerLevel(node, values, levels, fallback: null),
                "modifiers" => node is JsonObject single ? Modifiers(single, values, levels) : node!.DeepClone(),
                "effects" => new JsonArray(node!.AsArray().Select(e => (JsonNode)ResolveEffect(e!.AsObject(), values, levels)).ToArray()),
                _ => node is JsonValue v && v.TryGetValue(out string? s) && IsReference(s) ? Scalar(node, values, levels, fallback: null) : node?.DeepClone(),
            };
        }

        return resolved;
    }

    /// <summary>A modifier template made concrete for each level (references to per-level values pick that level's value).</summary>
    private static JsonArray Modifiers(JsonObject template, Dictionary<string, JsonNode?> values, int levels)
    {
        var result = new JsonArray();
        for (int level = 0; level < levels; level++)
            result.Add(ResolveAt(template, values, level));
        return result;
    }

    /// <summary>
    /// A template made concrete for one level: references pick that level's value; inside, "amounts" and
    /// "counts" become one-element lists and a single "modifiers" template becomes a one-element list
    /// (on-hit effects and auras live inside a modifier, which already is per level).
    /// </summary>
    private static JsonNode? ResolveAt(JsonNode? node, Dictionary<string, JsonNode?> values, int level, string field = "")
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue(out string? s) && IsReference(s):
                decimal value = At(Resolve(s, values), level);
                return field is "amounts" or "counts" ? new JsonArray(JsonValue.Create(value)) : JsonValue.Create(value);
            case JsonObject obj when field == "modifiers":
                return new JsonArray(ResolveAt(obj, values, level));
            case JsonObject obj:
                var resolved = new JsonObject();
                foreach (var (key, child) in obj)
                    resolved[key] = ResolveAt(child, values, level, key);
                return resolved;
            case JsonArray list:
                return new JsonArray(list.Select(item => ResolveAt(item, values, level, field)).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    private static bool IsReference(string s) => s.StartsWith("$") || s.StartsWith("-$");

    private static JsonArray PerLevel(JsonNode? node, Dictionary<string, JsonNode?> values, int levels, decimal? fallback)
    {
        decimal[] resolved = node is JsonValue v && v.TryGetValue(out string? s) && IsReference(s) ? Resolve(s, values)
            : node == null || (node is JsonValue b && b.TryGetValue(out bool _)) ? (fallback is decimal f ? new[] { f } : throw new InvalidDataException("missing per-level value"))
            : Numbers(node);
        return new JsonArray(Enumerable.Range(0, levels).Select(i => (JsonNode)JsonValue.Create(At(resolved, i))!).ToArray());
    }

    private static JsonNode Scalar(JsonNode? node, Dictionary<string, JsonNode?> values, int levels, decimal? fallback)
    {
        decimal[] resolved = node is JsonValue v && v.TryGetValue(out string? s) && IsReference(s)
            ? (values.ContainsKey(s.TrimStart('-', '$')) || fallback == null ? Resolve(s, values) : new[] { fallback.Value })
            : Numbers(node);
        return JsonValue.Create(resolved[^1])!;   // per-level radii and ranges: the top level's value (simplification)
    }

    private static decimal At(decimal[] values, int level) => values[Math.Min(level, values.Length - 1)];

    /// <summary>"$key", "-$key", or a product or quotient such as "$attack_damage*4" or "$a*$b/$c" (per level, element by element).</summary>
    private static decimal[] Resolve(string reference, Dictionary<string, JsonNode?> values)
    {
        bool negate = reference.StartsWith("-");
        decimal[] result = { negate ? -1 : 1 };
        string expression = reference.TrimStart('-');
        int start = 0;
        while (start < expression.Length)
        {
            int end = expression.IndexOfAny(new[] { '*', '/' }, start);
            if (end < 0)
                end = expression.Length;
            bool divide = start > 0 && expression[start - 1] == '/';
            string factor = expression.Substring(start, end - start);
            start = end + 1;
            decimal[] part;
            if (factor.StartsWith("$"))
            {
                string key = factor.Substring(1);
                if (!values.TryGetValue(key, out JsonNode? node))
                    throw new InvalidDataException("no data value \"" + key + "\"");
                part = Numbers(node);
            }
            else
            {
                part = new[] { decimal.Parse(factor, CultureInfo.InvariantCulture) };
            }

            int n = Math.Max(result.Length, part.Length);
            // Quotients are rounded to 4 decimals: the pack reads numbers exactly and finer digits mean nothing in fixed point.
            result = Enumerable.Range(0, n).Select(i => divide ? Math.Round(At(result, i) / At(part, i), 4) : At(result, i) * At(part, i)).ToArray();
        }

        return result;
    }

    private static decimal[] Numbers(JsonNode? node) => node switch
    {
        JsonArray list => list.Select(n => Parse(n!)).ToArray(),
        JsonValue v when v.TryGetValue(out string? s) && s.Contains('/') => s.Split('/').Select(p => Parse(JsonValue.Create(p.Trim())!)).ToArray(),
        JsonNode one => new[] { Parse(one) },
        _ => throw new InvalidDataException("missing value"),
    };

    private static decimal Parse(JsonNode node) => node is JsonValue v && v.TryGetValue(out string? s)
        ? decimal.Parse(s.Trim().TrimEnd('%'), CultureInfo.InvariantCulture)
        : node.GetValue<decimal>();

    private static int Count(JsonNode? node) => node switch
    {
        JsonArray list => list.Count,
        JsonValue v when v.TryGetValue(out string? s) && s.Contains('/') => s.Split('/').Length,
        JsonValue v when v.TryGetValue(out bool _) => 0,
        null => 0,
        _ => 1,
    };
}
