using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Json;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Content
{
    public sealed class RulesetPackException : Exception
    {
        public RulesetPackException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Gameplay ruleset packs (spec §27, decision D-048): a JSON document that replaces the heroes and hero
    /// rules of a base ruleset; the map, creeps, structures, waves and the rest stay from the base. Format
    /// <c>dynasty.ruleset-pack</c>, version 1 (documented in <c>docs/DOTA_PACK.md</c>).
    /// </summary>
    public static class RulesetPack
    {
        public const string Format = "dynasty.ruleset-pack";
        public const int FormatVersion = 1;

        /// <summary>The base ruleset with the pack applied; throws <see cref="RulesetPackException"/> naming the bad field.</summary>
        public static RulesetContent Apply(RulesetContent baseRules, string json)
        {
            JsonValue doc;
            try
            {
                doc = JsonValue.Parse(json);
            }
            catch (JsonException e)
            {
                throw new RulesetPackException("Not valid JSON: " + e.Message);
            }

            var reader = new Reader(doc, "");
            if (reader.String("format") != Format)
                throw new RulesetPackException("Unknown format: expected \"" + Format + "\".");
            int formatVersion = reader.Int("formatVersion");
            if (formatVersion != FormatVersion)
                throw new RulesetPackException("Unsupported formatVersion " + formatVersion + " (this build reads " + FormatVersion + ").");

            string id = reader.String("id");
            int version = reader.Int("version");
            IReadOnlyList<HeroDefinition> heroes = reader.Array("heroes").Select(ReadHero).ToList();
            HeroRules? heroRules = reader.OptionalObject("heroRules") is Reader rules ? ReadHeroRules(rules, baseRules.HeroRules) : baseRules.HeroRules;

            var content = new RulesetContent(id, version, baseRules.TicksPerSecond, baseRules.Combat, baseRules.CreepTypes, baseRules.StructureTypes,
                baseRules.Map, baseRules.Waves, heroes, heroRules, baseRules.Abilities, baseRules.Items, baseRules.Boss, baseRules.Runes,
                baseRules.DayNight, baseRules.Draft);
            IReadOnlyList<string> errors = content.Validate();
            if (errors.Count > 0)
                throw new RulesetPackException("The pack makes an invalid ruleset: " + string.Join("; ", errors));
            return content;
        }

        private static HeroDefinition ReadHero(Reader h)
        {
            bool melee = h.String("attackType") switch
            {
                "melee" => true,
                "ranged" => false,
                _ => throw h.Error("attackType", "expected \"melee\" or \"ranged\""),
            };
            PrimaryAttribute primary = h.String("primaryAttribute") switch
            {
                "strength" => PrimaryAttribute.Strength,
                "agility" => PrimaryAttribute.Agility,
                "intelligence" => PrimaryAttribute.Intelligence,
                "universal" => PrimaryAttribute.Universal,
                _ => throw h.Error("primaryAttribute", "expected strength, agility, intelligence or universal"),
            };
            int day = h.Int("dayVision");
            var stats = new UnitStats(
                h.Int("baseHealth"), h.Fixed("baseArmor"), h.Int("moveSpeed"), h.Int("attackDamageMin"), h.Int("attackDamageMax"),
                h.Int("attackRange"), h.Milliseconds("baseAttackTime"), h.Milliseconds("attackPoint"),
                acquisitionRange: System.Math.Max(800, h.Int("attackRange")), boundingRadius: 24, magicResistPercent: h.Int("magicResist"),
                visionRange: day, projectileSpeed: melee ? 0 : h.Int("projectileSpeed"), nightVisionRange: h.Int("nightVision"));
            var attributes = new HeroAttributes(primary, h.Int("strength"), h.Int("agility"), h.Int("intelligence"),
                h.Fixed("strengthGain"), h.Fixed("agilityGain"), h.Fixed("intelligenceGain"), h.Int("baseAttackSpeed"));
            return new HeroDefinition(h.String("id"), h.String("name"), h.Strings("roles"), stats,
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: h.Int("baseMana"), manaPerLevel: 0,
                hpRegenPerSecond: h.Fixed("baseHealthRegen"), manaRegenPerSecond: h.Fixed("baseManaRegen"),
                abilityIds: h.OptionalStrings("abilities"), itemBuild: h.OptionalStrings("itemBuild"), attributes: attributes);
        }

        private static HeroRules ReadHeroRules(Reader r, HeroRules? b)
        {
            if (b == null)
                throw r.Error("heroRules", "the base ruleset has no hero rules to extend");
            return new HeroRules(
                r.OptionalInts("xpForLevel") ?? b.XpForLevel, r.OptionalInt("xpShareRadius") ?? b.XpShareRadius,
                r.OptionalInt("heroKillXpBase") ?? b.HeroKillXpBase, r.OptionalInt("heroKillXpPerLevel") ?? 0,
                b.HeroKillGoldBase, b.HeroKillGoldPerLevel, b.AssistWindowMs, b.GoldPerMinute, b.StartingGold,
                b.RespawnBaseMs, b.RespawnPerLevelMs, b.GoldLossPerLevel, b.StructureTeamGold, b.FountainHealPercentPerSecond,
                b.BuybackBaseGold, b.BuybackGoldPerLevel, b.BuybackCooldownMs,
                r.OptionalInt("heroKillXpPercentOfVictim") ?? b.HeroKillXpPercentOfVictim, b.Attributes);
        }

        /// <summary>Reads fields of one JSON object and names the field in every error (e.g. <c>heroes[1].agility</c>).</summary>
        private sealed class Reader
        {
            private readonly JsonValue _value;
            private readonly string _path;

            public Reader(JsonValue value, string path)
            {
                if (value.Kind != JsonKind.Object)
                    throw new RulesetPackException((path.Length == 0 ? "The pack" : path) + ": expected an object.");
                _value = value;
                _path = path;
            }

            public RulesetPackException Error(string field, string what) =>
                new RulesetPackException(Path(field) + ": " + what + ".");

            private string Path(string field) => _path.Length == 0 ? field : _path + "." + field;

            private T Read<T>(string field, Func<JsonValue, T> read)
            {
                JsonValue? value = _value.Get(field);
                if (value == null)
                    throw Error(field, "missing");
                try
                {
                    return read(value);
                }
                catch (JsonException e)
                {
                    throw Error(field, e.Message.TrimEnd('.'));
                }
            }

            public string String(string field) => Read(field, v => v.AsString());
            public int Int(string field) => Read(field, v => v.AsInt());
            public Fixed Fixed(string field) => Read(field, v => v.AsFixed());

            /// <summary>Seconds in the document, milliseconds in the ruleset.</summary>
            public int Milliseconds(string field) => (Read(field, v => v.AsFixed()) * Dynasty.Core.Mathematics.Fixed.FromInt(1000) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor();

            public int? OptionalInt(string field) => _value.Has(field) ? Int(field) : (int?)null;

            public IReadOnlyList<string> Strings(string field) => Read(field, v => ItemsOf(v).Select(i => i.AsString()).ToList());

            public IReadOnlyList<string>? OptionalStrings(string field) => _value.Has(field) ? Strings(field) : null;

            public IReadOnlyList<int>? OptionalInts(string field) => _value.Has(field) ? Read(field, v => ItemsOf(v).Select(i => i.AsInt()).ToList()) : null;

            public IEnumerable<Reader> Array(string field)
            {
                IReadOnlyList<JsonValue> items = Read(field, ItemsOf);
                for (int i = 0; i < items.Count; i++)
                    yield return new Reader(items[i], Path(field) + "[" + i + "]");
            }

            public Reader? OptionalObject(string field) => _value.Has(field) ? new Reader(_value[field], Path(field)) : null;

            private static IReadOnlyList<JsonValue> ItemsOf(JsonValue v) =>
                v.Kind == JsonKind.Array ? v.Items : throw new JsonException("Expected Array, found " + v.Kind + ".");
        }
    }
}
