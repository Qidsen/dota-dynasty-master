using Dynasty.Drafting;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting.Tests;

public class PositionPlanTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();

    [Fact]
    public void ClassicFive_GoToTheirNaturalPositionsAndLanes()
    {
        PositionPlan plan = PositionPlan.For(Ruleset, Team.Dawn, new[] { "thornkeeper", "emberwisp", "dawnmender", "ironvow", "stonewarden" });

        Assert.Equal("ironvow", plan.HeroAt(1));
        Assert.Equal("emberwisp", plan.HeroAt(2));
        Assert.Equal("stonewarden", plan.HeroAt(3));
        Assert.Equal("bot", plan.LaneOf(plan.HeroAt(1)));     // Dawn's safe lane
        Assert.Equal("mid", plan.LaneOf(plan.HeroAt(2)));
        Assert.Equal("top", plan.LaneOf(plan.HeroAt(3)));     // Dawn's off lane
        Assert.Equal("top", plan.LaneOf(plan.HeroAt(4)));
        Assert.Equal("bot", plan.LaneOf(plan.HeroAt(5)));
        Assert.Equal(new[] { "dawnmender", "thornkeeper" }.ToHashSet(), new[] { plan.HeroAt(4), plan.HeroAt(5) }.ToHashSet());
    }

    [Fact]
    public void DuskLanes_AreMirrored()
    {
        PositionPlan plan = PositionPlan.For(Ruleset, Team.Dusk, new[] { "starborn", "frostweaver", "hivecaller", "thornkeeper", "dawnmender" });

        Assert.Equal("top", plan.LaneOf(plan.HeroAt(1)));
        Assert.Equal("bot", plan.LaneOf(plan.HeroAt(3)));
    }

    [Fact]
    public void Assignment_MaximisesFitForFlexibleHeroes()
    {
        // Ashblade can carry or offlane; with a natural carry present it goes offlane.
        PositionPlan plan = PositionPlan.For(Ruleset, Team.Dawn, new[] { "ashblade", "duskrunner", "stormcaller", "windsinger", "lumenpriest" });

        Assert.Equal("duskrunner", plan.HeroAt(1));
        Assert.Equal("ashblade", plan.HeroAt(3));
    }

    [Fact]
    public void Plans_BuildAPlayableLineup()
    {
        PositionPlan dawn = PositionPlan.For(Ruleset, Team.Dawn, new[] { "ironvow", "emberwisp", "stonewarden", "thornkeeper", "dawnmender" });
        PositionPlan dusk = PositionPlan.For(Ruleset, Team.Dusk, new[] { "starborn", "frostweaver", "hivecaller", "chainwarden", "lumenpriest" });

        MatchLineup lineup = PositionPlan.Lineup(dawn, dusk);

        var kernel = Match.Create(Ruleset, 1, lineup);
        Assert.Equal(10, kernel.State.Heroes.Count);
    }
}
