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

    /// <summary>Berserker's Call, Duel: a taunted hero attacks the taunting unit whatever it was ordered to do, and cannot cast.</summary>
    [Fact]
    public void Taunt_MakesTheHeroAttackTheTaunter()
    {
        var kernel = Arena();
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("call", 3000, ModifierFlags.Taunted), H(ctx.State, "alpha").Id);
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(6000, 5000)));
        }));
        kernel.AddSystem(new LambdaSystem(3, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(6000, 5000)))));
        kernel.RunTicks(30 * 2);

        Assert.Contains(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "beta").Id && a.Target == H(kernel.State, "alpha").Id);
        Assert.True(H(kernel.State, "beta").Position.X < Fixed.FromInt(5300));   // it did not walk off
    }

    /// <summary>Terror Wave, Requiem: a feared hero runs away from the source and neither attacks nor casts.</summary>
    [Fact]
    public void Fear_MakesTheHeroRunAwayWithoutAttacking()
    {
        var kernel = Arena();
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("terror", 2000, ModifierFlags.Feared), H(ctx.State, "alpha").Id);
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id));
        }));
        kernel.RunTicks(30 * 2);

        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == H(kernel.State, "beta").Id);
        Assert.True(FixedVector2.Distance(H(kernel.State, "alpha").Position, H(kernel.State, "beta").Position) > Fixed.FromInt(600));   // 200 apart at the start, about 300 a second away
    }

    /// <summary>A feared creep or summon (not ordered around like heroes) at least stops attacking.</summary>
    [Fact]
    public void Fear_StopsCreepsFromAttacking()
    {
        var kernel = Arena();
        MatchUnit? bug = null;
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            bug = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "beta"), ctx.State.Ruleset.CreepTypes.First(), ctx.Tick.Value, ctx.Tick.Value + 100000);
            bug.Position = P(5050, 5000);
            bug.StartPosition = bug.Position;
            Modifiers.Apply(ctx, bug, ModifierSpec.Status("terror", 3000, ModifierFlags.Feared), H(ctx.State, "alpha").Id);
        }));
        kernel.RunTicks(30 * 2);

        Assert.DoesNotContain(kernel.EventsOf<AttackStarted>(), a => a.Attacker == bug!.Id);
    }

    /// <summary>Reincarnation: a hero that would die comes back at once with full health and mana and its on-death effects; then the revive is on cooldown.</summary>
    [Fact]
    public void Revive_BringsTheHeroBack_OncePerCooldown()
    {
        var reincarnation = new ModifierSpec("reincarnation", -1, revives: true, procCooldownMs: 60000, onDeath: new[]
        {
            EffectDefinition.Area(400, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 30)),
        });
        var kernel = Arena(onBeta: reincarnation);
        kernel.AddSystem(new LambdaSystem(2, ctx =>
        {
            H(ctx.State, "beta").Hero!.Mana = Fixed.Zero;
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "beta"), Fixed.FromInt(5000), DamageType.Pure);
        }));
        bool backAtFull = false, killedBeforeRevive = true;
        kernel.AddSystem(new LambdaSystem(4, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            backAtFull = beta.IsAlive && !beta.Hero!.IsDead && beta.Hp == Fixed.FromInt(beta.MaxHp) && beta.Hero.Mana == Fixed.FromInt(beta.Hero.MaxMana);
            killedBeforeRevive = kernel.EventsOf<HeroKilled>().Any();
            Damage.Deal(ctx, H(ctx.State, "alpha").Id, beta, Fixed.FromInt(5000), DamageType.Pure);
        }));
        kernel.RunTicks(6);

        Assert.True(backAtFull);
        Assert.False(killedBeforeRevive);
        Assert.Equal(30, PureOn(kernel, "alpha").Single().Amount.ToIntFloor());   // the revive's effects, once
        Assert.Single(kernel.EventsOf<HeroKilled>());   // the second death within the cooldown is real
        Assert.True(H(kernel.State, "beta").Hero!.IsDead);
    }
}
