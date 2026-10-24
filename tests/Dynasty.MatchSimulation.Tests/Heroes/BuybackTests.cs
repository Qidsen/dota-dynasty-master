using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Heroes;

public class BuybackTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Draft rules: buyback costs 200 + 50 per level, cooldown 60 s; respawn is 4 s + 2 s per level.</summary>
    private static SimulationKernel<MatchState> Start()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.BuybackBaseGold = 200;
        draft.BuybackGoldPerLevel = 50;
        draft.BuybackCooldownMs = 60000;
        return draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
    }

    private static void Kill(SimulationKernel<MatchState> kernel, long tick) =>
        At(kernel, tick, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(99999), DamageType.Pure);
        });

    [Fact]
    public void Buyback_RespawnsImmediatelyAtTheFountainForGold()
    {
        var kernel = Start();
        Kill(kernel, 0);
        At(kernel, 2, ctx => HeroActions.QueueBuyback(H(ctx.State, "alpha")));

        kernel.RunTicks(4);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(alpha.IsAlive);
        Assert.Equal(P(200, 5000), alpha.Position);
        HeroBoughtBack buyback = Assert.Single(kernel.EventsOf<HeroBoughtBack>());
        Assert.Equal(250, buyback.Cost);   // 200 + 50 × level 1
        // 600 start + passive income − 30 death penalty − 250 buyback.
        Assert.InRange(alpha.Hero!.Gold.ToDouble(), 320, 321);
        Assert.Empty(kernel.EventsOf<HeroRespawned>());
    }

    [Fact]
    public void Buyback_FailsWhileAlive()
    {
        var kernel = Start();
        At(kernel, 0, ctx => HeroActions.QueueBuyback(H(ctx.State, "alpha")));

        kernel.RunTicks(2);

        Assert.Equal("not_dead", Assert.Single(kernel.EventsOf<BuybackFailed>()).Reason);
    }

    [Fact]
    public void Buyback_FailsWithoutEnoughGold()
    {
        var kernel = Start();
        Kill(kernel, 0);
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(100);
            HeroActions.QueueBuyback(H(ctx.State, "alpha"));
        });

        kernel.RunTicks(4);

        Assert.False(H(kernel.State, "alpha").IsAlive);
        Assert.Equal("gold", Assert.Single(kernel.EventsOf<BuybackFailed>()).Reason);
    }

    [Fact]
    public void Buyback_HasACooldown()
    {
        var kernel = Start();
        Kill(kernel, 0);
        At(kernel, 2, ctx => HeroActions.QueueBuyback(H(ctx.State, "alpha")));
        Kill(kernel, 10);
        At(kernel, 12, ctx => HeroActions.QueueBuyback(H(ctx.State, "alpha")));

        kernel.RunTicks(14);

        Assert.Single(kernel.EventsOf<HeroBoughtBack>());
        Assert.Equal("cooldown", Assert.Single(kernel.EventsOf<BuybackFailed>()).Reason);
        Assert.False(H(kernel.State, "alpha").IsAlive);
    }

    [Fact]
    public void Buyback_IsDisabledWhenTheRulesetHasNoCost()
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        Kill(kernel, 0);
        At(kernel, 2, ctx => HeroActions.QueueBuyback(H(ctx.State, "alpha")));

        kernel.RunTicks(4);

        Assert.Equal("disabled", Assert.Single(kernel.EventsOf<BuybackFailed>()).Reason);
    }

    [Fact]
    public void Fingerprint_ChangesWithBuybackRules()
    {
        var cheap = new RulesetDraft { BuybackBaseGold = 100 };
        var dear = new RulesetDraft { BuybackBaseGold = 101 };

        Assert.NotEqual(cheap.Build().ToDescriptor().Fingerprint, dear.Build().ToDescriptor().Fingerprint);
    }
}
