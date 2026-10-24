using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

public class ProjectileTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Alpha is ranged (range 600, projectile 600 u/s, 300 ms attack point); beta stands 500 away.</summary>
    private static RulesetDraft Ranged()
    {
        var draft = TestAbilities.Draft();
        var stats = new UnitStats(600, Fixed.FromInt(2), 300, 50, 54, 600, 1500, 300, 800, 24, 0, visionRange: 1200, projectileSpeed: 600);
        draft.Heroes[draft.Heroes.FindIndex(h => h.Id == "alpha")] = Hero("alpha", stats, abilities: new[] { "lance" });
        draft.Abilities.Add(new AbilityDefinition("lance", "Lance", AbilityTargeting.Unit, TargetTeam.Enemy, 900, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Projectile(500, EffectDefinition.Damage(DamageType.Magical, 100)) }));
        return draft;
    }

    private static SimulationKernel<MatchState> Duel(RulesetDraft draft)
    {
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(5500, 3000);
        });
        return kernel;
    }

    [Fact]
    public void RangedAttack_LandsWhenTheProjectileArrives()
    {
        var kernel = Duel(Ranged());
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));

        kernel.StepUntil(() => kernel.EventsOf<DamageDealt>().Any(), 200);

        long started = kernel.TimedEventsOf<AttackStarted>().First().Tick;
        long landed = kernel.TimedEventsOf<DamageDealt>().First().Tick;
        // 9 ticks of wind-up, then ~476 units (500 minus beta's 24 radius) at 20 units per tick ≈ 24 ticks.
        Assert.InRange(landed - started, 9 + 22, 9 + 26);
        Assert.Single(kernel.EventsOf<ProjectileLaunched>());
    }

    [Fact]
    public void MeleeAttacks_StillLandAtTheAttackPoint()
    {
        var kernel = Duel(TestAbilities.Draft());
        At(kernel, 0, ctx => H(ctx.State, "beta").Position = P(5150, 3000));
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));

        kernel.StepUntil(() => kernel.EventsOf<DamageDealt>().Any(), 200);

        Assert.Equal(9, kernel.TimedEventsOf<DamageDealt>().First().Tick - kernel.TimedEventsOf<AttackStarted>().First().Tick);
        Assert.Empty(kernel.EventsOf<ProjectileLaunched>());
    }

    [Fact]
    public void ADisjointingDash_DodgesProjectilesInFlight()
    {
        var draft = Ranged().WithHeroAbilities("beta", "leap");
        var kernel = Duel(draft);
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));
        kernel.AddSystem(new TriggerSystem(
            _ => kernel.EventsOf<ProjectileLaunched>().Any(),
            ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast("leap", H(ctx.State, "alpha").Id))));

        kernel.RunTicks(60);

        Assert.Contains(kernel.EventsOf<ProjectileDodged>(), d => d.Target == H(kernel.State, "beta").Id);
        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Attacker == H(kernel.State, "alpha").Id && d.DamageType == DamageType.Physical);
    }

    [Fact]
    public void ProjectilesAtADeadTarget_Fizzle()
    {
        var kernel = Duel(Ranged());
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));
        kernel.AddSystem(new TriggerSystem(
            _ => kernel.EventsOf<ProjectileLaunched>().Any(),
            ctx => Damage.Deal(ctx, EntityId.None, H(ctx.State, "beta"), Fixed.FromInt(9999), DamageType.Pure)));

        kernel.RunTicks(80);

        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Attacker == H(kernel.State, "alpha").Id);
        Assert.Empty(kernel.State.Projectiles);
    }

    [Fact]
    public void HomingProjectiles_FollowAFleeingTarget()
    {
        var kernel = Duel(Ranged());
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "beta").Id)));
        kernel.AddSystem(new TriggerSystem(
            _ => kernel.EventsOf<ProjectileLaunched>().Any(),
            ctx =>
            {
                HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Idle);
                HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(9000, 3000)));
            }));

        kernel.RunTicks(150);

        Assert.Single(kernel.EventsOf<DamageDealt>(), d => d.Attacker == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void AbilityProjectiles_ApplyTheirEffectsOnArrival()
    {
        var kernel = Duel(Ranged());
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("lance", H(ctx.State, "beta").Id)));

        kernel.RunTicks(60);

        long cast = kernel.TimedEventsOf<AbilityCast>().Single().Tick;
        (long hit, DamageDealt damage) = kernel.TimedEventsOf<DamageDealt>().Single();
        Assert.Equal(DamageType.Magical, damage.DamageType);
        Assert.InRange(hit - cast, 26, 31);   // 476 units at 500 u/s ≈ 29 ticks
    }

    [Fact]
    public void Validation_RejectsProjectilesOnNonUnitAbilities()
    {
        var draft = Ranged();
        draft.Abilities.Add(new AbilityDefinition("bad", "Bad", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Projectile(500, EffectDefinition.Damage(DamageType.Magical, 1)) }));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("bad"));
    }
}
