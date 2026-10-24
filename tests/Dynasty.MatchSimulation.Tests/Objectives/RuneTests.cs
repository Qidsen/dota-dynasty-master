using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

public class RuneTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static List<MatchUnit> Runes(MatchState state) => state.Units.Where(u => u.Kind == UnitKind.Rune).ToList();

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly RuneType Haste = new("haste", new ModifierSpec("rune.haste", 20000, moveSpeedPercent: 40));
    private static readonly RuneType Bounty = new("bounty", gold: 60);

    /// <summary>Runes at (5000, 3000) and (5000, 7000); first at 1 s, then every 10 s.</summary>
    private static RulesetDraft WithRunes(params RuneType[] types)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Runes = new RuneRules(new[] { P(5000, 3000), P(5000, 7000) }, firstSpawnMs: 1000, intervalMs: 10000,
            types.Length > 0 ? types : new[] { Haste, Bounty });
        return draft;
    }

    [Fact]
    public void Runes_SpawnAtEverySpotOnSchedule()
    {
        var kernel = WithRunes().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.RunTicks(30);
        Assert.Empty(Runes(kernel.State));

        kernel.Step();
        Assert.Equal(new[] { P(5000, 3000), P(5000, 7000) }, Runes(kernel.State).Select(r => r.Position));
        Assert.Equal(2, kernel.EventsOf<RuneSpawned>().Count());
    }

    [Fact]
    public void OccupiedSpots_DoNotStackRunes()
    {
        var kernel = WithRunes().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.RunTicks(30 + 300 + 1);

        Assert.Equal(2, Runes(kernel.State).Count);
        Assert.Equal(2, kernel.EventsOf<RuneSpawned>().Count());
    }

    [Fact]
    public void WalkingOntoARune_PicksItUp()
    {
        var kernel = WithRunes(Haste).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 31, ctx => H(ctx.State, "alpha").Position = P(5050, 3000));

        kernel.RunTicks(33);

        MatchUnit alpha = H(kernel.State, "alpha");
        RunePickedUp pickup = Assert.Single(kernel.EventsOf<RunePickedUp>());
        Assert.Equal(alpha.Id, pickup.Hero);
        Assert.Equal("haste", pickup.RuneTypeId);
        Assert.Contains(alpha.Modifiers, m => m.Spec.Key == "rune.haste");
        Assert.Single(Runes(kernel.State));
    }

    [Fact]
    public void BountyRunes_PayGold()
    {
        var kernel = WithRunes(Bounty).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 31, ctx => H(ctx.State, "alpha").Position = P(5050, 3000));

        kernel.RunTicks(33);

        Assert.Equal(60, Assert.Single(kernel.EventsOf<GoldEarned>(), g => g.Reason == "rune").Amount);
    }

    [Fact]
    public void ContestedRune_GoesToOneHeroAndNeitherSideIsFavoured()
    {
        int dawnWins = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var kernel = WithRunes(Haste).StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") }, seed);
            At(kernel, 31, ctx =>
            {
                H(ctx.State, "alpha").Position = P(4950, 3000);
                H(ctx.State, "beta").Position = P(5050, 3000);   // exactly as close
            });
            kernel.RunTicks(33);
            RunePickedUp pickup = Assert.Single(kernel.EventsOf<RunePickedUp>(), p => p.RuneTypeId == "haste" && p.Position == P(5000, 3000));
            if (pickup.Hero == H(kernel.State, "alpha").Id)
                dawnWins++;
        }

        Assert.InRange(dawnWins, 5, 15);
    }

    [Fact]
    public void Runes_CannotBeAttackedAndAreOnlyKnownWhenSeen()
    {
        var kernel = WithRunes(Haste).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

        kernel.RunTicks(40);

        MatchUnit rune = Runes(kernel.State)[0];
        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, rune));   // alpha is at its fountain, far away
        Assert.False(rune.CanAttackAtAll);
        Assert.Empty(kernel.EventsOf<AttackStarted>());
    }

    [Fact]
    public void Ai_PicksUpAVisibleRune()
    {
        var kernel = WithRunes(Haste).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(4000, 3000));
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });

        kernel.RunTicks(30 * 10);

        Assert.Contains(kernel.EventsOf<RunePickedUp>(), p => p.Hero == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void Validation_RejectsEmptyRuneRules()
    {
        var draft = WithRunes();
        draft.Runes = new RuneRules(new[] { P(5000, 3000) }, 1000, 10000, Array.Empty<RuneType>());

        Assert.Contains(draft.Build().Validate(), e => e.Contains("rune", StringComparison.OrdinalIgnoreCase));
    }
}
