using Dynasty.Players;
using Dynasty.Training;

namespace Dynasty.Training.Tests;

public class TrainingTests
{
    private static PlayerProfile Player(int level = 10, int potential = 18, int learningSpeed = 12, int professionalism = 12) =>
        new("p", "p", "p", "", 2003, new[] { "en" }, "", new[] { 90, 40, 40, 40, 40 }, AttributeSet.Uniform(level),
            new HiddenQualities(potential, professionalism, 10, 8, learningSpeed),
            new[] { new HeroPoolRecord("alpha", 50, 50, 60, 70, 50, 60), new HeroPoolRecord("beta", 50, 50, 60, 70, 50, 60) });

    private static WeeklyPlan Mechanics(int hours) => new WeeklyPlan(new Dictionary<TrainingFocus, int> { [TrainingFocus.Mechanics] = hours, [TrainingFocus.Rest] = 10 });

    private static readonly TrainingEnvironment Average = new(coachQuality: 10, facilities: 10);

    private static (PlayerProfile Player, PlayerCondition Condition) Weeks(int weeks, PlayerProfile player, WeeklyPlan plan, TrainingEnvironment environment)
    {
        PlayerCondition condition = PlayerCondition.Fresh;
        for (int week = 0; week < weeks; week++)
            (player, condition) = TrainingWeek.Apply(player, condition, plan, environment);
        return (player, condition);
    }

    [Fact]
    public void OneWeek_NeverJumpsAnAttributeByMoreThanOne()
    {
        var (after, condition) = TrainingWeek.Apply(Player(), PlayerCondition.Fresh, Mechanics(40), Average);

        Assert.All(Attributes.All, a => Assert.InRange(after.Attributes[a], 10, 11));
        Assert.True(condition.Progress(Dynasty.Players.Attribute.LastHit) > 0, "the work is not lost: progress builds up");
    }

    [Fact]
    public void FocusedTraining_GrowsTheFocusedAttributesOverAWeeks()
    {
        var (after, _) = Weeks(12, Player(), Mechanics(30), Average);

        Assert.True(after.Attributes[Dynasty.Players.Attribute.LastHit] > 10);
        Assert.True(after.Attributes[Dynasty.Players.Attribute.LastHit] > after.Attributes[Dynasty.Players.Attribute.Vision], "unfocused attributes stay behind");
    }

    [Fact]
    public void BetterCoachesAndQuickerLearners_GrowFaster()
    {
        int Grown(PlayerProfile player, TrainingEnvironment environment) => Weeks(12, player, Mechanics(30), environment).Player.Attributes[Dynasty.Players.Attribute.Reaction];

        Assert.True(Grown(Player(), new TrainingEnvironment(18, 10)) > Grown(Player(), new TrainingEnvironment(3, 10)));
        Assert.True(Grown(Player(learningSpeed: 19), Average) > Grown(Player(learningSpeed: 3), Average));
    }

    [Fact]
    public void Growth_StopsAtThePotential()
    {
        var (after, _) = Weeks(80, Player(level: 10, potential: 12), Mechanics(30), new TrainingEnvironment(20, 20));

        Assert.Equal(12, after.Attributes[Dynasty.Players.Attribute.LastHit]);
    }

    [Fact]
    public void Overtraining_TiresThePlayerAndCostsEfficiency_RestRecovers()
    {
        var heavy = new WeeklyPlan(new Dictionary<TrainingFocus, int> { [TrainingFocus.Mechanics] = 60 });
        var (_, tired) = Weeks(4, Player(), heavy, Average);
        var (_, fine) = Weeks(4, Player(), Mechanics(30), Average);

        Assert.True(tired.Fatigue > fine.Fatigue + 20, $"fatigue heavy {tired.Fatigue}, moderate {fine.Fatigue}");
        Assert.True(tired.BurnoutRisk > fine.BurnoutRisk);

        var rest = new WeeklyPlan(new Dictionary<TrainingFocus, int> { [TrainingFocus.Rest] = 40 });
        var (_, rested) = TrainingWeek.Apply(Player(), tired, rest, Average);
        Assert.True(rested.Fatigue < tired.Fatigue);
    }

    [Fact]
    public void HeroPractice_SharpensThePracticedHeroAndLetsOthersFade()
    {
        var plan = new WeeklyPlan(new Dictionary<TrainingFocus, int> { [TrainingFocus.Heroes] = 20, [TrainingFocus.Rest] = 10 }, practicedHeroes: new[] { "alpha" });

        var (after, _) = Weeks(3, Player(), plan, Average);

        Assert.True(after.Hero("alpha")!.RecentPractice > 50);
        Assert.True(after.Hero("beta")!.RecentPractice < 50);
        Assert.True(after.Hero("alpha")!.Mastery >= 50);
    }

    [Fact]
    public void Plan_RejectsAWeekLongerThanTheWeek()
    {
        Assert.Throws<ArgumentException>(() => new WeeklyPlan(new Dictionary<TrainingFocus, int> { [TrainingFocus.Mechanics] = 100, [TrainingFocus.Scrims] = 80 }));
    }
}
