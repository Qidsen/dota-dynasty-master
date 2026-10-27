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
        public Team Team { get; }
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
        public bool CanMove => (StartFlags & (ModifierFlags.Stunned | ModifierFlags.Rooted)) == 0 && StepPerTick > Fixed.Zero;
        public bool CanStartAttack => (StartFlags & (ModifierFlags.Stunned | ModifierFlags.Disarmed)) == 0 && CanAttackAtAll;

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
                if (byArmor > 0 || (byArmor == 0 && (byRegen > 0 || (byRegen == 0 && before))))
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
                movePercent += spec.MoveSpeedHundredths * n;
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
            AttackDamageMin = Stats.AttackDamageMin + damageBonus;
            AttackDamageMax = Stats.AttackDamageMax + damageBonus;
            AttackSpeedBonus = Math.Max(attackSpeed, -80);
            if (Combat != null && Combat.MaxAttackSpeed > 0)
                AttackSpeedBonus = Math.Min(AttackSpeedBonus, Combat.MaxAttackSpeed - 100);
            Flags = flags;
            CritChanceHundredths = Math.Min(critChance, 10000);
            CritMultiplierPercent = critMultiplier;
            StructureDamagePercent = structurePercent;
            LifestealPercent = lifesteal;

            // Dota: flat bonuses first, then percentages; at least 100 (or the base, if lower), at most 550.
            int speed = Math.Min((Stats.MoveSpeed + moveFlat) * (10000 + movePercent) / 10000, MaxMoveSpeed);   // movePercent in hundredths
            MoveSpeed = Stats.MoveSpeed == 0 ? 0 : Math.Max(speed, Math.Min(100, Stats.MoveSpeed));
            AttackRange = Stats.AttackRange + attackRangeBonus;
            IncomingDamageHundredths = Math.Max(incomingDamage, -10000);
            AttackDamagePercent = Math.Max(attackDamagePercent, -100);
            EvasionHundredths = dota ? (int)(10000 - evasionLeft) : Math.Min(evasion, 10000);
            StepPerTick = Fixed.FromRatio(MoveSpeed, _rate.TicksPerSecond);
            AttackIntervalTicks = Math.Max(1, _rate.TicksFor(Stats.AttackIntervalMs * 100 / (100 + AttackSpeedBonus)));
            AttackPointTicks = _rate.TicksFor(Stats.AttackPointMs * 100 / (100 + AttackSpeedBonus));
        }

        /// <summary>Waypoint by walking order: Dawn walks the lane forward, Dusk in reverse.</summary>
        internal FixedVector2 WaypointAt(int index)
        {
            IReadOnlyList<FixedVector2> points = Lane!.Waypoints;
            return Team == Team.Dawn ? points[index] : points[points.Count - 1 - index];
        }
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
            SightGrid = Navigation.NavGrid.Build(ruleset.Map.Width, ruleset.Map.Height, ruleset.Map.NavCellSize, ruleset.Map.Obstacles, clearance: 0);
            _elevation = BuildElevation(Nav, ruleset.Map.Elevation);
            if (ruleset.Boss != null)
                NextBossSpawnTick = rate.TicksFor(ruleset.Boss.FirstSpawnMs);
            foreach (NeutralCampDefinition camp in ruleset.Map.Camps)
                Camps.Add(new CampState(camp, rate.TicksFor(camp.FirstSpawnMs)));
            if (ruleset.Runes != null)
                NextRuneTick = rate.TicksFor(ruleset.Runes.FirstSpawnMs);
        }

        /// <summary>Tick of the next rune spawn; -1 without runes.</summary>
        internal long NextRuneTick { get; set; } = -1;

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

        /// <summary>
        /// Whether the team currently sees the unit (own units: always). Recomputed every few ticks.
        /// Neutral units act on proximity alone and are treated as seeing everything they react to.
        /// </summary>
        public bool IsVisibleTo(Team team, MatchUnit unit) => team == Team.Neutral || KnowledgeOf(team).Sees(unit);

        /// <summary>The living boss, if the ruleset has one and it is up.</summary>
        public MatchUnit? Boss { get; internal set; }

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

        /// <summary>Ground level at a point (0 = low ground): the highest zone covering its grid cell center.</summary>
        public int ElevationAt(FixedVector2 point) => _elevation.Length == 0 ? 0 : _elevation[Nav.IndexOf(point)];

        private readonly int[] _elevation;

        private static int[] BuildElevation(Navigation.NavGrid grid, IReadOnlyList<ElevationZone> zones)
        {
            if (zones.Count == 0)
                return Array.Empty<int>();

            var levels = new int[grid.Columns * grid.Rows];
            for (int i = 0; i < levels.Length; i++)
            {
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
            StructurePlacement? placement = unit.Placement;
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
            if (!target.IsAlive || target == attacker || target.Kind == UnitKind.Rune)
                return false;
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

        internal MatchUnit SpawnStructure(EntityId id, StructurePlacement placement, StructureType type)
        {
            var unit = new MatchUnit(id, placement.Team, UnitKind.Structure, type.Id, type.Stats, placement.Position, Rate)
            {
                Placement = placement,
            };
            foreach (ModifierSpec spec in type.Modifiers)
                unit.AddModifier(new ActiveModifier(spec, id, 0, -1, 0));
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

        internal MatchUnit SpawnBoss(EntityId id, BossDefinition boss)
        {
            CreepType type = Ruleset.FindCreepType(boss.CreepTypeId)!;
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.Boss, type.Id, type.Stats, boss.Position, Rate)
            {
                NeutralHome = boss.Position,
                LeashRadius = boss.LeashRadius,
            };
            Add(unit);
            Boss = unit;
            NextBossSpawnTick = -1;
            return unit;
        }

        internal void Remove(MatchUnit unit)
        {
            if (unit == Boss)
                Boss = null;
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
