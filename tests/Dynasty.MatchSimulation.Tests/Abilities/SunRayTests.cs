using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Sun Ray (Dota 7.41f data and official notes; the project owner: the beam follows the fight target): a beam of 1200 by 130 from
/// Phoenix, first towards the cast point, then turning towards the target it attacks at most 25° a second; every 0.2 s enemies on it
/// take damage and allies are healed, Phoenix paying a share of its current health; a stun or silence ends it.
/// </summary>
public class SunRayTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>6 s; every 0.2 s: enemies 20 + 1% of max health, allies 10 + 0.5%, Phoenix 1% of its current health.</summary>
    private static readonly EffectDefinition Ray = EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("sun_ray", 6000,
        ModifierFlags.Rooted | ModifierFlags.Disarmed, dispellable: false, periodMs: 200, periodicFromBearer: true, beamLength: 1200, beamTurnPerSecond: 25,
        endsWhenStunnedOrSilenced: true,
        periodicEffects: new[]
        {
            EffectDefinition.LineArea(130, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 20),
                EffectDefinition.ScaledDamage(DamageType.Pure, DamageScaling.TargetMaxHealthPercent, 100)),
            EffectDefinition.LineArea(130, TargetTeam.Ally, EffectDefinition.ScaledHeal(DamageScaling.FlatHundredths, 1000), EffectDefinition.ScaledHeal(DamageScaling.TargetMaxHealthPercent, 50)),
            EffectDefinition.OnCaster(EffectDefinition.ScaledDamage(DamageType.Pure, DamageScaling.TargetCurrentHealthPercent, 100)),
        })));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "gamma").Position = P(5500, 5000);
            H(ctx.State, "beta").Position = P(5900, 5000);
            H(ctx.State, "delta").Position = P(5000, 5900);
            H(ctx.State, "gamma").Hp = Fixed.FromInt(1000);
        });
        return kernel;
    }

    private static Fixed Lost(MatchState state, string hero) => Fixed.FromInt(5000) - H(state, hero).Hp;

    [Fact]
    public void TheBeam_HurtsEnemiesAndHealsAlliesOnIt_PhoenixPaying()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => H(ctx.State, "alpha").Facing = P(-1, 0));   // facing away: the beam goes where it is cast
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Ray }, 1, H(ctx.State, "alpha"), P(6000, 5000)));

        kernel.RunTicks(2 + 31);   // five pulses, the last one 1 s after the cast

        Assert.Equal(5 * (20 + 50), Lost(kernel.State, "beta").ToDouble(), 0);
        Assert.Equal(0, Lost(kernel.State, "delta").ToDouble(), 0);   // off the beam
        Assert.Equal(1000 + 5 * (10 + 25), H(kernel.State, "gamma").Hp.ToDouble(), 0);
        Assert.Equal(5000 * 0.99 * 0.99 * 0.99 * 0.99 * 0.99, H(kernel.State, "alpha").Hp.ToDouble(), 0);   // not healed by its own beam
    }

    /// <summary>The beam turns towards the target Phoenix attacks, 5° a pulse: a quarter turn takes 18 pulses (3.6 s).</summary>
    [Fact]
    public void TheBeam_TurnsTowardsTheTarget_AtTheTurnRate()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => H(ctx.State, "beta").Position = P(9000, 9000));
        At(kernel, 2, ctx =>
        {
            Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Ray }, 1, H(ctx.State, "alpha"), P(6000, 5000));
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Attack(H(ctx.State, "delta").Id));
        });
        Fixed afterTwoSeconds = Fixed.Zero;
        At(kernel, 2 + 60, ctx => afterTwoSeconds = Lost(ctx.State, "delta"));

        kernel.RunTicks(2 + 150);

        Assert.Equal(0, afterTwoSeconds.ToDouble(), 0);
        Assert.True(Lost(kernel.State, "delta") > Fixed.Zero);
        Assert.Equal(P(5000, 5000), H(kernel.State, "alpha").Position);   // turning in place
    }

    [Fact]
    public void AStun_EndsIt()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Ray }, 1, H(ctx.State, "alpha"), P(6000, 5000)));
        At(kernel, 20, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), ModifierSpec.Status("stun", 100, ModifierFlags.Stunned), H(ctx.State, "beta").Id));
        Fixed atStun = Fixed.Zero;
        At(kernel, 21, ctx => atStun = Lost(ctx.State, "beta"));

        kernel.RunTicks(2 + 60);

        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "sun_ray");
        Assert.Equal(atStun, Lost(kernel.State, "beta"));
    }
}
