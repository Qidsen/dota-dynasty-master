using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Enemy debuffs are not applied (Dota's debuff immunity, Black King Bar); damage still is.</summary>
        DebuffImmune = 32,

        /// <summary>Damage cannot bring the unit below 1 health (Shallow Grave, False Promise).</summary>
        CannotDie = 64,
    }

    /// <summary>
    /// An aura: while the modifier carrying it is active, units of the given team within the radius around its
    /// bearer keep receiving the aura's own modifier (refreshed regularly, so it fades soon after leaving).
    /// </summary>
    public sealed class AuraSpec
    {
        public AuraSpec(int radius, Rules.TargetTeam team, bool heroesOnly, ModifierSpec modifier)
        {
            Radius = radius;
            Team = team;
            HeroesOnly = heroesOnly;
            Modifier = modifier;
        }

        public int Radius { get; }
        public Rules.TargetTeam Team { get; }
        public bool HeroesOnly { get; }
        public ModifierSpec Modifier { get; }

        internal void WriteCanonical(Dynasty.Core.Hashing.CanonicalWriter w)
        {
            w.WriteInt64(Radius);
            w.WriteInt64((int)Team);
            w.WriteBool(HeroesOnly);
            Modifier.WriteCanonical(w);
        }
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
            Fixed hpRegenBonus = default,
            bool breakOnAction = false,
            int breakRadius = 0,
            bool dispellable = true,
            int strengthBonus = 0,
            int agilityBonus = 0,
            int intelligenceBonus = 0,
            Fixed manaRegenBonus = default,
            int moveSpeedBonus = 0,
            int attackRangeBonus = 0,
            int incomingDamagePercent = 0,
            int attackDamagePercent = 0,
            int evasionPercent = 0,
            IReadOnlyList<Rules.EffectDefinition>? onHit = null,
            int onHitChancePercent = 100,
            AuraSpec? aura = null,
            int cleavePercent = 0,
            int cleaveRadius = 0,
            int barrier = 0,
            bool consumedOnHit = false)
        {
            ConsumedOnHit = consumedOnHit;
            CleavePercent = cleavePercent;
            CleaveRadius = cleaveRadius;
            Barrier = barrier;
            IncomingDamagePercent = incomingDamagePercent;
            AttackDamagePercent = attackDamagePercent;
            EvasionPercent = evasionPercent;
            OnHit = onHit?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
            OnHitChancePercent = onHitChancePercent;
            Aura = aura;
            ManaRegenBonus = manaRegenBonus;
            MoveSpeedBonus = moveSpeedBonus;
            AttackRangeBonus = attackRangeBonus;
            StrengthBonus = strengthBonus;
            AgilityBonus = agilityBonus;
            IntelligenceBonus = intelligenceBonus;
            Dispellable = dispellable;
            BreakOnAction = breakOnAction;
            BreakRadius = breakRadius;
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

        /// <summary>Attribute bonuses (items, abilities); they matter for heroes with attributes.</summary>
        public int StrengthBonus { get; }
        public int AgilityBonus { get; }
        public int IntelligenceBonus { get; }

        public Fixed ManaRegenBonus { get; }

        /// <summary>Flat movement speed (boots), added before <see cref="MoveSpeedPercent"/> multiplies.</summary>
        public int MoveSpeedBonus { get; }

        public int AttackRangeBonus { get; }

        /// <summary>Percent added to the damage the unit takes (negative: damage reduction).</summary>
        public int IncomingDamagePercent { get; }

        /// <summary>Percent added to the unit's attack damage (negative: e.g. Breathe Fire's reduction).</summary>
        public int AttackDamagePercent { get; }

        /// <summary>Chance that an attack on the unit misses.</summary>
        public int EvasionPercent { get; }

        /// <summary>Effects run on the target of each of the unit's landing attacks (bash, bonus magic damage, slows), with <see cref="OnHitChancePercent"/>.</summary>
        public IReadOnlyList<Rules.EffectDefinition> OnHit { get; }
        public int OnHitChancePercent { get; }

        public AuraSpec? Aura { get; }

        /// <summary>Melee attacks also deal this percent of their damage to other enemies within <see cref="CleaveRadius"/> of the target.</summary>
        public int CleavePercent { get; }
        public int CleaveRadius { get; }

        /// <summary>Damage of any type this modifier absorbs before it is used up and removed.</summary>
        public int Barrier { get; }

        /// <summary>Removed once the unit's next attack lands ("next attack" buffs).</summary>
        public bool ConsumedOnHit { get; }

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

        /// <summary>Removed as soon as the bearer starts an attack or completes a cast (smoke).</summary>
        public bool BreakOnAction { get; }

        /// <summary>Removed when an enemy hero or tower comes this close to the bearer; 0 = never.</summary>
        public int BreakRadius { get; }

        /// <summary>Whether dispels may remove it (permanent modifiers never are, regardless).</summary>
        public bool Dispellable { get; }

        /// <summary>
        /// Harmful to its bearer: a disabling status, any negative stat change, or damage over time.
        /// Invisibility counts as a buff. Dispels on allies remove debuffs, on enemies buffs.
        /// </summary>
        public bool IsDebuff =>
            (Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted | ModifierFlags.Silenced | ModifierFlags.Disarmed)) != 0 ||
            MoveSpeedPercent < 0 || AttackSpeedBonus < 0 || ArmorBonus < Fixed.Zero || DamageBonus < 0 || MagicResistBonus < 0 ||
            DamagePerSecond > Fixed.Zero;

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
            w.WriteBool(BreakOnAction);
            w.WriteInt64(BreakRadius);
            w.WriteBool(Dispellable);
            w.WriteInt64(StrengthBonus);
            w.WriteInt64(AgilityBonus);
            w.WriteInt64(IntelligenceBonus);
            w.WriteFixed(ManaRegenBonus);
            w.WriteInt64(MoveSpeedBonus);
            w.WriteInt64(AttackRangeBonus);
            w.WriteInt64(IncomingDamagePercent);
            w.WriteInt64(AttackDamagePercent);
            w.WriteInt64(EvasionPercent);
            w.WriteInt64(OnHit.Count);
            foreach (Rules.EffectDefinition effect in OnHit)
                effect.WriteCanonical(w);
            w.WriteInt64(OnHitChancePercent);
            w.WriteBool(Aura != null);
            Aura?.WriteCanonical(w);
            w.WriteInt64(CleavePercent);
            w.WriteInt64(CleaveRadius);
            w.WriteInt64(Barrier);
            w.WriteBool(ConsumedOnHit);
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
            BarrierLeft = spec.Barrier;
        }

        public ModifierSpec Spec { get; }

        /// <summary>Damage the modifier's barrier can still absorb.</summary>
        public int BarrierLeft { get; internal set; }
        public EntityId Source { get; }
        public long AppliedTick { get; internal set; }

        /// <summary>Tick at which it is removed; -1 for permanent.</summary>
        public long ExpiresTick { get; internal set; }

        internal long NextPeriodTick { get; set; }
    }

    public static class Modifiers
    {
        /// <summary>Applies or refreshes the modifier; an enemy debuff on a debuff-immune unit is not applied (null).</summary>
        public static ActiveModifier? Apply(SimulationContext<MatchState> context, MatchUnit target, ModifierSpec spec, EntityId source)
        {
            if (spec.IsDebuff && (target.Flags & ModifierFlags.DebuffImmune) != 0 && context.State.Find(source)?.Team != target.Team)
                return null;
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
                existing.BarrierLeft = spec.Barrier;
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

        /// <summary>Removes the unit's break-on-action modifiers (it just attacked or cast).</summary>
        internal static void BreakOnAction(SimulationContext<MatchState> context, MatchUnit unit)
        {
            if (unit.Modifiers.Count == 0)
                return;
            foreach (ActiveModifier modifier in unit.Modifiers.Where(m => m.Spec.BreakOnAction).ToList())
                Remove(context, unit, modifier, "broken");
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
            if (target.IncomingDamagePercent != 0)
                amount = amount * Fixed.FromInt(100 + target.IncomingDamagePercent) / Fixed.FromInt(100);
            amount = Absorb(context, target, amount);
            if ((target.Flags & ModifierFlags.CannotDie) != 0)
                amount = Fixed.Max(Fixed.Zero, Fixed.Min(amount, target.Hp - Fixed.One));
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

        /// <summary>Barriers take the damage first, in the order they were applied; a used-up barrier is removed.</summary>
        private static Fixed Absorb(SimulationContext<MatchState> context, MatchUnit target, Fixed amount)
        {
            foreach (ActiveModifier modifier in target.Modifiers.Where(m => m.BarrierLeft > 0).ToList())
            {
                if (amount <= Fixed.Zero)
                    break;
                Fixed absorbed = Fixed.Min(amount, Fixed.FromInt(modifier.BarrierLeft));
                modifier.BarrierLeft -= absorbed.ToIntFloor() + (absorbed > Fixed.FromInt(absorbed.ToIntFloor()) ? 1 : 0);
                amount -= absorbed;
                if (modifier.BarrierLeft <= 0)
                    Modifiers.Remove(context, target, modifier, "barrier_used");
            }

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
