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

        /// <summary>Physical damage (attacks included) does nothing (Guardian Angel, Ghost Scepter).</summary>
        PhysicalImmune = 128,

        /// <summary>Runs away from the modifier's source; cannot attack or cast (Terror Wave, Requiem).</summary>
        Feared = 256,

        /// <summary>Attacks the modifier's source whatever it was ordered to do; cannot cast (Berserker's Call, Duel).</summary>
        Taunted = 512,

        /// <summary>Cannot be attacked, targeted or affected by others' abilities, and takes no damage (Dota's invulnerability: Omnislash).</summary>
        Invulnerable = 1024,

        /// <summary>Acts on its own: takes no orders, does not walk, attack or cast by itself (the slashing hero of Omnislash).</summary>
        Busy = 2048,
    }

    /// <summary>
    /// An aura: while the modifier carrying it is active, units of the given team within the radius around its
    /// bearer keep receiving the aura's own modifier (refreshed regularly, so it fades soon after leaving).
    /// </summary>
    public sealed class AuraSpec
    {
        public AuraSpec(int radius, Rules.TargetTeam team, bool heroesOnly, ModifierSpec modifier, bool creepsOnly = false)
        {
            CreepsOnly = creepsOnly;
            Radius = radius;
            Team = team;
            HeroesOnly = heroesOnly;
            Modifier = modifier;
        }

        public int Radius { get; }
        public Rules.TargetTeam Team { get; }
        public bool HeroesOnly { get; }

        /// <summary>Only creeps (Dota's flagbearer: not heroes, illusions or summons).</summary>
        public bool CreepsOnly { get; }

        public ModifierSpec Modifier { get; }

        internal void WriteCanonical(Dynasty.Core.Hashing.CanonicalWriter w)
        {
            w.WriteInt64(Radius);
            w.WriteInt64((int)Team);
            w.WriteBool(HeroesOnly);
            w.WriteBool(CreepsOnly);
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
            bool consumedOnHit = false,
            Fixed damagePerSecondMaxHealthPercent = default,
            int onHitAfterAttacks = 0,
            IReadOnlyList<Rules.EffectDefinition>? whenAttacked = null,
            int whenAttackedEvery = 1,
            int maxStacks = 0,
            int procCooldownMs = 0,
            int whenAttackedChancePercent = 100,
            IReadOnlyList<Rules.EffectDefinition>? onCast = null,
            IReadOnlyList<Rules.EffectDefinition>? onKill = null,
            IReadOnlyList<Rules.EffectDefinition>? onDeath = null,
            bool onHitHeroesOnly = false,
            int extraTargets = 0,
            int extraTargetRadius = 0,
            int extraTargetDamagePercent = 100,
            bool onKillHeroesOnly = false,
            bool revives = false,
            int attackCharges = 0,
            bool extraTargetRadiusFromAttackRange = false,
            int extraTargetChancePercent = 100,
            int moveSpeedHundredths = 0,
            int critChanceHundredths = 0,
            int evasionHundredths = 0,
            int onHitChanceHundredths = 0,
            int whenAttackedChanceHundredths = 0,
            int incomingDamageHundredths = 0,
            bool pseudoRandom = false,
            bool unique = false,
            int splashPercent = 0,
            int splashRadius = 0,
            int reviveDelayMs = 0,
            bool consumedOnRevive = false,
            ModifierSpec? expireModifier = null,
            Fixed healPerSecondMaxHealthPercent = default,
            Fixed manaPerSecondMaxManaPercent = default,
            int statusResistancePercent = 0,
            int slowResistancePercent = 0,
            int spellBlockCooldownMs = 0,
            int illusionDamageTakenPercent = 0,
            int baseDamagePercent = 0,
            int spellAmplificationPercent = 0,
            int cooldownReductionPercent = 0,
            int manaCostReductionPercent = 0,
            int fixedMoveSpeed = 0,
            int barrierMaxHealthPercent = 0,
            Fixed damagedRegenMaxPercent = default,
            int damagedRegenMs = 0,
            bool endsWhenFull = false,
            int reflectHundredths = 0,
            int reflectFullRadius = 0,
            int reflectRadius = 0,
            int returnPercent = 0,
            IReadOnlyList<Rules.EffectDefinition>? onReturn = null,
            int damageBlock = 0,
            int damageBlockCreepPercent = 100,
            int cleanseThreshold = 0,
            int cleanseWindowMs = 0,
            int absorbInstances = 0,
            int absorbBarrier = 0,
            int layers = 0,
            int layerReductionPercent = 0,
            int layerRecoveryMs = 0,
            int layerMinimumDamage = 0,
            int sideReductionPercent = 0,
            int backReductionPercent = 0,
            int sideAngle = 0,
            int backAngle = 0,
            string? releaseAbilityId = null,
            int releaseThreshold = 0,
            Fixed manaCostPerSecond = default,
            int attributeShift = 0,
            int instanceBlock = 0,
            bool damageHeals = false,
            int critMultiplierMaxPercent = 0,
            int critLifestealPercent = 0,
            string? resetStacksKey = null,
            int onHitManaCost = 0,
            int onHitManaCostPercent = 0,
            int fullBelowHealthPercent = 0,
            Fixed hpRegenStrengthPercent = default,
            int invisibleAfterIdleMs = 0,
            int shareDamagePercent = 0,
            int spreadDamagePercent = 0,
            int spreadRadius = 0,
            IReadOnlyList<Rules.EffectDefinition>? periodicEffects = null,
            int breakDistance = 0,
            int healSharePercent = 0,
            int manaSharePercent = 0,
            bool returnsOnExpiry = false,
            int castDamage = 0,
            int castDamageManaPercent = 0,
            int proximityRadius = 0,
            int proximityDelayMs = 0,
            IReadOnlyList<Rules.EffectDefinition>? onProximity = null,
            bool periodicFromBearer = false,
            int baseAttackTimeMs = 0,
            int growsWithChannelMs = 0,
            int baseArmorReductionPercent = 0,
            int baseMagicResistReductionPercent = 0,
            int attackDamageHundredths = 0,
            int critEveryAttacks = 0,
            int critHealPercent = 0,
            int critHealRadius = 0,
            int critHealAlliesPercent = 0,
            int critHealCreepPercent = 100)
        {
            CritEveryAttacks = critEveryAttacks;
            CritHealPercent = critHealPercent;
            CritHealRadius = critHealRadius;
            CritHealAlliesPercent = critHealAlliesPercent;
            CritHealCreepPercent = critHealCreepPercent;
            BaseArmorReductionPercent = baseArmorReductionPercent;
            BaseMagicResistReductionPercent = baseMagicResistReductionPercent;
            AttackDamageHundredths = attackDamagePercent * 100 + attackDamageHundredths;
            BaseAttackTimeMs = baseAttackTimeMs;
            GrowsWithChannelMs = growsWithChannelMs;
            PeriodicFromBearer = periodicFromBearer;
            ProximityRadius = proximityRadius;
            ProximityDelayMs = proximityDelayMs;
            OnProximity = onProximity?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
            ReturnsOnExpiry = returnsOnExpiry;
            CastDamage = castDamage;
            CastDamageManaPercent = castDamageManaPercent;
            ShareDamagePercent = shareDamagePercent;
            SpreadDamagePercent = spreadDamagePercent;
            SpreadRadius = spreadRadius;
            PeriodicEffects = (periodicEffects ?? Array.Empty<Rules.EffectDefinition>()).ToArray();
            BreakDistance = breakDistance;
            HealSharePercent = healSharePercent;
            ManaSharePercent = manaSharePercent;
            FullBelowHealthPercent = fullBelowHealthPercent;
            HpRegenStrengthPercent = hpRegenStrengthPercent;
            InvisibleAfterIdleMs = invisibleAfterIdleMs;
            CritMultiplierMaxPercent = critMultiplierMaxPercent;
            CritLifestealPercent = critLifestealPercent;
            ResetStacksKey = resetStacksKey;
            OnHitManaCost = onHitManaCost;
            OnHitManaCostPercent = onHitManaCostPercent;
            InstanceBlock = instanceBlock;
            DamageHeals = damageHeals;
            AttributeShift = attributeShift;
            ManaCostPerSecond = manaCostPerSecond;
            SideReductionPercent = sideReductionPercent;
            BackReductionPercent = backReductionPercent;
            SideAngle = sideAngle;
            BackAngle = backAngle;
            ReleaseAbilityId = releaseAbilityId;
            ReleaseThreshold = releaseThreshold;
            Layers = layers;
            LayerReductionPercent = layerReductionPercent;
            LayerRecoveryMs = layerRecoveryMs;
            LayerMinimumDamage = layerMinimumDamage;
            ReflectHundredths = reflectHundredths;
            ReflectFullRadius = reflectFullRadius;
            ReflectRadius = reflectRadius;
            ReturnPercent = returnPercent;
            OnReturn = (onReturn ?? Array.Empty<Rules.EffectDefinition>()).ToArray();
            DamageBlock = damageBlock;
            DamageBlockCreepPercent = damageBlockCreepPercent;
            CleanseThreshold = cleanseThreshold;
            CleanseWindowMs = cleanseWindowMs;
            AbsorbInstances = absorbInstances;
            AbsorbBarrier = absorbBarrier;
            BaseDamagePercent = baseDamagePercent;
            SpellAmplificationPercent = spellAmplificationPercent;
            CooldownReductionPercent = cooldownReductionPercent;
            ManaCostReductionPercent = manaCostReductionPercent;
            FixedMoveSpeed = fixedMoveSpeed;
            BarrierMaxHealthPercent = barrierMaxHealthPercent;
            DamagedRegenMaxPercent = damagedRegenMaxPercent;
            DamagedRegenMs = damagedRegenMs;
            EndsWhenFull = endsWhenFull;
            StatusResistancePercent = statusResistancePercent;
            SlowResistancePercent = slowResistancePercent;
            SpellBlockCooldownMs = spellBlockCooldownMs;
            IllusionDamageTakenPercent = illusionDamageTakenPercent;
            ReviveDelayMs = reviveDelayMs;
            ConsumedOnRevive = consumedOnRevive;
            ExpireModifier = expireModifier;
            HealPerSecondMaxHealthPercent = healPerSecondMaxHealthPercent;
            ManaPerSecondMaxManaPercent = manaPerSecondMaxManaPercent;
            SplashPercent = splashPercent;
            SplashRadius = splashRadius;
            PseudoRandom = pseudoRandom;
            Unique = unique;
            IncomingDamageHundredths = incomingDamagePercent * 100 + incomingDamageHundredths;
            // Percents Dota gives with decimals (2.5% per stack, 33.33%) are kept in hundredths: the whole percent plus the fraction.
            MoveSpeedHundredths = moveSpeedPercent * 100 + moveSpeedHundredths;
            CritChanceHundredths = critChancePercent * 100 + critChanceHundredths;
            EvasionHundredths = evasionPercent * 100 + evasionHundredths;
            OnHitChanceHundredths = onHitChancePercent * 100 + onHitChanceHundredths;
            WhenAttackedChanceHundredths = whenAttackedChancePercent * 100 + whenAttackedChanceHundredths;
            AttackCharges = attackCharges;
            ExtraTargetRadiusFromAttackRange = extraTargetRadiusFromAttackRange;
            ExtraTargetChancePercent = extraTargetChancePercent;
            Revives = revives;
            OnKillHeroesOnly = onKillHeroesOnly;
            ExtraTargets = extraTargets;
            ExtraTargetRadius = extraTargetRadius;
            ExtraTargetDamagePercent = extraTargetDamagePercent;
            OnCast = onCast?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
            OnKill = onKill?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
            OnDeath = onDeath?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
            OnHitHeroesOnly = onHitHeroesOnly;
            MaxStacks = maxStacks;
            ProcCooldownMs = procCooldownMs;

            OnHitAfterAttacks = onHitAfterAttacks;
            WhenAttacked = whenAttacked?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
            WhenAttackedEvery = whenAttackedEvery;
            DamagePerSecondMaxHealthPercent = damagePerSecondMaxHealthPercent;
            ConsumedOnHit = consumedOnHit;
            CleavePercent = cleavePercent;
            CleaveRadius = cleaveRadius;
            Barrier = barrier;
            OnHit = onHit?.ToArray() ?? System.Array.Empty<Rules.EffectDefinition>();
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
            CritMultiplierPercent = critMultiplierPercent;
            StructureDamagePercent = structureDamagePercent;
            LifestealPercent = lifestealPercent;
            Key = key;
            DurationMs = durationMs;
            Flags = flags;
            ArmorBonus = armorBonus;
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
        public int IncomingDamagePercent => IncomingDamageHundredths / 100;

        /// <summary>Damage taken change in hundredths of a percent (Wild Axes' talent: +6.5% = 650).</summary>
        public int IncomingDamageHundredths { get; }

        /// <summary>Percent added to the unit's attack damage (negative: e.g. Breathe Fire's reduction).</summary>
        public int AttackDamagePercent => AttackDamageHundredths / 100;

        /// <summary>Chance that an attack on the unit misses, in hundredths of a percent.</summary>
        public int EvasionHundredths { get; }

        public int EvasionPercent => EvasionHundredths / 100;

        /// <summary>Effects run on the target of each of the unit's landing attacks (bash, bonus magic damage, slows), with <see cref="OnHitChancePercent"/>.</summary>
        public IReadOnlyList<Rules.EffectDefinition> OnHit { get; }
        public int OnHitChanceHundredths { get; }

        public int OnHitChancePercent => OnHitChanceHundredths / 100;

        /// <summary>On-hit effects run only on the attack after this many attacks without them ("after 3 attacks, the next one bashes"); 0 = every attack.</summary>
        public int OnHitAfterAttacks { get; }

        /// <summary>
        /// Effects run when an attack lands on the bearer (Counter Helix, Return), on every <see cref="WhenAttackedEvery"/>th
        /// such attack, with the bearer as the caster and the attacker as the target.
        /// </summary>
        public IReadOnlyList<Rules.EffectDefinition> WhenAttacked { get; }
        public int WhenAttackedEvery { get; }

        /// <summary>Chance that <see cref="WhenAttacked"/> runs on an attack taken (Storm Surge).</summary>
        public int WhenAttackedChanceHundredths { get; }

        public int WhenAttackedChancePercent => WhenAttackedChanceHundredths / 100;

        /// <summary>After its on-hit or on-attacked effects run, they do not run again for this long (Greater Bash, Mortal Strike).</summary>
        public int ProcCooldownMs { get; }

        /// <summary>
        /// Above 0, reapplying the modifier from the same source adds a stack (up to this many) and refreshes the
        /// duration; every stack adds the modifier's stats and damage over time (Fury Swipes, Essence Shift).
        /// </summary>
        public int MaxStacks { get; }

        /// <summary>Its on-hit and on-attacked chances use Dota's pseudo-random distribution (Bash, Counter Helix: dota2.fandom «Random Distribution»).</summary>
        public bool PseudoRandom { get; }

        /// <summary>
        /// Instances of it from several sources do not stack (Dota: the same aura, Tower Protection): only the strongest
        /// counts for the bearer's stats — the most armor, then the most health regeneration, then the first applied.
        /// </summary>
        public bool Unique { get; }

        /// <summary>On-hit effects run only when the attacked unit is a hero (Essence Shift).</summary>
        public bool OnHitHeroesOnly { get; }

        /// <summary>
        /// Each attack also hits up to this many other enemies within <see cref="ExtraTargetRadius"/> of the attacker,
        /// nearest first, with <see cref="ExtraTargetDamagePercent"/> of the attack and its on-hit effects (Split Shot, Flak Cannon).
        /// </summary>
        public int ExtraTargets { get; }
        public int ExtraTargetRadius { get; }
        public int ExtraTargetDamagePercent { get; }

        /// <summary>The radius of extra targets is counted from the edge of the bearer's attack range (Split Shot: attack range + bonus).</summary>
        public bool ExtraTargetRadiusFromAttackRange { get; }

        /// <summary>Chance per attack that the extra targets are hit (Gunslinger).</summary>
        public int ExtraTargetChancePercent { get; }

        /// <summary>The modifier ends after this many of the bearer's attacks land (Overpower, Lil' Shredder); 0 = no limit.</summary>
        public int AttackCharges { get; }

        /// <summary>Effects run when the bearer casts an ability, after its effects: the cast's target (the bearer for untargeted casts) and point (Aftershock).</summary>
        public IReadOnlyList<Rules.EffectDefinition> OnCast { get; }

        /// <summary>Effects run when the bearer kills a unit, with the victim as the target (Sadist).</summary>
        public IReadOnlyList<Rules.EffectDefinition> OnKill { get; }

        /// <summary>
        /// The bearer, when it would die, comes back at once with full health and mana instead (Reincarnation); the
        /// modifier's <see cref="OnDeath"/> effects run at the revive, and <see cref="ProcCooldownMs"/> is its cooldown.
        /// </summary>
        public bool Revives { get; }

        /// <summary>
        /// With <see cref="Revives"/>: the hero stays down this long where it died before coming back there (Dota's Aegis: 5 s);
        /// the death is not counted. 0 = at once.
        /// </summary>
        public int ReviveDelayMs { get; }

        /// <summary>With <see cref="Revives"/>: the modifier is used up by the revive (Dota's Aegis).</summary>
        public bool ConsumedOnRevive { get; }

        /// <summary>Applied to the bearer when the modifier runs out (Dota's unused Aegis: a quick regeneration).</summary>
        public ModifierSpec? ExpireModifier { get; }

        /// <summary>Percent of the bearer's maximum health and mana restored per second (Dota's Aegis regeneration).</summary>
        public Fixed HealPerSecondMaxHealthPercent { get; }
        public Fixed ManaPerSecondMaxManaPercent { get; }

        /// <summary>
        /// Shortens enemy debuffs on the bearer by this percent (Dota's status resistance; sources multiply). Damage over time
        /// keeps its full duration (Dota ticks it faster instead, so the total stays).
        /// </summary>
        public int StatusResistancePercent { get; }

        /// <summary>Scales percentage slows of the bearer's movement speed down by this percent (Dota; sources multiply).</summary>
        public int SlowResistancePercent { get; }

        /// <summary>Blocks an enemy spell targeted at the bearer, then not again for this long (Dota's Linken's Sphere, Roshan); 0 = none.</summary>
        public int SpellBlockCooldownMs { get; }

        /// <summary>Changes the damage illusions deal to the bearer by this percent (Dota's Roshan: −80).</summary>
        public int IllusionDamageTakenPercent { get; }

        /// <summary>A percent of the bearer's base attack damage added (Dota's Amplify Damage rune: 80).</summary>
        public int BaseDamagePercent { get; }

        /// <summary>The bearer's spells deal this percent more damage (Dota's spell amplification).</summary>
        public int SpellAmplificationPercent { get; }

        /// <summary>The bearer's cooldowns and mana costs are this percent shorter and cheaper (Dota's Arcane rune; sources multiply).</summary>
        public int CooldownReductionPercent { get; }
        public int ManaCostReductionPercent { get; }

        /// <summary>The bearer moves at exactly this speed (Dota's Haste rune: 550); 0 = none.</summary>
        public int FixedMoveSpeed { get; }

        /// <summary>A barrier of this percent of the bearer's maximum health (Dota's Shield rune: 50).</summary>
        public int BarrierMaxHealthPercent { get; }

        /// <summary>
        /// For <see cref="DamagedRegenMs"/> after damage from a player's unit or the boss the percent regeneration is this instead
        /// (Dota's Regeneration rune: 1%).
        /// </summary>
        public Fixed DamagedRegenMaxPercent { get; }
        public int DamagedRegenMs { get; }

        /// <summary>The modifier ends once the bearer's health and mana are full (Dota's Regeneration rune).</summary>
        public bool EndsWhenFull { get; }

        /// <summary>
        /// Dota's Dispersion: this share (hundredths of a percent) of any damage taken, before reductions, is not dealt to the bearer
        /// but to each enemy around it as the same damage type: in full within <see cref="ReflectFullRadius"/>, then less, linearly,
        /// down to nothing at <see cref="ReflectRadius"/>. Illusions do not reflect.
        /// </summary>
        public int ReflectHundredths { get; }

        public int ReflectFullRadius { get; }
        public int ReflectRadius { get; }

        /// <summary>
        /// Dota's Spiked Carapace: damage from a player's unit (hero, summon, illusion) is negated, once per source, and this percent of
        /// it, after reductions, is dealt back to the source as the same type, with <see cref="OnReturn"/> run on it (the stun).
        /// </summary>
        public int ReturnPercent { get; }

        public IReadOnlyList<Rules.EffectDefinition> OnReturn { get; }

        /// <summary>Dota's damage block (Kraken Shell): taken off each attack's damage before armor; the strongest block counts.</summary>
        public int DamageBlock { get; }

        /// <summary>Percent of <see cref="DamageBlock"/> against creeps' attacks.</summary>
        public int DamageBlockCreepPercent { get; }

        /// <summary>
        /// Kraken Shell's cleanse: damage from players' units taken with no pause longer than <see cref="CleanseWindowMs"/> adds up;
        /// past this much the bearer is strongly dispelled and the count starts again.
        /// </summary>
        public int CleanseThreshold { get; }

        public int CleanseWindowMs { get; }

        /// <summary>
        /// Dota's Refraction: this many instances of damage are swallowed whole; a small barrier (<see cref="AbsorbBarrier"/>) wears down
        /// with smaller ones and an instance it cannot hold uses up a charge. Without charges the modifier ends.
        /// </summary>
        public int AbsorbInstances { get; }

        public int AbsorbBarrier { get; }

        /// <summary>
        /// Dota's Gravekeeper's Cloak: layers, each cutting damage taken by <see cref="LayerReductionPercent"/>; damage from a player's
        /// unit (not the bearer's own) over <see cref="LayerMinimumDamage"/> after the cut takes one off, back after <see cref="LayerRecoveryMs"/>.
        /// </summary>
        public int Layers { get; }

        public int LayerReductionPercent { get; }
        public int LayerRecoveryMs { get; }
        public int LayerMinimumDamage { get; }

        /// <summary>
        /// Dota's Bristleback: damage whose source stands within <see cref="BackAngle"/> degrees of the bearer's back is cut by
        /// <see cref="BackReductionPercent"/>, within <see cref="SideAngle"/> by <see cref="SideReductionPercent"/> (by the way the
        /// bearer faces, <see cref="MatchUnit.Facing"/>).
        /// </summary>
        public int SideReductionPercent { get; }

        public int BackReductionPercent { get; }
        public int SideAngle { get; }
        public int BackAngle { get; }

        /// <summary>
        /// Damage taken from the rear adds up; each <see cref="ReleaseThreshold"/> of it makes the bearer release its ability
        /// <see cref="ReleaseAbilityId"/> at the level it has learned (Bristleback's Quill Spray), none when not learned.
        /// </summary>
        public string? ReleaseAbilityId { get; }

        public int ReleaseThreshold { get; }

        /// <summary>
        /// Mana the bearer pays every period for keeping it (a toggled ability's upkeep: Pulse Nova drains mana with each pulse); the
        /// modifier ends when the bearer cannot pay.
        /// </summary>
        public Fixed ManaCostPerSecond { get; }

        /// <summary>
        /// Morphling's Attribute Shift: every period one point of the bearer's base attributes moves from strength to agility (1) or
        /// back (-1), never below one base point of the source and never the attributes items give.
        /// </summary>
        public int AttributeShift { get; }

        /// <summary>Dota's Flesh Heap: this much off every instance of damage of any type, before reductions; the strongest counts.</summary>
        public int InstanceBlock { get; }

        /// <summary>Dota's Borrowed Time: damage taken, after its reductions, heals the bearer instead.</summary>
        public bool DamageHeals { get; }

        /// <summary>Chaos Strike: the critical strike's multiplier is drawn between <see cref="CritMultiplierPercent"/> and this.</summary>
        public int CritMultiplierMaxPercent { get; }

        /// <summary>Chaos Strike: lifesteal of the attack this critical strike lands on.</summary>
        public int CritLifestealPercent { get; }

        /// <summary>Fervor: the stacking modifier with this key is dropped when the bearer attacks another target than the last.</summary>
        public string? ResetStacksKey { get; }

        /// <summary>Impetus: mana each on-hit proc costs; without it the attack does not proc.</summary>
        public int OnHitManaCost { get; }

        /// <summary>Arcane Orb: percent of the current mana each on-hit proc costs, spent before its effects.</summary>
        public int OnHitManaCostPercent { get; }

        /// <summary>
        /// Berserker's Blood: the attack speed, magic resistance and <see cref="HpRegenStrengthPercent"/> grow linearly with the
        /// bearer's missing health, none at full and all below this percent of health. 0: given in full.
        /// </summary>
        public int FullBelowHealthPercent { get; }

        /// <summary>Health regeneration a second as a percent of the bearer's strength.</summary>
        public Fixed HpRegenStrengthPercent { get; }

        /// <summary>Riki's Backstab (7.41): the bearer turns invisible after this long without starting an attack; an attack or a cast reveals it.</summary>
        public int InvisibleAfterIdleMs { get; }

        /// <summary>
        /// Fatal Bonds: this percent of the damage a bearer takes (after reductions) is dealt, as the same type, to every other bearer of
        /// the same modifier from the same caster.
        /// </summary>
        public int ShareDamagePercent { get; }

        /// <summary>
        /// Disseminate: this percent of the damage the bearer takes (after reductions) is dealt, as the same type, to every enemy of the
        /// modifier's caster within <see cref="SpreadRadius"/> of the bearer, the bearer itself included when it is one.
        /// </summary>
        public int SpreadDamagePercent { get; }

        public int SpreadRadius { get; }

        /// <summary>Effects run every period by the modifier's caster on the bearer (Static Link's drain).</summary>
        public IReadOnlyList<Rules.EffectDefinition> PeriodicEffects { get; }

        /// <summary>The bearer itself runs <see cref="PeriodicEffects"/>, whether its caster lives or not (Tombstone raising zombies).</summary>
        public bool PeriodicFromBearer { get; }

        /// <summary>
        /// Natural Order: this share of the bearer's base armor (its own and from agility) is gone, bonuses stay; the strongest of
        /// such modifiers counts.
        /// </summary>
        public int BaseArmorReductionPercent { get; }

        /// <summary>Natural Order: this share of the bearer's own base magic resistance is gone (whether intelligence's part counts is not confirmed: B-173).</summary>
        public int BaseMagicResistReductionPercent { get; }

        /// <summary>Attack damage change in hundredths of a percent (Corrosive Weaponry: −2.5% a stack is −250).</summary>
        public int AttackDamageHundredths { get; }

        /// <summary>
        /// Luminosity: after this many attacks the next one is a critical strike of <see cref="CritMultiplierPercent"/> (not against
        /// buildings, whose attacks do not count) that heals the attacker by <see cref="CritHealPercent"/> of the damage dealt (
        /// <see cref="CritHealCreepPercent"/> as much from a non-hero) and allied heroes within <see cref="CritHealRadius"/> by
        /// <see cref="CritHealAlliesPercent"/> of that.
        /// </summary>
        public int CritEveryAttacks { get; }

        public int CritHealPercent { get; }

        public int CritHealRadius { get; }

        public int CritHealAlliesPercent { get; }

        public int CritHealCreepPercent { get; }

        /// <summary>Sets the bearer's base attack time while it lasts (Lil' Shredder); 0: the unit's own.</summary>
        public int BaseAttackTimeMs { get; }

        /// <summary>
        /// The modifier's slow, applied by a channelling caster, is in full only after the caster has channelled this long, and in
        /// proportion before (Upheaval: by the length of the channel, not by the time the bearer stood in the area).
        /// </summary>
        public int GrowsWithChannelMs { get; }

        /// <summary>The modifier ends when the bearer and its caster are farther apart than this (a link breaking).</summary>
        public int BreakDistance { get; }

        /// <summary>Tether: this percent of the health and of the mana the modifier's caster restores is restored to the bearer too.</summary>
        public int HealSharePercent { get; }

        public int ManaSharePercent { get; }

        /// <summary>X Marks the Spot: when the modifier runs out the bearer is put back where it was when it got it.</summary>
        public bool ReturnsOnExpiry { get; }

        /// <summary>
        /// Nether Ward: when the bearer casts a spell (not an item) the modifier's source deals it this much magical damage plus
        /// <see cref="CastDamageManaPercent"/> of the mana spent, before the spell; a bearer killed by it casts nothing.
        /// </summary>
        public int CastDamage { get; }

        public int CastDamageManaPercent { get; }

        /// <summary>
        /// Proximity Mines: once an enemy (hero, creep or summon, seen or not) has stood within this radius of the bearer for
        /// <see cref="ProximityDelayMs"/> without a break, <see cref="OnProximity"/> runs with the bearer as the caster and the bearer is gone.
        /// </summary>
        public int ProximityRadius { get; }

        public int ProximityDelayMs { get; }

        public IReadOnlyList<Rules.EffectDefinition> OnProximity { get; }

        /// <summary><see cref="OnKill"/> runs only for hero victims (Flesh Heap).</summary>
        public bool OnKillHeroesOnly { get; }

        /// <summary>Effects run when the bearer dies, with the bearer as the caster and its killer as the target (Caustic Finale, Retribution).</summary>
        public IReadOnlyList<Rules.EffectDefinition> OnDeath { get; }

        public AuraSpec? Aura { get; }

        /// <summary>Melee attacks also deal this percent of their damage to other enemies within <see cref="CleaveRadius"/> of the target.</summary>
        public int CleavePercent { get; }
        public int CleaveRadius { get; }

        /// <summary>Any attack (ranged too) also deals this percent of its damage to other enemies within <see cref="SplashRadius"/> of the target (Dota: the fountain).</summary>
        public int SplashPercent { get; }
        public int SplashRadius { get; }

        /// <summary>Damage of any type this modifier absorbs before it is used up and removed.</summary>
        public int Barrier { get; }

        /// <summary>Removed once the unit's next attack lands ("next attack" buffs).</summary>
        public bool ConsumedOnHit { get; }

        /// <summary>Damage over time as a percent of the bearer's max health each second (in <see cref="DamageType"/>).</summary>
        public Fixed DamagePerSecondMaxHealthPercent { get; }

        /// <summary>Change of move speed in hundredths of a percent; negative slows.</summary>
        public int MoveSpeedHundredths { get; }

        /// <summary>Whole percent of <see cref="MoveSpeedHundredths"/> (for estimates outside the simulation).</summary>
        public int MoveSpeedPercent => MoveSpeedHundredths / 100;

        /// <summary>Attack speed bonus in percent; +100 halves the attack interval.</summary>
        public int AttackSpeedBonus { get; }

        public int DamageBonus { get; }
        public int MagicResistBonus { get; }
        public Fixed DamagePerSecond { get; }
        public DamageType DamageType { get; }
        public Fixed HealPerSecond { get; }

        /// <summary>Interval of periodic damage/heal; each period deals per-second value × period.</summary>
        public int PeriodMs { get; }

        /// <summary>Chance per attack (hundredths of a percent) to deal <see cref="CritMultiplierPercent"/> damage.</summary>
        public int CritChanceHundredths { get; }

        public int CritChancePercent => CritChanceHundredths / 100;

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
            (Flags & (ModifierFlags.Stunned | ModifierFlags.Rooted | ModifierFlags.Silenced | ModifierFlags.Disarmed | ModifierFlags.Feared | ModifierFlags.Taunted)) != 0 ||
            MoveSpeedHundredths < 0 || AttackSpeedBonus < 0 || ArmorBonus < Fixed.Zero || DamageBonus < 0 || MagicResistBonus < 0 ||
            DamagePerSecond > Fixed.Zero;

        public bool IsPeriodic => DamagePerSecond > Fixed.Zero || HealPerSecond > Fixed.Zero || DamagePerSecondMaxHealthPercent > Fixed.Zero ||
                                  HealPerSecondMaxHealthPercent > Fixed.Zero || ManaPerSecondMaxManaPercent > Fixed.Zero || ManaCostPerSecond > Fixed.Zero ||
                                  AttributeShift != 0 || PeriodicEffects.Count > 0;

        public bool IsPermanent => DurationMs < 0;

        internal void WriteCanonical(Dynasty.Core.Hashing.CanonicalWriter w)
        {
            w.WriteString(Key);
            w.WriteInt64(DurationMs);
            w.WriteInt64((int)Flags);
            w.WriteFixed(ArmorBonus);
            w.WriteInt64(MoveSpeedHundredths);
            w.WriteInt64(AttackSpeedBonus);
            w.WriteInt64(DamageBonus);
            w.WriteInt64(MagicResistBonus);
            w.WriteFixed(DamagePerSecond);
            w.WriteInt64((int)DamageType);
            w.WriteFixed(HealPerSecond);
            w.WriteInt64(PeriodMs);
            w.WriteInt64(CritChanceHundredths);
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
            w.WriteInt64(IncomingDamageHundredths);
            w.WriteInt64(AttackDamagePercent);
            w.WriteInt64(EvasionHundredths);
            w.WriteInt64(OnHit.Count);
            foreach (Rules.EffectDefinition effect in OnHit)
                effect.WriteCanonical(w);
            w.WriteInt64(OnHitChanceHundredths);
            w.WriteBool(Aura != null);
            Aura?.WriteCanonical(w);
            w.WriteInt64(CleavePercent);
            w.WriteInt64(CleaveRadius);
            w.WriteInt64(Barrier);
            w.WriteBool(ConsumedOnHit);
            w.WriteFixed(DamagePerSecondMaxHealthPercent);
            w.WriteInt64(OnHitAfterAttacks);
            w.WriteInt64(WhenAttacked.Count);
            foreach (Rules.EffectDefinition effect in WhenAttacked)
                effect.WriteCanonical(w);
            w.WriteInt64(WhenAttackedEvery);
            w.WriteInt64(WhenAttackedChanceHundredths);
            w.WriteInt64(ProcCooldownMs);
            w.WriteInt64(MaxStacks);
            w.WriteBool(OnHitHeroesOnly);
            w.WriteInt64(ExtraTargets);
            w.WriteInt64(ExtraTargetRadius);
            w.WriteInt64(ExtraTargetDamagePercent);
            w.WriteBool(OnKillHeroesOnly);
            w.WriteBool(Revives);
            w.WriteBool(PseudoRandom);
            w.WriteBool(Unique);
            w.WriteInt64(SplashPercent);
            w.WriteInt64(SplashRadius);
            w.WriteInt64(ReviveDelayMs);
            w.WriteBool(ConsumedOnRevive);
            w.WriteBool(ExpireModifier != null);
            ExpireModifier?.WriteCanonical(w);
            w.WriteFixed(HealPerSecondMaxHealthPercent);
            w.WriteFixed(ManaPerSecondMaxManaPercent);
            w.WriteInt64(StatusResistancePercent);
            w.WriteInt64(SlowResistancePercent);
            w.WriteInt64(SpellBlockCooldownMs);
            w.WriteInt64(IllusionDamageTakenPercent);
            foreach (int value in new[] { BaseDamagePercent, SpellAmplificationPercent, CooldownReductionPercent, ManaCostReductionPercent, FixedMoveSpeed, BarrierMaxHealthPercent, DamagedRegenMs })
                w.WriteInt64(value);
            w.WriteFixed(DamagedRegenMaxPercent);
            w.WriteBool(EndsWhenFull);
            w.WriteInt64(AttackCharges);
            w.WriteBool(ExtraTargetRadiusFromAttackRange);
            w.WriteInt64(ExtraTargetChancePercent);
            foreach (IReadOnlyList<Rules.EffectDefinition> list in new[] { OnCast, OnKill, OnDeath, OnReturn, OnProximity })
            {
                w.WriteInt64(list.Count);
                foreach (Rules.EffectDefinition effect in list)
                    effect.WriteCanonical(w);
            }
            foreach (int value in new[] { ReflectHundredths, ReflectFullRadius, ReflectRadius, ReturnPercent, DamageBlock, DamageBlockCreepPercent,
                                          CleanseThreshold, CleanseWindowMs, AbsorbInstances, AbsorbBarrier, Layers, LayerReductionPercent, LayerRecoveryMs, LayerMinimumDamage,
                                          SideReductionPercent, BackReductionPercent, SideAngle, BackAngle, ReleaseThreshold })
                w.WriteInt64(value);
            w.WriteString(ReleaseAbilityId ?? "");
            w.WriteFixed(ManaCostPerSecond);
            w.WriteInt64(AttributeShift);
            w.WriteInt64(InstanceBlock);
            w.WriteBool(DamageHeals);
            foreach (int value in new[] { CritMultiplierMaxPercent, CritLifestealPercent, OnHitManaCost, OnHitManaCostPercent, FullBelowHealthPercent, InvisibleAfterIdleMs,
                                          ShareDamagePercent, SpreadDamagePercent, SpreadRadius, BreakDistance, HealSharePercent, ManaSharePercent,
                                          CastDamage, CastDamageManaPercent, ProximityRadius, ProximityDelayMs, BaseAttackTimeMs, GrowsWithChannelMs,
                                          BaseArmorReductionPercent, BaseMagicResistReductionPercent, AttackDamageHundredths,
                                          CritEveryAttacks, CritHealPercent, CritHealRadius, CritHealAlliesPercent, CritHealCreepPercent })
                w.WriteInt64(value);
            w.WriteBool(ReturnsOnExpiry);
            w.WriteBool(PeriodicFromBearer);
            w.WriteInt64(PeriodicEffects.Count);
            foreach (Rules.EffectDefinition effect in PeriodicEffects)
                effect.WriteCanonical(w);
            w.WriteFixed(HpRegenStrengthPercent);
            w.WriteString(ResetStacksKey ?? "");
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
            InstancesLeft = spec.AbsorbInstances;
            InstanceBarrierLeft = spec.AbsorbBarrier;
        }

        /// <summary>Sources whose damage it has already returned (<see cref="ModifierSpec.ReturnPercent"/>: once each).</summary>
        internal HashSet<long> ReturnedTo { get; } = new HashSet<long>();

        /// <summary>Players' damage counted toward <see cref="ModifierSpec.CleanseThreshold"/>, and when the last of it came.</summary>
        internal Fixed CleanseTaken { get; set; }

        internal long CleanseLastTick { get; set; } = -1;

        /// <summary>Charges and the current barrier of <see cref="ModifierSpec.AbsorbInstances"/>.</summary>
        internal int InstancesLeft { get; set; }

        internal int InstanceBarrierLeft { get; set; }

        /// <summary>Where the bearer was when it got the modifier (<see cref="ModifierSpec.ReturnsOnExpiry"/>).</summary>
        internal FixedVector2 Mark { get; set; }

        /// <summary>Attacks counted towards a charged critical strike (<see cref="ModifierSpec.CritEveryAttacks"/>).</summary>
        internal int AttacksCounted { get; set; }

        /// <summary>The unit the bearer is bound to (Soulbind); none for other modifiers.</summary>
        internal EntityId Partner { get; set; }

        /// <summary>How far the bearer may be from <see cref="Partner"/>.</summary>
        internal Fixed LeashDistance { get; set; }

        /// <summary>The share of the modifier's slow that applies: a sprung trap's charge (Psionic Trap); 100 otherwise.</summary>
        internal int SlowStrengthPercent { get; set; } = 100;

        /// <summary>The tick an enemy came within the proximity radius and has stayed there since; -1 when none is near.</summary>
        internal long ProximitySinceTick { get; set; } = -1;

        /// <summary>The target of the bearer's last attack (<see cref="ModifierSpec.ResetStacksKey"/>).</summary>
        internal EntityId LastTarget { get; set; }

        /// <summary>Damage from the rear counted toward <see cref="ModifierSpec.ReleaseThreshold"/>.</summary>
        internal Fixed ReleaseTaken { get; set; }

        /// <summary>When each lost layer of <see cref="ModifierSpec.Layers"/> comes back, in order.</summary>
        internal List<long> LayerReturns { get; } = new List<long>();

        /// <summary>Layers of <see cref="ModifierSpec.Layers"/> on at the tick, counting those back by then.</summary>
        internal int LayersAt(long tick)
        {
            LayerReturns.RemoveAll(t => t <= tick);
            return Spec.Layers - LayerReturns.Count;
        }

        public ModifierSpec Spec { get; }

        /// <summary>Damage the modifier's barrier can still absorb.</summary>
        public int BarrierLeft { get; internal set; }
        public EntityId Source { get; }
        public long AppliedTick { get; internal set; }

        /// <summary>Tick at which it is removed; -1 for permanent.</summary>
        public long ExpiresTick { get; internal set; }

        internal long NextPeriodTick { get; set; }

        /// <summary>Attacks counted toward <see cref="ModifierSpec.OnHitAfterAttacks"/> or <see cref="ModifierSpec.WhenAttackedEvery"/>.</summary>
        internal int AttackCount { get; set; }

        /// <summary>Stacks of a stacking modifier (<see cref="ModifierSpec.MaxStacks"/>); 1 for others.</summary>
        public int Stacks { get; internal set; } = 1;

        /// <summary>Tick from which its on-hit or on-attacked effects may run again (<see cref="ModifierSpec.ProcCooldownMs"/>).</summary>
        internal long NextProcTick { get; set; }

        /// <summary>Its on-hit effects are running: an attack they make does not run them again (no chain of procs).</summary>
        internal bool Procing { get; set; }

        /// <summary>Tries since its last pseudo-random proc.</summary>
        internal int PrdTries;

        /// <summary>The bearer's attacks landed since the modifier was applied (<see cref="ModifierSpec.AttackCharges"/>).</summary>
        internal int AttacksLanded { get; set; }
    }

    public static class Modifiers
    {
        /// <summary>Applies or refreshes the modifier; an enemy debuff on a debuff-immune unit is not applied (null).</summary>
        public static ActiveModifier? Apply(SimulationContext<MatchState> context, MatchUnit target, ModifierSpec spec, EntityId source)
        {
            if (spec.IsDebuff && (target.Flags & ModifierFlags.DebuffImmune) != 0 && context.State.Find(source)?.Team != target.Team)
                return null;
            long now = context.Tick.Value;
            int durationMs = spec.DurationMs;
            // Dota: status resistance shortens enemy debuffs (not damage over time: its total stays).
            if (durationMs > 0 && spec.IsDebuff && !spec.IsPeriodic && target.StatusResistanceHundredths > 0 && context.State.Find(source)?.Team is Rules.Team from && from != target.Team)
                durationMs = (int)((long)durationMs * (10000 - target.StatusResistanceHundredths) / 10000);
            long expires = spec.DurationMs < 0 ? -1 : now + context.TickRate.TicksFor(durationMs);

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
                existing.BarrierLeft = spec.BarrierMaxHealthPercent > 0 ? target.MaxHp * spec.BarrierMaxHealthPercent / 100 : spec.Barrier;
                existing.AttacksLanded = 0;
                modifier = existing;
                if (spec.MaxStacks > existing.Stacks)
                {
                    existing.Stacks++;
                    target.Recalculate();
                }
            }
            else
            {
                modifier = new ActiveModifier(spec, source, now, expires, now + context.TickRate.TicksFor(spec.PeriodMs)) { Mark = target.Position };
                if (spec.BarrierMaxHealthPercent > 0)
                    modifier.BarrierLeft = target.MaxHp * spec.BarrierMaxHealthPercent / 100;
                target.AddModifier(modifier);
            }

            context.Emit(new ModifierApplied(target.Id, source, spec.Key, expires));
            if (spec.IsDebuff && target.Hero != null && context.State.Ruleset.HeroRules?.Economy != null &&
                context.State.Find(source) is MatchUnit by && by.Hero != null && by.Team != target.Team)
                target.Hero.RecentHeroAttackers[source.Value] = now;   // Dota: debuffing a hero counts for its assists
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
            if (modifier.Spec.ReturnsOnExpiry && reason == "expired" && target.IsAlive)
            {
                // X Marks the Spot: back to the mark; a channel there is interrupted (the official note).
                target.Position = modifier.Mark;
                target.StartPosition = modifier.Mark;
                target.TeleportedTick = context.Tick.Value;
                if (target.Hero != null)
                {
                    target.Hero.Path = null;
                    if (target.Hero.Channel != null)
                        target.Hero.Channel.Cancelled = true;
                }
            }
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
        /// <summary>Deals damage; <c>reflected</c> marks damage a unit reflects or returns (Dispersion, Spiked Carapace), which is not reflected or returned again.</summary>
        public static Fixed Deal(SimulationContext<MatchState> context, EntityId attacker, MatchUnit target, Fixed rawAmount, DamageType type, bool isCritical = false,
            bool reflected = false)
        {
            if (!target.IsAlive || target.Kind == UnitKind.Outpost || (target.Flags & ModifierFlags.Invulnerable) != 0)
                return Fixed.Zero;

            MatchUnit? from = attacker.IsValid ? context.State.Find(attacker) : null;
            List<(MatchUnit Unit, Fixed Amount)>? reflections = reflected ? null : Reflections(context.State, target, ref rawAmount);
            int instanceBlock = 0;
            foreach (ActiveModifier blocking in target.Modifiers)
                instanceBlock = Math.Max(instanceBlock, blocking.Spec.InstanceBlock);
            if (instanceBlock > 0)
                rawAmount = Fixed.Max(Fixed.Zero, rawAmount - Fixed.FromInt(instanceBlock));

            Fixed amount = type == DamageType.Physical && (target.Flags & ModifierFlags.PhysicalImmune) != 0 ? Fixed.Zero
                : type == DamageType.Magical && context.State.Ruleset.Combat.DotaStacking
                    ? rawAmount * Fixed.FromInt(10000 - target.MagicResistHundredths) / Fixed.FromInt(10000)
                    : DamageMath.Mitigate(rawAmount, type, target.Armor, target.MagicResistPercent, context.State.Ruleset.Combat.ArmorFactor);
            // A whole percent keeps its old formula (same fixed-point result); a fractional one works in hundredths.
            if (from != null && from.CastingSpell > 0 && from.SpellAmplificationPercent != 0)
                amount = amount * Fixed.FromInt(100 + from.SpellAmplificationPercent) / Fixed.FromInt(100);
            if (from != null && from.IllusionOf.IsValid && target.IllusionDamageTakenPercent != 0)
                amount = amount * Fixed.FromInt(Math.Max(0, 100 + target.IllusionDamageTakenPercent)) / Fixed.FromInt(100);
            if (target.BackdoorActive)
            {
                // Dota: adds to the other incoming damage changes; even less from illusions and summons.
                bool summoned = from != null && (from.IllusionOf.IsValid || from.Kind == UnitKind.Summon);
                int reduction = summoned ? target.Backdoor!.SummonDamageReductionPercent : target.Backdoor!.DamageReductionPercent;
                amount = amount * Fixed.FromInt(Math.Max(0, 10000 + target.IncomingDamageHundredths - reduction * 100)) / Fixed.FromInt(10000);
            }
            else if (target.IncomingDamageHundredths % 100 != 0)
                amount = amount * Fixed.FromInt(10000 + target.IncomingDamageHundredths) / Fixed.FromInt(10000);
            else if (target.IncomingDamageHundredths != 0)
                amount = amount * Fixed.FromInt(100 + target.IncomingDamageHundredths / 100) / Fixed.FromInt(100);
            if (target.Kind == UnitKind.Miniboss && context.State.Ruleset.Miniboss != null)
            {
                // Dota's Tormentor: reflects part of the damage as taken, before any reduction; its barrier takes the damage first.
                Systems.MinibossSystem.Took(context, target, from, rawAmount, type);
                Fixed absorbed = Fixed.Min(amount, target.MinibossBarrier);
                target.MinibossBarrier -= absorbed;
                amount -= absorbed;
            }

            bool byPlayer = from != null && from.Team != target.Team && (from.Kind == UnitKind.Hero || from.Kind == UnitKind.Summon || from.IllusionOf.IsValid);
            amount = ThroughLayers(context, target, from, amount);
            Fixed healInstead = Fixed.Zero;
            if (amount > Fixed.Zero && target.Modifiers.Any(m => m.Spec.DamageHeals))
            {
                healInstead = amount;   // Borrowed Time: after the reductions, before any barrier (Aphotic Shield lets it through)
                amount = Fixed.Zero;
            }
            bool fromRear = false;
            amount = ByDirection(target, from, amount, ref fromRear);
            ActiveModifier? returning = null;
            Fixed returned = Fixed.Zero;
            if (!reflected && byPlayer && amount > Fixed.Zero)
            {
                returning = target.Modifiers.FirstOrDefault(m => m.Spec.ReturnPercent > 0 && !m.ReturnedTo.Contains(from!.Id.Value));
                if (returning != null)
                {
                    returning.ReturnedTo.Add(from!.Id.Value);
                    returned = amount * Fixed.FromInt(returning.Spec.ReturnPercent) / Fixed.FromInt(100);
                    amount = Fixed.Zero;
                }
            }

            if (amount > Fixed.Zero && from != target)
                amount = SwallowInstance(context, target, amount);
            amount = Absorb(context, target, amount);
            if ((target.Flags & ModifierFlags.CannotDie) != 0)
                amount = Fixed.Max(Fixed.Zero, Fixed.Min(amount, target.Hp - Fixed.One));
            Fixed before = target.Hp;
            target.Hp = Fixed.Max(Fixed.Zero, target.Hp - amount);
            // Damage from a player's unit (any team, the unit itself too) or the boss: Dota's Regeneration rune slows for a while.
            if (amount > Fixed.Zero && from != null && (from.Kind == UnitKind.Hero || from.Kind == UnitKind.Summon || from.Kind == UnitKind.Boss || from.IllusionOf.IsValid))
                target.LastPlayerDamageTick = context.Tick.Value;
            if (target.BackdoorActive && from != null && from.Team != target.Team)
                target.BackdoorHealLeft += before - target.Hp;
            if (attacker.IsValid)
            {
                target.LastAttackerId = attacker;
                MatchUnit? source = context.State.Find(attacker);
                if (target.Hero != null && source?.Hero != null && source.Team != target.Team)
                    target.Hero.RecentHeroAttackers[attacker.Value] = context.Tick.Value;
            }
            context.Emit(new DamageDealt(attacker, target.Id, amount, target.Hp, type, isCritical));
            if (byPlayer && amount > Fixed.Zero)
                CountTowardCleanse(context, target, amount);
            if (fromRear && amount > Fixed.Zero)
                CountTowardRelease(context, target, amount);
            if (healInstead > Fixed.Zero)
                Heal(context, target.Id, target, healInstead);
            if (!reflected && amount > Fixed.Zero && target.Modifiers.Count > 0)
                PassOn(context, target, amount, type);
            if (amount > Fixed.Zero && target.Hero != null && target.IsAlive)
                Systems.AbilitySystem.CastBelowHealth(context, target);
            if (reflections != null)
            {
                foreach (var (unit, share) in reflections)
                    Deal(context, target.Id, unit, share, type, reflected: true);
            }

            if (returning != null && from!.IsAlive)
            {
                Deal(context, target.Id, from, returned, type, reflected: true);
                Systems.Effects.Run(context, target, returning.Spec.OnReturn, 1, from, from.Position);
            }

            return amount;
        }

        /// <summary>
        /// Dispersion: takes each reflecting modifier's share off the raw damage and spreads it over the enemies around, full near and
        /// less farther; null when the target reflects nothing.
        /// </summary>
        private static List<(MatchUnit, Fixed)>? Reflections(MatchState state, MatchUnit target, ref Fixed rawAmount)
        {
            if (target.IllusionOf.IsValid || target.Modifiers.Count == 0 || !target.Modifiers.Any(m => m.Spec.ReflectHundredths > 0))
                return null;
            var reflections = new List<(MatchUnit, Fixed)>();
            foreach (ActiveModifier modifier in target.Modifiers.Where(m => m.Spec.ReflectHundredths > 0).ToList())
            {
                ModifierSpec spec = modifier.Spec;
                Fixed share = rawAmount * Fixed.FromInt(spec.ReflectHundredths) / Fixed.FromInt(10000);
                rawAmount -= share;
                foreach (MatchUnit unit in state.Units.Where(u => Systems.Effects.Matches(state, target, u, Rules.TargetTeam.Enemy)).OrderBy(u => u.Id.Value))
                {
                    Fixed distance = FixedVector2.Distance(target.Position, unit.Position);
                    if (distance > Fixed.FromInt(spec.ReflectRadius))
                        continue;
                    Fixed part = distance <= Fixed.FromInt(spec.ReflectFullRadius) ? share
                        : share * (Fixed.FromInt(spec.ReflectRadius) - distance) / Fixed.FromInt(spec.ReflectRadius - spec.ReflectFullRadius);
                    if (part > Fixed.Zero)
                        reflections.Add((unit, part));
                }
            }

            return reflections;
        }

        /// <summary>cos(degrees) in ten-thousandths for whole degrees 0..180: angles compared without floating point.</summary>
        private static readonly int[] CosTenThousandths = { 10000, 9998, 9994, 9986, 9976, 9962, 9945, 9925, 9903, 9877, 9848, 9816, 9781, 9744, 9703, 9659, 9613, 9563, 9511, 9455, 9397, 9336, 9272, 9205, 9135, 9063, 8988, 8910, 8829, 8746, 8660, 8572, 8480, 8387, 8290, 8192, 8090, 7986, 7880, 7771, 7660, 7547, 7431, 7314, 7193, 7071, 6947, 6820, 6691, 6561, 6428, 6293, 6157, 6018, 5878, 5736, 5592, 5446, 5299, 5150, 5000, 4848, 4695, 4540, 4384, 4226, 4067, 3907, 3746, 3584, 3420, 3256, 3090, 2924, 2756, 2588, 2419, 2250, 2079, 1908, 1736, 1564, 1392, 1219, 1045, 872, 698, 523, 349, 175, 0, -175, -349, -523, -698, -872, -1045, -1219, -1392, -1564, -1736, -1908, -2079, -2250, -2419, -2588, -2756, -2924, -3090, -3256, -3420, -3584, -3746, -3907, -4067, -4226, -4384, -4540, -4695, -4848, -5000, -5150, -5299, -5446, -5592, -5736, -5878, -6018, -6157, -6293, -6428, -6561, -6691, -6820, -6947, -7071, -7193, -7314, -7431, -7547, -7660, -7771, -7880, -7986, -8090, -8192, -8290, -8387, -8480, -8572, -8660, -8746, -8829, -8910, -8988, -9063, -9135, -9205, -9272, -9336, -9397, -9455, -9511, -9563, -9613, -9659, -9703, -9744, -9781, -9816, -9848, -9877, -9903, -9925, -9945, -9962, -9976, -9986, -9994, -9998, -10000 };

        /// <summary>
        /// Bristleback: cuts damage from the side or rear by where the source stands against the way the bearer faces (a unit that
        /// has not faced anywhere yet takes it as from the front).
        /// </summary>
        private static Fixed ByDirection(MatchUnit target, MatchUnit? from, Fixed amount, ref bool fromRear)
        {
            if (from == null || from == target || amount <= Fixed.Zero || target.Modifiers.Count == 0 || target.Facing == FixedVector2.Zero)
                return amount;
            ActiveModifier? hide = target.Modifiers.FirstOrDefault(m => m.Spec.SideAngle > 0 || m.Spec.BackAngle > 0);
            if (hide == null)
                return amount;
            FixedVector2 toSource = from.Position - target.Position;
            if (toSource == FixedVector2.Zero)
                return amount;
            // The source is within A degrees of the back when the angle between the facing and it is at least 180 - A,
            // that is when cos <= -cos(A): dot <= -cos(A) * |facing| * |toSource|.
            Fixed dot = target.Facing.X * toSource.X + target.Facing.Y * toSource.Y;
            Fixed lengths = target.Facing.Length * toSource.Length;
            bool Within(int degrees) => degrees > 0 && dot * Fixed.FromInt(10000) <= Fixed.FromInt(-CosTenThousandths[System.Math.Min(180, degrees)]) * lengths;
            ModifierSpec spec = hide.Spec;
            int cut = 0;
            if (Within(spec.BackAngle))
            {
                cut = spec.BackReductionPercent;
                fromRear = true;
            }
            else if (Within(spec.SideAngle))
                cut = spec.SideReductionPercent;
            return amount * Fixed.FromInt(100 - cut) / Fixed.FromInt(100);
        }

        /// <summary>Quill Spray released by Bristleback: each threshold of damage from the rear casts the learned ability once.</summary>
        private static void CountTowardRelease(SimulationContext<MatchState> context, MatchUnit target, Fixed amount)
        {
            foreach (ActiveModifier modifier in target.Modifiers.Where(m => m.Spec.ReleaseThreshold > 0 && m.Spec.ReleaseAbilityId != null).ToList())
            {
                modifier.ReleaseTaken += amount;
                if (modifier.ReleaseTaken < Fixed.FromInt(modifier.Spec.ReleaseThreshold))
                    continue;
                modifier.ReleaseTaken -= Fixed.FromInt(modifier.Spec.ReleaseThreshold);
                int level = target.Hero?.AbilityLevel(modifier.Spec.ReleaseAbilityId!) ?? 0;
                Rules.AbilityDefinition? ability = context.State.Ruleset.FindAbility(modifier.Spec.ReleaseAbilityId!);
                if (level > 0 && ability != null && target.IsAlive)
                    Systems.Effects.Run(context, target, ability.Effects, level, target, target.Position);
            }
        }

        /// <summary>Gravekeeper's Cloak: the layers cut the damage; a player's damage over the minimum takes one off.</summary>
        private static Fixed ThroughLayers(SimulationContext<MatchState> context, MatchUnit target, MatchUnit? from, Fixed amount)
        {
            if (target.Modifiers.Count == 0 || amount <= Fixed.Zero)
                return amount;
            long now = context.Tick.Value;
            foreach (ActiveModifier cloak in target.Modifiers.Where(m => m.Spec.Layers > 0))
            {
                int layers = cloak.LayersAt(now);
                amount = amount * Fixed.FromInt(System.Math.Max(0, 100 - layers * cloak.Spec.LayerReductionPercent)) / Fixed.FromInt(100);
                bool player = from != null && from != target && (from.Kind == UnitKind.Hero || from.Kind == UnitKind.Summon || from.IllusionOf.IsValid);
                if (player && layers > 0 && amount > Fixed.FromInt(cloak.Spec.LayerMinimumDamage))
                    cloak.LayerReturns.Add(now + context.TickRate.TicksFor(cloak.Spec.LayerRecoveryMs));
            }

            return amount;
        }

        /// <summary>Refraction: the instance is swallowed whole while charges last; one it cannot hold uses up a charge.</summary>
        private static Fixed SwallowInstance(SimulationContext<MatchState> context, MatchUnit target, Fixed amount)
        {
            ActiveModifier? refraction = target.Modifiers.FirstOrDefault(m => m.Spec.AbsorbInstances > 0 && m.InstancesLeft > 0);
            if (refraction == null)
                return amount;
            if (amount >= Fixed.FromInt(refraction.InstanceBarrierLeft))
            {
                refraction.InstancesLeft--;
                refraction.InstanceBarrierLeft = refraction.Spec.AbsorbBarrier;
                if (refraction.InstancesLeft == 0)
                    Modifiers.Remove(context, target, refraction, "instances_used");
            }
            else
                refraction.InstanceBarrierLeft -= amount.ToIntFloor() + (amount > Fixed.FromInt(amount.ToIntFloor()) ? 1 : 0);

            return Fixed.Zero;
        }

        /// <summary>Kraken Shell: players' damage taken without a long pause adds up; past the threshold the bearer is strongly dispelled.</summary>
        private static void CountTowardCleanse(SimulationContext<MatchState> context, MatchUnit target, Fixed amount)
        {
            if (target.Modifiers.Count == 0)
                return;
            long now = context.Tick.Value;
            bool cleanse = false;
            foreach (ActiveModifier modifier in target.Modifiers.Where(m => m.Spec.CleanseThreshold > 0))
            {
                if (modifier.CleanseLastTick >= 0 && now - modifier.CleanseLastTick > context.TickRate.TicksFor(modifier.Spec.CleanseWindowMs))
                    modifier.CleanseTaken = Fixed.Zero;
                modifier.CleanseTaken += amount;
                modifier.CleanseLastTick = now;
                if (modifier.CleanseTaken >= Fixed.FromInt(modifier.Spec.CleanseThreshold))
                {
                    modifier.CleanseTaken = Fixed.Zero;
                    cleanse = true;
                }
            }

            if (cleanse)
                Systems.Effects.Dispel(context, target, target, strong: true);
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

        /// <summary>
        /// Kills the target outright (Culling Blade): no armor, damage reduction or barrier applies, but a unit
        /// that cannot die keeps 1 health.
        /// </summary>
        public static void Execute(SimulationContext<MatchState> context, EntityId attacker, MatchUnit target)
        {
            if (!target.IsAlive)
                return;
            Fixed amount = (target.Flags & ModifierFlags.CannotDie) != 0 ? target.Hp - Fixed.One : target.Hp;
            target.Hp -= amount;
            target.LastAttackerId = attacker;
            MatchUnit? source = context.State.Find(attacker);
            if (target.Hero != null && source?.Hero != null && source.Team != target.Team)
                target.Hero.RecentHeroAttackers[attacker.Value] = context.Tick.Value;
            context.Emit(new DamageDealt(attacker, target.Id, amount, target.Hp, DamageType.Pure, false));
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
            if (restored > Fixed.Zero)
                ShareRestore(context, target, restored, health: true);
            return restored;
        }

        /// <summary>Mana restored to a unit (spells, items); Tether passes a share on.</summary>
        public static void RestoreMana(SimulationContext<MatchState> context, MatchUnit target, Fixed amount)
        {
            if (target.Hero == null || !target.IsAlive)
                return;
            Fixed before = target.Hero.Mana;
            target.Hero.Mana = Fixed.Min(Fixed.FromInt(target.Hero.MaxMana), target.Hero.Mana + amount);
            Fixed restored = target.Hero.Mana - before;
            if (restored > Fixed.Zero)
                ShareRestore(context, target, restored, health: false);
        }

        /// <summary>Tether: the units tethered by <paramref name="caster"/> get their share of what it restored.</summary>
        private static void ShareRestore(SimulationContext<MatchState> context, MatchUnit caster, Fixed restored, bool health)
        {
            foreach (MatchUnit tethered in context.State.Units)
            {
                if (tethered == caster || !tethered.IsAlive || tethered.Modifiers.Count == 0)
                    continue;
                foreach (ActiveModifier tether in tethered.Modifiers)
                {
                    int percent = health ? tether.Spec.HealSharePercent : tether.Spec.ManaSharePercent;
                    if (percent <= 0 || tether.Source != caster.Id)
                        continue;
                    Fixed share = restored * Fixed.FromInt(percent) / Fixed.FromInt(100);
                    if (health)
                        tethered.Hp = Fixed.Min(Fixed.FromInt(tethered.MaxHp), tethered.Hp + share);
                    else if (tethered.Hero != null)
                        tethered.Hero.Mana = Fixed.Min(Fixed.FromInt(tethered.Hero.MaxMana), tethered.Hero.Mana + share);
                }
            }
        }

        /// <summary>Fatal Bonds and Disseminate: a part of the damage the unit took goes on to the bonded units or the enemies around.</summary>
        private static void PassOn(SimulationContext<MatchState> context, MatchUnit target, Fixed amount, DamageType type)
        {
            MatchState state = context.State;
            foreach (ActiveModifier link in target.Modifiers.Where(m => m.Spec.ShareDamagePercent > 0 || m.Spec.SpreadDamagePercent > 0).ToList())
            {
                ModifierSpec spec = link.Spec;
                if (spec.ShareDamagePercent > 0)
                {
                    Fixed share = amount * Fixed.FromInt(spec.ShareDamagePercent) / Fixed.FromInt(100);
                    foreach (MatchUnit other in state.Units.Where(u => u != target && u.IsAlive && u.Modifiers.Any(m => m.Spec.Key == spec.Key && m.Source == link.Source))
                                 .OrderBy(u => u.Id.Value).ToList())
                        Deal(context, link.Source, other, share, type, reflected: true);
                }

                MatchUnit? caster = state.Find(link.Source);
                if (spec.SpreadDamagePercent > 0 && caster != null)
                {
                    Fixed share = amount * Fixed.FromInt(spec.SpreadDamagePercent) / Fixed.FromInt(100);
                    foreach (MatchUnit enemy in state.Units.Where(u => Systems.Effects.Matches(state, caster, u, Rules.TargetTeam.Enemy) &&
                                                                       Systems.Geometry.WithinRange(target.Position, u.Position, spec.SpreadRadius + u.BoundingRadius))
                                 .OrderBy(u => u.Id.Value).ToList())
                        Deal(context, link.Source, enemy, share, type, reflected: true);
                }
            }
        }
    }
}
