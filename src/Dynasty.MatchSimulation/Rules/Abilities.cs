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

        /// <summary>
        /// Slashes (Dota's Omnislash): the caster gets the level's modifier and, while it lasts, makes one normal attack every
        /// attack interval divided by the rate (<see cref="EffectDefinition.Amounts"/>, percent) on a random visible enemy within
        /// <see cref="EffectDefinition.Radius"/>, moved next to it, starting with the target; no enemy near ends them.
        /// </summary>
        Slashes = 22,

        /// <summary>
        /// The inner effects on the matching units within <see cref="EffectDefinition.Radius"/> of a random point between
        /// <see cref="EffectDefinition.MinDistance"/> and <see cref="EffectDefinition.MaxDistance"/> from the caster (Freezing Field).
        /// </summary>
        RandomArea = 23,

        /// <summary>Experience (<see cref="EffectDefinition.Amounts"/> per level) for the caster (Backstab's bonus for a hero kill).</summary>
        Experience = 24,

        /// <summary>The caster and the target exchange positions; the target's channel is interrupted (Nether Swap).</summary>
        SwapPositions = 25,

        /// <summary>The inner effects only when the target is of <see cref="EffectDefinition.Team"/> to the caster (an enemy or an ally).</summary>
        IfTeam = 26,

        /// <summary>
        /// The caster and the target exchange their shares of current health, each kept at least at <see cref="EffectDefinition.Amounts"/>
        /// percent; a debuff-immune enemy keeps its health (Sunder).
        /// </summary>
        SwapHealthPercent = 27,

        /// <summary>The inner effects on the matching units within <see cref="EffectDefinition.Radius"/> of the line from the caster to the target (Tether's slow).</summary>
        LineArea = 28,

        /// <summary>
        /// The target goes back to where it was <see cref="EffectDefinition.DurationMs"/> ago (with its health and mana then when
        /// <see cref="EffectDefinition.Strong"/>: Time Lapse), then the inner effects run on it (Glimpse's damage by the distance); an
        /// illusion is killed at once.
        /// </summary>
        Rewind = 29,

        /// <summary>
        /// A ring wall of <see cref="EffectDefinition.Radius"/> that stands after <see cref="EffectDefinition.DurationMs"/> (formation)
        /// for <see cref="EffectDefinition.Amounts"/> milliseconds per level; units of <see cref="EffectDefinition.Team"/> to the caster
        /// can't cross it either way, walking or pushed; debuff-immune units pass (Kinetic Field, Arena of Blood) unless
        /// <see cref="EffectDefinition.Strong"/> (a barrier of ice). With <see cref="EffectDefinition.MaxDistance"/> degrees the wall is
        /// an arc of that size, open towards the caster (Ice Shards).
        /// </summary>
        Wall = 30,

        /// <summary>
        /// Summons (amounts per level) of <see cref="EffectDefinition.SummonTypeId"/> next to the target, for the caster's owner when the
        /// caster is a summon itself, bound to the target: they attack only it and die when it is dead or unseen (Tombstone's zombies).
        /// <see cref="EffectDefinition.DurationMs"/> below zero: until they die. The inner effects run on each summoned unit.
        /// </summary>
        SummonNear = 31,

        /// <summary>
        /// Springs the caster's summon of <see cref="EffectDefinition.SummonTypeId"/> nearest to the caster (Psionic Trap): its charge
        /// rises from <see cref="EffectDefinition.DamagePercent"/> to 100 percent over <see cref="EffectDefinition.DurationMs"/> since it was
        /// placed; the inner effects run with it as the caster at its position (their slows weakened to the charge, damage of
        /// <see cref="DamageScaling.FullChargeOnly"/> only at full charge), then it is gone.
        /// </summary>
        Spring = 32,

        /// <summary>
        /// Binds the target to its nearest allied hero within <see cref="EffectDefinition.Radius"/> (Soulbind): both get the level's
        /// modifier, linked to each other; neither can go farther from the other than the distance at binding times
        /// <see cref="EffectDefinition.DamagePercent"/> percent plus <see cref="EffectDefinition.MinDistance"/>; a unit-targeted ability
        /// either receives is cast on the other too. With no ally near, the target alone gets the modifier.
        /// </summary>
        Bind = 33,

        /// <summary>Kills the target outright (Midnight Feast's creep).</summary>
        Kill = 34,

        /// <summary>Ends now every modifier <see cref="EffectDefinition.StackKey"/> the caster put on any unit, as if it ran out (Shadow Poison Release).</summary>
        Release = 35,

        /// <summary>
        /// An illusion of the caster next to the target, bound to it (it attacks only that unit and goes with it), carrying the level's
        /// modifier (damage dealt and taken), for <see cref="EffectDefinition.DurationMs"/> (Shadow Step, Haunt).
        /// </summary>
        Haunt = 36,

        /// <summary>The caster goes to its own illusion nearest to the point, which is destroyed (Reality).</summary>
        Assume = 37,

        /// <summary>
        /// The inner effects on one random enemy the caster's team sees within <see cref="EffectDefinition.Radius"/> that has fewer than
        /// <see cref="EffectDefinition.MaxTargets"/> stacks of the marker <see cref="EffectDefinition.StackKey"/> from the caster; the
        /// marker (the level's modifier) gets a stack (Eclipse's beams). Without a marker: <see cref="EffectDefinition.Amounts"/> strikes
        /// per run, each at random, enemy buildings that can be hurt too when <see cref="EffectDefinition.Strong"/> (Exorcism's spirits).
        /// </summary>
        RandomStrike = 38,
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

        /// <summary>Hundredths of a percent of the caster's current mana (Arcane Orb).</summary>
        CasterCurrentManaPercent = 11,

        /// <summary>Hundredths of a percent of the distance the target's last rewind moved it (Glimpse).</summary>
        RewoundDistancePercent = 13,

        /// <summary>Hundredths of a percent of the distance from the caster to the target, at most the effect's radius when set (Impetus).</summary>
        DistanceToTargetPercent = 12,

        /// <summary>Flat amounts, only when the caster (a sprung trap) is fully charged (see <see cref="EffectKind.Spring"/>).</summary>
        FullChargeOnly = 15,

        /// <summary>Amounts for every second the caster has been channelling, at most <see cref="EffectDefinition.Ceiling"/> (Upheaval).</summary>
        PerChannelSecond = 16,

        /// <summary>
        /// Shadow Poison's deferred damage: the amounts times 1, 2, 4… by the ending modifier's stacks up to <see cref="EffectDefinition.MaxTargets"/>
        /// stacks, and <see cref="EffectDefinition.DamagePercent"/> more for each stack beyond.
        /// </summary>
        DoublingPerStack = 18,

        /// <summary>
        /// Amounts for every unit within <see cref="EffectDefinition.Radius"/> of the caster that its team sees, up to
        /// <see cref="EffectDefinition.MaxTargets"/>: heroes, creeps and summons of both teams, not the caster, not invulnerable ones nor
        /// debuff-immune enemies (Soul Rip; damage or healing).
        /// </summary>
        PerUnitAround = 17,

        /// <summary>
        /// Flat amounts, in full within <see cref="EffectDefinition.MinDistance"/> of the caster and falling evenly to
        /// <see cref="EffectDefinition.DamagePercent"/> at <see cref="EffectDefinition.Radius"/> (Proximity Mines).
        /// </summary>
        DistanceFalloff = 14,
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
            int periodMs = 0,
            int minDistance = 0,
            int maxDistance = 0,
            bool abilitiesOnly = false,
            int maxTargets = 0,
            int[]? floor = null,
            int[]? ceiling = null,
            bool lowestHealthFirst = false)
        {
            LowestHealthFirst = lowestHealthFirst;
            Floor = floor ?? Array.Empty<int>();
            Ceiling = ceiling ?? Array.Empty<int>();
            MaxTargets = maxTargets;
            AbilitiesOnly = abilitiesOnly;
            MinDistance = minDistance;
            MaxDistance = maxDistance;
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

        /// <summary>Scaled damage only: the least and the most per level (Glimpse: 25 to 100–280); empty for no bound.</summary>
        public IReadOnlyList<int> Floor { get; }

        public IReadOnlyList<int> Ceiling { get; }

        /// <summary>Areas only: at most this many units, the nearest to the centre first (Fatal Bonds); 0: all of them.</summary>
        public int MaxTargets { get; }

        /// <summary>Areas with <see cref="MaxTargets"/>: the units with the least health first (Eye of the Storm), not the nearest.</summary>
        public bool LowestHealthFirst { get; }

        /// <summary>
        /// The effect works at the caster's current level of this ability, not of its own (Eclipse's beams are Lucent Beam's); nothing
        /// while that ability is not learned.
        /// </summary>
        public string? LevelOf { get; private set; }

        /// <summary>Damage only: the share of it a building takes (Exorcism: 50); 0 for the full damage.</summary>
        public int StructurePercent { get; private set; }

        /// <summary>The damage with <paramref name="percent"/> of it to buildings (see <see cref="StructurePercent"/>).</summary>
        public EffectDefinition WithStructurePercent(int percent)
        {
            StructurePercent = percent;
            return this;
        }

        /// <summary>The effect at the caster's level of <paramref name="abilityId"/> (see <see cref="LevelOf"/>).</summary>
        public EffectDefinition LevelledBy(string abilityId)
        {
            LevelOf = abilityId;
            return this;
        }

        /// <summary>Cooldown refreshes only: the hero's abilities, not its items (Dota's Rearm in 7.41).</summary>
        public bool AbilitiesOnly { get; }

        /// <summary>Random areas only: the ring their centres fall in, around the caster.</summary>
        public int MinDistance { get; }

        public int MaxDistance { get; }

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

        /// <summary>Scaled damage kept between a floor and a ceiling per level (Glimpse).</summary>
        public static EffectDefinition BoundedScaledDamage(DamageType type, DamageScaling scaling, int[] floor, int[] ceiling, params int[] percentPerLevel) =>
            new EffectDefinition(EffectKind.Damage, percentPerLevel, type, scaling: scaling, floor: floor, ceiling: ceiling);

        /// <summary>See <see cref="DamageScaling.DoublingPerStack"/>.</summary>
        public static EffectDefinition DamageByStacksDoubling(DamageType type, int doublingStacks, int extraPerStack, params int[] stackDamagePerLevel) =>
            new EffectDefinition(EffectKind.Damage, stackDamagePerLevel, type, scaling: DamageScaling.DoublingPerStack, maxTargets: doublingStacks, damagePercent: extraPerStack);

        /// <summary>See <see cref="EffectKind.RandomStrike"/>.</summary>
        public static EffectDefinition RandomStrike(int radius, TargetTeam team, string markerKey, int hitsPerUnit, ModifierSpec marker, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.RandomStrike, radius: radius, team: team, stackKey: markerKey, maxTargets: hitsPerUnit, modifiers: new[] { marker }, inner: inner);

        /// <summary><paramref name="strikesPerLevel"/> random strikes a run without a marker, on enemy buildings too when <paramref name="buildings"/> (Exorcism).</summary>
        public static EffectDefinition RandomStrike(int radius, TargetTeam team, int[] strikesPerLevel, bool buildings, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.RandomStrike, strikesPerLevel, radius: radius, team: team, strong: buildings, inner: inner);

        /// <summary>Overload for a single level.</summary>
        public static EffectDefinition RandomStrike(int radius, TargetTeam team, int strikes, bool buildings, params EffectDefinition[] inner) =>
            RandomStrike(radius, team, new[] { strikes }, buildings, inner);

        /// <summary>See <see cref="EffectKind.Haunt"/>.</summary>
        public static EffectDefinition Haunt(int durationMs, ModifierSpec[] modifierPerLevel) =>
            new EffectDefinition(EffectKind.Haunt, modifiers: modifierPerLevel, durationMs: durationMs);

        /// <summary>See <see cref="EffectKind.Assume"/>.</summary>
        public static EffectDefinition Assume() => new EffectDefinition(EffectKind.Assume);

        /// <summary>See <see cref="EffectKind.Release"/>.</summary>
        public static EffectDefinition Release(string modifierKey) => new EffectDefinition(EffectKind.Release, stackKey: modifierKey);

        /// <summary>See <see cref="EffectKind.Kill"/>.</summary>
        public static EffectDefinition Kill() => new EffectDefinition(EffectKind.Kill);

        /// <summary>Healing by a scaling (hundredths of a percent): the target's max health (Midnight Feast on the caster).</summary>
        public static EffectDefinition ScaledHeal(DamageScaling scaling, params int[] hundredthsPerLevel) =>
            new EffectDefinition(EffectKind.Heal, hundredthsPerLevel, scaling: scaling);

        /// <summary>Mana restored by a scaling (hundredths of a percent): the target's max mana (Midnight Feast on the caster).</summary>
        public static EffectDefinition ScaledRestoreMana(DamageScaling scaling, params int[] hundredthsPerLevel) =>
            new EffectDefinition(EffectKind.RestoreMana, hundredthsPerLevel, scaling: scaling);

        /// <summary>See <see cref="EffectKind.Bind"/>.</summary>
        public static EffectDefinition Bind(int radius, int leashPercent, int leashBuffer, params ModifierSpec[] perLevel) =>
            new EffectDefinition(EffectKind.Bind, radius: radius, damagePercent: leashPercent, minDistance: leashBuffer, modifiers: perLevel);

        /// <summary>See <see cref="EffectKind.Spring"/>.</summary>
        public static EffectDefinition Spring(string trapTypeId, int chargeMs, int minChargePercent, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Spring, summonTypeId: trapTypeId, durationMs: chargeMs, damagePercent: minChargePercent, inner: inner);

        /// <summary>Damage or healing (<paramref name="kind"/>) of <see cref="DamageScaling.PerUnitAround"/>.</summary>
        public static EffectDefinition PerUnitAround(EffectKind kind, DamageType type, int radius, int maxUnits, params int[] perUnit) =>
            new EffectDefinition(kind, perUnit, type, scaling: DamageScaling.PerUnitAround, radius: radius, maxTargets: maxUnits);

        /// <summary>See <see cref="DamageScaling.PerChannelSecond"/>.</summary>
        public static EffectDefinition ChannelGrowthDamage(DamageType type, int[] maximum, params int[] perSecond) =>
            new EffectDefinition(EffectKind.Damage, perSecond, type, scaling: DamageScaling.PerChannelSecond, ceiling: maximum);

        /// <summary>See <see cref="DamageScaling.FullChargeOnly"/>.</summary>
        public static EffectDefinition FullChargeDamage(DamageType type, params int[] perLevel) =>
            new EffectDefinition(EffectKind.Damage, perLevel, type, scaling: DamageScaling.FullChargeOnly);

        /// <summary>
        /// A summon keeping at most <paramref name="maxAlive"/> (per level, in <see cref="Ceiling"/>) of the type for the caster: the
        /// oldest goes (Psionic Trap's limit).
        /// </summary>
        public static EffectDefinition Summon(string creepTypeId, int durationMs, bool atPoint, int[] countPerLevel, int[] maxAlive, params EffectDefinition[] onEach) =>
            new EffectDefinition(EffectKind.Summon, countPerLevel, summonTypeId: creepTypeId, durationMs: durationMs, atPoint: atPoint, inner: onEach, ceiling: maxAlive);

        /// <summary>See <see cref="EffectKind.SummonNear"/>.</summary>
        public static EffectDefinition SummonNear(string creepTypeId, int durationMs, int[] countPerLevel, params EffectDefinition[] onEach) =>
            new EffectDefinition(EffectKind.SummonNear, countPerLevel, summonTypeId: creepTypeId, durationMs: durationMs, inner: onEach);

        /// <summary>See <see cref="DamageScaling.DistanceFalloff"/>.</summary>
        public static EffectDefinition FalloffDamage(DamageType type, int fullRadius, int edgeRadius, int edgePercent, params int[] perLevel) =>
            new EffectDefinition(EffectKind.Damage, perLevel, type, scaling: DamageScaling.DistanceFalloff, minDistance: fullRadius, radius: edgeRadius, damagePercent: edgePercent);

        /// <summary>See <see cref="EffectKind.Wall"/>.</summary>
        public static EffectDefinition Wall(int radius, AreaCenter center, TargetTeam team, int formationMs, params int[] durationMsPerLevel) =>
            new EffectDefinition(EffectKind.Wall, durationMsPerLevel, radius: radius, center: center, team: team, durationMs: formationMs);

        /// <summary>An arc of <paramref name="arcDegrees"/> open towards the caster (0: a ring), holding the debuff-immune too when <paramref name="blocksImmune"/>.</summary>
        public static EffectDefinition Wall(int radius, AreaCenter center, TargetTeam team, int formationMs, int arcDegrees, bool blocksImmune, params int[] durationMsPerLevel) =>
            new EffectDefinition(EffectKind.Wall, durationMsPerLevel, radius: radius, center: center, team: team, durationMs: formationMs, maxDistance: arcDegrees, strong: blocksImmune);

        /// <summary>See <see cref="EffectKind.Rewind"/>.</summary>
        public static EffectDefinition Rewind(int backMs, bool restoresVitals, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Rewind, durationMs: backMs, strong: restoresVitals, inner: inner);

        /// <summary>Scaled damage whose base is capped (Impetus: the distance counts up to <paramref name="cap"/>).</summary>
        public static EffectDefinition CappedScaledDamage(DamageType type, DamageScaling scaling, int cap, params int[] percentPerLevel) =>
            new EffectDefinition(EffectKind.Damage, percentPerLevel, type, scaling: scaling, radius: cap);

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

        public static EffectDefinition RefreshCooldowns(bool abilitiesOnly = false) => new EffectDefinition(EffectKind.RefreshCooldowns, abilitiesOnly: abilitiesOnly);

        public static EffectDefinition IfMelee(params EffectDefinition[] inner) => new EffectDefinition(EffectKind.IfMelee, inner: inner);

        public static EffectDefinition IfRanged(params EffectDefinition[] inner) => new EffectDefinition(EffectKind.IfRanged, inner: inner);

        public static EffectDefinition Modifier(params ModifierSpec[] perLevel) =>
            new EffectDefinition(EffectKind.ApplyModifier, modifiers: perLevel);

        public static EffectDefinition Area(int radius, AreaCenter center, TargetTeam team, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Area, radius: radius, center: center, team: team, inner: inner);

        /// <summary>An area taking at most <paramref name="maxTargets"/> units, the nearest to its centre first.</summary>
        public static EffectDefinition Area(int radius, AreaCenter center, TargetTeam team, int maxTargets, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Area, radius: radius, center: center, team: team, inner: inner, maxTargets: maxTargets);

        /// <summary>An area taking at most <paramref name="maxTargets"/> units, the weakest first when <paramref name="lowestHealthFirst"/>.</summary>
        public static EffectDefinition Area(int radius, AreaCenter center, TargetTeam team, int maxTargets, bool lowestHealthFirst, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.Area, radius: radius, center: center, team: team, inner: inner, maxTargets: maxTargets, lowestHealthFirst: lowestHealthFirst);

        public static EffectDefinition LineArea(int width, TargetTeam team, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.LineArea, radius: width, team: team, inner: inner);

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
        public static EffectDefinition Experience(params int[] perLevel) => new EffectDefinition(EffectKind.Experience, perLevel);

        public static EffectDefinition SwapPositions() => new EffectDefinition(EffectKind.SwapPositions);

        public static EffectDefinition IfTeam(TargetTeam team, params EffectDefinition[] inner) => new EffectDefinition(EffectKind.IfTeam, team: team, inner: inner);

        public static EffectDefinition SwapHealthPercent(params int[] minimumPercentPerLevel) => new EffectDefinition(EffectKind.SwapHealthPercent, minimumPercentPerLevel);

        /// <summary>See <see cref="EffectKind.RandomArea"/>.</summary>
        public static EffectDefinition RandomArea(int radius, int minDistance, int maxDistance, TargetTeam team, params EffectDefinition[] inner) =>
            new EffectDefinition(EffectKind.RandomArea, radius: radius, team: team, inner: inner, minDistance: minDistance, maxDistance: maxDistance);

        /// <summary>Slashes (see <see cref="EffectKind.Slashes"/>): the rate in percent of the attack rate, and the slashing modifier, per level.</summary>
        public static EffectDefinition Slashes(int radius, int[] ratePercentPerLevel, params ModifierSpec[] perLevel) =>
            new EffectDefinition(EffectKind.Slashes, ratePercentPerLevel, modifiers: perLevel, radius: radius);

        public static EffectDefinition SummonAtPoint(string creepTypeId, int durationMs, params int[] countPerLevel) =>
            new EffectDefinition(EffectKind.Summon, countPerLevel, summonTypeId: creepTypeId, durationMs: durationMs, atPoint: true);

        /// <summary>
        /// A summon whose <paramref name="onEach"/> effects run on each summoned unit at the ability's level (Dota's Healing Ward:
        /// its healing aura grows with the level, the ward's unit type does not).
        /// </summary>
        public static EffectDefinition Summon(string creepTypeId, int durationMs, bool atPoint, int[] countPerLevel, params EffectDefinition[] onEach) =>
            new EffectDefinition(EffectKind.Summon, countPerLevel, summonTypeId: creepTypeId, durationMs: durationMs, atPoint: atPoint, inner: onEach);

        /// <summary>A summon at the cast point with effects on each summoned unit (see <see cref="Summon(string, int, bool, int[], EffectDefinition[])"/>).</summary>
        public static EffectDefinition SummonAtPoint(string creepTypeId, int durationMs, int[] countPerLevel, params EffectDefinition[] onEach) =>
            Summon(creepTypeId, durationMs, true, countPerLevel, onEach);

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
            w.WriteInt64(MinDistance);
            w.WriteInt64(MaxDistance);
            w.WriteBool(AbilitiesOnly);
            w.WriteInt64(MaxTargets);
            w.WriteBool(LowestHealthFirst);
            w.WriteString(LevelOf ?? "");
            w.WriteInt64(StructurePercent);
            foreach (IReadOnlyList<int> bound in new[] { Floor, Ceiling })
            {
                w.WriteInt64(bound.Count);
                foreach (int value in bound)
                    w.WriteInt64(value);
            }
            w.WriteInt64(Inner.Count);
            foreach (EffectDefinition child in Inner)
                child.WriteCanonical(w);
        }
    }

    /// <summary>An ability as upgraded by a set of talents, "scepter" and/or "shard".</summary>
    /// <summary>
    /// A channel (Dota): after the cast the hero stands channelling for the level's duration; <see cref="Pulse"/> runs every
    /// <see cref="PeriodMs"/> from the start, <see cref="End"/> only when the channel is completed, and <see cref="WhileChannelling"/>
    /// is on the hero meanwhile. A stun, a silence, fear, a taunt, death or a new order ends it early.
    /// </summary>
    public sealed class ChannelSpec
    {
        public ChannelSpec(IReadOnlyList<int> durationMs, int periodMs, IReadOnlyList<EffectDefinition> pulse, IReadOnlyList<EffectDefinition> end,
            ModifierSpec? whileChannelling)
        {
            DurationMs = durationMs.ToArray();
            PeriodMs = periodMs;
            Pulse = pulse.ToArray();
            End = end.ToArray();
            WhileChannelling = whileChannelling;
        }

        public IReadOnlyList<int> DurationMs { get; }
        public int PeriodMs { get; }
        public IReadOnlyList<EffectDefinition> Pulse { get; }
        public IReadOnlyList<EffectDefinition> End { get; }
        public ModifierSpec? WhileChannelling { get; }

        public int DurationAt(int level) => EffectDefinition.ValueAt(DurationMs, level);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(DurationMs.Count);
            foreach (int value in DurationMs)
                w.WriteInt64(value);
            w.WriteInt64(PeriodMs);
            foreach (IReadOnlyList<EffectDefinition> list in new[] { Pulse, End })
            {
                w.WriteInt64(list.Count);
                foreach (EffectDefinition effect in list)
                    effect.WriteCanonical(w);
            }

            w.WriteBool(WhileChannelling != null);
            WhileChannelling?.WriteCanonical(w);
        }
    }

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
            IReadOnlyList<AbilityVariant>? variants = null,
            ChannelSpec? channel = null,
            IReadOnlyList<ModifierSpec>? toggle = null,
            string? levelsWith = null,
            int autoCastBelowHealth = 0,
            string? grantedBy = null,
            bool nightOnly = false,
            bool targetsCreepsOnly = false)
        {
            NightOnly = nightOnly;
            TargetsCreepsOnly = targetsCreepsOnly;
            GrantedBy = grantedBy;
            AutoCastBelowHealth = autoCastBelowHealth;
            LevelsWith = levelsWith;
            Channel = channel;
            Toggle = (toggle ?? Array.Empty<ModifierSpec>()).ToArray();
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

        /// <summary>
        /// A toggled ability's modifier per level (Dota's Pulse Nova): casting puts it on the caster, for the cast's mana and
        /// cooldown; casting again takes it off, free and even on cooldown. Empty for abilities that are not toggled.
        /// </summary>
        public IReadOnlyList<ModifierSpec> Toggle { get; }

        public bool IsToggle => Toggle.Count > 0;

        /// <summary>
        /// An ability levelled together with this one, for its skill point (Morphling: Strength Gain with Agility Gain); toggles
        /// linked so turn each other off. Null for abilities levelled on their own.
        /// </summary>
        public string? LevelsWith { get; }

        /// <summary>
        /// Dota's Borrowed Time: when damage leaves the hero below this much health and the ability is ready, it is cast by itself, free,
        /// even when silenced. 0 for abilities cast only by order.
        /// </summary>
        public int AutoCastBelowHealth { get; }

        /// <summary>The channel the ability starts after its effects; null for abilities that are not channelled.</summary>
        public ChannelSpec? Channel { get; }

        /// <summary>Permanent modifier per level for passive abilities.</summary>
        public IReadOnlyList<ModifierSpec> PassiveModifiers { get; }

        /// <summary>Unit-target abilities only: allied structures are valid targets (teleports).</summary>
        public bool TargetsStructures { get; }

        /// <summary>Learned at level one for free and never levelled with skill points (Dota's innate abilities).</summary>
        public bool Innate { get; }

        /// <summary>
        /// The upgrade that grants the ability ("scepter" or "shard": Lightning Hands, Nosedive): the hero has it at its one level only
        /// while it carries the upgrade; it takes no skill points.
        /// </summary>
        public string? GrantedBy { get; }

        /// <summary>Castable only at night (Midnight Feast).</summary>
        public bool NightOnly { get; }

        /// <summary>A unit-targeted ability that takes creeps and summons only, not heroes nor illusions (Midnight Feast).</summary>
        public bool TargetsCreepsOnly { get; }

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
            w.WriteBool(Channel != null);
            Channel?.WriteCanonical(w);
            w.WriteInt64(Toggle.Count);
            foreach (ModifierSpec modifier in Toggle)
                modifier.WriteCanonical(w);
            w.WriteString(LevelsWith ?? "");
            w.WriteString(GrantedBy ?? "");
            w.WriteBool(NightOnly);
            w.WriteBool(TargetsCreepsOnly);
            w.WriteInt64(AutoCastBelowHealth);
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
