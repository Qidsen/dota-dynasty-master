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

    /// <param name="links">The hero file's talent, scepter and shard links per hero (Talents.Links), for the abilities' variants.</param>
    public static Result Convert(JsonObject data, JsonObject heroAbilities, JsonObject map, Dictionary<string, Dictionary<string, string>> gameUnits,
        Func<string, Dictionary<string, Dictionary<string, Talents.Link>>?> links)
    {
        foreach (var (key, entry) in map)
            if (entry is JsonObject template)
                RejectHandTypedNumbers(key, template, "");
        var abilities = new JsonArray();
        var unitTypes = new JsonArray();
        var byHero = new Dictionary<string, JsonArray>();
        var coverage = new List<string>();
        var upgraded = new HashSet<string>(StringComparer.Ordinal);   // upgrades that changed at least one carried-over ability
        var rounded = new List<string>();                              // upgraded values rounded to whole numbers
        static HashSet<string> heroTalents(JsonNode? hero) =>
            new HashSet<string>((hero?["talents"] as JsonArray)?.Select(t => t!["name"]!.GetValue<string>()).Where(t => t.StartsWith("special_bonus_", StringComparison.Ordinal))
                                ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
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
                JsonObject ability = Build(key, data[key]!.AsObject(), entry, ultimate: false, innate: true);
                AddVariants(hero, ability, key, data[key]!.AsObject(), entry, false, true, links(hero), heroTalents(node), upgraded, rounded);
                ability["innate"] = true;
                abilities.Add(ability);
                foreach (JsonNode? unit in entry["unitTypes"] as JsonArray ?? new JsonArray())
                    unitTypes.Add(ResolveUnit(unit!.AsObject(), data[key]!.AsObject(), gameUnits));
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
                JsonObject built = Build(key, data[key]!.AsObject(), entry, ultimate);
                AddVariants(hero, built, key, data[key]!.AsObject(), entry, ultimate, false, links(hero), heroTalents(node), upgraded, rounded);
                abilities.Add(built);
                foreach (JsonNode? unit in entry["unitTypes"] as JsonArray ?? new JsonArray())
                    unitTypes.Add(ResolveUnit(unit!.AsObject(), data[key]!.AsObject(), gameUnits));
                list.Add(key);
                mapped++;
                if (entry["status"]?.GetValue<string>() == "simplified")
                    simplified.Add(key + " (" + entry["note"]?.GetValue<string>() + ")");
            }

            byHero[hero] = list;
            var talentsNotCarried = heroTalents(node).Where(t => t.Contains("_unique_") && !upgraded.Contains(t)).ToList();
            if (talentsNotCarried.Count > 0)
                coverage.Add($"hero {hero}: talents not carried over: {string.Join(", ", talentsNotCarried.Select(t => t + " (" + data[t]?["dname"]?.GetValue<string>() + ")"))}");
            foreach (string upgrade in new[] { "scepter", "shard" })
            {
                if (!upgraded.Contains(hero + ":" + upgrade))
                    coverage.Add($"hero {hero}: {upgrade} not carried over");
            }

            if (missing.Count > 0)
                coverage.Add($"hero {hero}: abilities missing: {string.Join(", ", missing)}");
            if (simplified.Count > 0)
                coverage.Add($"hero {hero}: simplified: {string.Join("; ", simplified)}");
        }

        if (rounded.Count > 0)
            coverage.Add("upgraded values rounded to whole numbers (B-135): " + string.Join("; ", rounded));
        return new Result(abilities, unitTypes, byHero, coverage, mapped, total);
    }

    /// <summary>
    /// The map refers to the data for every number (docs/DOTA_PACK.md). Allowed literals are the engine's own
    /// conventions: an aura's modifier (and a modifier of a zone without a period) lasts 1 s and is refreshed, 20000 and more means "global",
    /// summon counts, and a critical strike certain to land (100) when a cooldown is what limits it (Mortal Strike).
    /// </summary>
    private static void RejectHandTypedNumbers(string key, JsonNode? node, string path, bool inContinuousZone = false)
    {
        switch (node)
        {
            case JsonObject obj:
                // A zone without a period refreshes its modifiers like an aura: they last 1 s by the same convention.
                bool continuous = inContinuousZone || (obj["kind"]?.GetValue<string>() == "zone" && obj["period"] == null);
                foreach (var (field, child) in obj)
                    RejectHandTypedNumbers(key, child, path + "." + field, continuous);
                break;
            case JsonArray list:
                for (int i = 0; i < list.Count; i++)
                    RejectHandTypedNumbers(key, list[i], path + "[" + i + "]", inContinuousZone);
                break;
            case JsonValue text when text.TryGetValue(out string? s) && decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal written):
                throw new InvalidDataException($"abilities.json: {key}{path} = \"{written}\" is a number typed by hand; refer to the data (\"$key\")");
            case JsonValue v when v.TryGetValue(out decimal number):
                bool allowed = ((path.EndsWith(".aura.modifier.duration") || (inContinuousZone && path.EndsWith(".modifiers.duration"))) && number == 1) ||
                               number >= 20000 || path.Contains(".counts[") || (path.EndsWith(".critChance") && number == 100);
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
        if (source["cd"] is JsonNode cd && !values.ContainsKey("cd"))
            values["cd"] = cd;     // the ability's own cooldown, e.g. a passive's proc cooldown (Mortal Strike)
        if (source["mc"] is JsonNode mc && !values.ContainsKey("mc"))
            values["mc"] = mc;     // the ability's own mana cost, e.g. an autocast attack's cost per proc (Impetus)
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

    /// <summary>A unit of the game's npc_units.txt as a pack unit or structure type with the given id.</summary>
    public static JsonObject GameUnit(string id, string gameUnit, Dictionary<string, Dictionary<string, string>> gameUnits)
    {
        if (!gameUnits.TryGetValue(gameUnit, out Dictionary<string, string>? game))
            throw new InvalidDataException("no game unit \"" + gameUnit + "\"");
        var unit = new JsonObject { ["id"] = id };
        foreach (var (field, value) in GameUnitStats(game))
            unit[field] = value;
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
        // The attack class comes from the unit's innate class ability (creep_irresolute: runty, creep_piercing: piercing,
        // creep_siege: reinforced — siege creeps, towers and buildings), otherwise from CombatClassAttack.
        string[] classAbilities = game.Where(kv => kv.Key.StartsWith("Ability", StringComparison.Ordinal)).Select(kv => kv.Value).ToArray();
        if (classAbilities.Contains("creep_irresolute"))
            yield return ("attackClass", "runty");
        else if (classAbilities.Contains("creep_piercing"))
            yield return ("attackClass", "piercing");
        else if (classAbilities.Contains("creep_siege") && game.GetValueOrDefault("AttackCapabilities") != "DOTA_UNIT_CAP_NO_ATTACK")
            yield return ("attackClass", "reinforced");
        else if (game.GetValueOrDefault("CombatClassAttack") is "DOTA_COMBAT_CLASS_ATTACK_BASIC" or "DOTA_COMBAT_CLASS_ATTACK_PIERCE")
            yield return ("attackClass", game["CombatClassAttack"] == "DOTA_COMBAT_CLASS_ATTACK_BASIC" ? "basic" : "piercing");
        if (Number("BountyGoldMin") is decimal goldMin && Number("BountyGoldMax") is decimal goldMax && Number("BountyXP") is decimal xp && goldMax + xp > 0)
            yield return ("bounty", new JsonObject { ["goldMin"] = goldMin, ["goldMax"] = goldMax, ["xp"] = xp });
    }

    /// <summary>
    /// The ability's variants for its upgrades (talents of the hero, "scepter", "shard"): for every combination of
    /// the upgrades linked to its values (all of them up to five upgrades, else each alone), the ability rebuilt
    /// from the upgraded data. A variant identical to the ability (the upgrade changes nothing carried over) is
    /// left out. <paramref name="upgraded"/> collects the upgrades that changed something (talents by id,
    /// scepter and shard as "hero:scepter").
    /// </summary>
    private static void AddVariants(string hero, JsonObject ability, string key, JsonObject source, JsonObject entry, bool ultimate, bool innate,
        Dictionary<string, Dictionary<string, Talents.Link>>? heroLinks, HashSet<string> heroTalents, HashSet<string> upgraded, List<string> rounded)
    {
        if (heroLinks == null || !heroLinks.TryGetValue(key, out Dictionary<string, Talents.Link>? links))
            return;
        List<string> upgrades = links.Values.SelectMany(l => l.Operations.Select(o => o.Upgrade))
            .Where(u => u is "scepter" or "shard" || heroTalents.Contains(u)).Distinct().OrderBy(u => u, StringComparer.Ordinal).ToList();
        if (upgrades.Count == 0)
            return;
        IEnumerable<List<string>> combinations = upgrades.Count <= 5
            ? Enumerable.Range(1, (1 << upgrades.Count) - 1).Select(mask => upgrades.Where((_, i) => (mask & (1 << i)) != 0).ToList())
            : upgrades.Select(u => new List<string> { u });
        string plain = ability.ToJsonString();
        var variants = new JsonArray();
        foreach (List<string> combination in combinations)
        {
            JsonObject variant = Build(key, Talents.Apply(source, links, combination), entry, ultimate, innate);
            if (variant.ToJsonString() == plain)
                continue;
            RoundWholeFields(variant, "", key + " with " + string.Join(" + ", combination), rounded);
            if (combination.Count == 1)
                upgraded.Add(combination[0] is "scepter" or "shard" ? hero + ":" + combination[0] : combination[0]);
            var withUpgrades = new JsonObject { ["upgrades"] = new JsonArray(combination.Select(u => (JsonNode)JsonValue.Create(u)!).ToArray()) };
            foreach (var (field, value) in variant)
                withUpgrades[field] = value?.DeepClone();
            variants.Add(withUpgrades);
        }

        if (variants.Count > 0)
            ability["variants"] = variants;
    }

    /// <summary>Fields the pack format reads as whole numbers (the engine keeps them as integers).</summary>
    private static readonly HashSet<string> WholeFields = new(StringComparer.Ordinal)
    {
        "amounts", "counts", "castRange", "radius", "speed", "damage", "attackSpeed", "magicResist", "maxHealth", "maxMana", "strength", "agility",
        "intelligence", "moveSpeed", "attackRange", "critMultiplier", "structureDamage", "lifesteal", "cleavePercent", "cleaveRadius", "barrier",
        "attackDamagePercent", "damagePercent", "onHitAfterAttacks", "whenAttackedEvery", "maxStacks", "breakRadius", "extraTargets",
        "extraTargetRadius", "extraTargetDamagePercent", "extraTargetDamageReduction", "extraTargetChance", "attackCharges", "charges", "requiredLevels", "manaCost",
    };

    /// <summary>
    /// An upgraded value can come out fractional in a field the engine keeps whole (Homing Missile with +50%: 112.5
    /// damage): it is rounded to the nearest whole number, and every such case is listed in the coverage report.
    /// </summary>
    private static void RoundWholeFields(JsonNode? node, string field, string where, List<string> rounded)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                    RoundWholeFields(child, key, where, rounded);
                break;
            case JsonArray list:
                for (int i = 0; i < list.Count; i++)
                {
                    if (WholeFields.Contains(field) && list[i] is JsonValue item && item.TryGetValue(out decimal value) && value != Math.Round(value))
                    {
                        rounded.Add($"{where}: {field} {value} → {Math.Round(value, MidpointRounding.AwayFromZero)}");
                        list[i] = JsonValue.Create(Math.Round(value, MidpointRounding.AwayFromZero));
                    }
                    else
                        RoundWholeFields(list[i], field, where, rounded);
                }

                break;
            case JsonValue v when WholeFields.Contains(field) && v.TryGetValue(out decimal number) && number != Math.Round(number):
                rounded.Add($"{where}: {field} {number} → {Math.Round(number, MidpointRounding.AwayFromZero)}");
                node.Parent!.AsObject()[field] = JsonValue.Create(Math.Round(number, MidpointRounding.AwayFromZero));
                break;
        }
    }

    /// <summary>
    /// An item's active from the item map (items.json): an ability "item_&lt;id&gt;" of one level, its numbers from the
    /// item's data; the same rule as for hero abilities (no hand-typed numbers).
    /// </summary>
    public static JsonObject BuildItemActive(string itemId, JsonObject source, JsonObject entry)
    {
        RejectHandTypedNumbers("items.json " + itemId, entry, ".active");
        JsonObject ability = Build("item_" + itemId, source, entry, ultimate: false, innate: true);
        ability["name"] = source["dname"]?.GetValue<string>() ?? itemId;
        return ability;
    }

    /// <summary>An item's passive from the item map: one modifier template made concrete with the item's data.</summary>
    public static JsonObject BuildItemPassive(string itemId, JsonObject source, JsonObject template)
    {
        RejectHandTypedNumbers("items.json " + itemId, template, ".passive");
        return Modifiers(template, ValuesOf(source), 1)[0]!.AsObject();
    }

    /// <summary>
    /// The pack ability. An innate has one level, learned at level 1: per-level values take their first value
    /// (simplification: innates that grow with the hero or another ability stay at their base value).
    /// </summary>
    private static JsonObject Build(string key, JsonObject source, JsonObject entry, bool ultimate, bool innate = false)
    {
        Dictionary<string, JsonNode?> values = ValuesOf(source);

        int levels = new[] { Count(source["cd"]), Count(source["mc"]) }.Concat(values.Values.Select(Count)).Max();
        if (levels <= 1)
            levels = Count(source["cd"]) == 1 && Count(source["mc"]) <= 1 && values.Values.All(v => Count(v) <= 1) ? (ultimate ? 3 : 4) : levels;
        int[] required = innate ? new[] { 1 } : (ultimate ? UltimateLevels : BasicLevels).Take(levels).ToArray();
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

        if (entry["charges"] is JsonNode charges)
        {
            ability["charges"] = PerLevel(charges, values, levels, fallback: null);
            ability["chargeRestore"] = PerLevel(entry["chargeRestore"] ?? throw new InvalidDataException(key + ": charges without chargeRestore"), values, levels, fallback: null);
        }

        if (entry["targetsStructures"] is JsonNode structures)
            ability["targetsStructures"] = structures.DeepClone();
        ability["effects"] = new JsonArray((entry["effects"] as JsonArray ?? new JsonArray()).Select(e => (JsonNode)ResolveEffect(e!.AsObject(), values, levels)).ToArray());
        if (entry["passive"] is JsonObject passive)
            ability["passive"] = Modifiers(passive, values, levels);
        if (entry["autoCastBelowHealth"] is JsonNode threshold)
            ability["autoCastBelowHealth"] = Scalar(threshold, values, levels, fallback: null);
        if (entry["levelsWith"] is JsonNode levelsWith)
            ability["levelsWith"] = levelsWith.DeepClone();
        if (entry["toggle"] is JsonObject toggle)
            ability["toggle"] = Modifiers(toggle, values, levels);
        if (entry["channel"] is JsonObject channel)
        {
            var resolved = new JsonObject
            {
                ["duration"] = PerLevel(channel["duration"] ?? JsonValue.Create("$abilitychanneltime"), values, levels, fallback: null),
                ["period"] = Scalar(channel["period"] ?? JsonValue.Create(1m), values, levels, fallback: null),
                ["pulse"] = new JsonArray((channel["pulse"] as JsonArray ?? new JsonArray()).Select(e => (JsonNode)ResolveEffect(e!.AsObject(), values, levels)).ToArray()),
                ["end"] = new JsonArray((channel["end"] as JsonArray ?? new JsonArray()).Select(e => (JsonNode)ResolveEffect(e!.AsObject(), values, levels)).ToArray()),
            };
            if (channel["whileModifier"] is JsonObject whileModifier)
                resolved["whileModifier"] = ResolveAt(whileModifier, values, 0);   // the same at every level (Phase Shift's invulnerability)
            ability["channel"] = resolved;
        }

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
        if (reference.Contains('+'))
        {
            // A sum of terms, each "$key" or a product (Static Link's break: "$abilitycastrange+$drain_range_buffer").
            decimal[][] terms = reference.Split('+').Select(t => Resolve(t, values)).ToArray();
            int n = terms.Max(t => t.Length);
            return Enumerable.Range(0, n).Select(i => terms.Sum(t => At(t, i))).ToArray();
        }

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

    internal static decimal[] NumbersOf(JsonNode node) => Numbers(node);

    internal static bool HasNumbers(JsonNode node) => Count(node) > 0;

    private static decimal[] Numbers(JsonNode? node) => node switch
    {
        JsonArray list => list.Where(n => !IsBlank(n)).Select(n => Parse(n!)).ToArray(),
        JsonValue v when v.TryGetValue(out string? s) && s.Contains('/') => s.Split('/').Select(p => Parse(JsonValue.Create(p.Trim())!)).ToArray(),
        JsonNode one => new[] { Parse(one) },
        _ => throw new InvalidDataException("missing value"),
    };

    /// <summary>An empty entry in a per-level list (Divine Favor's cooldown ends with one).</summary>
    private static bool IsBlank(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) && s.Trim().Length == 0;

    private static decimal Parse(JsonNode node) => node is JsonValue v && v.TryGetValue(out string? s)
        ? decimal.Parse(s.Trim().TrimEnd('%'), CultureInfo.InvariantCulture)
        : node.GetValue<decimal>();

    private static int Count(JsonNode? node) => node switch
    {
        JsonArray list => list.Count(n => !IsBlank(n)),
        JsonValue v when v.TryGetValue(out string? s) && s.Contains('/') => s.Split('/').Length,
        JsonValue v when v.TryGetValue(out bool _) => 0,
        null => 0,
        _ => 1,
    };
}
