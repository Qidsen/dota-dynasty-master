using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Heroes;

public class TeleportTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    public static RulesetDraft WithTeleport(RulesetDraft? draft = null)
    {
        draft ??= new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("teleport", "Teleport", AbilityTargeting.Unit, TargetTeam.Ally, 20000, 3000,
            new[] { 60000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Teleport() }, targetsStructures: true));
        draft.Items.Add(new ItemDefinition("tp_scroll", "Teleport Scroll", 90, activeAbilityId: "teleport", charges: 1));
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft)
    {
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "tp_scroll"));
        return kernel;
    }

    private static void TeleportTo(SimulationKernel<MatchState> kernel, long tick, string placement) =>
        At(kernel, tick, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("teleport", ctx.State.FindStructure(placement)!.Id)));

    [Fact]
    public void Teleport_ChannelsThenMovesTheHeroBesideTheAlliedStructure()
    {
        var kernel = Start(WithTeleport());
        TeleportTo(kernel, 2, "dawn.mid.t1");

        kernel.RunTicks(3 + 89);
        Assert.Equal(P(200, 5000), H(kernel.State, "alpha").Position);   // still channelling

        kernel.RunTicks(2);
        MatchUnit alpha = H(kernel.State, "alpha");
        double fromTower = FixedVector2.Distance(alpha.Position, P(3000, 5000)).ToDouble();
        Assert.InRange(fromTower, 100, 400);
        Assert.True(alpha.Position.X < Fixed.FromInt(3000), "arrives on its own base side");
        Assert.Empty(alpha.Hero!.Inventory);
        Assert.Single(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "teleport");
    }

    [Fact]
    public void Stun_InterruptsTheChannelAndKeepsTheScroll()
    {
        var kernel = Start(WithTeleport());
        TeleportTo(kernel, 2, "dawn.mid.t1");
        At(kernel, 40, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), ModifierSpec.Status("stun", 500, ModifierFlags.Stunned), EntityId.None));

        kernel.RunTicks(150);

        Assert.Equal(P(200, 5000), H(kernel.State, "alpha").Position);
        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "interrupted");
        Assert.Single(H(kernel.State, "alpha").Hero!.Inventory);
    }

    [Fact]
    public void Teleport_CannotTargetEnemyStructures()
    {
        var kernel = Start(WithTeleport());
        TeleportTo(kernel, 2, "dusk.mid.t1");

        kernel.RunTicks(10);

        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "invalid_target");
    }

    [Fact]
    public void OrdinaryAbilities_StillCannotTargetStructures()
    {
        var draft = WithTeleport();
        draft.Abilities.Add(new AbilityDefinition("mend", "Mend", AbilityTargeting.Unit, TargetTeam.Ally, 20000, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Heal(100) }));
        draft.Heroes[0] = Hero("alpha", abilities: new[] { "mend" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("mend", ctx.State.FindStructure("dawn.mid.t1")!.Id)));

        kernel.RunTicks(5);

        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "invalid_target");
    }

    private static SimulationKernel<MatchState> DistantThreat(HeroPersonality alphaPersonality)
    {
        var draft = WithTeleport();
        draft.StructureTypes[0] = new StructureType("tower", TowerStats(dmg: 1));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            HeroShop.QueuePurchase(H(ctx.State, "alpha"), "tp_scroll");
            H(ctx.State, "beta").Position = P(3300, 5000);   // the threat exists from the first plan update on
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(ctx.State.FindStructure("dawn.mid.t1")!.Id));
        });
        At(kernel, 2, ctx => H(ctx.State, "alpha").Position = P(1000, 9000));   // far away in a corner, outside the fountain
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Personalities["alpha"] = alphaPersonality;
        HeroAi.Attach(kernel, settings);
        return kernel;
    }

    [Fact]
    public void Ai_TeleportsToDefendADistantStructure()
    {
        var kernel = DistantThreat(new HeroPersonality { Greed = 0.5 });

        kernel.RunTicks(30 * 30);

        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "teleport");
    }

    [Fact]
    public void Ai_GreedyCarryKeepsFarmingInsteadOfDefendingFarAway()
    {
        var kernel = DistantThreat(new HeroPersonality { Greed = 0.8 });

        kernel.RunTicks(30 * 30);

        Assert.DoesNotContain(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "teleport");
    }
}
