using System.Collections.Generic;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Navigation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Vision
{
    /// <summary>Where and when a team last saw an enemy hero.</summary>
    public sealed class LastSeen
    {
        internal LastSeen(FixedVector2 position, long tick)
        {
            Position = position;
            Tick = tick;
        }

        public FixedVector2 Position { get; }
        public long Tick { get; }
    }

    /// <summary>
    /// What one team knows (spec §9, layer 2): which enemy units it sees right now and where it
    /// last saw each enemy hero. AI and team-view presentation must read only this, never world truth.
    /// </summary>
    public sealed class TeamKnowledge
    {
        private readonly HashSet<long> _visible = new HashSet<long>();
        private readonly Dictionary<long, LastSeen> _lastSeen = new Dictionary<long, LastSeen>();

        internal TeamKnowledge(Team team)
        {
            Team = team;
        }

        public Team Team { get; }

        /// <summary>Own units and structures (static map knowledge) are always known; others only while seen.</summary>
        public bool Sees(MatchUnit unit) => unit.Team == Team || unit.Kind == UnitKind.Structure || _visible.Contains(unit.Id.Value);

        public LastSeen? LastSeenOf(EntityId unit) => _lastSeen.TryGetValue(unit.Value, out LastSeen? seen) ? seen : null;

        internal void Reset() => _visible.Clear();

        internal void MarkVisible(MatchUnit unit, long tick)
        {
            _visible.Add(unit.Id.Value);
            if (unit.Hero != null)
                _lastSeen[unit.Id.Value] = new LastSeen(unit.Position, tick);
        }
    }

    /// <summary>
    /// Recomputes team vision every <see cref="IntervalTicks"/> ticks. An enemy is visible when some
    /// living allied unit has it within vision range with an unobstructed line (obstacles block
    /// sight); invisible units additionally need to be inside an allied true-sight radius.
    /// </summary>
    internal sealed class VisionSystem : ISimulationSystem<MatchState>
    {
        public const int IntervalTicks = 3;

        private readonly List<Observer> _observers = new List<Observer>();

        public string Name => "vision";

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            if (now % IntervalTicks != 0)
                return;

            MatchState state = context.State;
            foreach (Team team in new[] { Team.Dawn, Team.Dusk })
                Update(state, state.KnowledgeOf(team), now);
        }

        private void Update(MatchState state, TeamKnowledge knowledge, long now)
        {
            _observers.Clear();
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Team == knowledge.Team && unit.IsAlive && unit.Stats.VisionRange > 0)
                    _observers.Add(new Observer(unit.Position, Square(unit.Stats.VisionRange), Square(unit.Stats.TrueSightRange), state.ElevationAt(unit.Position)));
            }

            knowledge.Reset();
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Team == knowledge.Team || !unit.IsAlive)
                    continue;
                if (IsSeen(state.SightGrid, unit, state.ElevationAt(unit.Position)))
                    knowledge.MarkVisible(unit, now);
            }
        }

        private bool IsSeen(NavGrid sight, MatchUnit target, int targetLevel)
        {
            bool invisible = (target.Flags & ModifierFlags.Invisible) != 0;
            if (invisible)
            {
                bool detected = false;
                foreach (Observer observer in _observers)
                {
                    if (FixedVector2.DistanceSquared(observer.Position, target.Position) <= observer.TrueSightSquared)
                    {
                        detected = true;
                        break;
                    }
                }

                if (!detected)
                    return false;
            }

            // Low ground cannot see up onto high ground.
            foreach (Observer observer in _observers)
            {
                if (observer.Level >= targetLevel &&
                    FixedVector2.DistanceSquared(observer.Position, target.Position) <= observer.VisionSquared &&
                    sight.HasClearLine(observer.Position, target.Position))
                    return true;
            }

            return false;
        }

        private static Fixed Square(int value)
        {
            Fixed v = Fixed.FromInt(value);
            return v * v;
        }

        private readonly struct Observer
        {
            public Observer(FixedVector2 position, Fixed visionSquared, Fixed trueSightSquared, int level)
            {
                Position = position;
                VisionSquared = visionSquared;
                TrueSightSquared = trueSightSquared;
                Level = level;
            }

            public FixedVector2 Position { get; }
            public int Level { get; }
            public Fixed VisionSquared { get; }
            public Fixed TrueSightSquared { get; }
        }
    }
}
