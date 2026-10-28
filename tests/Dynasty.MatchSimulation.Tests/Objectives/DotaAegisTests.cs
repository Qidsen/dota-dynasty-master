using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// The Aegis on the ground (Fandom «Aegis of the Immortal»; the project owner on 2026-10-28: «anyone may pick it up — the killers take
/// it, the others steal it — and deny it»): killed, Roshan drops it where he died; a hero of either team walking onto it takes it,
/// a hero hitting it destroys it; it goes when Roshan comes back.
/// </summary>
public class DotaAegisTests
{
    private static readonly ModifierSpec Aegis = new("aegis", 300000, reviveDelayMs: 5000, consumedOnRevive: true);

    private static RulesetDraft WithRoshan(int respawnMs = 60000)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Add(Hero("delta"));
        draft.Creeps.Add(new CreepType("roshan", new UnitStats(2000, Fixed.FromInt(5), 270, 60, 60, 150, 1000, 300, 400, 60, visionRange: 600)));
        draft.Boss = new BossDefinition("roshan", "roshan", P(5000, 2500), leashRadius: 600, firstSpawnMs: 0, respawnMinMs: respawnMs, respawnMaxMs: respawnMs,
            teamGold: 0, reward: null, aegis: Aegis, provokeRadius: 0, aegisDrops: true);
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static MatchUnit? Ground(MatchState state) => state.Units.SingleOrDefault(u => u.Kind == UnitKind.GroundItem);

    private static void KillRoshan(SimulationKernel<MatchState> kernel, long tick, string killer) => At(kernel, tick, ctx =>
    {
        foreach (string hero in new[] { "alpha", "beta", "gamma", "delta" })
            H(ctx.State, hero).Position = P(1000 + 300 * hero.Length, 9000);
        Damage.Deal(ctx, H(ctx.State, killer).Id, ctx.State.Boss!, Fixed.FromInt(99999), DamageType.Pure);
    });

    [Fact]
    public void Killed_RoshanDropsTheAegisWhereHeDied_NobodyHasItYet()
    {
        var kernel = Start(WithRoshan());
        KillRoshan(kernel, 5, "alpha");

        kernel.RunTicks(10);

        MatchUnit aegis = Ground(kernel.State)!;
        Assert.Equal(P(5000, 2500), aegis.Position);
        Assert.DoesNotContain(kernel.State.Heroes, h => h.Modifiers.Any(m => m.Spec.Key == "aegis"));
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("gamma")]   // the other team steals it
    public void AHeroWalkingOntoIt_TakesIt(string taker)
    {
        var kernel = Start(WithRoshan());
        KillRoshan(kernel, 5, "alpha");
        At(kernel, 12, ctx => H(ctx.State, taker).Position = P(5050, 2500));

        kernel.RunTicks(16);

        Assert.Null(Ground(kernel.State));
        Assert.Contains(H(kernel.State, taker).Modifiers, m => m.Spec.Key == "aegis");
        Assert.Equal(H(kernel.State, taker).Id, Assert.Single(kernel.EventsOf<AegisPicked>()).Hero);
    }

    [Fact]
    public void AHeroHittingIt_DeniesIt()
    {
        var kernel = Start(WithRoshan());
        KillRoshan(kernel, 5, "alpha");
        At(kernel, 12, ctx => Damage.Deal(ctx, H(ctx.State, "gamma").Id, Ground(ctx.State)!, Fixed.FromInt(10), DamageType.Physical));

        kernel.RunTicks(16);

        Assert.Null(Ground(kernel.State));
        Assert.DoesNotContain(kernel.State.Heroes, h => h.Modifiers.Any(m => m.Spec.Key == "aegis"));
        Assert.Equal(H(kernel.State, "gamma").Id, Assert.Single(kernel.EventsOf<AegisDenied>()).Hero);
    }

    [Fact]
    public void ItGoes_WhenRoshanComesBack()
    {
        var kernel = Start(WithRoshan(respawnMs: 3000));
        KillRoshan(kernel, 5, "alpha");

        kernel.RunTicks(30 * 4);

        Assert.Null(Ground(kernel.State));
        Assert.NotNull(kernel.State.Boss);
    }

    [Fact]
    public void TheKillersRichestHero_GoesToPickItUp_WhileAnEnemyNearbyHitsIt()
    {
        var kernel = Start(WithRoshan());
        KillRoshan(kernel, 5, "alpha");
        At(kernel, 6, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5500, 2500);
            H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(3000);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        foreach (string hero in new[] { "beta", "gamma", "delta" })
            settings.Uncontrolled.Add(hero);
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 5);

        Assert.Equal(H(kernel.State, "alpha").Id, Assert.Single(kernel.EventsOf<AegisPicked>()).Hero);

        var other = Start(WithRoshan());
        KillRoshan(other, 5, "alpha");
        At(other, 6, ctx => H(ctx.State, "gamma").Position = P(5300, 2500));   // an enemy right by it
        var enemyAi = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        foreach (string hero in new[] { "alpha", "beta", "delta" })
            enemyAi.Uncontrolled.Add(hero);
        HeroAi.Attach(other, enemyAi);

        other.RunTicks(30 * 5);

        Assert.True(other.EventsOf<AegisPicked>().Any(p => p.Hero == H(other.State, "gamma").Id) || other.EventsOf<AegisDenied>().Any(), "the enemy steals or denies it");
    }
}
