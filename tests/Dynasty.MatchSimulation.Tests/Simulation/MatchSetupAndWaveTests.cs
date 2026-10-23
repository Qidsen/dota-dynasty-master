using Dynasty.Core.Mathematics;
using Dynasty.Core.Time;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

public class MatchSetupAndWaveTests
{
    [Fact]
    public void Create_PlacesEveryStructureWithFullHpBeforeTheFirstTick()
    {
        var kernel = new RulesetDraft().Start();

        var structures = kernel.State.Units.Where(u => u.Kind == UnitKind.Structure).ToList();

        Assert.Equal(4, structures.Count);
        MatchUnit tower = kernel.State.FindStructure("dusk.mid.t1")!;
        Assert.Equal(P(7000, 5000), tower.Position);
        Assert.Equal(Fixed.FromInt(1800), tower.Hp);
        Assert.Equal(Team.Dusk, tower.Team);
        Assert.Equal(SimTick.Zero, kernel.CurrentTick);
    }

    [Fact]
    public void Create_PinsTheRulesetAndItsTickRate()
    {
        RulesetContent ruleset = new RulesetDraft { TicksPerSecond = 20 }.Build();

        var kernel = Match.Create(ruleset, 1);

        Assert.Equal(ruleset.ToDescriptor(), kernel.Config.Ruleset);
        Assert.Equal(20, kernel.Config.TickRate.TicksPerSecond);
    }

    [Fact]
    public void Create_RejectsInvalidRuleset()
    {
        var draft = new RulesetDraft();
        draft.DawnWave.Add(new WaveEntry("dragon", 1));

        Assert.Throws<InvalidOperationException>(() => draft.Start());
    }

    [Fact]
    public void FirstWave_SpawnsAtLaneEndsForBothTeams()
    {
        var kernel = new RulesetDraft().Start();

        kernel.Step();

        MatchUnit dawn = Assert.Single(kernel.State.Creeps(Team.Dawn));
        MatchUnit dusk = Assert.Single(kernel.State.Creeps(Team.Dusk));
        Assert.Equal("mid", dawn.LaneId);
        Assert.Equal(2, kernel.EventsOf<CreepSpawned>().Count());
        Assert.All(kernel.EventsOf<CreepSpawned>(), e => Assert.Equal("melee", e.CreepTypeId));
        // Spawned at the lane ends, then moved one tick (10 units at 300 u/s, 30 tps).
        Assert.Equal(P(1010, 5000), dawn.Position);
        Assert.Equal(P(8990, 5000), dusk.Position);
    }

    [Fact]
    public void Waves_RepeatAtTheConfiguredInterval()
    {
        var draft = new RulesetDraft { FirstSpawnMs = 500, IntervalMs = 1000 };
        var kernel = draft.Start();

        kernel.RunTicks(76);

        Assert.Equal(new long[] { 15, 15, 45, 45, 75, 75 }, kernel.TimedEventsOf<CreepSpawned>().Select(e => e.Tick));
    }

    [Fact]
    public void WaveComposition_SpawnsEveryEntryOnEveryLane()
    {
        var draft = new RulesetDraft();
        draft.Lanes.Add(new LaneDefinition("side", new[] { P(1000, 1000), P(9000, 1000) }));
        draft.DawnWave.Add(new WaveEntry("melee", 2));

        var kernel = draft.Start();
        kernel.Step();

        Assert.Equal(6, kernel.State.Creeps(Team.Dawn).Count());
        Assert.Equal(2, kernel.State.Creeps(Team.Dusk).Count());
    }
}
