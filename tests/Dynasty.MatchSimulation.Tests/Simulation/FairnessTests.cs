using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

/// <summary>
/// Units are processed sequentially within a tick (Dawn first: lower IDs). These tests pin down
/// that the processing order gives neither side an advantage.
/// </summary>
public class FairnessTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>Hero <paramref name="chaser"/> attacks <paramref name="runner"/>, who flees at equal speed from just inside melee range.</summary>
    private static int AttacksDuringChase(string chaser, string runner)
    {
        var kernel = new RulesetDraft().WithoutWaves().StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, chaser).Position = P(4000, 3000);
            H(ctx.State, runner).Position = P(4190, 3000);   // melee reach is 150 + 2 × 24 = 198
        }));
        kernel.AddSystem(new LambdaSystem(3, ctx =>   // once vision has seen the new positions
        {
            HeroOrders.Issue(H(ctx.State, chaser), HeroOrder.Attack(H(ctx.State, runner).Id));
            HeroOrders.Issue(H(ctx.State, runner), HeroOrder.Move(P(9000, 3000)));
        }));

        kernel.RunTicks(200);

        // The chase must really have happened, otherwise "equal" could mean "both did nothing".
        Assert.True(H(kernel.State, chaser).Position.X > Fixed.FromInt(5500), "chaser did not pursue");
        EntityId chaserId = H(kernel.State, chaser).Id;
        return kernel.EventsOf<AttackStarted>().Count(a => a.Attacker == chaserId);
    }

    [Fact]
    public void EqualSpeedChase_IsTheSameForBothSides()
    {
        Assert.Equal(AttacksDuringChase("alpha", "beta"), AttacksDuringChase("beta", "alpha"));
    }

    [Fact]
    public void SimultaneousStuns_BothResolveRegardlessOfProcessingOrder()
    {
        var draft = Abilities.TestAbilities.Draft()
            .WithHeroAbilities("alpha", "quake")
            .WithHeroAbilities("beta", "quake");
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(1, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 3000);
            H(ctx.State, "beta").Position = P(4200, 3000);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("quake"));
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.CastNoTarget("quake"));
        }));

        kernel.RunTicks(4);

        Assert.Equal(2, kernel.EventsOf<AbilityCast>().Count());
        Assert.True(H(kernel.State, "alpha").IsStunned);
        Assert.True(H(kernel.State, "beta").IsStunned);
    }
}
