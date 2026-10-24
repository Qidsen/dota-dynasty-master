using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Vision;

public class DayNightTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>Day 2 s, night 2 s; heroes see 1200 by day and 800 at night.</summary>
    private static RulesetDraft Draft()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.DayNight = new DayNightRules(dayMs: 2000, nightMs: 2000);
        for (int i = 0; i < draft.Heroes.Count; i++)
        {
            var stats = new UnitStats(600, Fixed.FromInt(2), 300, 50, 54, 150, 1500, 300, 800, 24, 25, visionRange: 1200, nightVisionRange: 800);
            draft.Heroes[i] = Hero(draft.Heroes[i].Id, stats);
        }

        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft, int distance)
    {
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 2000);
            H(ctx.State, "beta").Position = P(5000 + distance, 2000);
        }));
        return kernel;
    }

    [Fact]
    public void Cycle_AlternatesDayAndNight()
    {
        var kernel = Start(Draft(), 1000);

        Assert.False(kernel.State.IsNight(0));
        Assert.False(kernel.State.IsNight(59));
        Assert.True(kernel.State.IsNight(60));
        Assert.True(kernel.State.IsNight(119));
        Assert.False(kernel.State.IsNight(120));
    }

    [Fact]
    public void AtNight_HeroesSeeLess()
    {
        var kernel = Start(Draft(), 1000);

        kernel.RunTicks(10);
        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));    // day: 1000 < 1200

        kernel.RunTicks(60);
        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));   // night: 1000 > 800
    }

    [Fact]
    public void UnitsWithoutANightRange_KeepTheirVision()
    {
        var kernel = Start(Draft(), 1000);
        kernel.AddSystem(new LambdaSystem(0, ctx => H(ctx.State, "beta").Position = P(3800, 5000)));   // 800 from the Dawn tower (vision 900, no night range)

        kernel.RunTicks(75);

        Assert.True(kernel.State.IsNight(kernel.CurrentTick.Value - 1));
        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
    }

    [Fact]
    public void Transitions_AreReportedAsEvents()
    {
        var kernel = Start(Draft(), 1000);

        kernel.RunTicks(130);

        Assert.Equal(new[] { (60L, true), (120L, false) },
            kernel.TimedEventsOf<DayNightChanged>().Select(e => (e.Tick, e.Event.IsNight)));
    }

    [Fact]
    public void WithoutDayNightRules_ItIsAlwaysDay()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        Assert.False(kernel.State.IsNight(100000));
    }

    [Fact]
    public void Validation_RejectsNonPositivePhases()
    {
        var draft = Draft();
        draft.DayNight = new DayNightRules(dayMs: 0, nightMs: 1000);

        Assert.Contains(draft.Build().Validate(), e => e.Contains("Day", StringComparison.OrdinalIgnoreCase));
    }
}
