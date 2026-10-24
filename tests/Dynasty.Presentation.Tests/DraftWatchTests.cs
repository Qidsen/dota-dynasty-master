using Dynasty.Drafting;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class DraftWatchTests
{
    [Fact]
    public void DemoRosters_AreTwoDistinctFullTeams()
    {
        var (dawn, dusk) = DemoMatch.Rosters();

        Assert.NotEqual(dawn.Name, dusk.Name);
        Assert.All(new[] { dawn, dusk }, r => Assert.All(r.Players, p => Assert.True(p.Pool.Count() >= 3, p.Name)));
        Assert.Equal(DemoMatch.Rosters().Dawn.At(1).Pool.Select(e => (e.HeroId, e.Mastery)), dawn.At(1).Pool.Select(e => (e.HeroId, e.Mastery)));   // deterministic
    }

    [Fact]
    public void Watch_StepsThroughTheDraftAtTheChosenPace()
    {
        var watch = DraftWatch.Start(BaseRuleset.Create(), seed: 1, secondsPerStep: 2.0);

        watch.Update(1.0);
        Assert.Equal(0, watch.Session.StepIndex);
        watch.Update(1.1);
        Assert.Equal(1, watch.Session.StepIndex);
        Assert.NotNull(watch.LastChoice);
        Assert.Equal(watch.Session.BansOf(watch.Session.FirstPick)[0], watch.LastChoice!.HeroId);
    }

    [Fact]
    public void FinishedWatch_ExplainsBothPlansAndStartsTheMatchWithThem()
    {
        var watch = DraftWatch.Start(BaseRuleset.Create(), seed: 1, secondsPerStep: 0.5);
        watch.Finish();

        Assert.True(watch.Session.IsComplete);
        Assert.NotNull(watch.DawnPlan);
        Assert.NotNull(watch.DuskPlan);
        MatchSession match = watch.StartMatch(seed: 5);
        var dawnHeroes = match.Kernel.State.Heroes.Where(h => h.Team == Team.Dawn).Select(h => h.Hero!.Definition.Id).ToHashSet();
        Assert.Equal(watch.Session.PicksOf(Team.Dawn).ToHashSet(), dawnHeroes);
        Assert.Equal(TeamStrategy.From(watch.DawnPlan!).Archetype, match.AiSettings!.DawnStrategy.Archetype);
    }

    [Fact]
    public void Watch_IsDeterministicPerSeed()
    {
        string Picks(ulong seed)
        {
            var watch = DraftWatch.Start(BaseRuleset.Create(), seed, 1.0);
            watch.Finish();
            return string.Join(",", watch.Session.PicksOf(Team.Dawn).Concat(watch.Session.PicksOf(Team.Dusk)));
        }

        Assert.Equal(Picks(3), Picks(3));
    }
}
