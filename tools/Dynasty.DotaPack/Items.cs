using System.Text.Json.Nodes;

/// <summary>
/// Shop items of dotaconstants as pack items: passive stat bonuses from a key table with their Dota
/// meaning, components, and the actives of the consumables the AI uses (teleport, wards, healing, smoke).
/// Everything not carried over is listed for the coverage report.
/// </summary>
static class Items
{
    /// <summary>dotaconstants passive stat key → pack modifier field. Keys with several meanings are noted.</summary>
    private static readonly Dictionary<string, string> Stats = new()
    {
        ["bonus_damage"] = "damage",
        ["damage"] = "damage",
        ["bonus_damage_base"] = "damage",
        ["bonus_damage_range"] = "damage",          // Phase Boots: the ranged value (simplification, backlog)
        ["bonus_strength"] = "strength",
        ["bonus_str"] = "strength",
        ["strength"] = "strength",
        ["bonus_agility"] = "agility",
        ["agility"] = "agility",
        ["bonus_intellect"] = "intelligence",
        ["bonus_stat"] = "agility",                 // Power Treads' selected attribute: agility (simplification, backlog)
        ["bonus_attack_speed"] = "attackSpeed",
        ["bonus_speed"] = "attackSpeed",
        ["bonus_armor"] = "armor",
        ["armor"] = "armor",
        ["bonus_health"] = "maxHealth",
        ["bonus_hp"] = "maxHealth",
        ["bonus_mana"] = "maxMana",
        ["bonus_health_regen"] = "healthRegen",
        ["bonus_regen"] = "healthRegen",
        ["health_regen"] = "healthRegen",
        ["bonus_mana_regen"] = "manaRegen",
        ["mana_regen"] = "manaRegen",
        ["bonus_movement_speed"] = "moveSpeed",
        ["bonus_movement"] = "moveSpeed",
        ["movement_speed"] = "moveSpeed",
        ["self_movement_speed"] = "moveSpeed",
        ["bonus_movement_speed_ranged"] = "moveSpeed",   // Power Treads: the ranged value (simplification, backlog)
        ["movement_speed_percent_bonus"] = "moveSpeedPercent",
        ["tooltip_resist"] = "magicResist",
        ["bonus_spell_resist"] = "magicResist",
        ["bonus_magical_armor"] = "magicResist",
        ["magic_resistance"] = "magicResist",
        ["lifesteal_percent"] = "lifesteal",
        ["base_attack_range"] = "attackRange",      // ranged heroes only in Dota; given to everyone (backlog)
        ["crit_chance"] = "critChance",
        ["crit_multiplier"] = "critMultiplier",
    };

    private static readonly HashSet<string> AllStats = new() { "bonus_all_stats", "bonus_stats" };

    /// <summary>Keys that describe something other than a passive stat (tooltips, actives): not a gap in coverage.</summary>
    private static readonly HashSet<string> NotStats = new()
    {
        "bonus_damage_melee", "bonus_movement_speed_melee", "melee_attack_range", "tp_cooldown", "tp_channel_time", "use_cooldown",
        "observer_vision_range_tooltip", "observer_duration_minutes_tooltip", "sentry_duration_minutes_tooltip", "true_sight_range", "cooldown_duration",
    };

    public sealed record Result(JsonArray Items, JsonArray Abilities, JsonArray UnitTypes, List<string> Coverage, HashSet<string> Ids);

    public static Result Convert(JsonObject items, HashSet<string> shopNames)
    {
        var shop = items.Where(kv => !kv.Key.StartsWith("recipe_") && kv.Value!["cost"] != null && kv.Value["dname"] is JsonValue name && shopNames.Contains(name.GetValue<string>()))
            .ToDictionary(kv => kv.Key, kv => kv.Value!.AsObject());
        string[] unmatched = shopNames.Where(n => shop.Values.All(i => i["dname"]!.GetValue<string>() != n)).ToArray();
        if (unmatched.Length > 0)
            throw new InvalidDataException("Shop items without data: " + string.Join(", ", unmatched));
        var ids = new HashSet<string>(shop.Keys);
        var packItems = new JsonArray();
        var coverage = new List<string>();
        Consumables consumables = Consumables.For(shop);
        foreach (var (id, item) in shop.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var bonuses = new JsonObject { ["key"] = "item_" + id };
            var missing = new List<string>();
            int allStats = 0;
            foreach (JsonNode? attribute in item["attrib"]?.AsArray() ?? new JsonArray())
            {
                string key = attribute!["key"]!.GetValue<string>();
                bool shown = attribute["display"] != null || key == "crit_chance" || key == "crit_multiplier";
                if (!shown || NotStats.Contains(key))
                    continue;
                JsonNode? value = attribute["value"];
                if (value is not JsonValue || value.GetValue<object>()?.ToString()?.Contains('/') == true)
                {
                    missing.Add(key + " (per level)");
                    continue;
                }

                if (AllStats.Contains(key))
                {
                    allStats += (int)DotaData.Decimal(value);
                    continue;
                }

                if (!Stats.TryGetValue(key, out string? field))
                {
                    missing.Add(key);
                    continue;
                }

                decimal amount = DotaData.Decimal(value) + (bonuses[field] is JsonNode existing ? existing.GetValue<decimal>() : 0);
                bonuses[field] = JsonValue.Create(IsWhole(field) ? Math.Round(amount) : amount);
            }

            foreach (string attribute in new[] { "strength", "agility", "intelligence" })
            {
                if (allStats != 0)
                    bonuses[attribute] = (bonuses[attribute]?.GetValue<decimal>() ?? 0) + allStats;
            }

            var packItem = new JsonObject
            {
                ["id"] = id,
                ["name"] = item["dname"]?.GetValue<string>() ?? id,
                ["cost"] = (int)DotaData.Decimal(item["cost"]),
            };
            string[] components = (item["components"] as JsonArray)?.Select(c => c!.GetValue<string>()).Where(ids.Contains).ToArray() ?? Array.Empty<string>();
            if (components.Length > 0)
                packItem["components"] = new JsonArray(components.Select(c => (JsonNode)c!).ToArray());
            if (bonuses.Count > 1)
                packItem["bonuses"] = bonuses;
            if (consumables.Actives.TryGetValue(id, out (string Ability, int Charges) active))
            {
                packItem["active"] = active.Ability;
                packItem["charges"] = active.Charges;
            }
            else if (item["abilities"] is JsonArray abilities)
            {
                foreach (JsonNode? ability in abilities)
                    missing.Add(ability!["type"]!.GetValue<string>() + " \"" + ability["title"]!.GetValue<string>() + "\"");
            }

            packItems.Add(packItem);
            if (missing.Count > 0)
                coverage.Add($"item {id}: not carried over: {string.Join(", ", missing)}");
        }

        return new Result(packItems, consumables.Abilities, consumables.UnitTypes, coverage, ids);
    }

    private static bool IsWhole(string field) => field is not ("armor" or "healthRegen" or "manaRegen");

    /// <summary>The consumables' actives, with Dota's numbers from the data.</summary>
    private sealed class Consumables
    {
        public Dictionary<string, (string Ability, int Charges)> Actives { get; } = new();
        public JsonArray Abilities { get; } = new();
        public JsonArray UnitTypes { get; } = new();

        public static Consumables For(Dictionary<string, JsonObject> shop)
        {
            var c = new Consumables();
            decimal Attr(string item, string key) => DotaData.Decimal(shop[item]["attrib"]!.AsArray().First(a => a!["key"]!.GetValue<string>() == key)!["value"]);
            decimal Cooldown(string item) => shop[item]["cd"] is JsonValue v && v.TryGetValue(out decimal d) ? d : 1;

            c.Add("tpscroll", 1, new JsonObject
            {
                ["targeting"] = "unit", ["targetTeam"] = "ally", ["castRange"] = 30000, ["castPoint"] = Attr("tpscroll", "tooltip_channel_time"),
                ["cooldown"] = new JsonArray(Cooldown("tpscroll")), ["targetsStructures"] = true,
                ["effects"] = new JsonArray(new JsonObject { ["kind"] = "teleport" }),
            });
            c.UnitTypes.Add(new JsonObject
            {
                ["id"] = "ward_observer_unit", ["health"] = (int)Attr("ward_observer", "health"), ["armor"] = 0, ["moveSpeed"] = 0, ["radius"] = 8,
                ["visionRange"] = (int)Attr("ward_observer", "vision_range_tooltip"), ["invisible"] = true,
            });
            c.Add("ward_observer", 1, Ward("ward_observer_unit", Attr("ward_observer", "lifetime")));
            c.UnitTypes.Add(new JsonObject
            {
                ["id"] = "ward_sentry_unit", ["health"] = (int)Attr("ward_sentry", "health"), ["armor"] = 0, ["moveSpeed"] = 0, ["radius"] = 8,
                ["visionRange"] = 150, ["trueSight"] = (int)Attr("ward_sentry", "true_sight_range"), ["invisible"] = true,
            });
            c.Add("ward_sentry", 1, Ward("ward_sentry_unit", Attr("ward_sentry", "lifetime")));
            c.Add("flask", 1, SelfBuff("item_flask", Attr("flask", "buff_duration"), new JsonObject { ["healPerSecond"] = Attr("flask", "health_regen") }));
            c.Add("tango", 3, SelfBuff("item_tango", Attr("tango", "buff_duration"), new JsonObject { ["healPerSecond"] = Attr("tango", "health_regen") }));
            c.Add("clarity", 1, SelfBuff("item_clarity", Attr("clarity", "buff_duration"), new JsonObject { ["manaRegen"] = Attr("clarity", "mana_regen") }));
            c.Add("faerie_fire", 1, new JsonObject
            {
                ["targeting"] = "noTarget", ["targetTeam"] = "ally", ["castRange"] = 0, ["castPoint"] = 0, ["cooldown"] = new JsonArray(Cooldown("faerie_fire")),
                ["effects"] = new JsonArray(new JsonObject { ["kind"] = "heal", ["amounts"] = new JsonArray((int)Attr("faerie_fire", "hp_restore")) }),
            });
            c.Add("smoke_of_deceit", 1, new JsonObject
            {
                ["targeting"] = "noTarget", ["targetTeam"] = "ally", ["castRange"] = 0, ["castPoint"] = 0, ["cooldown"] = new JsonArray(Cooldown("smoke_of_deceit")),
                ["effects"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "area", ["radius"] = (int)Attr("smoke_of_deceit", "application_radius"), ["center"] = "caster", ["team"] = "ally", ["heroesOnly"] = true,
                    ["effects"] = new JsonArray(new JsonObject
                    {
                        ["kind"] = "modifier",
                        ["modifiers"] = new JsonArray(new JsonObject
                        {
                            ["key"] = "item_smoke_of_deceit", ["duration"] = Attr("smoke_of_deceit", "duration"), ["flags"] = new JsonArray("invisible"),
                            ["moveSpeedPercent"] = (int)Attr("smoke_of_deceit", "bonus_movement_speed"), ["breakOnAction"] = true,
                            ["breakRadius"] = (int)Attr("smoke_of_deceit", "visibility_radius"),
                        }),
                    }),
                }),
            });
            return c;
        }

        private void Add(string item, int charges, JsonObject ability)
        {
            string id = "item_" + item;
            var full = new JsonObject { ["id"] = id, ["name"] = id };
            foreach (var (key, value) in ability)
                full[key] = value?.DeepClone();
            if (!full.ContainsKey("requiredLevels"))
                full["requiredLevels"] = new JsonArray(1);
            Abilities.Add(full);
            Actives[item] = (id, charges);
        }

        private static JsonObject Ward(string unit, decimal lifetime) => new()
        {
            ["targeting"] = "point", ["targetTeam"] = "any", ["castRange"] = 500, ["castPoint"] = 0, ["cooldown"] = new JsonArray(1),
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "summon", ["unitType"] = unit, ["duration"] = lifetime, ["counts"] = new JsonArray(1), ["atPoint"] = true }),
        };

        private static JsonObject SelfBuff(string key, decimal duration, JsonObject fields)
        {
            var modifier = new JsonObject { ["key"] = key, ["duration"] = duration };
            foreach (var (k, v) in fields)
                modifier[k] = v?.DeepClone();
            return new JsonObject
            {
                ["targeting"] = "noTarget", ["targetTeam"] = "ally", ["castRange"] = 0, ["castPoint"] = 0, ["cooldown"] = new JsonArray(1),
                ["effects"] = new JsonArray(new JsonObject { ["kind"] = "modifier", ["modifiers"] = new JsonArray(modifier) }),
            };
        }
    }
}
