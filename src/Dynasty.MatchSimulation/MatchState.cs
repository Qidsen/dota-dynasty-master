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
        public int MagicResistPercent { get; private set; }
        public int AttackDamageMin { get; private set; }
        public int AttackDamageMax { get; private set; }
        public int AttackSpeedBonus { get; private set; }
        public int MoveSpeed { get; private set; }
        public ModifierFlags Flags { get; private set; }
        public int AttackRange => Stats.AttackRange;
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

        /// <summary>Tick a temporary unit disappears; -1 for permanent units.</summary>
        public long ExpiresTick { get; internal set; } = -1;

        /// <summary>Extra HP regeneration per second from modifiers and items.</summary>
        public Fixed HpRegenBonus { get; private set; }

        public int CritChancePercent { get; private set; }
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

        internal LaneDefinition? Lane { get; set; }
        internal int NextWaypoint { get; set; }
        internal Fixed StepPerTick { get; private set; }
        internal long AttackIntervalTicks { get; private set; }
        internal long AttackPointTicks { get; private set; }
        internal long NextAttackTick { get; set; }
        internal long PendingAttackTick { get; set; }
        internal EntityId PendingTargetId { get; set; }
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
        internal void Recalculate()
        {
            Fixed armor = Stats.Armor;
            int magicResist = Stats.MagicResistPercent;
            int damageBonus = 0;
            int attackSpeed = 0;
            int movePercent = 0;
            int critChance = 0;
            int critMultiplier = 0;
            int structurePercent = 0;
            int lifesteal = 0;
            int maxHpBonus = 0;
            int maxManaBonus = 0;
            Fixed hpRegenBonus = Fixed.Zero;
            ModifierFlags flags = Stats.InnateFlags;
            foreach (ActiveModifier modifier in _modifiers)
            {
                ModifierSpec spec = modifier.Spec;
                // Critical strikes do not stack: the highest chance (with its multiplier) wins.
                if (spec.CritChancePercent > critChance)
                {
                    critChance = spec.CritChancePercent;
                    critMultiplier = spec.CritMultiplierPercent;
                }

                structurePercent += spec.StructureDamagePercent;
                lifesteal += spec.LifestealPercent;
                maxHpBonus += spec.MaxHpBonus;
                maxManaBonus += spec.MaxManaBonus;
                hpRegenBonus += spec.HpRegenBonus;
                armor += spec.ArmorBonus;
                magicResist += spec.MagicResistBonus;
                damageBonus += spec.DamageBonus;
                attackSpeed += spec.AttackSpeedBonus;
                movePercent += spec.MoveSpeedPercent;
                flags |= spec.Flags;
            }

            MaxHp = Stats.MaxHp + maxHpBonus;
            HpRegenBonus = hpRegenBonus;
            if (Hp > Fixed.FromInt(MaxHp))
                Hp = Fixed.FromInt(MaxHp);
            if (Hero != null)
            {
                Hero.MaxMana = Hero.Definition.MaxManaAtLevel(Hero.Level) + maxManaBonus;
                if (Hero.Mana > Fixed.FromInt(Hero.MaxMana))
                    Hero.Mana = Fixed.FromInt(Hero.MaxMana);
            }

            Armor = armor;
            MagicResistPercent = Math.Min(magicResist, 100);
            AttackDamageMin = Stats.AttackDamageMin + damageBonus;
            AttackDamageMax = Stats.AttackDamageMax + damageBonus;
            AttackSpeedBonus = Math.Max(attackSpeed, -80);
            Flags = flags;
            CritChancePercent = Math.Min(critChance, 100);
            CritMultiplierPercent = critMultiplier;
            StructureDamagePercent = structurePercent;
            LifestealPercent = lifesteal;

            int speed = Stats.MoveSpeed * (100 + movePercent) / 100;
            MoveSpeed = Stats.MoveSpeed == 0 ? 0 : Math.Max(speed, Math.Min(100, Stats.MoveSpeed));
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

    /// <summary>Authoritative state of one match (world truth).</summary>
    public sealed class MatchState
    {
        private readonly List<MatchUnit> _units = new List<MatchUnit>();
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
            if (ruleset.Boss != null)
                NextBossSpawnTick = rate.TicksFor(ruleset.Boss.FirstSpawnMs);
        }

        internal Navigation.NavGrid Nav { get; }
        internal Navigation.Pathfinder Pathfinder { get; }

        /// <summary>All heroes (alive or waiting to respawn) in spawn order.</summary>
        public IEnumerable<MatchUnit> Heroes => _units.Where(u => u.Hero != null);

        public RulesetContent Ruleset { get; }

        /// <summary>Living units in creation order (which is also ascending ID order).</summary>
        public IReadOnlyList<MatchUnit> Units => _units;

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

            return placement.Vulnerability == VulnerabilityRule.AnyProtectorDestroyed
                ? placement.ProtectedBy.Any(_destroyed.Contains)
                : placement.ProtectedBy.All(_destroyed.Contains);
        }

        /// <summary>
        /// Whether <paramref name="attacker"/> may attack <paramref name="target"/> right now. Heroes
        /// may also attack allied creeps below half HP (deny).
        /// </summary>
        internal bool CanAttack(MatchUnit attacker, MatchUnit target)
        {
            if (!target.IsAlive || target == attacker)
                return false;
            if (target.Team == attacker.Team)
                return attacker.Kind == UnitKind.Hero && target.Kind == UnitKind.Creep && target.Hp * Fixed.FromInt(2) < Fixed.FromInt(target.MaxHp);

            return IsVulnerable(target) && !(attacker.Kind == UnitKind.Structure && target.Kind == UnitKind.Structure);
        }

        internal MatchUnit SpawnHero(EntityId id, Team team, HeroDefinition definition, string laneId, FixedVector2 position, int startingGold)
        {
            var abilities = definition.AbilityIds.Select(a => Ruleset.FindAbility(a)!).ToList();
            var unit = new MatchUnit(id, team, UnitKind.Hero, definition.Id, definition.StatsAtLevel(1), position, Rate)
            {
                Hero = new Heroes.HeroState(definition, laneId, startingGold, abilities),
            };
            Add(unit);
            return unit;
        }

        internal MatchUnit SpawnStructure(EntityId id, StructurePlacement placement, StructureType type)
        {
            var unit = new MatchUnit(id, placement.Team, UnitKind.Structure, type.Id, type.Stats, placement.Position, Rate)
            {
                Placement = placement,
            };
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

        internal MatchUnit SpawnBoss(EntityId id, BossDefinition boss)
        {
            CreepType type = Ruleset.FindCreepType(boss.CreepTypeId)!;
            var unit = new MatchUnit(id, Team.Neutral, UnitKind.Boss, type.Id, type.Stats, boss.Position, Rate);
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
            _units.Add(unit);
            _byId.Add(unit.Id, unit);
        }
    }
}
