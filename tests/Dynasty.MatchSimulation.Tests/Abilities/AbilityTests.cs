using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.Abilities.TestAbilities;

namespace Dynasty.MatchSimulation.Tests.Abilities;

public class AbilityTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Dawn: alpha (with abilities) and gamma; Dusk: beta and delta. Positions set at tick 0.</summary>
    private static SimulationKernel<MatchState> Arena(string[] alphaAbilities, Action<SimulationContext<MatchState>>? setup = null, RulesetDraft? draft = null)
    {
        draft ??= Draft();
        draft.WithHeroAbilities("alpha", alphaAbilities);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "gamma").Position = P(4900, 3000);
            H(ctx.State, "beta").Position = P(5200, 3000);
            H(ctx.State, "delta").Position = P(5700, 3000);
            setup?.Invoke(ctx);
        });
        return kernel;
    }

    private static void Order(SimulationKernel<MatchState> kernel, long tick, Func<MatchState, HeroOrder> order) =>
        At(kernel, tick, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), order(ctx.State)));

    [Fact]
    public void HeroLearnsItsFirstAbilityOnTheFirstTick()
    {
        var kernel = Arena(new[] { "bolt" });
        Assert.Equal(0, H(kernel.State, "alpha").Hero!.AbilityLevel("bolt"));

        kernel.Step();

        Assert.Equal(1, H(kernel.State, "alpha").Hero!.AbilityLevel("bolt"));
        AbilityLearned learned = Assert.Single(kernel.EventsOf<AbilityLearned>(), e => e.Hero == H(kernel.State, "alpha").Id);
        Assert.Equal("bolt", learned.AbilityId);
    }

    [Fact]
    public void UnitTargetNuke_DealsMagicalDamageAfterCastPointAndSpendsMana()
    {
        var kernel = Arena(new[] { "bolt" });
        Order(kernel, 1, s => HeroOrder.Cast("bolt", H(s, "beta").Id));

        kernel.RunTicks(30);

        (long castTick, AbilityCast cast) = Assert.Single(kernel.TimedEventsOf<AbilityCast>());
        Assert.Equal("bolt", cast.AbilityId);
        Assert.Equal(11, castTick);   // ordered on tick 1, wind-up starts on tick 2, 9-tick cast point
        DamageDealt hit = Assert.Single(kernel.EventsOf<DamageDealt>(), d => d.Target == H(kernel.State, "beta").Id);
        Assert.Equal(DamageType.Magical, hit.DamageType);
        Assert.Equal(Fixed.FromInt(75), hit.Amount);   // 100 vs 25 % magic resist
        Assert.InRange(H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 200, 215);
    }

    [Fact]
    public void Cooldown_PreventsImmediateRecast()
    {
        var kernel = Arena(new[] { "bolt" });
        Order(kernel, 1, s => HeroOrder.Cast("bolt", H(s, "beta").Id));
        Order(kernel, 20, s => HeroOrder.Cast("bolt", H(s, "beta").Id));

        kernel.RunTicks(40);

        Assert.Single(kernel.EventsOf<AbilityCast>());
        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "cooldown");
    }

    [Fact]
    public void OutOfRangeTarget_IsApproachedBeforeCasting()
    {
        var kernel = Arena(new[] { "bolt" }, ctx => H(ctx.State, "beta").Position = P(7000, 3000));
        Order(kernel, 1, s => HeroOrder.Cast("bolt", H(s, "beta").Id));

        kernel.RunTicks(300);

        Assert.Single(kernel.EventsOf<AbilityCast>());
        Assert.True(H(kernel.State, "alpha").Position.X > Fixed.FromInt(6000));
    }

    [Fact]
    public void NotEnoughMana_FailsTheCast()
    {
        var kernel = Arena(new[] { "bolt" }, ctx => H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(50));
        Order(kernel, 1, s => HeroOrder.Cast("bolt", H(s, "beta").Id));

        kernel.RunTicks(20);

        Assert.Empty(kernel.EventsOf<AbilityCast>());
        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "mana");
    }

    [Fact]
    public void Silence_PreventsCasting()
    {
        var kernel = Arena(new[] { "bolt" }, ctx =>
            Modifiers.Apply(ctx, H(ctx.State, "alpha"), ModifierSpec.Status("hush", 5000, ModifierFlags.Silenced), EntityId.None));
        Order(kernel, 1, s => HeroOrder.Cast("bolt", H(s, "beta").Id));

        kernel.RunTicks(20);

        Assert.Empty(kernel.EventsOf<AbilityCast>());
        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "silenced");
    }

    [Fact]
    public void StunDuringCastPoint_InterruptsWithoutSpendingMana()
    {
        var kernel = Arena(new[] { "bolt" });
        Order(kernel, 1, s => HeroOrder.Cast("bolt", H(s, "beta").Id));
        kernel.AddSystem(new TriggerSystem(
            ctx => H(ctx.State, "alpha").Hero!.IsCasting,
            ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), ModifierSpec.Status("stun", 2000, ModifierFlags.Stunned), EntityId.None)));

        kernel.RunTicks(30);

        Assert.Empty(kernel.EventsOf<AbilityCast>());
        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "interrupted");
        Assert.True(H(kernel.State, "alpha").Hero!.Mana >= Fixed.FromInt(300));
    }

    [Fact]
    public void AreaStun_HitsOnlyEnemiesInsideTheRadius()
    {
        var kernel = Arena(new[] { "quake" });
        Order(kernel, 1, _ => HeroOrder.CastNoTarget("quake"));

        kernel.RunTicks(3);

        MatchState s = kernel.State;
        Assert.True(H(s, "beta").IsStunned);
        Assert.False(H(s, "delta").IsStunned);
        Assert.False(H(s, "gamma").IsStunned);
        Assert.Single(kernel.EventsOf<DamageDealt>());
    }

    [Fact]
    public void Heal_RestoresAnAllyUpToMaxHp()
    {
        var kernel = Arena(new[] { "mend" }, ctx => H(ctx.State, "gamma").Hp = Fixed.FromInt(300));
        Order(kernel, 1, s => HeroOrder.Cast("mend", H(s, "gamma").Id));

        kernel.RunTicks(3);

        Assert.InRange(H(kernel.State, "gamma").Hp.ToDouble(), 500, 501);
    }

    [Fact]
    public void Heal_EmitsTheAmountActuallyRestored()
    {
        var kernel = Arena(new[] { "mend" }, ctx => H(ctx.State, "gamma").Hp = Fixed.FromInt(500));
        Order(kernel, 1, s => HeroOrder.Cast("mend", H(s, "gamma").Id));

        kernel.RunTicks(3);

        HealApplied heal = Assert.Single(kernel.EventsOf<HealApplied>());
        Assert.Equal(H(kernel.State, "alpha").Id, heal.Source);
        Assert.Equal(H(kernel.State, "gamma").Id, heal.Target);
        Assert.InRange(heal.Amount.ToDouble(), 99, 101);   // capped by max HP (600)
    }

    [Fact]
    public void AllyAbility_CannotTargetAnEnemy()
    {
        var kernel = Arena(new[] { "mend" });
        Order(kernel, 1, s => HeroOrder.Cast("mend", H(s, "beta").Id));

        kernel.RunTicks(5);

        Assert.Empty(kernel.EventsOf<AbilityCast>());
        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.Reason == "invalid_target");
    }

    [Fact]
    public void Dash_MovesTheCasterNextToTheTargetThenHits()
    {
        var kernel = Arena(new[] { "leap" });
        Order(kernel, 1, s => HeroOrder.Cast("leap", H(s, "delta").Id));

        kernel.RunTicks(3);

        double gap = FixedVector2.Distance(H(kernel.State, "alpha").Position, H(kernel.State, "delta").Position).ToDouble();
        Assert.InRange(gap, 40, 60);   // two 24-unit hero radii
        Assert.Contains(kernel.EventsOf<DamageDealt>(), d => d.Target == H(kernel.State, "delta").Id && d.DamageType == DamageType.Physical);
    }

    [Fact]
    public void Knockback_PushesTheTargetAwayFromTheCaster()
    {
        var kernel = Arena(new[] { "shove" });
        Order(kernel, 1, s => HeroOrder.Cast("shove", H(s, "beta").Id));

        kernel.RunTicks(3);

        Assert.Equal(P(5600, 3000), H(kernel.State, "beta").Position);
    }

    [Fact]
    public void Knockback_StopsAtObstacles()
    {
        var draft = Draft();
        draft.Obstacles.Add(ObstacleDefinition.Rectangle("rock", P(5400, 2800), P(5500, 3200)));
        var kernel = Arena(new[] { "shove" }, draft: draft);
        Order(kernel, 1, s => HeroOrder.Cast("shove", H(s, "beta").Id));

        kernel.RunTicks(3);

        Fixed x = H(kernel.State, "beta").Position.X;
        Assert.True(x > Fixed.FromInt(5200) && x < Fixed.FromInt(5400), "stopped at " + x);
    }

    [Fact]
    public void Summon_CreatesTemporaryFightersThatExpireWithoutBounty()
    {
        var kernel = Arena(new[] { "swarm" });
        Order(kernel, 1, _ => HeroOrder.CastNoTarget("swarm"));

        kernel.RunTicks(30);
        var summons = kernel.State.Units.Where(u => u.Kind == UnitKind.Summon).ToList();
        Assert.Equal(2, summons.Count);
        Assert.All(summons, s => Assert.Equal(Team.Dawn, s.Team));
        Assert.Contains(kernel.EventsOf<DamageDealt>(), d => summons.Any(s => s.Id == d.Attacker));

        kernel.RunTicks(300);
        Assert.DoesNotContain(kernel.State.Units, u => u.Kind == UnitKind.Summon && summons.Any(s => s.Id == u.Id));
        Assert.Equal(2, kernel.EventsOf<UnitExpired>().Count());
    }

    [Fact]
    public void PointArea_DamagesEnemiesAroundThePoint()
    {
        var kernel = Arena(new[] { "meteor" });
        Order(kernel, 1, _ => HeroOrder.CastAtPoint("meteor", P(5600, 3000)));

        kernel.RunTicks(3);

        // delta stands 100 from the point (hit); beta 400 away and ally gamma are not.
        var targets = kernel.EventsOf<DamageDealt>().Select(d => d.Target).ToList();
        Assert.Equal(new[] { H(kernel.State, "delta").Id }, targets);
    }
}
