using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Effects of a modifier triggered by its bearer's actions and fate (docs/DOTA_PACK.md, wave 4): on casting an
/// ability (Aftershock), on killing a unit (Sadist), on dying (Caustic Finale, Retribution), and on-hit
/// effects limited to heroes (Essence Shift).
/// </summary>
public class TriggerTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private const int VisionRefresh = Dynasty.MatchSimulation.Vision.VisionSystem.IntervalTicks + 1;

    /// <summary>Alpha (Dawn, knows Bolt) and beta (Dusk) 200 apart, gamma (Dawn) behind alpha; the modifiers are applied at tick 0.</summary>
    private static SimulationKernel<MatchState> Arena(ModifierSpec? onAlpha = null, ModifierSpec? onBeta = null)
    {
        RulesetDraft draft = TestAbilities.Draft().WithoutWaves();
        draft.WithHeroAbilities("alpha", new[] { "bolt" });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5200, 5000);
            H(ctx.State, "gamma").Position = P(4900, 5000);
            if (onAlpha != null)
                Modifiers.Apply(ctx, H(ctx.State, "alpha"), onAlpha, EntityId.None);
            if (onBeta != null)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), onBeta, EntityId.None);
        }));
        return kernel;
    }

    private static IEnumerable<DamageDealt> PureOn(SimulationKernel<MatchState> kernel, string heroId) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Target == H(kernel.State, heroId).Id && d.DamageType == DamageType.Pure);

    [Fact]
    public void OnCast_RunsWhenTheBearerCastsAnAbility()
    {
        var aftershock = new ModifierSpec("aftershock", -1, onCast: new[]
        {
            EffectDefinition.Area(300, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 25)),
        });
        var kernel = Arena(onAlpha: aftershock);
        kernel.AddSystem(new LambdaSystem(VisionRefresh, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("bolt", H(ctx.State, "beta").Id))));

        kernel.RunTicks(30 * 2);

        Assert.Single(kernel.EventsOf<AbilityCast>());
        Assert.Equal(25, PureOn(kernel, "beta").Single().Amount.ToIntFloor());
    }

    [Fact]
    public void OnKill_RunsWhenTheBearerKillsAUnit()
    {
        var sadist = new ModifierSpec("sadist", -1, onKill: new[] { EffectDefinition.OnCaster(EffectDefinition.Heal(100)) });
        var kernel = Arena(onAlpha: sadist);
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(200);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(5000), DamageType.Pure);
        }));

        kernel.RunTicks(4);

        Assert.True(H(kernel.State, "beta").Hero!.IsDead);
        Assert.InRange(H(kernel.State, "alpha").Hp.ToDouble(), 300, 301);
    }

    [Fact]
    public void OnDeath_RunsWhenTheBearerDies()
    {
        var finale = new ModifierSpec("finale", 10000, onDeath: new[]
        {
            EffectDefinition.Area(400, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 50)),
        });
        var kernel = Arena(onBeta: finale);
        kernel.AddSystem(new LambdaSystem(2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(5000), DamageType.Pure)));

        kernel.RunTicks(4);

        Assert.Equal(new[] { 50, 50 }, new[] { "alpha", "gamma" }.Select(h => PureOn(kernel, h).Single().Amount.ToIntFloor()));
    }

    [Fact]
    public void OnHitHeroesOnly_SkipsCreepsAndSummons()
    {
        var shift = new ModifierSpec("shift", -1, onHit: new[] { EffectDefinition.Damage(DamageType.Pure, 40) }, onHitHeroesOnly: true);
        var kernel = Arena(onAlpha: shift);
        MatchUnit? summon = null;
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            CreepType melee = ctx.State.Ruleset.CreepTypes.First();
            summon = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), melee, ctx.Tick.Value, ctx.Tick.Value + 100000);
            summon.Position = P(5100, 5100);
            MatchUnit alpha = H(ctx.State, "alpha");
            AttackSystem.Strike(ctx, alpha, summon, 100);
            AttackSystem.Strike(ctx, alpha, H(ctx.State, "beta"), 100);
        }));

        kernel.RunTicks(3);

        Assert.Single(PureOn(kernel, "beta"));
        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Target == summon!.Id && d.DamageType == DamageType.Pure);
    }

    /// <summary>Flesh Heap, Feast's hero bonus: on-kill effects limited to hero victims.</summary>
    [Fact]
    public void OnKillHeroesOnly_IgnoresCreepKills()
    {
        var heap = new ModifierSpec("heap", -1, onKill: new[] { EffectDefinition.OnCaster(EffectDefinition.Heal(100)) }, onKillHeroesOnly: true);
        var kernel = Arena(onAlpha: heap);
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            H(ctx.State, "alpha").Hp = Fixed.FromInt(200);
            MatchUnit bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, ctx.Tick.Value + 100000);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, bug, Fixed.FromInt(5000), DamageType.Pure);
        }));
        kernel.AddSystem(new LambdaSystem(4, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(5000), DamageType.Pure)));

        double afterCreep = 0;
        kernel.AddSystem(new LambdaSystem(4, ctx => afterCreep = H(ctx.State, "alpha").Hp.ToDouble()));
        kernel.RunTicks(6);

        Assert.InRange(afterCreep, 200, 201);   // the creep kill healed nothing
        Assert.InRange(H(kernel.State, "alpha").Hp.ToDouble(), 300, 301);
    }

    /// <summary>Guardian Angel, Ghost Scepter: physical damage (attacks) does nothing; magical damage still lands.</summary>
    [Fact]
    public void PhysicalImmunity_BlocksPhysicalDamageOnly()
    {
        var kernel = Arena(onBeta: ModifierSpec.Status("angel", 10000, ModifierFlags.PhysicalImmune));
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Physical);
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(100), DamageType.Pure);
        }));
        kernel.RunTicks(3);

        Assert.Equal(new[] { 0, 100 }, kernel.EventsOf<DamageDealt>().Where(d => d.Target == H(kernel.State, "beta").Id).Select(d => d.Amount.ToIntFloor()));
    }
}
