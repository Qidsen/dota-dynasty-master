using Dynasty.Drafting;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting.Tests;

public class DraftSessionTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();

    /// <summary>Plays the whole draft taking the first available hero each step.</summary>
    private static DraftSession Complete(Team firstPick = Team.Dawn)
    {
        var session = DraftSession.Start(Ruleset, firstPick);
        while (!session.IsComplete)
            Assert.Equal(DraftResult.Accepted, session.Submit(session.TeamToAct!.Value, session.AvailableHeroes[0]));
        return session;
    }

    [Fact]
    public void Draft_FollowsTheRulesetOrder_TheFirstPickSideStarting()
    {
        var session = DraftSession.Start(Ruleset, Team.Dusk);

        Assert.Equal(Team.Dusk, session.TeamToAct);
        Assert.Equal(DraftAction.Ban, session.ActionToTake);
        session.Submit(Team.Dusk, "ironvow");
        Assert.Equal(Team.Dawn, session.TeamToAct);
    }

    [Fact]
    public void Submit_RejectsActingOutOfTurn()
    {
        var session = DraftSession.Start(Ruleset, Team.Dawn);

        Assert.Equal(DraftResult.NotYourTurn, session.Submit(Team.Dusk, "ironvow"));
        Assert.Equal(0, session.StepIndex);
    }

    [Fact]
    public void Submit_RejectsATakenOrUnknownOrDisabledHero()
    {
        var rules = new DraftRules(Ruleset.Draft!.Steps, disabledHeroes: new[] { "starborn" });
        var session = DraftSession.Start(BaseRuleset.Create(rules), Team.Dawn);
        session.Submit(Team.Dawn, "ironvow");

        Assert.Equal(DraftResult.Unavailable, session.Submit(Team.Dusk, "ironvow"));
        Assert.Equal(DraftResult.Unavailable, session.Submit(Team.Dusk, "starborn"));
        Assert.Equal(DraftResult.UnknownHero, session.Submit(Team.Dusk, "nobody"));
        Assert.DoesNotContain("starborn", session.AvailableHeroes);
    }

    [Fact]
    public void CompleteDraft_GivesEachTeamFiveDistinctPicksAndFourBans()
    {
        DraftSession session = Complete();

        Assert.True(session.IsComplete);
        Assert.Null(session.TeamToAct);
        Assert.Equal(DraftResult.Complete, session.Submit(Team.Dawn, session.AvailableHeroes[0]));
        foreach (Team team in new[] { Team.Dawn, Team.Dusk })
        {
            Assert.Equal(5, session.PicksOf(team).Count);
            Assert.Equal(4, session.BansOf(team).Count);
        }

        var all = session.PicksOf(Team.Dawn).Concat(session.PicksOf(Team.Dusk)).Concat(session.BansOf(Team.Dawn)).Concat(session.BansOf(Team.Dusk)).ToList();
        Assert.Equal(18, all.Distinct().Count());
        Assert.Equal(Ruleset.Heroes.Count - 18, session.AvailableHeroes.Count);
    }

    [Fact]
    public void Draft_IsRecordedAsEvents()
    {
        DraftSession session = Complete();

        var events = session.Events.Records.Select(r => r.Event).ToList();
        Assert.Equal(8, events.OfType<DraftBanCommitted>().Count());
        Assert.Equal(10, events.OfType<DraftPickCommitted>().Count());
        Assert.IsType<DraftCompleted>(events[^1]);
        Assert.Equal(Complete().Events.Checksum, session.Events.Checksum);
    }
}
