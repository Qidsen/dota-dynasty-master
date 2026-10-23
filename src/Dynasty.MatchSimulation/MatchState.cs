using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Time;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation
{
    public enum UnitKind
    {
        Creep = 0,
        Structure = 1,
    }

    /// <summary>
    /// A creep or structure in the authoritative match state. Presentation and analytics read
    /// it; only match systems write it.
    /// </summary>
    public sealed class MatchUnit
    {
        internal MatchUnit(EntityId id, Team team, UnitKind kind, string typeId, UnitStats stats, FixedVector2 position, TickRate rate)
        {
            Id = id;
            Team = team;
            Kind = kind;
            TypeId = typeId;
            Stats = stats;
            Position = position;
            Hp = Fixed.FromInt(stats.MaxHp);
            StepPerTick = Fixed.FromRatio(stats.MoveSpeed, rate.TicksPerSecond);
            AttackIntervalTicks = rate.TicksFor(stats.AttackIntervalMs);
            AttackPointTicks = rate.TicksFor(stats.AttackPointMs);
            PendingAttackTick = -1;
        }

        public EntityId Id { get; }
        public Team Team { get; }
        public UnitKind Kind { get; }
        public string TypeId { get; }
        public UnitStats Stats { get; }
        public FixedVector2 Position { get; internal set; }
        public Fixed Hp { get; internal set; }
        public bool IsAlive => Hp > Fixed.Zero;

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
        internal Fixed StepPerTick { get; }
        internal long AttackIntervalTicks { get; }
        internal long AttackPointTicks { get; }
        internal long NextAttackTick { get; set; }
        internal long PendingAttackTick { get; set; }
        internal EntityId PendingTargetId { get; set; }
        internal EntityId LastAttackerId { get; set; }

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
        }

        public RulesetContent Ruleset { get; }

        /// <summary>Living units in creation order (which is also ascending ID order).</summary>
        public IReadOnlyList<MatchUnit> Units => _units;

        public Team? Winner { get; internal set; }

        /// <summary>Placement IDs in the order they were destroyed.</summary>
        public IReadOnlyCollection<string> DestroyedStructures => _destroyedOrder;

        internal TickRate Rate { get; }
        internal long NextWaveTick { get; set; }

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

        /// <summary>Whether <paramref name="attacker"/> may attack <paramref name="target"/> right now.</summary>
        internal bool CanAttack(MatchUnit attacker, MatchUnit target) =>
            target.Team != attacker.Team &&
            target.IsAlive &&
            IsVulnerable(target) &&
            !(attacker.Kind == UnitKind.Structure && target.Kind == UnitKind.Structure);

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
            unit.NextWaypoint = 1;
            Add(unit);
            return unit;
        }

        internal void Remove(MatchUnit unit)
        {
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
