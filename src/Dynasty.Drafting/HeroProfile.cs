using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting
{
    /// <summary>
    /// What a hero brings to a draft (spec §22: control, damage, initiation, push, scaling, tempo),
    /// derived from its definition and abilities at max level — never typed in by hand, so a patch
    /// that changes the numbers changes the profile. Integer points (draft decisions feed the match,
    /// so they stay free of floating point).
    /// </summary>
    public sealed class HeroProfile
    {
        private HeroProfile(string heroId)
        {
            HeroId = heroId;
        }

        public string HeroId { get; }

        /// <summary>Disables (stun, root, silence) and slows, weighted by cooldown and area.</summary>
        public int Control { get; private set; }

        /// <summary>Ability damage (nukes and damage over time), weighted by cooldown and area.</summary>
        public int Burst { get; private set; }

        /// <summary>Share of ability damage that is magical or pure, 0–100.</summary>
        public int MagicalPercent { get; private set; }

        /// <summary>Starting fights: a dash into a disable, a wide area disable, or (half) a ranged single-target catch.</summary>
        public int Initiation { get; private set; }

        /// <summary>Healing and protection for allies (and lifesteal for oneself).</summary>
        public int Sustain { get; private set; }

        /// <summary>Taking structures: bonus structure damage and summoned units.</summary>
        public int Push { get; private set; }

        /// <summary>Growth into the late game: damage per level, attack steroids and crits.</summary>
        public int Scaling { get; private set; }

        /// <summary>Early strength: base attack damage and level-1 ability damage.</summary>
        public int Tempo { get; private set; }

        public int Durability { get; private set; }
        public int Mobility { get; private set; }
        public bool Ranged { get; private set; }

        /// <summary>Removes disables from allies.</summary>
        public bool Dispel { get; private set; }

        public static HeroProfile Of(RulesetContent ruleset, string heroId) =>
            Of(ruleset.FindHero(heroId) ?? throw new ArgumentException("Unknown hero '" + heroId + "'."), ruleset.Abilities);

        public static HeroProfile Of(HeroDefinition hero, IReadOnlyList<AbilityDefinition> abilities)
        {
            var profile = new HeroProfile(hero.Id);
            UnitStats stats = hero.BaseStats;
            profile.Ranged = stats.AttackRange > 300;
            profile.Durability = (stats.MaxHp + 15 * hero.HpPerLevel) / 50 + 5 * stats.Armor.ToIntFloor();
            profile.Scaling = 10 * hero.DamagePerLevel + (hero.Roles.Contains("late") ? 15 : 0);
            profile.Tempo = stats.AttackDamageMin / 2;

            int magical = 0, physical = 0;
            foreach (string id in hero.AbilityIds)
            {
                AbilityDefinition? ability = abilities.FirstOrDefault(a => a.Id == id);
                if (ability == null)
                    continue;
                if (ability.Targeting == AbilityTargeting.Passive)
                {
                    ModifierSpec top = ability.PassiveModifiers[ability.PassiveModifiers.Count - 1];
                    AddPassive(profile, top);
                    continue;
                }

                int cdFactor = CooldownFactor(ability);   // percent
                var tally = new Tally();
                foreach (EffectDefinition effect in ability.Effects)
                    Walk(effect, ability, areaPercent: 100, tally);

                profile.Durability += tally.SelfDurability * cdFactor / 100;
                profile.Scaling += tally.SelfScaling * cdFactor / 100;
                profile.Control += tally.Control * cdFactor / 100;
                profile.Burst += (tally.Magical + tally.Physical) * cdFactor / 100;
                magical += tally.Magical;
                physical += tally.Physical;
                profile.Sustain += tally.Sustain * cdFactor / 100;
                profile.Push += tally.Push;
                profile.Mobility += tally.Dashes * 20;
                profile.Tempo += tally.FirstLevelDamage / 10;
                profile.Dispel |= tally.Dispel;
                if (tally.Control > 0 && (tally.Dashes > 0 || tally.DisableRadius >= 350))
                    profile.Initiation += tally.Control * cdFactor / 100 + tally.DisableRadius / 25 + tally.Dashes * 10;
                else if (tally.Control > 0 && ability.Targeting == AbilityTargeting.Unit && ability.CastRange >= 500)
                    profile.Initiation += tally.Control * cdFactor / 200;   // a ranged catch on one hero
            }

            profile.MagicalPercent = magical + physical == 0 ? 0 : 100 * magical / (magical + physical);
            return profile;
        }

        private static void AddPassive(HeroProfile profile, ModifierSpec spec)
        {
            profile.Scaling += spec.AttackSpeedBonus / 2 + spec.DamageBonus + spec.CritChancePercent * Math.Max(0, spec.CritMultiplierPercent - 100) / 100;
            profile.Sustain += spec.LifestealPercent;
            profile.Durability += spec.MaxHpBonus / 50 + 5 * spec.ArmorBonus.ToIntFloor() + spec.HpRegenBonus.ToIntFloor();
            profile.Push += spec.StructureDamagePercent / 2;
            profile.Mobility += spec.MoveSpeedPercent;
        }

        /// <summary>Shorter cooldowns count for more: 15 s is the reference, clamped to 40–150 percent.</summary>
        private static int CooldownFactor(AbilityDefinition ability)
        {
            int cd = ability.CooldownMs.Count == 0 ? 15000 : ability.CooldownMs[ability.CooldownMs.Count - 1];
            return Math.Max(40, Math.Min(150, 15000 * 100 / Math.Max(1, cd)));
        }

        private static void Walk(EffectDefinition effect, AbilityDefinition ability, int areaPercent, Tally tally)
        {
            bool enemy = ability.TargetTeam != TargetTeam.Ally;
            switch (effect.Kind)
            {
                case EffectKind.Area:
                    // A wider area hits more heroes: 100% at single target, up to 200% at radius 500+.
                    int wide = 100 + Math.Min(100, effect.Radius / 5);
                    if (effect.Team != TargetTeam.Ally)
                        tally.DisableRadius = Math.Max(tally.DisableRadius, effect.Radius);
                    foreach (EffectDefinition inner in effect.Inner)
                        WalkTeam(inner, ability, wide, effect.Team != TargetTeam.Ally, tally);
                    return;
                case EffectKind.Projectile:
                    foreach (EffectDefinition inner in effect.Inner)
                        Walk(inner, ability, areaPercent, tally);
                    return;
                case EffectKind.ApplyModifier when !enemy && ability.Targeting == AbilityTargeting.NoTarget:
                    // A buff on the caster only: it makes the hero sturdier or hit harder, it does not protect allies.
                    ModifierSpec self = effect.Modifiers[effect.Modifiers.Count - 1];
                    tally.SelfDurability += 3 * self.ArmorBonus.ToIntFloor() + self.MagicResistBonus / 2 + self.MaxHpBonus / 50;
                    tally.SelfScaling += self.AttackSpeedBonus / 4 + self.DamageBonus / 2;
                    return;
                default:
                    WalkTeam(effect, ability, areaPercent, enemy, tally);
                    return;
            }
        }

        private static void WalkTeam(EffectDefinition effect, AbilityDefinition ability, int areaPercent, bool enemy, Tally tally)
        {
            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    int amount = effect.Amounts[effect.Amounts.Count - 1] * areaPercent / 100;
                    if (effect.DamageType == DamageType.Physical)
                        tally.Physical += amount / 10;
                    else
                        tally.Magical += amount / 10;
                    tally.FirstLevelDamage += effect.Amounts[0] * areaPercent / 100;
                    break;
                case EffectKind.Heal:
                    tally.Sustain += effect.Amounts[effect.Amounts.Count - 1] * areaPercent / 100 / 10;
                    break;
                case EffectKind.ApplyModifier:
                    ModifierSpec spec = effect.Modifiers[effect.Modifiers.Count - 1];
                    if (enemy)
                    {
                        int seconds10 = Math.Max(0, spec.DurationMs) / 100;   // tenths of a second
                        int weight = (spec.Flags & ModifierFlags.Stunned) != 0 ? 10 : (spec.Flags & ModifierFlags.Rooted) != 0 ? 7 : (spec.Flags & ModifierFlags.Silenced) != 0 ? 5 : 0;
                        int slow = Math.Max(0, -spec.MoveSpeedPercent) * seconds10 / 100;
                        tally.Control += (weight * seconds10 / 10 + slow) * areaPercent / 100;
                        int dot = spec.DamagePerSecond.ToIntFloor() * seconds10 / 10 * areaPercent / 100 / 10;
                        if (spec.DamageType == DamageType.Physical)
                            tally.Physical += dot;
                        else
                            tally.Magical += dot;
                    }
                    else
                    {
                        tally.Sustain += (spec.HealPerSecond.ToIntFloor() * Math.Max(0, spec.DurationMs) / 1000 / 10
                                          + 3 * spec.ArmorBonus.ToIntFloor() + spec.MagicResistBonus / 2
                                          + spec.DamageBonus / 2 + spec.AttackSpeedBonus / 4) * areaPercent / 100;
                    }

                    break;
                case EffectKind.Dash:
                    tally.Dashes++;
                    break;
                case EffectKind.Knockback:
                    tally.Control += effect.Amounts[effect.Amounts.Count - 1] / 40 * areaPercent / 100;
                    break;
                case EffectKind.Summon:
                    tally.Push += effect.Amounts[effect.Amounts.Count - 1] * 15;
                    break;
                case EffectKind.Dispel:
                    tally.Dispel = true;
                    tally.Sustain += 10;
                    break;
            }
        }

        private sealed class Tally
        {
            public int Control;
            public int Magical;
            public int Physical;
            public int Sustain;
            public int Push;
            public int Dashes;
            public int FirstLevelDamage;
            public int DisableRadius;
            public int SelfDurability;
            public int SelfScaling;
            public bool Dispel;
        }
    }
}
