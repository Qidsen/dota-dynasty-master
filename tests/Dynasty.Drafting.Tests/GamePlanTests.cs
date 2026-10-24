using Dynasty.Drafting;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting.Tests;

public class GamePlanTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();

    private static GamePlan Plan(params string[] heroes) => GamePlan.For(Ruleset, heroes);

    [Fact]
    public void PushersAndSummoners_MakeAPushPlan()
    {
        GamePlan plan = Plan("siegewright", "wildheart", "hivecaller", "spiritwarden", "duskrunner");

        Assert.Equal(GameArchetype.Push, plan.Archetype);
        Assert.True(plan.PowerPeakMinute < 20, "peak " + plan.PowerPeakMinute);
    }

    [Fact]
    public void ScalingCarries_MakeALateGamePlan()
    {
        GamePlan plan = Plan("starborn", "gunwright", "bulwark", "lumenpriest", "dawnmender");

        Assert.Equal(GameArchetype.LateGame, plan.Archetype);
        Assert.True(plan.PowerPeakMinute >= 30, "peak " + plan.PowerPeakMinute);
    }

    [Fact]
    public void InitiationControlAndAreaDamage_MakeATeamfightPlan()
    {
        GamePlan plan = Plan("tidebreaker", "stonewarden", "frostweaver", "emberwisp", "windsinger");

        Assert.Equal(GameArchetype.Teamfight, plan.Archetype);
    }

    [Fact]
    public void Analysis_FlagsMissingNeeds()
    {
        GamePlan plan = Plan("ironvow", "starborn", "gunwright", "duskrunner", "shadeblade");

        Assert.Contains(DraftWeakness.NoInitiation, plan.Weaknesses);
        Assert.Contains(DraftWeakness.LittleControl, plan.Weaknesses);
        Assert.Contains(DraftWeakness.NoSustain, plan.Weaknesses);
    }

    [Fact]
    public void Analysis_FlagsOneSidedDamage()
    {
        Assert.Contains(DraftWeakness.MostlyMagicalDamage, Plan("emberwisp", "stormcaller", "frostweaver", "voidseer", "plaguebringer").Weaknesses);
    }

    [Fact]
    public void Analysis_NamesStrengths()
    {
        GamePlan plan = Plan("tidebreaker", "stonewarden", "frostweaver", "emberwisp", "windsinger");

        Assert.Contains(DraftStrength.StrongInitiation, plan.Strengths);
        Assert.Contains(DraftStrength.HeavyControl, plan.Strengths);
    }
}
