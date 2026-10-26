using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    public enum AbilityTargeting
    {
        /// <summary>Instant, centered on the caster.</summary>
        NoTarget = 0,
        Unit = 1,
        Point = 2,

        /// <summary>Never cast; grants its level's modifier permanently once learned.</summary>
        Passive = 3,
    }

    /// <summary>Which units a targeted ability or an area effect may affect, relative to the caster.</summary>
    public enum TargetTeam
    {
        Enemy = 0,

        /// <summary>Allies including the caster.</summary>
        Ally = 1,
        Any = 2,
    }

    public enum EffectKind
    {
        Damage = 0,
        Heal = 1,
        ApplyModifier = 2,

        /// <summary>Applies inner effects to every matching unit in a radius.</summary>
        Area = 3,

        /// <summary>Moves the caster next to the target unit (or to the target point).</summary>
        Dash = 4,

        /// <summary>Pushes the target directly away from the caster.</summary>
        Knockback = 5,

        /// <summary>Spawns temporary units of a creep type that fight for the caster's team.</summary>
        Summon = 6,

        /// <summary>Moves the caster next to the target (an allied structure), on its own base side.</summary>
        Teleport = 7,

        /// <summary>Removes debuffs from an ally or buffs from an enemy; stuns only with a strong dispel.</summary>
        Dispel = 8,

        /// <summary>Launches a homing projectile at the target; the inner effects apply on arrival. Dodged by disjoints.</summary>
        Projectile = 9,

        /// <summary>Burns the target's mana (amounts) and deals <see cref="EffectDefinition.DamagePercent"/> of the mana burnt as damage.</summary>
        ManaBurn = 10,
    }

    /// <summary>What a damage effect's amounts mean: flat damage, or a percent of the target's health or mana.</summary>
    public enum DamageScaling
    {
        Flat = 0,
        TargetMaxHealthPercent = 1,
        TargetMissingHealthPercent = 2,
        TargetMissingManaPercent = 3,
        TargetCurrentHealthPercent = 4,
    }

    public enum AreaCenter
    {
        Caster = 0,
        Target = 1,
        Point = 2,
    }

    /// <summary>
    /// One composable effect primitive. Per-level arrays are indexed by ability level; a shorter
    /// array repeats its last value.
    /// </summary>
    public sealed class EffectDefinition
    {
        private EffectDefinition(
            EffectKind kind,
            int[]? amounts = null,
            DamageType damageType = DamageType.Magical,
            ModifierSpec[]? modifiers = null,
            int radius = 0,
            AreaCenter center = AreaCenter.Caster,
            TargetTeam team = TargetTeam.Enemy,
            EffectDefinition[]? inner = null,
            string? summonTypeId = null,
            int durationMs = 0,
            bool atPoint = false,
            bool heroesOnly = false,
            bool strong = false,
            int speed = 0,
            DamageScaling scaling = DamageScaling.Flat,
            int damagePercent = 0)
        {
            Scaling = scaling;
            DamagePercent = damagePercent;
            Speed = speed;
            AtPoint = atPoint;
            HeroesOnly = heroesOnly;
            Strong = strong;
            Kind = kind;
            Amounts = amounts ?? Array.Empty<int>();
            DamageType = damageType;
            Modifiers = modifiers ?? Array.Empty<ModifierSpec>();
            Radius = radius;
            Center = center;
            Team = team;
            Inner = inner ?? Array.Empty<EffectDefinition>();
            SummonTypeId = summonTypeId;
            DurationMs = durationMs;
        }

        public EffectKind Kind { get; }

        /// <summary>Damage, heal, knockback distance or summon count per level.</summary>
        public IReadOnlyList<int> Amounts { get; }

        public DamageType DamageType { get; }
        public IReadOnlyList<ModifierSpec> Modifiers { get; }
        public int Radius { get; }
        public AreaCenter Center { get; }
        public TargetTeam Team { get; }
        public IReadOnlyList<EffectDefinition> Inner { get; }
        public string? SummonTypeId { get; }

        /// <summary>Summon lifetime.</summary>
        public int DurationMs { get; }

        /// <summary>Summons appear at the cast point rather than at the caster.</summary>
        public bool AtPoint { get; }

        /// <summary>Area effects only: affect heroes, not creeps or summons.</summary>
        public bool HeroesOnly { get; }

        /// <summary>Dispels only: also removes stuns.</summary>
        public bool Strong { get; }

        /// <summary>Projectiles only: travel speed in units per second.</summary>
        public int Speed { get; }

        /// <summary>Damage effects: how the amounts scale (flat by default).</summary>
        public DamageScaling Scaling { get; }

        /// <summary>Mana burn: percent of the mana burnt dealt as damage.</summary>
        public int DamagePercent { get; }

        public static EffectDefinition Damage(DamageType type, params int[] perLevel) =>
            new EffectDefinition(EffectKind.Damage, perLevel, type);

        public static EffectDefinition Heal(params int[] perLevel) => new EffectDefinition(EffectKind.Heal, perLevel);

        /// <summary>Damage as a percent (per level, in hundredths of a percent) of the target's max, missing or current health or missing mana.</summary>
        public static EffectDefinition ScaledDamage(DamageType type, DamageScaling scaling, params int[] percentPerLevel) =>
            new EffectDefinition(EffectKind.Damage, percentPerLevel, type, scaling: scaling);

        /// <summary>Burns mana (per level) and deals the given percent of the mana burnt as damage.</summary>
        public static EffectDefinition ManaBurn(int damagePercent, DamageType type, params int[] manaPerLevel) =>
            new EffectDefinition(EffectKind.ManaBurn, manaPerLevel, type, damagePercent: damagePercent);

        public static EffectDefinition Modifier(params ModifierSpec[] perLevel) =>
            new EffectDefinition(EffectKind.ApplyModifier, modifiers: perLevel);

        public static EffectDefinition Area(int radius, AreaCenter center, TargetTeam team, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Area, radius: radius, center: center, team: team, inner: inner);

        /// <summary>Area restricted to heroes when <paramref name="heroesOnly"/> (e.g. smoke).</summary>
        public static EffectDefinition Area(int radius, AreaCenter center, TargetTeam team, bool heroesOnly, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Area, radius: radius, center: center, team: team, inner: inner, heroesOnly: heroesOnly);

        public static EffectDefinition Dash() => new EffectDefinition(EffectKind.Dash);

        public static EffectDefinition Projectile(int speed, params EffectDefinition[] onHit) =>
            new EffectDefinition(EffectKind.Projectile, inner: onHit, speed: speed);

        public static EffectDefinition Dispel(bool strong) => new EffectDefinition(EffectKind.Dispel, strong: strong);

        /// <summary>Use with a long cast point as the channel; stuns and silences interrupt it.</summary>
        public static EffectDefinition Teleport() => new EffectDefinition(EffectKind.Teleport);

        public static EffectDefinition Knockback(params int[] distancePerLevel) =>
            new EffectDefinition(EffectKind.Knockback, distancePerLevel);

        public static EffectDefinition Summon(string creepTypeId, int durationMs, params int[] countPerLevel) =>
            new EffectDefinition(EffectKind.Summon, countPerLevel, summonTypeId: creepTypeId, durationMs: durationMs);

        /// <summary>Summon at the cast point instead of next to the caster (wards); point-target abilities only.</summary>
        public static EffectDefinition SummonAtPoint(string creepTypeId, int durationMs, params int[] countPerLevel) =>
            new EffectDefinition(EffectKind.Summon, countPerLevel, summonTypeId: creepTypeId, durationMs: durationMs, atPoint: true);

        public int AmountAt(int level) => ValueAt(Amounts, level);

        public ModifierSpec ModifierAt(int level) => ValueAt(Modifiers, level);

        internal static T ValueAt<T>(IReadOnlyList<T> values, int level) => values[Math.Min(Math.Max(level, 1), values.Count) - 1];

        internal IEnumerable<EffectDefinition> SelfAndDescendants()
        {
            yield return this;
            foreach (EffectDefinition child in Inner)
            {
                foreach (EffectDefinition nested in child.SelfAndDescendants())
                    yield return nested;
            }
        }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64((int)Kind);
            w.WriteInt64(Amounts.Count);
            foreach (int amount in Amounts)
                w.WriteInt64(amount);
            w.WriteInt64((int)DamageType);
            w.WriteInt64(Modifiers.Count);
            foreach (ModifierSpec modifier in Modifiers)
                modifier.WriteCanonical(w);
            w.WriteInt64(Radius);
            w.WriteInt64((int)Center);
            w.WriteInt64((int)Team);
            w.WriteString(SummonTypeId ?? "");
            w.WriteInt64(DurationMs);
            w.WriteBool(AtPoint);
            w.WriteBool(HeroesOnly);
            w.WriteBool(Strong);
            w.WriteInt64(Speed);
            w.WriteInt64((int)Scaling);
            w.WriteInt64(DamagePercent);
            w.WriteInt64(Inner.Count);
            foreach (EffectDefinition child in Inner)
                child.WriteCanonical(w);
        }
    }

    public sealed class AbilityDefinition
    {
        public AbilityDefinition(
            string id,
            string name,
            AbilityTargeting targeting,
            TargetTeam targetTeam,
            int castRange,
            int castPointMs,
            IReadOnlyList<int> cooldownMs,
            IReadOnlyList<int> manaCost,
            IReadOnlyList<int> requiredHeroLevel,
            IReadOnlyList<EffectDefinition> effects,
            IReadOnlyList<ModifierSpec>? passiveModifiers = null,
            bool targetsStructures = false,
            bool innate = false)
        {
            Innate = innate;
            TargetsStructures = targetsStructures;
            Id = id;
            Name = name;
            Targeting = targeting;
            TargetTeam = targetTeam;
            CastRange = castRange;
            CastPointMs = castPointMs;
            CooldownMs = cooldownMs.ToArray();
            ManaCost = manaCost.ToArray();
            RequiredHeroLevel = requiredHeroLevel.ToArray();
            Effects = effects.ToArray();
            PassiveModifiers = (passiveModifiers ?? Array.Empty<ModifierSpec>()).ToArray();
        }

        public string Id { get; }
        public string Name { get; }
        public AbilityTargeting Targeting { get; }
        public TargetTeam TargetTeam { get; }
        public int CastRange { get; }
        public int CastPointMs { get; }
        public IReadOnlyList<int> CooldownMs { get; }
        public IReadOnlyList<int> ManaCost { get; }

        /// <summary>Hero level needed for each ability level; its length is the ability's max level.</summary>
        public IReadOnlyList<int> RequiredHeroLevel { get; }

        public int MaxLevel => RequiredHeroLevel.Count;
        public IReadOnlyList<EffectDefinition> Effects { get; }

        /// <summary>Permanent modifier per level for passive abilities.</summary>
        public IReadOnlyList<ModifierSpec> PassiveModifiers { get; }

        /// <summary>Unit-target abilities only: allied structures are valid targets (teleports).</summary>
        public bool TargetsStructures { get; }

        /// <summary>Learned at level one for free and never levelled with skill points (Dota's innate abilities).</summary>
        public bool Innate { get; }

        public int CooldownAt(int level) => EffectDefinition.ValueAt(CooldownMs, level);

        /// <summary>Mana needed to cast at the given level; passives (no costs listed) need none.</summary>
        public int ManaCostAt(int level) => ManaCost.Count == 0 ? 0 : EffectDefinition.ValueAt(ManaCost, level);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteString(Name);
            w.WriteInt64((int)Targeting);
            w.WriteInt64((int)TargetTeam);
            w.WriteInt64(CastRange);
            w.WriteInt64(CastPointMs);
            foreach (IReadOnlyList<int> list in new[] { CooldownMs, ManaCost, RequiredHeroLevel })
            {
                w.WriteInt64(list.Count);
                foreach (int value in list)
                    w.WriteInt64(value);
            }

            w.WriteInt64(Effects.Count);
            foreach (EffectDefinition effect in Effects)
                effect.WriteCanonical(w);
            w.WriteInt64(PassiveModifiers.Count);
            foreach (ModifierSpec modifier in PassiveModifiers)
                modifier.WriteCanonical(w);
            w.WriteBool(TargetsStructures);
            w.WriteBool(Innate);
        }
    }
}
