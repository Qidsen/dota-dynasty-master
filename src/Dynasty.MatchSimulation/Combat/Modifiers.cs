using System;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;

namespace Dynasty.MatchSimulation.Combat
{
    public enum DamageType
    {
        Physical = 0,
        Magical = 1,
        Pure = 2,
    }

    [Flags]
    public enum ModifierFlags
    {
        None = 0,

        /// <summary>No movement, attacks or casts; interrupts attack wind-up.</summary>
        Stunned = 1,

        /// <summary>No movement.</summary>
        Rooted = 2,

        /// <summary>No ability casts.</summary>
        Silenced = 4,

        /// <summary>No attacks; interrupts attack wind-up.</summary>
        Disarmed = 8,

        /// <summary>Not visible to enemies unless inside one of their true-sight radii.</summary>
        Invisible = 16,
    }

    /// <summary>
    /// Immutable description of a buff/debuff: status flags, stat deltas and periodic effects.
    /// Abilities and items reference specs; units carry <see cref="ActiveModifier"/> instances.
    /// </summary>
    public sealed class ModifierSpec
    {
        public ModifierSpec(
            string key,
            int durationMs,
            ModifierFlags flags = ModifierFlags.None,
            Fixed armorBonus = default,
            int moveSpeedPercent = 0,
            int attackSpeedBonus = 0,
            int damageBonus = 0,
            int magicResistBonus = 0,
            Fixed damagePerSecond = default,
            DamageType damageType = DamageType.Magical,
            Fixed healPerSecond = default,
            int periodMs = 1000,
            int critChancePercent = 0,
            int critMultiplierPercent = 0,
            int structureDamagePercent = 0,
            int lifestealPercent = 0,
            int maxHpBonus = 0,
            int maxManaBonus = 0,
            Fixed hpRegenBonus = default)
        {
            MaxHpBonus = maxHpBonus;
            MaxManaBonus = maxManaBonus;
            HpRegenBonus = hpRegenBonus;
            CritChancePercent = critChancePercent;
            CritMultiplierPercent = critMultiplierPercent;
            StructureDamagePercent = structureDamagePercent;
            LifestealPercent = lifestealPercent;
            Key = key;
            DurationMs = durationMs;
            Flags = flags;
            ArmorBonus = armorBonus;
            MoveSpeedPercent = moveSpeedPercent;
            AttackSpeedBonus = attackSpeedBonus;
            DamageBonus = damageBonus;
            MagicResistBonus = magicResistBonus;
            DamagePerSecond = damagePerSecond;
            DamageType = damageType;
            HealPerSecond = healPerSecond;
            PeriodMs = periodMs;
        }

        /// <summary>Stable key; reapplying the same key from the same source refreshes instead of stacking.</summary>
        public string Key { get; }

        /// <summary>Negative = permanent until removed.</summary>
        public int DurationMs { get; }

        public ModifierFlags Flags { get; }
        public Fixed ArmorBonus { get; }

        /// <summary>Percent change of move speed; negative slows.</summary>
        public int MoveSpeedPercent { get; }

        /// <summary>Attack speed bonus in percent; +100 halves the attack interval.</summary>
        public int AttackSpeedBonus { get; }

        public int DamageBonus { get; }
        public int MagicResistBonus { get; }
        public Fixed DamagePerSecond { get; }
        public DamageType DamageType { get; }
        public Fixed HealPerSecond { get; }

        /// <summary>Interval of periodic damage/heal; each period deals per-second value × period.</summary>
        public int PeriodMs { get; }

        /// <summary>Chance per attack to deal <see cref="CritMultiplierPercent"/> damage.</summary>
        public int CritChancePercent { get; }

        /// <summary>Attack damage on a critical hit, e.g. 200 = double.</summary>
        public int CritMultiplierPercent { get; }

        /// <summary>Extra attack damage against structures, in percent.</summary>
        public int StructureDamagePercent { get; }

        /// <summary>Share of dealt attack damage returned to the attacker as HP.</summary>
        public int LifestealPercent { get; }

        public int MaxHpBonus { get; }
        public int MaxManaBonus { get; }

        /// <summary>Extra HP regeneration per second (heroes).</summary>
        public Fixed HpRegenBonus { get; }

        public bool IsPeriodic => DamagePerSecond > Fixed.Zero || HealPerSecond > Fixed.Zero;

        public bool IsPermanent => DurationMs < 0;

        internal void WriteCanonical(Dynasty.Core.Hashing.CanonicalWriter w)
        {
            w.WriteString(Key);
            w.WriteInt64(DurationMs);
            w.WriteInt64((int)Flags);
            w.WriteFixed(ArmorBonus);
            w.WriteInt64(MoveSpeedPercent);
            w.WriteInt64(AttackSpeedBonus);
            w.WriteInt64(DamageBonus);
            w.WriteInt64(MagicResistBonus);
            w.WriteFixed(DamagePerSecond);
            w.WriteInt64((int)DamageType);
            w.WriteFixed(HealPerSecond);
            w.WriteInt64(PeriodMs);
            w.WriteInt64(CritChancePercent);
            w.WriteInt64(CritMultiplierPercent);
            w.WriteInt64(StructureDamagePercent);
            w.WriteInt64(LifestealPercent);
            w.WriteInt64(MaxHpBonus);
            w.WriteInt64(MaxManaBonus);
            w.WriteFixed(HpRegenBonus);
        }

        public static ModifierSpec Status(string key, int durationMs, ModifierFlags flags) => new ModifierSpec(key, durationMs, flags);
    }

    public sealed class ActiveModifier
    {
        internal ActiveModifier(ModifierSpec spec, EntityId source, long appliedTick, long expiresTick, long nextPeriodTick)
        {
            Spec = spec;
            Source = source;
            AppliedTick = appliedTick;
            ExpiresTick = expiresTick;
            NextPeriodTick = nextPeriodTick;
        }

        public ModifierSpec Spec { get; }
        public EntityId Source { get; }
        public long AppliedTick { get; internal set; }

        /// <summary>Tick at which it is removed; -1 for permanent.</summary>
        public long ExpiresTick { get; internal set; }

        internal long NextPeriodTick { get; set; }
    }

    public static class Modifiers
    {
        public static ActiveModifier Apply(SimulationContext<MatchState> context, MatchUnit target, ModifierSpec spec, EntityId source)
        {
            long now = context.Tick.Value;
            long expires = spec.DurationMs < 0 ? -1 : now + context.TickRate.TicksFor(spec.DurationMs);

            ActiveModifier? existing = target.FindModifier(spec.Key, source);
            if (existing != null && !ReferenceEquals(existing.Spec, spec))
            {
                // Same key, different spec (e.g. a passive's next level): replace, don't refresh.
                target.RemoveModifier(existing);
                existing = null;
            }

            ActiveModifier modifier;
            if (existing != null)
            {
                existing.AppliedTick = now;
                existing.ExpiresTick = expires;
                modifier = existing;
            }
            else
            {
                modifier = new ActiveModifier(spec, source, now, expires, now + context.TickRate.TicksFor(spec.PeriodMs));
                target.AddModifier(modifier);
            }

            context.Emit(new ModifierApplied(target.Id, source, spec.Key, expires));
            return modifier;
        }

        /// <summary>
        /// Adds a permanent instance that never merges with others of the same key (item bonuses:
        /// two identical items stack). Removed explicitly via <see cref="Remove"/>.
        /// </summary>
        public static ActiveModifier AddInstance(SimulationContext<MatchState> context, MatchUnit target, ModifierSpec spec, EntityId source)
        {
            var modifier = new ActiveModifier(spec, source, context.Tick.Value, -1, long.MaxValue);
            target.AddModifier(modifier);
            context.Emit(new ModifierApplied(target.Id, source, spec.Key, -1));
            return modifier;
        }

        internal static void Remove(SimulationContext<MatchState> context, MatchUnit target, ActiveModifier modifier, string reason)
        {
            target.RemoveModifier(modifier);
            context.Emit(new ModifierRemoved(target.Id, modifier.Spec.Key, reason));
        }
    }

    public static class DamageMath
    {
        /// <summary>Damage multiplier for armor: 1 - f·a / (1 + f·|a|). Negative armor amplifies damage.</summary>
        public static Fixed ArmorMultiplier(Fixed armor, Fixed armorFactor) =>
            Fixed.One - armorFactor * armor / (Fixed.One + armorFactor * Fixed.Abs(armor));

        public static Fixed Mitigate(Fixed amount, DamageType type, Fixed armor, int magicResistPercent, Fixed armorFactor)
        {
            switch (type)
            {
                case DamageType.Physical:
                    return amount * ArmorMultiplier(armor, armorFactor);
                case DamageType.Magical:
                    return amount * Fixed.FromInt(100 - magicResistPercent) / Fixed.FromInt(100);
                default:
                    return amount;
            }
        }
    }

    /// <summary>The single entry point through which anything reduces a unit's HP.</summary>
    public static class Damage
    {
        public static Fixed Deal(SimulationContext<MatchState> context, EntityId attacker, MatchUnit target, Fixed rawAmount, DamageType type, bool isCritical = false)
        {
            if (!target.IsAlive)
                return Fixed.Zero;

            Fixed amount = DamageMath.Mitigate(rawAmount, type, target.Armor, target.MagicResistPercent, context.State.Ruleset.Combat.ArmorFactor);
            target.Hp = Fixed.Max(Fixed.Zero, target.Hp - amount);
            if (attacker.IsValid)
            {
                target.LastAttackerId = attacker;
                MatchUnit? source = context.State.Find(attacker);
                if (target.Hero != null && source?.Hero != null && source.Team != target.Team)
                    target.Hero.RecentHeroAttackers[attacker.Value] = context.Tick.Value;
            }
            context.Emit(new DamageDealt(attacker, target.Id, amount, target.Hp, type, isCritical));
            return amount;
        }

        public static Fixed Heal(SimulationContext<MatchState> context, EntityId source, MatchUnit target, Fixed amount)
        {
            if (!target.IsAlive)
                return Fixed.Zero;

            Fixed before = target.Hp;
            target.Hp = Fixed.Min(Fixed.FromInt(target.MaxHp), target.Hp + amount);
            Fixed restored = target.Hp - before;
            if (restored > Fixed.Zero)
                context.Emit(new HealApplied(source, target.Id, restored));
            return restored;
        }
    }
}
