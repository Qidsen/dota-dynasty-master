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

        /// <summary>Restores the target's mana (amounts), not above its max.</summary>
        RestoreMana = 11,

        /// <summary>Runs the inner effects on the caster (a self buff inside a targeted ability or an attack's on-hit effects).</summary>
        OnCaster = 12,

        /// <summary>The caster instantly attacks the target with a percent (amounts) of its attack damage; the attack's on-hit effects apply.</summary>
        Attack = 13,

        /// <summary>Leaves a zone on the ground (radius, team, duration) that runs the inner effects on the units inside every period.</summary>
        Zone = 14,

        /// <summary>Pulls the target toward the caster or the point (<see cref="EffectDefinition.Center"/>) by up to the amounts, without passing it.</summary>
        Pull = 15,

        /// <summary>Creates illusions (amounts per level) of the target hero for the duration, each carrying the level's modifier (damage dealt and taken).</summary>
        Illusion = 16,

        /// <summary>Runs the inner effects only when the effect's unit is a hero.</summary>
        IfHero = 17,

        /// <summary>Kills the unit outright if its health is at or below the amount (Culling Blade's threshold).</summary>
        Execute = 18,

        /// <summary>
        /// Ends the cooldowns of the caster's abilities and items (Dota's Refresher Orb and Shard), except those of abilities that
        /// refresh cooldowns themselves.
        /// </summary>
        RefreshCooldowns = 19,

        /// <summary>Runs its inner effects only on a melee target (Dota's Illusion rune: melee illusions take 200%).</summary>
        IfMelee = 20,

        /// <summary>Runs its inner effects only on a ranged target.</summary>
        IfRanged = 21,
    }

    /// <summary>What a damage effect's amounts mean: flat damage, or a percent of the target's health or mana.</summary>
    public enum DamageScaling
    {
        Flat = 0,
        TargetMaxHealthPercent = 1,
        TargetMissingHealthPercent = 2,
        TargetMissingManaPercent = 3,
        TargetCurrentHealthPercent = 4,
        TargetMaxManaPercent = 5,

        /// <summary>A percent of the caster's average attack damage.</summary>
        CasterAttackDamagePercent = 6,
        CasterStrengthPercent = 7,
        CasterAgilityPercent = 8,
        CasterIntelligencePercent = 9,

        /// <summary>Flat damage for each stack of the modifier <see cref="EffectDefinition.StackKey"/> the caster put on the target.</summary>
        PerTargetStack = 10,
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
            int damagePercent = 0,
            string? stackKey = null,
            int periodMs = 0)
        {
            PeriodMs = periodMs;
            StackKey = stackKey;
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

        /// <summary>Damage per stack: the key of the stacking modifier counted on the target.</summary>
        public string? StackKey { get; }

        /// <summary>Zones only: time between pulses; 0 = continuous (modifiers refreshed like an aura's).</summary>
        public int PeriodMs { get; }

        public static EffectDefinition Damage(DamageType type, params int[] perLevel) =>
            new EffectDefinition(EffectKind.Damage, perLevel, type);

        public static EffectDefinition Heal(params int[] perLevel) => new EffectDefinition(EffectKind.Heal, perLevel);

        /// <summary>Damage as a percent (per level, in hundredths of a percent) of the target's max, missing or current health or missing mana.</summary>
        public static EffectDefinition ScaledDamage(DamageType type, DamageScaling scaling, params int[] percentPerLevel) =>
            new EffectDefinition(EffectKind.Damage, percentPerLevel, type, scaling: scaling);

        /// <summary>Burns mana (per level) and deals the given percent of the mana burnt as damage.</summary>
        public static EffectDefinition ManaBurn(int damagePercent, DamageType type, params int[] manaPerLevel) =>
            new EffectDefinition(EffectKind.ManaBurn, manaPerLevel, type, damagePercent: damagePercent);

        /// <summary>Damage for each stack of the caster's modifier <paramref name="stackKey"/> on the target (Fury Swipes).</summary>
        public static EffectDefinition DamagePerStack(DamageType type, string stackKey, params int[] perStackPerLevel) =>
            new EffectDefinition(EffectKind.Damage, perStackPerLevel, type, scaling: DamageScaling.PerTargetStack, stackKey: stackKey);

        public static EffectDefinition OnCaster(params EffectDefinition[] inner) => new EffectDefinition(EffectKind.OnCaster, inner: inner);

        /// <summary>An instant attack with the given percent of the attack's damage per level (100 = a normal attack).</summary>
        public static EffectDefinition Attack(params int[] damagePercentPerLevel) => new EffectDefinition(EffectKind.Attack, damagePercentPerLevel);

        /// <summary>A zone at the caster, the target or the point that runs <paramref name="inner"/> on the units of <paramref name="team"/> inside every period.</summary>
        public static EffectDefinition Zone(int radius, AreaCenter center, TargetTeam team, int durationMs, int periodMs, params EffectDefinition[] inner) =>
            Zone(radius, center, team, durationMs, periodMs, false, inner);

        /// <summary>A zone affecting heroes only when <paramref name="heroesOnly"/> (Mystic Flare).</summary>
        public static EffectDefinition Zone(int radius, AreaCenter center, TargetTeam team, int durationMs, int periodMs, bool heroesOnly, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Zone, radius: radius, center: center, team: team, durationMs: durationMs, periodMs: periodMs, inner: inner, heroesOnly: heroesOnly);

        /// <summary>Pulls the target toward the caster or the point by up to the distance (per level).</summary>
        public static EffectDefinition Pull(AreaCenter toward, params int[] distancePerLevel) =>
            new EffectDefinition(EffectKind.Pull, distancePerLevel, center: toward);

        /// <summary>Illusions of the effect's unit (counts per level) that last <paramref name="durationMs"/> and carry the level's modifier.</summary>
        public static EffectDefinition Illusion(int durationMs, ModifierSpec[] modifierPerLevel, params int[] countPerLevel) =>
            new EffectDefinition(EffectKind.Illusion, countPerLevel, modifiers: modifierPerLevel, durationMs: durationMs);

        public static EffectDefinition IfHero(params EffectDefinition[] inner) => new EffectDefinition(EffectKind.IfHero, inner: inner);

        /// <summary>Kills a unit whose health is at or below the threshold (per level).</summary>
        public static EffectDefinition Execute(params int[] thresholdPerLevel) => new EffectDefinition(EffectKind.Execute, thresholdPerLevel);

        public static EffectDefinition RestoreMana(params int[] manaPerLevel) => new EffectDefinition(EffectKind.RestoreMana, manaPerLevel);

        public static EffectDefinition RefreshCooldowns() => new EffectDefinition(EffectKind.RefreshCooldowns);

        public static EffectDefinition IfMelee(params EffectDefinition[] inner) => new EffectDefinition(EffectKind.IfMelee, inner: inner);

        public static EffectDefinition IfRanged(params EffectDefinition[] inner) => new EffectDefinition(EffectKind.IfRanged, inner: inner);

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
            w.WriteString(StackKey ?? "");
            w.WriteInt64(PeriodMs);
            w.WriteInt64(Inner.Count);
            foreach (EffectDefinition child in Inner)
                child.WriteCanonical(w);
        }
    }

    /// <summary>An ability as upgraded by a set of talents, "scepter" and/or "shard".</summary>
    public sealed class AbilityVariant
    {
        public AbilityVariant(IReadOnlyList<string> upgrades, AbilityDefinition definition)
        {
            Upgrades = upgrades.OrderBy(u => u, StringComparer.Ordinal).ToArray();
            Definition = definition;
        }

        /// <summary>The upgrades this variant is for, in ordinal order.</summary>
        public IReadOnlyList<string> Upgrades { get; }

        public AbilityDefinition Definition { get; }
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
            bool innate = false,
            IReadOnlyList<int>? maxCharges = null,
            IReadOnlyList<int>? chargeRestoreMs = null,
            IReadOnlyList<AbilityVariant>? variants = null)
        {
            Variants = (variants ?? Array.Empty<AbilityVariant>()).ToArray();
            MaxCharges = (maxCharges ?? Array.Empty<int>()).ToArray();
            ChargeRestoreMs = (chargeRestoreMs ?? Array.Empty<int>()).ToArray();
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

        /// <summary>Upgraded versions (talents, Aghanim's Scepter and Shard); the ability itself is the version without upgrades.</summary>
        public IReadOnlyList<AbilityVariant> Variants { get; }

        /// <summary>
        /// The version for the hero's upgrades: the variant whose upgrades are exactly those of the hero that
        /// concern this ability (failing that, the one covering most of them), or the ability itself.
        /// </summary>
        public AbilityDefinition VariantFor(ICollection<string> upgrades)
        {
            if (Variants.Count == 0 || upgrades.Count == 0)
                return this;
            AbilityDefinition best = this;
            int bestCount = 0;
            foreach (AbilityVariant variant in Variants)
            {
                if (!variant.Upgrades.All(upgrades.Contains) || variant.Upgrades.Count <= bestCount)
                    continue;
                best = variant.Definition;
                bestCount = variant.Upgrades.Count;
            }

            return best;
        }

        /// <summary>Charges per level (Shrapnel, Phantom Strike); empty for abilities without charges.</summary>
        public IReadOnlyList<int> MaxCharges { get; }

        /// <summary>Time for a spent charge to come back, per level.</summary>
        public IReadOnlyList<int> ChargeRestoreMs { get; }

        public int MaxChargesAt(int level) => MaxCharges.Count == 0 || level <= 0 ? 0 : EffectDefinition.ValueAt(MaxCharges, level);

        public int ChargeRestoreAt(int level) => EffectDefinition.ValueAt(ChargeRestoreMs, level);

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
            foreach (IReadOnlyList<int> list in new[] { CooldownMs, ManaCost, RequiredHeroLevel, MaxCharges, ChargeRestoreMs })
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
            w.WriteInt64(Variants.Count);
            foreach (AbilityVariant variant in Variants)
            {
                w.WriteInt64(variant.Upgrades.Count);
                foreach (string upgrade in variant.Upgrades)
                    w.WriteString(upgrade);
                variant.Definition.WriteCanonical(w);
            }
        }
    }
}
