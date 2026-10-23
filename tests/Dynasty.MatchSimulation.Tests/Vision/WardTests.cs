using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Vision;

public class WardTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static RulesetDraft Draft()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("observer", new UnitStats(4, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 8, visionRange: 1600, innateFlags: ModifierFlags.Invisible), new Bounty(25, 25, 0)));
        draft.Creeps.Add(new CreepType("sentry", new UnitStats(4, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 8, visionRange: 300, trueSightRange: 900, innateFlags: ModifierFlags.Invisible)));
        draft.Abilities.Add(new AbilityDefinition("place_observer", "Place Observer", AbilityTargeting.Point, TargetTeam.Ally, 500, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.SummonAtPoint("observer", 6000, 1) }));
        draft.Abilities.Add(new AbilityDefinition("place_sentry", "Place Sentry", AbilityTargeting.Point, TargetTeam.Ally, 500, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.SummonAtPoint("sentry", 6000, 1) }));
        draft.Items.Add(new ItemDefinition("observer_ward", "Observer Ward", 75, activeAbilityId: "place_observer", charges: 1));
        draft.Items.Add(new ItemDefinition("sentry_ward", "Sentry Ward", 100, activeAbilityId: "place_sentry", charges: 1));
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void ObserverWard_IsPlacedAtThePointAndConsumesTheItem()
    {
        var kernel = Start(Draft());
        At(kernel, 0, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "observer_ward"));
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastAtPoint("place_observer", P(500, 5300))));

        kernel.RunTicks(5);

        MatchUnit ward = Assert.Single(kernel.State.Units, u => u.TypeId == "observer");
        Assert.Equal(P(500, 5300), ward.Position);
        Assert.Equal(Team.Dawn, ward.Team);
        Assert.Empty(H(kernel.State, "alpha").Hero!.Inventory);
    }

    [Fact]
    public void ObserverWard_RevealsEnemiesForItsTeamWhileStayingHidden()
    {
        var kernel = Start(Draft());
        At(kernel, 0, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "observer_ward"));
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastAtPoint("place_observer", P(500, 5300))));
        At(kernel, 5, ctx =>
        {
            H(ctx.State, "alpha").Position = P(200, 3000);   // walk away; only the ward watches now
            H(ctx.State, "beta").Position = P(1500, 5300);   // 1000 from the ward, 2300+ from alpha
        });

        kernel.RunTicks(10);

        MatchUnit ward = kernel.State.Units.Single(u => u.TypeId == "observer");
        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
        Assert.False(kernel.State.IsVisibleTo(Team.Dusk, ward));
    }

    [Fact]
    public void Wards_ExpireAfterTheirDuration()
    {
        var kernel = Start(Draft());
        At(kernel, 0, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "observer_ward"));
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastAtPoint("place_observer", P(500, 5300))));

        kernel.RunTicks(3 + 180 + 2);

        Assert.DoesNotContain(kernel.State.Units, u => u.TypeId == "observer");
        Assert.Single(kernel.EventsOf<UnitExpired>());
    }

    [Fact]
    public void SentryWard_RevealsEnemyObserversSoTheyCanBeAttacked()
    {
        var draft = Draft();
        var kernel = Start(draft);
        At(kernel, 0, ctx =>
        {
            HeroShop.QueuePurchase(H(ctx.State, "alpha"), "sentry_ward");
            HeroShop.QueuePurchase(H(ctx.State, "beta"), "observer_ward");
        });
        At(kernel, 2, ctx => H(ctx.State, "beta").Position = P(1000, 5600));
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.CastAtPoint("place_observer", P(900, 5600))));
        At(kernel, 6, ctx =>
        {
            H(ctx.State, "beta").Position = P(9000, 9000);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastAtPoint("place_sentry", P(500, 5400)));
        });

        kernel.RunTicks(12);

        MatchUnit observer = kernel.State.Units.Single(u => u.TypeId == "observer");
        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, observer));
    }

    [Fact]
    public void Validation_RejectsSummonAtPointForNonPointAbilities()
    {
        var draft = Draft();
        draft.Abilities.Add(new AbilityDefinition("bad_ward", "Bad", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.SummonAtPoint("observer", 6000, 1) }));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("bad_ward"));
    }
}
