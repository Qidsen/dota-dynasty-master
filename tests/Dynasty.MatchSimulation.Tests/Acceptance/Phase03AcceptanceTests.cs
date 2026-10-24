using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Acceptance;

/// <summary>
/// Phase 03 acceptance: on the built-in map the AI plays coherently as teams (pushes that end
/// matches, team fights, rotations, vision) without seeing through fog (see VisionTests audit).
/// </summary>
[Trait("Category", "Acceptance")]
public class Phase03AcceptanceTests
{
    private const int TicksPerMinute = 30 * 60;

    private static readonly MatchLineup Lineup = new(
        new[]
        {
            new HeroPick("ironvow", "bot"), new HeroPick("dawnmender", "bot"), new HeroPick("emberwisp", "mid"),
            new HeroPick("stonewarden", "top"), new HeroPick("thornkeeper", "top"),
        },
        new[]
        {
            new HeroPick("starborn", "top"), new HeroPick("shadeblade", "top"), new HeroPick("frostweaver", "mid"),
            new HeroPick("hivecaller", "bot"), new HeroPick("siegewright", "bot"),
        });

    private sealed class PlayedMatch
    {
        public PlayedMatch(SimulationKernel<MatchState> kernel, BehaviourAudit audit)
        {
            Kernel = kernel;
            Audit = audit;
        }

        public SimulationKernel<MatchState> Kernel { get; }
        public BehaviourAudit Audit { get; }

        public IEnumerable<T> Events<T>() => Kernel.Events.Records.Select(r => r.Event).OfType<T>();
    }

    private static readonly Lazy<List<PlayedMatch>> Matches = new(() => new ulong[] { 1, 2, 3, 4 }.Select(Play).ToList());

    private static PlayedMatch Play(ulong seed)
    {
        var kernel = Match.Create(BaseRuleset.Create(), seed, Lineup);
        HeroAi.Attach(kernel);
        var audit = new BehaviourAudit();
        kernel.AddSystem(audit);
        kernel.RunUntilFinished(60 * TicksPerMinute);
        return new PlayedMatch(kernel, audit);
    }

    [Fact]
    public void MostMatches_EndByDestroyingAnAncientWithinAnHour()
    {
        int finished = Matches.Value.Count(m => m.Kernel.IsFinished);

        Assert.True(finished >= 3, $"only {finished}/4 matches ended within 60 minutes");
        Assert.All(Matches.Value.Where(m => m.Kernel.IsFinished), m => Assert.Single(m.Events<MatchFinished>()));
    }

    [Fact]
    public void TeamFights_InvolveSeveralHeroesPerKill()
    {
        foreach (PlayedMatch match in Matches.Value)
            Assert.Contains(match.Events<HeroKilled>(), k => k.Assists.Count >= 2);
    }

    [Fact]
    public void Rotations_BringHeroesFromDifferentLanesTogether()
    {
        foreach (PlayedMatch match in Matches.Value)
        {
            Dictionary<EntityId, string> lane = match.Kernel.State.Heroes.ToDictionary(h => h.Id, h => h.Hero!.AssignedLane);
            bool rotation = match.Events<HeroKilled>().Any(k =>
                k.Assists.Append(k.Killer).Where(lane.ContainsKey).Select(id => lane[id]).Distinct().Count() >= 2);
            Assert.True(rotation, "no kill combined heroes from different lanes");
        }
    }

    [Fact]
    public void Pushes_AreMadeByGroupsOfHeroes()
    {
        // At least a quarter of all structure kills happen with three or more attacking heroes nearby,
        // and every match has some (short matches end with few structures down).
        string counts = string.Join(", ", Matches.Value.Select(m => $"{m.Audit.GroupedStructureKills}/{m.Kernel.State.DestroyedStructures.Count}"));
        int grouped = Matches.Value.Sum(m => m.Audit.GroupedStructureKills);
        int total = Matches.Value.Sum(m => m.Kernel.State.DestroyedStructures.Count);
        Assert.True(grouped * 4 >= total, $"grouped/total structure kills per match: {counts}");
        Assert.All(Matches.Value, m => Assert.True(m.Audit.GroupedStructureKills >= 2, $"grouped/total structure kills per match: {counts}"));
    }

    [Fact]
    public void Supports_KeepTheMapWarded()
    {
        foreach (PlayedMatch match in Matches.Value)
        {
            var wardOwners = match.Events<UnitSummoned>().Where(s => s.CreepTypeId == "observer_ward").Select(s => s.Owner).ToHashSet();
            Assert.True(match.Events<UnitSummoned>().Count(s => s.CreepTypeId == "observer_ward") >= 5);
            Assert.All(wardOwners, owner =>
            {
                var roles = match.Kernel.State.Find(owner)!.Hero!.Definition.Roles;
                Assert.True(roles.Contains("support") || roles.Contains("healer"));
            });
        }
    }

    [Fact]
    public void Heroes_RarelyStandAround()
    {
        foreach (PlayedMatch match in Matches.Value)
            Assert.True(match.Audit.IdleShare < 0.15, $"heroes idle {match.Audit.IdleShare:P0} of the time");
    }

    /// <summary>Observes the match each tick: grouped structure kills and idle heroes away from the fountain.</summary>
    private sealed class BehaviourAudit : ISimulationSystem<MatchState>
    {
        private readonly Dictionary<long, FixedVector2> _lastPosition = new();
        private int _seenDestroyed;
        private long _aliveTicks;
        private long _idleTicks;

        public string Name => "test-behaviour-audit";
        public int GroupedStructureKills { get; private set; }
        public double IdleShare => _aliveTicks == 0 ? 0 : _idleTicks / (double)_aliveTicks;

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            var destroyed = state.DestroyedStructures.ToList();
            for (; _seenDestroyed < destroyed.Count; _seenDestroyed++)
            {
                StructurePlacement placement = state.Ruleset.Map.Structures.Single(s => s.Id == destroyed[_seenDestroyed]);
                Fixed reach = Fixed.FromInt(1500);
                int near = state.Heroes.Count(h => h.Team != placement.Team && h.IsAlive &&
                                                   FixedVector2.DistanceSquared(h.Position, placement.Position) <= reach * reach);
                if (near >= 3)
                    GroupedStructureKills++;
            }

            foreach (MatchUnit hero in state.Heroes)
            {
                if (!hero.IsAlive)
                    continue;
                FountainPlacement fountain = state.Ruleset.Map.FountainOf(hero.Team)!;
                Fixed r = Fixed.FromInt(fountain.Radius);
                bool atFountain = FixedVector2.DistanceSquared(hero.Position, fountain.Position) <= r * r;
                bool still = _lastPosition.TryGetValue(hero.Id.Value, out FixedVector2 last) && last == hero.Position;
                _lastPosition[hero.Id.Value] = hero.Position;
                if (atFountain)
                    continue;
                _aliveTicks++;
                if (still && hero.Hero!.Order.Kind == HeroOrderKind.Idle && hero.PendingAttackTick < 0)
                    _idleTicks++;
            }
        }
    }
}
