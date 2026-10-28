using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.MatchSimulation.Combat;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Time;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation
{
    public enum UnitKind
    {
        Creep = 0,
        Structure = 1,
        Hero = 2,
        Summon = 3,

        /// <summary>Neutral objective (see <see cref="BossDefinition"/>).</summary>
        Boss = 4,

        /// <summary>A power-up lying on the map; picked up by walking onto it, never attacked.</summary>
        Rune = 5,

        /// <summary>Neutral objective that neither moves nor attacks (see <see cref="MinibossDefinition"/>).</summary>
        Miniboss = 6,

        /// <summary>A building of a team that cannot be attacked, captured by enemy heroes standing at it (see <see cref="OutpostRules"/>).</summary>
        Outpost = 7,

        /// <summary>An item lying on the ground (Dota's dropped Aegis): picked up by walking onto it, destroyed by a hero's hit.</summary>
        GroundItem = 8,
    }

    /// <summary>
    /// A creep or structure in the authoritative match state. Presentation and analytics read
    /// it; only match systems write it.
    /// </summary>
    public sealed class MatchUnit
    {
        private readonly List<ActiveModifier> _modifiers = new List<ActiveModifier>();
        private readonly TickRate _rate;

        internal MatchUnit(EntityId id, Team team, UnitKind kind, string typeId, UnitStats stats, FixedVector2 position, TickRate rate)
        {
            Id = id;
            Team = team;
            Kind = kind;
            TypeId = typeId;
            Stats = stats;
            Position = position;
            StartPosition = position;
            _rate = rate;
            PendingAttackTick = -1;
            Recalculate();
            Hp = Fixed.FromInt(MaxHp);
        }

        public EntityId Id { get; }
        /// <summary>The unit's side; only an Outpost changes it (captured).</summary>
        public Team Team { get; internal set; }
        public UnitKind Kind { get; }
        public string TypeId { get; }

        /// <summary>Base stats before modifiers.</summary>
        public UnitStats Stats { get; internal set; }

        public FixedVector2 Position { get; internal set; }
        public Fixed Hp { get; internal set; }
        public bool IsAlive => Hp > Fixed.Zero;

        // Effective values: base stats plus active modifiers. Refreshed by Recalculate().
        public int MaxHp { get; private set; }
        public Fixed Armor { get; private set; }
        /// <summary>Magic resistance in hundredths of a percent (Dota's multiplied sources give fractions).</summary>
        public int MagicResistHundredths { get; private set; }

        public int MagicResistPercent => MagicResistHundredths / 100;

        /// <summary>The match's combat rules (stacking, attack speed cap); set when the unit enters the match.</summary>
        internal CombatRules? Combat { get; set; }

        /// <summary>Critical strike sources from the strongest multiplier down (Dota's stacking).</summary>
        public IReadOnlyList<(int ChanceHundredths, int MultiplierPercent)> CritSources => CritModifiers.Select(m => (m.Spec.CritChanceHundredths, m.Spec.CritMultiplierPercent)).ToArray();

        /// <summary>The modifiers giving critical strikes, from the strongest multiplier down (each keeps its pseudo-random tries).</summary>
        internal IReadOnlyList<ActiveModifier> CritModifiers { get; private set; } = Array.Empty<ActiveModifier>();

        /// <summary>The tick it last started an attack, and on whom (Dota's aggro reads it the next tick).</summary>
        internal long AttackStartTick = -1;
        internal EntityId AttackStartTarget;

        /// <summary>Dota's aggro: the hero an attack order made this creep chase, and until when.</summary>
        internal EntityId ForcedTargetId;
        internal long ForcedUntilTick;

        /// <summary>Dota's aggro: whether this lane creep can be drawn (creeps spawned before 5:00 only near their tier 1).</summary>
        internal bool AggroUnlocked;

        /// <summary>A boss's spells: when each is ready again, and the one being cast with the tick it lands.</summary>
        internal long[]? BossAbilityReadyTicks;
        internal int BossCastingAbility = -1;
        internal long BossCastLandsTick;

        /// <summary>The boss growth intervals applied to it (Dota's Roshan).</summary>
        internal int BossGrowthIntervals = -1;

        /// <summary>A miniboss's barrier left (Dota's Tormentor: Unyielding Shield).</summary>
        public Fixed MinibossBarrier { get; internal set; }

        internal int MinibossIntervals;
        internal long MinibossSpawnTick;
        internal long MinibossLastHitTick = -1;
        internal long MinibossRegenCarry;

        /// <summary>Damage to reflect in the next wave, by damage type, and who dealt damage since the last wave.</summary>
        internal Fixed[]? MinibossReflected;
        internal readonly List<EntityId> MinibossDealers = new List<EntityId>();

        /// <summary>A boss's way to its other pit (Dota), around the terrain; null when not walking one.</summary>
        internal IReadOnlyList<FixedVector2>? BossPath;
        internal int BossPathIndex;

        /// <summary>Gold its killer gets on top of its type's bounty (Dota's creep upgrades).</summary>
        internal Fixed BountyGoldBonus;

        /// <summary>XP on top of its type's bounty (Dota's creep upgrades).</summary>
        public int BountyXpBonus { get; internal set; }

        /// <summary>A building's Backdoor Protection (<see cref="StructureType.Backdoor"/>); null = none.</summary>
        internal BackdoorProtection? Backdoor { get; set; }

        /// <summary>Whether its Backdoor Protection is on (no enemy lane creeps nearby lately).</summary>
        public bool BackdoorActive { get; internal set; }

        /// <summary>The tick until which its Backdoor Protection stays off.</summary>
        internal long BackdoorOffUntilTick;

        /// <summary>Enemy damage its Backdoor Protection has still to heal.</summary>
        internal Fixed BackdoorHealLeft;

        /// <summary>Percent more damage its spells deal (Dota's spell amplification).</summary>
        public int SpellAmplificationPercent { get; private set; }

        /// <summary>The parts of its cooldowns and mana costs left, in hundredths of a percent (Dota's Arcane rune).</summary>
        public int CooldownLeftHundredths { get; private set; } = 10000;
        public int ManaCostLeftHundredths { get; private set; } = 10000;

        /// <summary>The last tick a player's unit or the boss damaged it (-1 = never).</summary>
        /// <summary>When a player's unit (or the boss) last damaged it; -1 never.</summary>
        public long LastPlayerDamageTick { get; internal set; } = -1;

        /// <summary>Above zero while its spell's effects run (they get its spell amplification).</summary>
        internal int CastingSpell;

        /// <summary>Dota's status resistance in hundredths of a percent (enemy debuffs last that much less).</summary>
        public int StatusResistanceHundredths { get; private set; }

        /// <summary>Dota's slow resistance in hundredths of a percent.</summary>
        public int SlowResistanceHundredths { get; private set; }

        /// <summary>Percent change of the damage illusions deal to it.</summary>
        public int IllusionDamageTakenPercent { get; private set; }

        /// <summary>The modifier that blocks enemy targeted spells (<see cref="ModifierSpec.SpellBlockCooldownMs"/>), if any.</summary>
        internal ActiveModifier? SpellBlocker { get; private set; }

        /// <summary>Attacks taken since the last evasion (Dota: evasion is pseudo-random).</summary>
        internal int EvasionTries;
        public int AttackDamageMin { get; private set; }
        public int AttackDamageMax { get; private set; }
        public int AttackSpeedBonus { get; private set; }
        public int MoveSpeed { get; private set; }
        public ModifierFlags Flags { get; private set; }
        public int AttackRange { get; private set; }

        /// <summary>Percent added to damage taken, to the unit's attack damage, and the chance attacks on it miss (from modifiers).</summary>
        /// <summary>Change of damage taken in hundredths of a percent.</summary>
        public int IncomingDamageHundredths { get; private set; }

        public int IncomingDamagePercent => IncomingDamageHundredths / 100;
        public int AttackDamagePercent { get; private set; }
        /// <summary>Chance that an attack on the unit misses, in hundredths of a percent.</summary>
        public int EvasionHundredths { get; private set; }

        public int EvasionPercent => EvasionHundredths / 100;

        private const int MaxMoveSpeed = 550;
        public int AcquisitionRange => Stats.AcquisitionRange;
        public int BoundingRadius => Stats.BoundingRadius;
        public bool CanAttackAtAll => Stats.CanAttack;

        public bool IsStunned => (Flags & ModifierFlags.Stunned) != 0;

        // Action gates use the tick-start snapshot: a status applied during a tick takes effect
        // from the next tick, so the order units are processed in cannot decide who acts first.
        public bool CanMove => (StartFlags & (ModifierFlags.Stunned | ModifierFlags.Rooted | ModifierFlags.Busy)) == 0 && StepPerTick > Fixed.Zero;
        public bool CanStartAttack => (StartFlags & (ModifierFlags.Stunned | ModifierFlags.Disarmed | ModifierFlags.Busy)) == 0 && CanAttackAtAll;

        /// <summary>
        /// The way the unit faces (not normalised): the way it last stepped, or toward what it last started attacking; zero until then.
        /// Directional effects read it (Bristleback).
        /// </summary>
        public FixedVector2 Facing { get; internal set; }

        /// <summary>The slashes in progress (Dota's Omnislash); null when none.</summary>
        internal SlashState? Slashes { get; set; }

        /// <summary>Position at the start of the current tick; pursuit within a tick aims here.</summary>
        public FixedVector2 StartPosition { get; internal set; }

        /// <summary>Status flags at the start of the current tick.</summary>
        public ModifierFlags StartFlags { get; internal set; }

        public IReadOnlyList<ActiveModifier> Modifiers => _modifiers;

        /// <summary>Hero progression and economy; null for non-hero units.</summary>
        public Heroes.HeroState? Hero { get; internal set; }

        /// <summary>Hero that summoned this unit; None otherwise.</summary>
        public EntityId Owner { get; internal set; }

        /// <summary>The hero this unit is an illusion of; None for other units.</summary>
        public EntityId IllusionOf { get; internal set; }

        /// <summary>Tick a temporary unit disappears; -1 for permanent units.</summary>
        public long ExpiresTick { get; internal set; } = -1;

        /// <summary>Extra HP regeneration per second from modifiers and items.</summary>
        public Fixed HpRegenBonus { get; private set; }

        /// <summary>Heroes with attributes: current strength, agility and intelligence (level and bonuses included); zero otherwise.</summary>
        public Fixed Strength { get; private set; }
        public Fixed Agility { get; private set; }
        public Fixed Intelligence { get; private set; }

        /// <summary>Heroes: health and mana regeneration per second from all sources.</summary>
        public Fixed HpRegenPerSecond { get; private set; }
        public Fixed ManaRegenPerSecond { get; private set; }

        /// <summary>Critical strike chance in hundredths of a percent.</summary>
        public int CritChanceHundredths { get; private set; }

        public int CritChancePercent => CritChanceHundredths / 100;
        public int CritMultiplierPercent { get; private set; }
        public int StructureDamagePercent { get; private set; }
        public int LifestealPercent { get; private set; }

        /// <summary>Lane a creep belongs to; null for structures.</summary>
        public string? LaneId => Lane?.Id;

        /// <summary>Map placement of a structure; null for creeps.</summary>
        public StructurePlacement? Placement { get; internal set; }

        /// <summary>Current attack/chase target, or <see cref="EntityId.None"/>.</summary>
        public EntityId TargetId { get; internal set; }

        /// <summary>Last tick the unit moved or started an attack; used to detect stuck units.</summary>
        public long LastActiveTick { get; internal set; }

        /// <summary>Runes only: what the rune gives and which spot it occupies.</summary>
        public RuneType? Rune { get; internal set; }
        internal int RuneSpot { get; set; }

        /// <summary>A rune's schedule (Dota's rune timings; -1 = the plain rule) and when it appeared.</summary>
        internal int RuneSchedule { get; set; } = -1;
        internal long RuneSpawnTick { get; set; }

        /// <summary>Neutral units only: where they return to and how far they may chase.</summary>
        internal FixedVector2 NeutralHome { get; set; }
        internal int LeashRadius { get; set; }
        internal CampState? Camp { get; set; }

        internal LaneDefinition? Lane { get; set; }
        internal int NextWaypoint { get; set; }
        internal Fixed StepPerTick { get; private set; }
        internal long AttackIntervalTicks { get; private set; }
        internal long AttackPointTicks { get; private set; }
        /// <summary>Earliest tick the next attack can start (attack cooldown); a player knows this of its own hero.</summary>
        public long NextAttackTick { get; internal set; }
        /// <summary>Tick the attack being wound up lands, or -1 (the wind-up is visible, like an animation).</summary>
        public long PendingAttackTick { get; internal set; }

        /// <summary>Target of the attack being wound up.</summary>
        public EntityId PendingTargetId { get; internal set; }
        internal EntityId LastAttackerId { get; set; }

        internal ActiveModifier? FindModifier(string key, EntityId source)
        {
            foreach (ActiveModifier modifier in _modifiers)
            {
                if (modifier.Spec.Key == key && modifier.Source == source)
                    return modifier;
            }

            return null;
        }

        /// <summary>Whether another instance of this unique modifier's key counts instead (<see cref="ModifierSpec.Unique"/>).</summary>
        private bool OutrankedUnique(ActiveModifier modifier)
        {
            ModifierSpec spec = modifier.Spec;
            bool before = true;   // an equal instance counts only if it was applied first
            foreach (ActiveModifier other in _modifiers)
            {
                if (other == modifier)
                {
                    before = false;
                    continue;
                }

                if (!other.Spec.Unique || other.Spec.Key != spec.Key)
                    continue;
                int byArmor = other.Spec.ArmorBonus.CompareTo(spec.ArmorBonus);
                int byRegen = other.Spec.HpRegenBonus.CompareTo(spec.HpRegenBonus);
                int byResist = other.Spec.MagicResistBonus.CompareTo(spec.MagicResistBonus);
                if (byArmor > 0 || (byArmor == 0 && (byRegen > 0 || (byRegen == 0 && (byResist > 0 || (byResist == 0 && before))))))
                    return true;
            }

            return false;
        }

        internal void AddModifier(ActiveModifier modifier)
        {
            _modifiers.Add(modifier);
            Recalculate();
        }

        internal void RemoveModifier(ActiveModifier modifier)
        {
            _modifiers.Remove(modifier);
            Recalculate();
        }

        /// <summary>Drops timed modifiers (on death); permanent ones such as passives remain.</summary>
        internal void ClearTemporaryModifiers()
        {
            _modifiers.RemoveAll(m => !m.Spec.IsPermanent);
            Recalculate();
        }

        /// <summary>
        /// Recomputes effective stats from base stats and modifiers. Attack speed scales both the
        /// attack interval and the attack point; move speed has a floor of 100 while slowed.
        /// </summary>
        private static int Hundredths(Fixed value) => (value * Fixed.FromInt(100) + Fixed.FromRatio(1, 2)).ToIntFloor();

        /// <summary>Whole part of a product of fixed-point values, tolerant to their last-digit error (0.45 × 60 is 27, not 26).</summary>
        private static int WholePart(Fixed value) => (value + Fixed.FromRatio(1, 1000)).ToIntFloor();

        internal void Recalculate()
        {
            Fixed armor = Stats.Armor;
            int magicResist = Stats.MagicResistPercent;
            int damageBonus = 0;
            int attackSpeed = Hero != null ? Hero.Definition.AttackSpeedPerLevel * (Hero.Level - 1) : 0;
            int movePercent = 0;
            int moveFlat = 0;
            int incomingDamage = 0, attackDamagePercent = 0, evasion = 0;
            int attackRangeBonus = 0;
            Fixed manaRegenBonus = Fixed.Zero;
            int critChance = 0;
            int critMultiplier = 0;
            int structurePercent = 0;
            int lifesteal = 0;
            int maxHpBonus = 0;
            int maxManaBonus = 0;
            Fixed hpRegenBonus = Fixed.Zero;
            int strengthBonus = 0, agilityBonus = 0, intelligenceBonus = 0;
            ModifierFlags flags = Stats.InnateFlags;
            bool dota = Combat?.DotaStacking == true;
            long evasionLeft = 10000;          // Dota: the chance not to evade, multiplied by every source
            Fixed magicTakenFactor = Fixed.One; // Dota: the part of magic damage taken after the bonus sources
            List<ActiveModifier>? crits = null;
            long statusLeft = 10000, slowLeft = 10000;   // Dota: the parts left after status and slow resistance, every source multiplied
            int moveSlow = 0, illusionTaken = 0, baseDamagePercent = 0, spellAmp = 0, fixedSpeed = 0;
            long cooldownLeft = 10000, manaCostLeft = 10000;   // Dota: the parts of cooldowns and mana costs left, every source multiplied
            SpellBlocker = null;
            foreach (ActiveModifier modifier in _modifiers)
            {
                ModifierSpec spec = modifier.Spec;
                if (spec.Unique && OutrankedUnique(modifier))
                    continue;
                // Simplification: only the highest crit chance (with its multiplier) counts. In Dota every source rolls,
                // from the highest multiplier down, and the first that procs wins (docs/DOTA_RULES_AUDIT.md).
                if (spec.CritChanceHundredths > critChance)
                {
                    critChance = spec.CritChanceHundredths;
                    critMultiplier = spec.CritMultiplierPercent;
                }

                int n = modifier.Stacks;
                structurePercent += spec.StructureDamagePercent * n;
                lifesteal += spec.LifestealPercent * n;
                maxHpBonus += spec.MaxHpBonus * n;
                maxManaBonus += spec.MaxManaBonus * n;
                hpRegenBonus += spec.HpRegenBonus * Fixed.FromInt(n);
                strengthBonus += spec.StrengthBonus * n;
                agilityBonus += spec.AgilityBonus * n;
                intelligenceBonus += spec.IntelligenceBonus * n;
                armor += spec.ArmorBonus * Fixed.FromInt(n);
                if (!dota)
                    magicResist += spec.MagicResistBonus * n;
                damageBonus += spec.DamageBonus * n;
                attackSpeed += spec.AttackSpeedBonus * n;
                if (spec.MoveSpeedHundredths < 0)
                    moveSlow += spec.MoveSpeedHundredths * n;
                else
                    movePercent += spec.MoveSpeedHundredths * n;
                for (int i = 0; i < n && spec.StatusResistancePercent != 0; i++)
                    statusLeft = statusLeft * (100 - spec.StatusResistancePercent) / 100;
                for (int i = 0; i < n && spec.SlowResistancePercent != 0; i++)
                    slowLeft = slowLeft * (100 - spec.SlowResistancePercent) / 100;
                illusionTaken += spec.IllusionDamageTakenPercent * n;
                baseDamagePercent += spec.BaseDamagePercent * n;
                spellAmp += spec.SpellAmplificationPercent * n;
                for (int i = 0; i < n && spec.CooldownReductionPercent != 0; i++)
                    cooldownLeft = cooldownLeft * (100 - spec.CooldownReductionPercent) / 100;
                for (int i = 0; i < n && spec.ManaCostReductionPercent != 0; i++)
                    manaCostLeft = manaCostLeft * (100 - spec.ManaCostReductionPercent) / 100;
                if (spec.FixedMoveSpeed > fixedSpeed)
                    fixedSpeed = spec.FixedMoveSpeed;
                if (spec.SpellBlockCooldownMs > 0 && SpellBlocker == null)
                    SpellBlocker = modifier;
                moveFlat += spec.MoveSpeedBonus * n;
                incomingDamage += spec.IncomingDamageHundredths * n;
                attackDamagePercent += spec.AttackDamagePercent * n;
                evasion += spec.EvasionHundredths * n;
                if (dota)
                {
                    for (int i = 0; i < n && spec.EvasionHundredths != 0; i++)
                        evasionLeft = evasionLeft * (10000 - spec.EvasionHundredths) / 10000;
                    for (int i = 0; i < n && spec.MagicResistBonus != 0; i++)
                        magicTakenFactor = magicTakenFactor * Fixed.FromInt(100 - spec.MagicResistBonus) / Fixed.FromInt(100);
                    if (spec.CritChanceHundredths > 0)
                        (crits ??= new List<ActiveModifier>()).Add(modifier);
                }
                attackRangeBonus += spec.AttackRangeBonus * n;
                manaRegenBonus += spec.ManaRegenBonus * Fixed.FromInt(n);
                flags |= spec.Flags;
            }

            // A feared unit neither attacks nor casts; a taunted one attacks only (the forced-action system picks its target).
            if ((flags & ModifierFlags.Feared) != 0)
                flags |= ModifierFlags.Disarmed | ModifierFlags.Silenced;
            if ((flags & ModifierFlags.Taunted) != 0)
                flags |= ModifierFlags.Silenced;

            Fixed hpRegen = (Hero?.Definition.HpRegenPerSecond ?? Fixed.Zero) + hpRegenBonus;
            Fixed manaRegen = (Hero?.Definition.ManaRegenPerSecond ?? Fixed.Zero) + manaRegenBonus;
            HeroAttributes? attributes = Hero?.Definition.Attributes;
            AttributeRules? perPoint = Hero?.AttributeRules;
            if (attributes != null && perPoint != null)
            {
                // Dota's attributes: level, gain and bonuses make the current values; each point adds to the stats.
                // Attributes are kept in hundredths (Dota's gains have two decimals) so whole stats come out exact.
                int levels = Hero!.Level - 1;
                int strength100 = (attributes.Strength + strengthBonus) * 100 + Hundredths(attributes.StrengthGain) * levels;
                int agility100 = (attributes.Agility + agilityBonus) * 100 + Hundredths(attributes.AgilityGain) * levels;
                int intelligence100 = (attributes.Intelligence + intelligenceBonus) * 100 + Hundredths(attributes.IntelligenceGain) * levels;
                Strength = Fixed.FromRatio(strength100, 100);
                Agility = Fixed.FromRatio(agility100, 100);
                Intelligence = Fixed.FromRatio(intelligence100, 100);
                maxHpBonus += strength100 * perPoint.HealthPerStrength / 100;
                hpRegen += Strength * perPoint.HealthRegenPerStrength;
                armor += Agility * perPoint.ArmorPerAgility;
                attackSpeed += attributes.BaseAttackSpeed - 100 + agility100 * perPoint.AttackSpeedPerAgility / 100;
                maxManaBonus += intelligence100 * perPoint.ManaPerIntelligence / 100;
                manaRegen += Intelligence * perPoint.ManaRegenPerIntelligence;
                magicResist += WholePart(Intelligence * perPoint.MagicResistPercentPerIntelligence);
                damageBonus += WholePart(attributes.Primary switch
                {
                    PrimaryAttribute.Strength => Strength * perPoint.DamagePerPrimaryPoint,
                    PrimaryAttribute.Agility => Agility * perPoint.DamagePerPrimaryPoint,
                    PrimaryAttribute.Intelligence => Intelligence * perPoint.DamagePerPrimaryPoint,
                    _ => (Strength + Agility + Intelligence) * perPoint.DamagePerUniversalPoint,
                });
            }

            MaxHp = Stats.MaxHp + maxHpBonus;
            HpRegenBonus = hpRegenBonus;
            HpRegenPerSecond = hpRegen;
            ManaRegenPerSecond = manaRegen;
            if (Hp > Fixed.FromInt(MaxHp))
                Hp = Fixed.FromInt(MaxHp);
            if (Hero != null)
            {
                Hero.MaxMana = Hero.Definition.MaxManaAtLevel(Hero.Level) + maxManaBonus;
                if (Hero.Mana > Fixed.FromInt(Hero.MaxMana))
                    Hero.Mana = Fixed.FromInt(Hero.MaxMana);
            }

            Armor = armor;
            if (dota)
            {
                // The base (with intelligence) times the bonus sources: 1 − (1 − base) × Π(1 − source).
                Fixed taken = Fixed.FromInt(100 - Math.Min(magicResist, 100)) / Fixed.FromInt(100) * magicTakenFactor;
                MagicResistHundredths = Math.Min(10000, 10000 - (taken * Fixed.FromInt(10000) + Fixed.FromRatio(1, 2)).ToIntFloor());
            }
            else
            {
                MagicResistHundredths = Math.Min(magicResist, 100) * 100;
            }

            CritModifiers = crits == null ? (IReadOnlyList<ActiveModifier>)Array.Empty<ActiveModifier>()
                : crits.OrderByDescending(c => c.Spec.CritMultiplierPercent).ThenByDescending(c => c.Spec.CritChanceHundredths).ToArray();
            AttackDamageMin = Stats.AttackDamageMin + damageBonus + Stats.AttackDamageMin * baseDamagePercent / 100;
            AttackDamageMax = Stats.AttackDamageMax + damageBonus + Stats.AttackDamageMax * baseDamagePercent / 100;
            SpellAmplificationPercent = spellAmp;
            CooldownLeftHundredths = (int)cooldownLeft;
            ManaCostLeftHundredths = (int)manaCostLeft;
            AttackSpeedBonus = Math.Max(attackSpeed, -80);
            if (Combat != null && Combat.MaxAttackSpeed > 0)
                AttackSpeedBonus = Math.Min(AttackSpeedBonus, Combat.MaxAttackSpeed - 100);
            Flags = flags;
            CritChanceHundredths = Math.Min(critChance, 10000);
            CritMultiplierPercent = critMultiplier;
            StructureDamagePercent = structurePercent;
            LifestealPercent = lifesteal;

            // Dota: slow resistance scales the percentage slows (whole numbers keep the old result without resistance).
            movePercent += slowLeft == 10000 ? moveSlow : (int)(moveSlow * slowLeft / 10000);
            StatusResistanceHundredths = (int)(10000 - statusLeft);
            SlowResistanceHundredths = (int)(10000 - slowLeft);
            IllusionDamageTakenPercent = illusionTaken;

            // Dota: flat bonuses first, then percentages; at least 100 (or the base, if lower), at most 550.
            int speed = Math.Min((Stats.MoveSpeed + moveFlat) * (10000 + movePercent) / 10000, MaxMoveSpeed);   // movePercent in hundredths
            MoveSpeed = Stats.MoveSpeed == 0 ? 0 : fixedSpeed > 0 ? fixedSpeed : Math.Max(speed, Math.Min(100, Stats.MoveSpeed));
            AttackRange = Stats.AttackRange + attackRangeBonus;
            IncomingDamageHundredths = Math.Max(incomingDamage, -10000);
            AttackDamagePercent = Math.Max(attackDamagePercent, -100);
            EvasionHundredths = dota ? (int)(10000 - evasionLeft) : Math.Min(evasion, 10000);
            StepPerTick = Fixed.FromRatio(MoveSpeed, _rate.TicksPerSecond);
            AttackIntervalTicks = Math.Max(1, _rate.TicksFor(Stats.AttackIntervalMs * 100 / (100 + AttackSpeedBonus)));
            AttackPointTicks = _rate.TicksFor(Stats.AttackPointMs * 100 / (100 + AttackSpeedBonus));
        }

        /// <summary>Waypoint by walking order: Dawn walks the lane forward, Dusk its own path or the lane in reverse.</summary>
        internal FixedVector2 WaypointAt(int index)
        {
            if (Team != Team.Dawn && Lane!.DuskWaypoints != null)
                return Lane.DuskWaypoints[index];
            IReadOnlyList<FixedVector2> points = Lane!.Waypoints;
            return Team == Team.Dawn ? points[index] : points[points.Count - 1 - index];
        }

        /// <summary>Number of waypoints on this unit's path of its lane.</summary>
        internal int WaypointCount => Team != Team.Dawn && Lane!.DuskWaypoints != null ? Lane.DuskWaypoints.Count : Lane!.Waypoints.Count;
    }

    /// <summary>Runtime state of an Outpost (Dota).</summary>
    internal sealed class OutpostState
    {
        public OutpostState(MatchUnit unit)
        {
            Unit = unit;
        }

        public MatchUnit Unit { get; }

        /// <summary>Hero-ticks of each team's capture (Dawn, Dusk).</summary>
        public long[] Progress { get; } = new long[2];
    }

    /// <summary>Slashes in progress: the modifier that lasts them, where they reach and how often they come.</summary>
    internal sealed class SlashState
    {
        public SlashState(string modifierKey, int radius, int ratePercent, long nextTick)
        {
            ModifierKey = modifierKey;
            Radius = radius;
            RatePercent = ratePercent;
            NextTick = nextTick;
        }

        public string ModifierKey { get; }
        public int Radius { get; }
        public int RatePercent { get; }
        public long NextTick { get; set; }
    }

    /// <summary>Runtime state of a Watcher (Dota).</summary>
    internal sealed class WatcherState
    {
        /// <summary>The team it is on for; Neutral when ready or off.</summary>
        public Team Owner { get; set; } = Team.Neutral;

        public bool Off { get; set; }

        /// <summary>When it turns ready again (from on or off); -1 when ready.</summary>
        public long UntilTick { get; set; } = -1;

        /// <summary>Ticks of each team's channel (Dawn, Dusk).</summary>
        public long[] Progress { get; } = new long[2];
    }

    /// <summary>Runtime state of a Lotus Pool (Dota).</summary>
    internal sealed class LotusPoolState
    {
        /// <summary>Lotuses grown so far by the clock.</summary>
        public int Grown { get; set; }

        /// <summary>Lotuses in the pool.</summary>
        public int Lotuses { get; set; }

        /// <summary>Lotuses picked in a row by the current team.</summary>
        public int Picked { get; set; }

        public long Progress { get; set; }
        public Team ProgressTeam { get; set; }
    }

    /// <summary>Runtime state of a Shrine of Wisdom (Dota).</summary>
    internal sealed class ShrineState
    {
        /// <summary>Activations counted so far.</summary>
        public int Activations { get; set; }

        /// <summary>Fillings waiting to be taken.</summary>
        public int Charges { get; set; }

        /// <summary>Ticks of the countdown of <see cref="ProgressTeam"/>.</summary>
        public long Progress { get; set; }

        public Team ProgressTeam { get; set; }
    }

    /// <summary>Runtime state of a neutral camp.</summary>
    internal sealed class CampState
    {
        public CampState(NeutralCampDefinition definition, long nextSpawnTick)
        {
            Definition = definition;
            NextSpawnTick = nextSpawnTick;
        }

        public NeutralCampDefinition Definition { get; }

        /// <summary>Creeps of the current spawn (dead ones stay listed until the next spawn).</summary>
        public List<MatchUnit> Members { get; } = new List<MatchUnit>();

        /// <summary>Tick of the next spawn; -1 while creeps of the current spawn are alive.</summary>
        public long NextSpawnTick { get; set; }

        /// <summary>The variant of the last spawn (Dota: the next one differs); -1 before the first.</summary>
        public int LastVariant { get; set; } = -1;

        /// <summary>The evolution stage of the creeps spawned last; -1 before the first.</summary>
        public int Stage { get; set; } = -1;
    }

    /// <summary>Authoritative state of one match (world truth).</summary>
    public sealed class MatchState
    {
        private readonly List<MatchUnit> _units = new List<MatchUnit>();
        private readonly List<MatchUnit> _heroes = new List<MatchUnit>();
        private readonly Dictionary<EntityId, MatchUnit> _byId = new Dictionary<EntityId, MatchUnit>();
        private readonly Dictionary<string, MatchUnit> _structures = new Dictionary<string, MatchUnit>();
        private readonly List<string> _destroyedOrder = new List<string>();
        private readonly HashSet<string> _destroyed = new HashSet<string>();

        internal MatchState(RulesetContent ruleset, TickRate rate)
        {
            Ruleset = ruleset;
            Rate = rate;
            NextWaveTick = rate.TicksFor(ruleset.Waves.FirstSpawnMs);
            Nav = Navigation.NavGrid.FromMap(ruleset.Map);
            Pathfinder = new Navigation.Pathfinder(Nav);
            SightGrid = Navigation.NavGrid.SightOf(ruleset.Map);
            _elevation = BuildElevation(Nav, ruleset.Map.Elevation, ruleset.Map.Terrain);
            if (ruleset.Boss != null)
                NextBossSpawnTick = rate.TicksFor(ruleset.Boss.FirstSpawnMs);
            if (ruleset.Miniboss != null)
                NextMinibossSpawnTick = rate.TicksFor(ruleset.Miniboss.FirstSpawnMs);
            foreach (FixedVector2 _ in ruleset.Shrines?.Positions ?? Array.Empty<FixedVector2>())
                Shrines.Add(new ShrineState());
            foreach (FixedVector2 _ in ruleset.LotusPools?.Positions ?? Array.Empty<FixedVector2>())
                LotusPools.Add(new LotusPoolState());
            foreach (FixedVector2 _ in ruleset.Watchers?.Positions ?? Array.Empty<FixedVector2>())
                Watchers.Add(new WatcherState());
            foreach (NeutralCampDefinition camp in ruleset.Map.Camps)
                Camps.Add(new CampState(camp, rate.TicksFor(camp.FirstSpawnMs)));
            if (ruleset.Runes != null)
                NextRuneTick = rate.TicksFor(ruleset.Runes.FirstSpawnMs);
        }

        /// <summary>Tick of the next rune spawn; -1 without runes.</summary>
        internal long NextRuneTick { get; set; } = -1;

        /// <summary>Dota's rune schedules: how many spawns each has had, the types left in its cycle, and its last type.</summary>
        internal int[]? RuneScheduleSpawns;
        internal List<RuneType>[]? RuneBags;
        internal RuneType?[]? RuneLastTypes;

        private static readonly UnitStats RuneStats = new UnitStats(1, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16, visionRange: 0);

        internal MatchUnit SpawnRune(EntityId id, RuneType type, int spot, FixedVector2 position)
        {
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.Rune, type.Id, RuneStats, position, Rate)
            {
                Rune = type,
                RuneSpot = spot,
            };
            Add(unit);
            return unit;
        }

        internal List<CampState> Camps { get; } = new List<CampState>();

        /// <summary>Projectiles currently in flight, in launch order.</summary>
        public IReadOnlyList<Projectile> Projectiles => _projectiles;

        private readonly List<Projectile> _projectiles = new List<Projectile>();
        private long _nextProjectileId = 1;

        internal Projectile LaunchProjectile(MatchUnit source, MatchUnit target, int speed)
        {
            var projectile = new Projectile(_nextProjectileId++, source.Id, target.Id, source.Position, Fixed.FromRatio(speed, Rate.TicksPerSecond));
            _projectiles.Add(projectile);
            return projectile;
        }

        internal void RemoveProjectile(Projectile projectile) => _projectiles.Remove(projectile);

        /// <summary>The first hero kill of the match happened (Dota's first blood bonus is paid once).</summary>
        public bool FirstBloodTaken { get; internal set; }

        /// <summary>Ground zones left by abilities, in creation order.</summary>
        public IReadOnlyList<GroundZone> Zones => _zones;

        private readonly List<GroundZone> _zones = new List<GroundZone>();
        private long _nextZoneId = 1;

        internal GroundZone AddZone(EntityId source, FixedVector2 position, EffectDefinition definition, int level, long now)
        {
            // A zone without a period is continuous: it pulses at once and then with every aura refresh.
            var zone = new GroundZone(_nextZoneId++, source, position, definition, level, now + Rate.TicksFor(definition.DurationMs), now + Rate.TicksFor(definition.PeriodMs));
            _zones.Add(zone);
            return zone;
        }

        internal void RemoveZone(GroundZone zone) => _zones.Remove(zone);

        /// <summary>Removes every projectile homing on the unit (blink, teleport); returns them for event reporting.</summary>
        internal List<Projectile> Disjoint(MatchUnit unit)
        {
            List<Projectile> dodged = _projectiles.Where(p => p.Target == unit.Id).ToList();
            foreach (Projectile projectile in dodged)
                _projectiles.Remove(projectile);
            return dodged;
        }

        internal Navigation.NavGrid Nav { get; }
        internal Navigation.Pathfinder Pathfinder { get; }

        /// <summary>All heroes (alive or waiting to respawn) in spawn order.</summary>
        public UnitView Heroes => new UnitView(_heroes);

        public RulesetContent Ruleset { get; }

        /// <summary>Living units in creation order (which is also ascending ID order).</summary>
        public UnitView Units => new UnitView(_units);

        public Team? Winner { get; internal set; }

        /// <summary>Placement IDs in the order they were destroyed.</summary>
        public IReadOnlyCollection<string> DestroyedStructures => _destroyedOrder;

        internal TickRate Rate { get; }
        internal long NextWaveTick { get; set; }

        /// <summary>Waves spawned so far.</summary>
        public int WavesSpawned { get; internal set; }

        /// <summary>
        /// Whether the team currently sees the unit (own units: always). Recomputed every few ticks.
        /// Neutral units act on proximity alone and are treated as seeing everything they react to.
        /// </summary>
        public bool IsVisibleTo(Team team, MatchUnit unit) => team == Team.Neutral || KnowledgeOf(team).Sees(unit);

        /// <summary>The living boss, if the ruleset has one and it is up.</summary>
        public MatchUnit? Boss { get; internal set; }

        /// <summary>The Outposts, as in the rules (put on the map at the first tick).</summary>
        internal List<OutpostState> Outposts { get; } = new List<OutpostState>();

        /// <summary>The Aegis lying on the ground; null when there is none.</summary>
        public MatchUnit? GroundAegis { get; internal set; }

        internal MatchUnit SpawnGroundAegis(EntityId id, FixedVector2 position)
        {
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.GroundItem, "aegis", new UnitStats(1, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16, visionRange: 0, nightVisionRange: 0), position, Rate);
            Add(unit);
            GroundAegis = unit;
            return unit;
        }

        internal MatchUnit SpawnOutpost(EntityId id, Team owner, FixedVector2 position, UnitStats stats)
        {
            var unit = new MatchUnit(id, owner, UnitKind.Outpost, "outpost", stats, position, Rate);
            Add(unit);
            Outposts.Add(new OutpostState(unit));
            return unit;
        }

        /// <summary>The Watchers, as in the rules.</summary>
        internal List<WatcherState> Watchers { get; } = new List<WatcherState>();

        /// <summary>The team a Watcher is on for (public: its state shows), Neutral when ready or off.</summary>
        public Team WatcherOwner(int watcher) => Watchers[watcher].Owner;

        /// <summary>Whether a Watcher is off (nobody can take it).</summary>
        public bool WatcherOff(int watcher) => Watchers[watcher].Off;

        /// <summary>The Lotus Pools, as in the rules.</summary>
        internal List<LotusPoolState> LotusPools { get; } = new List<LotusPoolState>();

        /// <summary>Lotuses in a pool (public: they are seen in it).</summary>
        public int LotusCount(int pool) => LotusPools[pool].Lotuses;

        /// <summary>The Shrines of Wisdom, as in the rules.</summary>
        internal List<ShrineState> Shrines { get; } = new List<ShrineState>();

        /// <summary>Fillings of a Shrine of Wisdom waiting to be taken (public: a shrine glows when active).</summary>
        public int ShrineCharges(int shrine) => Shrines[shrine].Charges;

        /// <summary>The living miniboss (Dota's Tormentor); null when there is none.</summary>
        public MatchUnit? Miniboss { get; internal set; }

        /// <summary>Tick of the miniboss's next spawn; -1 while it lives or without one.</summary>
        public long NextMinibossSpawnTick { get; internal set; } = -1;

        /// <summary>Tick the boss (re)spawns; -1 when it is alive or the ruleset has none.</summary>
        public long NextBossSpawnTick { get; internal set; } = -1;

        public Vision.TeamKnowledge KnowledgeOf(Team team) => team == Team.Dawn ? _dawnKnowledge : _duskKnowledge;

        /// <summary>Whether the given tick falls in a night phase (the cycle starts with day).</summary>
        public bool IsNight(long tick)
        {
            DayNightRules? rules = Ruleset.DayNight;
            if (rules == null)
                return false;
            long day = Rate.TicksFor(rules.DayMs);
            long cycle = day + Rate.TicksFor(rules.NightMs);
            return tick % cycle >= day;
        }

        /// <summary>Ground level at a point (0 = low ground): the map terrain's level of its cell, or the highest zone covering its center.</summary>
        public int ElevationAt(FixedVector2 point) => _elevation.Length == 0 ? 0 : _elevation[Nav.IndexOf(point)];

        private readonly int[] _elevation;

        private static int[] BuildElevation(Navigation.NavGrid grid, IReadOnlyList<ElevationZone> zones, MapTerrain? terrain)
        {
            bool fromTerrain = terrain != null && terrain.HasLevels;
            if (zones.Count == 0 && !fromTerrain)
                return Array.Empty<int>();

            var levels = new int[grid.Columns * grid.Rows];
            for (int i = 0; i < levels.Length; i++)
            {
                if (fromTerrain)
                    levels[i] = terrain!.LevelOf(i);
                FixedVector2 center = grid.CenterOf(i);
                foreach (ElevationZone zone in zones)
                {
                    Fixed r = Fixed.FromInt(zone.Radius);
                    if (zone.Level > levels[i] && FixedVector2.DistanceSquared(center, zone.Center) <= r * r)
                        levels[i] = zone.Level;
                }
            }

            return levels;
        }

        private readonly Vision.TeamKnowledge _dawnKnowledge = new Vision.TeamKnowledge(Team.Dawn);
        private readonly Vision.TeamKnowledge _duskKnowledge = new Vision.TeamKnowledge(Team.Dusk);

        /// <summary>Obstacles without clearance: what blocks line of sight.</summary>
        internal Navigation.NavGrid SightGrid { get; }

        public MatchUnit? Find(EntityId id) => _byId.TryGetValue(id, out MatchUnit? unit) ? unit : null;

        public MatchUnit? FindStructure(string placementId) => _structures.TryGetValue(placementId, out MatchUnit? unit) ? unit : null;

        /// <summary>Creeps are always vulnerable; structures follow their placement's protection rule.</summary>
        public bool IsVulnerable(MatchUnit unit)
        {
            if (unit.Kind == UnitKind.Outpost)
                return false;
            StructurePlacement? placement = unit.Placement;
            if (placement?.Vulnerability == VulnerabilityRule.Never)
                return false;
            if (placement == null || placement.ProtectedBy.Count == 0)
                return true;

            bool any = placement.Vulnerability == VulnerabilityRule.AnyProtectorDestroyed;
            for (int i = 0; i < placement.ProtectedBy.Count; i++)
            {
                if (_destroyed.Contains(placement.ProtectedBy[i]) == any)
                    return any;
            }

            return !any;
        }

        /// <summary>
        /// Whether <paramref name="attacker"/> may attack <paramref name="target"/> right now. Heroes
        /// may also attack allied creeps below half HP and allied buildings below their deny threshold (deny).
        /// </summary>
        internal bool CanAttack(MatchUnit attacker, MatchUnit target)
        {
            if (!target.IsAlive || target == attacker || target.Kind == UnitKind.Rune || (target.Flags & ModifierFlags.Invulnerable) != 0)
                return false;
            if (target.Kind == UnitKind.GroundItem)
                return attacker.Kind == UnitKind.Hero;   // only heroes destroy a dropped Aegis
            if (target.Team == attacker.Team)
            {
                if (attacker.Kind != UnitKind.Hero)
                    return false;
                if (target.Kind == UnitKind.Creep)
                    return target.Hp * Fixed.FromInt(2) < Fixed.FromInt(target.MaxHp);
                int deny = target.Kind == UnitKind.Structure ? Ruleset.FindStructureType(target.TypeId)?.DenyHealthPercent ?? 0 : 0;
                return deny > 0 && IsVulnerable(target) && target.Hp * Fixed.FromInt(100) < Fixed.FromInt(target.MaxHp * deny);
            }

            return IsVulnerable(target) && !(attacker.Kind == UnitKind.Structure && target.Kind == UnitKind.Structure);
        }

        internal MatchUnit SpawnHero(EntityId id, Team team, HeroDefinition definition, string laneId, FixedVector2 position, int startingGold)
        {
            var abilities = definition.AbilityIds.Select(a => Ruleset.FindAbility(a)!).ToList();
            var unit = new MatchUnit(id, team, UnitKind.Hero, definition.Id, definition.StatsAtLevel(1), position, Rate)
            {
                Hero = new Heroes.HeroState(definition, laneId, startingGold, abilities) { AttributeRules = Ruleset.HeroRules?.Attributes, Rules = Ruleset.HeroRules },
            };
            unit.Recalculate();   // with the hero state: attributes, mana
            unit.Hp = Fixed.FromInt(unit.MaxHp);
            unit.Hero.Mana = Fixed.FromInt(unit.Hero.MaxMana);
            Add(unit);
            return unit;
        }

        /// <summary>
        /// Dota's Barracks Reinforcement: each building with <see cref="StructureType.ArmorPerStandingBarracks"/> gets that
        /// armor for every standing barracks of its team. Called once the structures are placed and when one falls.
        /// </summary>
        internal void UpdateBarracksReinforcement()
        {
            foreach (MatchUnit unit in _units)
            {
                if (unit.Kind != UnitKind.Structure || !unit.IsAlive)
                    continue;
                StructureType? type = Ruleset.FindStructureType(unit.TypeId);
                if (type?.BarracksReinforcement == null)
                    continue;
                int barracks = _units.Count(u => u.Kind == UnitKind.Structure && u.IsAlive && u.Team == unit.Team && u.Placement?.BarracksLane != null);
                ActiveModifier? current = unit.FindModifier(type.BarracksReinforcement.Key, unit.Id);
                if (current != null && current.Stacks == barracks)
                    continue;
                if (current != null)
                    unit.RemoveModifier(current);
                if (barracks > 0)
                    unit.AddModifier(new ActiveModifier(type.BarracksReinforcement, unit.Id, 0, -1, 0) { Stacks = barracks });
            }
        }

        private long _dawnGlyphReadyTick, _duskGlyphReadyTick;
        private bool _dawnGlyphRequested, _duskGlyphRequested;
        private readonly HashSet<string> _glyphResetsUsed = new HashSet<string>();   // "team/structure type"; membership only

        /// <summary>The tick from which the team's glyph can be used (<see cref="CombatRules.Glyph"/>).</summary>
        public long GlyphReadyTick(Team team) => team == Team.Dawn ? _dawnGlyphReadyTick : _duskGlyphReadyTick;

        internal void SetGlyphReadyTick(Team team, long tick)
        {
            if (team == Team.Dawn)
                _dawnGlyphReadyTick = tick;
            else
                _duskGlyphReadyTick = tick;
        }

        internal void RequestGlyph(Team team)
        {
            if (team == Team.Dawn)
                _dawnGlyphRequested = true;
            else if (team == Team.Dusk)
                _duskGlyphRequested = true;
        }

        /// <summary>Takes the team's pending glyph request.</summary>
        internal bool TakeGlyphRequest(Team team)
        {
            bool requested = team == Team.Dawn ? _dawnGlyphRequested : _duskGlyphRequested;
            if (team == Team.Dawn)
                _dawnGlyphRequested = false;
            else
                _duskGlyphRequested = false;
            return requested;
        }

        /// <summary>Whether this is the team's first loss of a building of this type (and records it).</summary>
        internal bool FirstLossOf(Team team, string structureTypeId) => _glyphResetsUsed.Add((int)team + "/" + structureTypeId);

        internal MatchUnit SpawnStructure(EntityId id, StructurePlacement placement, StructureType type)
        {
            var unit = new MatchUnit(id, placement.Team, UnitKind.Structure, type.Id, type.Stats, placement.Position, Rate)
            {
                Placement = placement,
            };
            foreach (ModifierSpec spec in type.Modifiers)
                unit.AddModifier(new ActiveModifier(spec, id, 0, -1, 0));
            unit.Backdoor = type.Backdoor;
            unit.BackdoorActive = type.Backdoor != null;
            Add(unit);
            _structures.Add(placement.Id, unit);
            return unit;
        }

        internal MatchUnit SpawnCreep(EntityId id, Team team, CreepType type, LaneDefinition lane, long tick)
        {
            var unit = new MatchUnit(id, team, UnitKind.Creep, type.Id, type.Stats, FixedVector2.Zero, Rate)
            {
                Lane = lane,
                LastActiveTick = tick,
            };
            unit.Position = unit.WaypointAt(0);
            unit.StartPosition = unit.Position;
            unit.NextWaypoint = 1;
            foreach (ModifierSpec spec in type.Modifiers)
                unit.AddModifier(new ActiveModifier(spec, id, 0, -1, 0));
            Add(unit);
            return unit;
        }

        internal MatchUnit SpawnSummon(EntityId id, MatchUnit owner, CreepType type, long tick, long expiresTick)
        {
            var unit = new MatchUnit(id, owner.Team, UnitKind.Summon, type.Id, type.Stats, owner.Position, Rate)
            {
                Owner = owner.Id,
                ExpiresTick = expiresTick,
                LastActiveTick = tick,
            };
            Add(unit);
            return unit;
        }

        /// <summary>
        /// An illusion of <paramref name="original"/> for <paramref name="owner"/>: a summon with the hero's current
        /// health, armor, magic resistance, speed and attack (attack speed folded into the attack interval).
        /// </summary>
        internal MatchUnit SpawnIllusion(EntityId id, MatchUnit owner, MatchUnit original, long tick, long expiresTick)
        {
            UnitStats o = original.Stats;
            int speedPercent = 100 + original.AttackSpeedBonus;
            var stats = new UnitStats(original.MaxHp, original.Armor, original.MoveSpeed, original.AttackDamageMin, original.AttackDamageMax,
                original.AttackRange, o.AttackIntervalMs * 100 / speedPercent, o.AttackPointMs * 100 / speedPercent, o.AcquisitionRange,
                o.BoundingRadius, original.MagicResistPercent, o.VisionRange, 0, ModifierFlags.None, o.ProjectileSpeed, o.NightVisionRange, AttackClass.Hero);
            var unit = new MatchUnit(id, owner.Team, UnitKind.Summon, "illusion:" + original.TypeId, stats, original.Position, Rate)
            {
                Owner = owner.Id,
                IllusionOf = original.Id,
                ExpiresTick = expiresTick,
                LastActiveTick = tick,
            };
            unit.Hp = Fixed.Min(original.Hp, Fixed.FromInt(unit.MaxHp));
            Add(unit);
            return unit;
        }

        internal MatchUnit SpawnCampCreep(EntityId id, CampState camp, CreepType type)
        {
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.Creep, type.Id, type.Stats, camp.Definition.Position, Rate)
            {
                NeutralHome = camp.Definition.Position,
                LeashRadius = camp.Definition.LeashRadius,
                Camp = camp,
            };
            Add(unit);
            camp.Members.Add(unit);
            return unit;
        }

        internal MatchUnit SpawnMiniboss(EntityId id, MinibossDefinition miniboss, FixedVector2 position)
        {
            CreepType type = Ruleset.FindCreepType(miniboss.CreepTypeId)!;
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.Miniboss, type.Id, type.Stats, position, Rate) { NeutralHome = position };
            foreach (ModifierSpec spec in type.Modifiers)
                unit.AddModifier(new ActiveModifier(spec, id, 0, -1, 0));
            Add(unit);
            Miniboss = unit;
            NextMinibossSpawnTick = -1;
            return unit;
        }

        internal MatchUnit SpawnBoss(EntityId id, BossDefinition boss, FixedVector2 pit)
        {
            CreepType type = Ruleset.FindCreepType(boss.CreepTypeId)!;
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.Boss, type.Id, type.Stats, pit, Rate)
            {
                NeutralHome = pit,
                LeashRadius = boss.LeashRadius,
            };
            foreach (ModifierSpec spec in type.Modifiers)
                unit.AddModifier(new ActiveModifier(spec, id, 0, -1, 0));
            Add(unit);
            Boss = unit;
            NextBossSpawnTick = -1;
            return unit;
        }

        /// <summary>How many times the boss has died.</summary>
        public int BossDeaths { get; internal set; }

        /// <summary>The team that last killed the boss (Dota's Roar of Retribution answers it); neutral before.</summary>
        public Team BossLastKillerTeam { get; internal set; } = Team.Neutral;

        internal void Remove(MatchUnit unit)
        {
            if (unit == Boss)
                Boss = null;
            if (unit == Miniboss)
                Miniboss = null;
            if (unit == GroundAegis)
                GroundAegis = null;
            _units.Remove(unit);
            if (unit.Hero != null)
                _heroes.Remove(unit);
            _byId.Remove(unit.Id);
            if (unit.Placement != null)
            {
                _structures.Remove(unit.Placement.Id);
                _destroyed.Add(unit.Placement.Id);
                _destroyedOrder.Add(unit.Placement.Id);
            }
        }

        private void Add(MatchUnit unit)
        {
            unit.Combat = Ruleset.Combat;
            if (Ruleset.Combat.DotaStacking || Ruleset.Combat.MaxAttackSpeed > 0)
                unit.Recalculate();
            _units.Add(unit);
            if (unit.Hero != null)
                _heroes.Add(unit);
            _byId.Add(unit.Id, unit);
        }
    }

    /// <summary>
    /// Read-only view of a unit list. A struct with a struct enumerator, so the hot
    /// <c>foreach (MatchUnit u in state.Units)</c> loops allocate nothing.
    /// </summary>
    public readonly struct UnitView : IReadOnlyList<MatchUnit>
    {
        private readonly List<MatchUnit> _list;

        internal UnitView(List<MatchUnit> list) => _list = list;

        public int Count => _list.Count;

        public MatchUnit this[int index] => _list[index];

        public List<MatchUnit>.Enumerator GetEnumerator() => _list.GetEnumerator();

        IEnumerator<MatchUnit> IEnumerable<MatchUnit>.GetEnumerator() => _list.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }
}
