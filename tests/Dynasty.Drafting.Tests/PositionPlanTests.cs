using Dynasty.Drafting;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting.Tests;

public class PositionPlanTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();

    /// <summary>
    /// A hero with known position shares (Dota pack: how often pros play it on each position) fits by them, not by its role tags:
    /// its main position fits fully, the others as far as they are played, an unplayed one not at all.
    /// </summary>
    [Fact]
    public void PositionShares_DecideTheFit_OverRoles()
    {
        HeroDefinition baseHero = Ruleset.FindHero("ironvow")!;   // a carry by its roles
        var hero = new HeroDefinition("pudge", "Pudge", baseHero.Roles, baseHero.BaseStats, 0, baseHero.ArmorPerLevel, 0, 0, 0,
            baseHero.HpRegenPerSecond, baseHero.ManaRegenPerSecond, positionShares: new[] { 0, 2, 30, 48, 20 });

        Assert.Equal(-5, PositionPlan.Fit(hero, 1));
        Assert.Equal(-5, PositionPlan.Fit(hero, 2));
        Assert.Equal(6, PositionPlan.Fit(hero, 3));
        Assert.Equal(10, PositionPlan.Fit(hero, 4));
        Assert.Equal(4, PositionPlan.Fit(hero, 5));
        Assert.Equal(10, PositionPlan.Fit(baseHero, 1));   // without shares: by roles
    }

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
