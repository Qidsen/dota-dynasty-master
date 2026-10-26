using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Json;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
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
    /// Gameplay ruleset packs (spec §27, decision D-048): a JSON document that replaces the heroes, hero
    /// rules, abilities and items of a base ruleset and adds unit types for summons; the map, lane creeps,
    /// structures, waves and the rest stay from the base. Format <c>dynasty.ruleset-pack</c>, version 1
    /// (documented in <c>docs/DOTA_PACK.md</c>). Unknown fields are errors, so a typo cannot pass silently.
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
            reader.OptionalString("name");
            reader.Ignore("provenance");
            IReadOnlyList<HeroDefinition> heroes = reader.Array("heroes").Select(ReadHero).ToList();
            HeroRules? heroRules = reader.OptionalObject("heroRules") is Reader rules ? ReadHeroRules(rules, baseRules.HeroRules) : baseRules.HeroRules;
            List<CreepType> unitTypes = reader.OptionalArray("unitTypes").Select(ReadUnitType).ToList();
            IReadOnlyList<AbilityDefinition> abilities = reader.Has("abilities") ? reader.Array("abilities").Select(ReadAbility).ToList() : baseRules.Abilities;
            IReadOnlyList<ItemDefinition> items = reader.Has("items") ? reader.Array("items").Select(ReadItem).ToList() : baseRules.Items;
            reader.RejectUnknown();

            var unitIds = new HashSet<string>(unitTypes.Select(u => u.Id));
            IReadOnlyList<CreepType> creepTypes = baseRules.CreepTypes.Where(c => !unitIds.Contains(c.Id)).Concat(unitTypes).ToList();
            var content = new RulesetContent(id, version, baseRules.TicksPerSecond, baseRules.Combat, creepTypes, baseRules.StructureTypes,
                baseRules.Map, baseRules.Waves, heroes, heroRules, abilities, items, baseRules.Boss, baseRules.Runes,
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
            int projectileSpeed = h.Int("projectileSpeed");
            var stats = new UnitStats(
                h.Int("baseHealth"), h.Fixed("baseArmor"), h.Int("moveSpeed"), h.Int("attackDamageMin"), h.Int("attackDamageMax"),
                h.Int("attackRange"), h.Milliseconds("baseAttackTime"), h.Milliseconds("attackPoint"),
                acquisitionRange: System.Math.Max(800, h.Int("attackRange")), boundingRadius: 24, magicResistPercent: h.Int("magicResist"),
                visionRange: day, projectileSpeed: melee ? 0 : projectileSpeed, nightVisionRange: h.Int("nightVision"));
            var attributes = new HeroAttributes(primary, h.Int("strength"), h.Int("agility"), h.Int("intelligence"),
                h.Fixed("strengthGain"), h.Fixed("agilityGain"), h.Fixed("intelligenceGain"), h.Int("baseAttackSpeed"));
            var hero = new HeroDefinition(h.String("id"), h.String("name"), h.Strings("roles"), stats,
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: h.Int("baseMana"), manaPerLevel: 0,
                hpRegenPerSecond: h.Fixed("baseHealthRegen"), manaRegenPerSecond: h.Fixed("baseManaRegen"),
                abilityIds: h.OptionalStrings("abilities"), itemBuild: h.OptionalStrings("itemBuild"), attributes: attributes);
            h.RejectUnknown();
            return hero;
        }

        private static HeroRules ReadHeroRules(Reader r, HeroRules? b)
        {
            if (b == null)
                throw r.Error("heroRules", "the base ruleset has no hero rules to extend");
            var rules = new HeroRules(
                r.OptionalInts("xpForLevel") ?? b.XpForLevel, r.OptionalInt("xpShareRadius") ?? b.XpShareRadius,
                r.OptionalInt("heroKillXpBase") ?? b.HeroKillXpBase, r.OptionalInt("heroKillXpPerLevel") ?? 0,
                b.HeroKillGoldBase, b.HeroKillGoldPerLevel, b.AssistWindowMs, b.GoldPerMinute, b.StartingGold,
                b.RespawnBaseMs, b.RespawnPerLevelMs, b.GoldLossPerLevel, b.StructureTeamGold, b.FountainHealPercentPerSecond,
                b.BuybackBaseGold, b.BuybackGoldPerLevel, b.BuybackCooldownMs,
                r.OptionalInt("heroKillXpPercentOfVictim") ?? b.HeroKillXpPercentOfVictim, b.Attributes);
            r.RejectUnknown();
            return rules;
        }

        private static CreepType ReadUnitType(Reader u)
        {
            string? classText = u.OptionalString("attackClass");
            AttackClass attackClass = classText switch
            {
                null => AttackClass.Basic,
                "basic" => AttackClass.Basic,
                "runty" => AttackClass.Runty,
                "piercing" => AttackClass.Piercing,
                "reinforced" => AttackClass.Reinforced,
                _ => throw u.Error("attackClass", "expected basic, runty, piercing or reinforced"),
            };
            int vision = u.Int("visionRange");
            var stats = new UnitStats(u.Int("health"), u.Fixed("armor"), u.Int("moveSpeed"), u.OptionalInt("attackDamageMin") ?? 0, u.OptionalInt("attackDamageMax") ?? 0,
                u.OptionalInt("attackRange") ?? 0, u.OptionalMilliseconds("baseAttackTime") ?? 1000, u.OptionalMilliseconds("attackPoint") ?? 0,
                u.OptionalInt("acquisitionRange") ?? 0, u.OptionalInt("radius") ?? 16, u.OptionalInt("magicResist") ?? 0, vision,
                u.OptionalInt("trueSight") ?? 0, u.OptionalBool("invisible") == true ? ModifierFlags.Invisible : ModifierFlags.None,
                u.OptionalInt("projectileSpeed") ?? 0, u.OptionalInt("nightVision") ?? vision, attackClass);
            Bounty bounty = Bounty.None;
            if (u.OptionalObject("bounty") is Reader b)
            {
                bounty = new Bounty(b.Int("goldMin"), b.Int("goldMax"), b.Int("xp"));
                b.RejectUnknown();
            }

            var type = new CreepType(u.String("id"), stats, bounty);
            u.RejectUnknown();
            return type;
        }

        private static AbilityDefinition ReadAbility(Reader a)
        {
            AbilityTargeting targeting = a.String("targeting") switch
            {
                "noTarget" => AbilityTargeting.NoTarget,
                "unit" => AbilityTargeting.Unit,
                "point" => AbilityTargeting.Point,
                "passive" => AbilityTargeting.Passive,
                _ => throw a.Error("targeting", "expected noTarget, unit, point or passive"),
            };
            IReadOnlyList<int> levels = a.Ints("requiredLevels");
            IReadOnlyList<int> cooldowns = a.Seconds("cooldown");
            IReadOnlyList<int> mana = a.OptionalInts("manaCost") ?? (targeting == AbilityTargeting.Passive ? new int[0] : levels.Select(_ => 0).ToArray());
            var ability = new AbilityDefinition(a.String("id"), a.String("name"), targeting, ReadTeam(a, "targetTeam"), a.Int("castRange"), a.Milliseconds("castPoint"),
                cooldowns, mana, levels, a.Array("effects").Select(ReadEffect).ToList(),
                a.OptionalArray("passive").Select(ReadModifier).ToList(), a.OptionalBool("targetsStructures") ?? false, a.OptionalBool("innate") ?? false,
                a.OptionalInts("charges"), a.Has("charges") ? a.Seconds("chargeRestore") : null);
            a.RejectUnknown();
            return ability;
        }

        private static TargetTeam ReadTeam(Reader r, string field) => r.String(field) switch
        {
            "enemy" => TargetTeam.Enemy,
            "ally" => TargetTeam.Ally,
            "any" => TargetTeam.Any,
            _ => throw r.Error(field, "expected enemy, ally or any"),
        };

        private static EffectDefinition ReadEffect(Reader e)
        {
            EffectDefinition effect;
            switch (e.String("kind"))
            {
                case "damage":
                    DamageScaling scaling = e.Has("scaling") ? ReadScaling(e, "scaling") : DamageScaling.Flat;
                    effect = scaling == DamageScaling.Flat
                        ? EffectDefinition.Damage(ReadDamageType(e, "damageType"), e.Ints("amounts").ToArray())
                        : scaling == DamageScaling.PerTargetStack
                        ? EffectDefinition.DamagePerStack(ReadDamageType(e, "damageType"), e.String("stackKey"), e.Ints("amounts").ToArray())
                        : EffectDefinition.ScaledDamage(ReadDamageType(e, "damageType"), scaling, e.Hundredths("amounts").ToArray());
                    break;
                case "manaBurn":
                    effect = EffectDefinition.ManaBurn(e.OptionalInt("damagePercent") ?? 0, ReadDamageType(e, "damageType"), e.Ints("amounts").ToArray());
                    break;
                case "heal":
                    effect = EffectDefinition.Heal(e.Ints("amounts").ToArray());
                    break;
                case "restoreMana":
                    effect = EffectDefinition.RestoreMana(e.Ints("amounts").ToArray());
                    break;
                case "modifier":
                    effect = EffectDefinition.Modifier(e.Array("modifiers").Select(ReadModifier).ToArray());
                    break;
                case "area":
                    AreaCenter center = ReadCenter(e, "center");
                    effect = EffectDefinition.Area(e.Int("radius"), center, ReadTeam(e, "team"), e.OptionalBool("heroesOnly") ?? false,
                        e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "dash":
                    effect = EffectDefinition.Dash();
                    break;
                case "knockback":
                    effect = EffectDefinition.Knockback(e.Ints("amounts").ToArray());
                    break;
                case "summon":
                    string unit = e.String("unitType");
                    int duration = e.Milliseconds("duration");
                    int[] counts = e.Ints("counts").ToArray();
                    effect = e.OptionalBool("atPoint") == true ? EffectDefinition.SummonAtPoint(unit, duration, counts) : EffectDefinition.Summon(unit, duration, counts);
                    break;
                case "teleport":
                    effect = EffectDefinition.Teleport();
                    break;
                case "dispel":
                    effect = EffectDefinition.Dispel(e.OptionalBool("strong") ?? false);
                    break;
                case "projectile":
                    effect = EffectDefinition.Projectile(e.Int("speed"), e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "onCaster":
                    effect = EffectDefinition.OnCaster(e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "zone":
                    EffectDefinition[] pulse = e.Array("effects").Select(ReadEffect).ToArray();
                    int period = e.OptionalMilliseconds("period") ?? 0;
                    // Without a period the zone refreshes its effects continuously: only lasting modifiers make sense there.
                    if (period == 0 && pulse.Any(p => p.Kind != EffectKind.ApplyModifier || p.Modifiers.Any(m => m.IsPermanent)))
                        throw e.Error("effects", "a zone without a period may only apply modifiers with a duration");
                    effect = EffectDefinition.Zone(e.Int("radius"), ReadCenter(e, "center"), ReadTeam(e, "team"), e.Milliseconds("duration"), period,
                        e.OptionalBool("heroesOnly") ?? false, pulse);
                    break;
                case "pull":
                    AreaCenter toward = e.Has("toward") ? ReadCenter(e, "toward") : AreaCenter.Caster;
                    if (toward == AreaCenter.Target)
                        throw e.Error("toward", "expected caster or point");
                    effect = EffectDefinition.Pull(toward, e.Ints("amounts").ToArray());
                    break;
                case "illusion":
                    effect = EffectDefinition.Illusion(e.Milliseconds("duration"), e.Array("modifiers").Select(ReadModifier).ToArray(), e.Ints("counts").ToArray());
                    break;
                case "ifHero":
                    effect = EffectDefinition.IfHero(e.Array("effects").Select(ReadEffect).ToArray());
                    break;
                case "execute":
                    effect = EffectDefinition.Execute(e.Ints("amounts").ToArray());
                    break;
                case "attack":
                    effect = EffectDefinition.Attack(e.OptionalInts("amounts")?.ToArray() ?? new[] { 100 });   // no amounts: a normal attack
                    break;
                default:
                    throw e.Error("kind", "unknown effect kind");
            }

            e.RejectUnknown();
            return effect;
        }

        private static AreaCenter ReadCenter(Reader r, string field) => r.String(field) switch
        {
            "caster" => AreaCenter.Caster,
            "target" => AreaCenter.Target,
            "point" => AreaCenter.Point,
            _ => throw r.Error(field, "expected caster, target or point"),
        };

        private static DamageType ReadDamageType(Reader r, string field) => r.String(field) switch
        {
            "physical" => DamageType.Physical,
            "magical" => DamageType.Magical,
            "pure" => DamageType.Pure,
            _ => throw r.Error(field, "expected physical, magical or pure"),
        };

        private static DamageScaling ReadScaling(Reader r, string field) => r.String(field) switch
        {
            "flat" => DamageScaling.Flat,
            "targetMaxHealthPercent" => DamageScaling.TargetMaxHealthPercent,
            "targetMissingHealthPercent" => DamageScaling.TargetMissingHealthPercent,
            "targetMissingManaPercent" => DamageScaling.TargetMissingManaPercent,
            "targetCurrentHealthPercent" => DamageScaling.TargetCurrentHealthPercent,
            "targetMaxManaPercent" => DamageScaling.TargetMaxManaPercent,
            "casterAttackDamagePercent" => DamageScaling.CasterAttackDamagePercent,
            "casterStrengthPercent" => DamageScaling.CasterStrengthPercent,
            "casterAgilityPercent" => DamageScaling.CasterAgilityPercent,
            "casterIntelligencePercent" => DamageScaling.CasterIntelligencePercent,
            "perTargetStack" => DamageScaling.PerTargetStack,
            _ => throw r.Error(field, "expected flat, targetMaxHealthPercent, targetMissingHealthPercent, targetCurrentHealthPercent, targetMaxManaPercent, " +
                                      "targetMissingManaPercent, casterAttackDamagePercent, casterStrengthPercent, casterAgilityPercent, casterIntelligencePercent or perTargetStack"),
        };

        private static ModifierFlags ReadFlag(Reader r, string flag) => flag switch
        {
            "stunned" => ModifierFlags.Stunned,
            "rooted" => ModifierFlags.Rooted,
            "silenced" => ModifierFlags.Silenced,
            "disarmed" => ModifierFlags.Disarmed,
            "invisible" => ModifierFlags.Invisible,
            "debuffImmune" => ModifierFlags.DebuffImmune,
            "cannotDie" => ModifierFlags.CannotDie,
            "physicalImmune" => ModifierFlags.PhysicalImmune,
            "feared" => ModifierFlags.Feared,
            "taunted" => ModifierFlags.Taunted,
            _ => throw r.Error("flags", "unknown flag '" + flag + "'"),
        };

        private static ModifierSpec ReadModifier(Reader m)
        {
            ModifierFlags flags = ModifierFlags.None;
            foreach (string flag in m.OptionalStrings("flags") ?? new string[0])
                flags |= ReadFlag(m, flag);

            var spec = new ModifierSpec(m.String("key"), m.OptionalMilliseconds("duration") ?? -1, flags,
                armorBonus: m.OptionalFixed("armor") ?? Fixed.Zero, moveSpeedHundredths: m.OptionalHundredth("moveSpeedPercent") ?? 0,
                attackSpeedBonus: m.OptionalInt("attackSpeed") ?? 0, damageBonus: m.OptionalInt("damage") ?? 0, magicResistBonus: m.OptionalInt("magicResist") ?? 0,
                damagePerSecond: m.OptionalFixed("damagePerSecond") ?? Fixed.Zero,
                damageType: m.Has("damageType") ? ReadDamageType(m, "damageType") : DamageType.Magical,
                healPerSecond: m.OptionalFixed("healPerSecond") ?? Fixed.Zero, periodMs: m.OptionalMilliseconds("period") ?? 1000,
                critChanceHundredths: m.OptionalHundredth("critChance") ?? 0, critMultiplierPercent: m.OptionalInt("critMultiplier") ?? 0,
                structureDamagePercent: m.OptionalInt("structureDamage") ?? 0, lifestealPercent: m.OptionalInt("lifesteal") ?? 0,
                maxHpBonus: m.OptionalInt("maxHealth") ?? 0, maxManaBonus: m.OptionalInt("maxMana") ?? 0, hpRegenBonus: m.OptionalFixed("healthRegen") ?? Fixed.Zero,
                breakOnAction: m.OptionalBool("breakOnAction") ?? false, breakRadius: m.OptionalInt("breakRadius") ?? 0, dispellable: m.OptionalBool("dispellable") ?? true,
                strengthBonus: m.OptionalInt("strength") ?? 0, agilityBonus: m.OptionalInt("agility") ?? 0, intelligenceBonus: m.OptionalInt("intelligence") ?? 0,
                manaRegenBonus: m.OptionalFixed("manaRegen") ?? Fixed.Zero, moveSpeedBonus: m.OptionalInt("moveSpeed") ?? 0, attackRangeBonus: m.OptionalInt("attackRange") ?? 0,
                incomingDamagePercent: m.OptionalInt("incomingDamagePercent") ?? 0, attackDamagePercent: m.OptionalInt("attackDamagePercent") ?? 0,
                evasionHundredths: m.OptionalHundredth("evasion") ?? 0, onHit: m.OptionalArray("onHit").Select(ReadEffect).ToList(),
                onHitChancePercent: 0, onHitChanceHundredths: m.OptionalHundredth("onHitChance") ?? 10000, aura: m.OptionalObject("aura") is Reader a ? ReadAura(a) : null,
                cleavePercent: m.OptionalInt("cleavePercent") ?? 0, cleaveRadius: m.OptionalInt("cleaveRadius") ?? 0, barrier: m.OptionalInt("barrier") ?? 0, consumedOnHit: m.OptionalBool("consumedOnHit") ?? false,
                damagePerSecondMaxHealthPercent: m.OptionalFixed("damagePerSecondMaxHealthPercent") ?? Fixed.Zero,
                onHitAfterAttacks: m.OptionalInt("onHitAfterAttacks") ?? 0, whenAttacked: m.OptionalArray("whenAttacked").Select(ReadEffect).ToList(),
                whenAttackedEvery: m.OptionalInt("whenAttackedEvery") ?? 1,
                maxStacks: m.OptionalBool("stacking") == true ? m.OptionalInt("maxStacks") ?? int.MaxValue : 0,
                procCooldownMs: m.OptionalMilliseconds("procCooldown") ?? 0, whenAttackedChancePercent: 0, whenAttackedChanceHundredths: m.OptionalHundredth("whenAttackedChance") ?? 10000,
                onCast: m.OptionalArray("onCast").Select(ReadEffect).ToList(), onKill: m.OptionalArray("onKill").Select(ReadEffect).ToList(),
                onDeath: m.OptionalArray("onDeath").Select(ReadEffect).ToList(), onHitHeroesOnly: m.OptionalBool("onHitHeroesOnly") ?? false,
                // A chance alone means one extra target (Gunslinger).
                extraTargets: m.OptionalInt("extraTargets") ?? (m.Has("extraTargetChance") ? 1 : 0), extraTargetRadius: m.OptionalInt("extraTargetRadius") ?? 0,
                // Dota gives bounces either as the share dealt or as a reduction (Moon Glaive: damage_reduction_percent).
                extraTargetDamagePercent: m.OptionalInt("extraTargetDamagePercent") ?? 100 - (m.OptionalInt("extraTargetDamageReduction") ?? 0), onKillHeroesOnly: m.OptionalBool("onKillHeroesOnly") ?? false,
                revives: m.OptionalBool("revives") ?? false, attackCharges: m.OptionalInt("attackCharges") ?? 0,
                extraTargetRadiusFromAttackRange: m.OptionalBool("extraTargetRadiusFromAttackRange") ?? false,
                extraTargetChancePercent: m.OptionalInt("extraTargetChance") ?? 100);
            m.RejectUnknown();
            return spec;
        }

        private static AuraSpec ReadAura(Reader a)
        {
            var aura = new AuraSpec(a.Int("radius"), ReadTeam(a, "team"), a.OptionalBool("heroesOnly") ?? false, ReadModifier(a.OptionalObject("modifier") ?? throw a.Error("modifier", "missing")));
            a.RejectUnknown();
            return aura;
        }

        private static ItemDefinition ReadItem(Reader i)
        {
            ModifierSpec? bonuses = i.OptionalObject("bonuses") is Reader b ? ReadModifier(b) : null;
            var item = new ItemDefinition(i.String("id"), i.String("name"), i.Int("cost"), bonuses, i.OptionalStrings("components"),
                i.OptionalString("active"), i.OptionalInt("charges") ?? 0);
            i.RejectUnknown();
            return item;
        }

        /// <summary>Reads fields of one JSON object and names the field in every error (e.g. <c>heroes[1].agility</c>).</summary>
        private sealed class Reader
        {
            private readonly JsonValue _value;
            private readonly string _path;
            private readonly HashSet<string> _read = new HashSet<string>();

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

            public bool Has(string field)
            {
                _read.Add(field);
                return _value.Has(field);
            }

            /// <summary>A known field this build does not use.</summary>
            public void Ignore(string field) => _read.Add(field);

            /// <summary>A field the format does not know is a mistake: a typo would otherwise be dropped silently.</summary>
            public void RejectUnknown()
            {
                foreach (KeyValuePair<string, JsonValue> member in _value.Members)
                {
                    if (!_read.Contains(member.Key))
                        throw Error(member.Key, "unknown field");
                }
            }

            private T Read<T>(string field, Func<JsonValue, T> read)
            {
                _read.Add(field);
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
            public int Milliseconds(string field) => ToMilliseconds(Read(field, v => v.AsFixed()));

            public int? OptionalInt(string field) => Has(field) ? Int(field) : (int?)null;

            /// <summary>A percent that may have decimals (33.33) in the document, hundredths of a percent in the ruleset.</summary>
            public int? OptionalHundredth(string field) => Has(field)
                ? (Fixed(field) * Dynasty.Core.Mathematics.Fixed.FromInt(100) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor()
                : (int?)null;
            public Fixed? OptionalFixed(string field) => Has(field) ? Fixed(field) : (Fixed?)null;
            public bool? OptionalBool(string field) => Has(field) ? Read(field, v => v.AsBool()) : (bool?)null;
            public string? OptionalString(string field) => Has(field) ? String(field) : null;
            public int? OptionalMilliseconds(string field) => Has(field) ? Milliseconds(field) : (int?)null;

            public IReadOnlyList<int> Ints(string field) => Read(field, v => ItemsOf(v).Select(i => i.AsInt()).ToList());

            /// <summary>Seconds per level in the document, milliseconds in the ruleset.</summary>
            public IReadOnlyList<int> Seconds(string field) => Read(field, v => ItemsOf(v).Select(i => ToMilliseconds(i.AsFixed())).ToList());

            /// <summary>Percents per level in the document, hundredths of a percent in the ruleset.</summary>
            public IReadOnlyList<int> Hundredths(string field) => Read(field, v => ItemsOf(v)
                .Select(i => (i.AsFixed() * Dynasty.Core.Mathematics.Fixed.FromInt(100) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor()).ToList());

            public IReadOnlyList<string> Strings(string field) => Read(field, v => ItemsOf(v).Select(i => i.AsString()).ToList());

            public IReadOnlyList<string>? OptionalStrings(string field) => Has(field) ? Strings(field) : null;

            public IReadOnlyList<int>? OptionalInts(string field) => Has(field) ? Ints(field) : null;

            public IEnumerable<Reader> Array(string field)
            {
                IReadOnlyList<JsonValue> items = Read(field, ItemsOf);
                for (int i = 0; i < items.Count; i++)
                    yield return new Reader(items[i], Path(field) + "[" + i + "]");
            }

            public IEnumerable<Reader> OptionalArray(string field) => Has(field) ? Array(field) : Enumerable.Empty<Reader>();

            public Reader? OptionalObject(string field) => Has(field) ? new Reader(_value[field], Path(field)) : null;

            private static int ToMilliseconds(Fixed seconds) =>
                (seconds * Dynasty.Core.Mathematics.Fixed.FromInt(1000) + Dynasty.Core.Mathematics.Fixed.FromRatio(1, 2)).ToIntFloor();

            private static IReadOnlyList<JsonValue> ItemsOf(JsonValue v) =>
                v.Kind == JsonKind.Array ? v.Items : throw new JsonException("Expected Array, found " + v.Kind + ".");
        }
    }
}
