using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Exorcism (Dota 7.41f data and the official description; the project owner: each spirit strikes on a timer of its round trip): every
/// period each spirit strikes a random enemy unit or building the caster's team sees around; buildings take half; at the end the caster
/// heals by a share of the spirits' damage.
/// </summary>
public class ExorcismTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Three spirits striking every second for 3 s, 40 each, half to buildings; a quarter of it back at the end.</summary>
    private static readonly ModifierSpec Exorcism = new ModifierSpec("exorcism", 3000, periodMs: 1000, periodicFromBearer: true, healAtEndPercent: 25,
        periodicEffects: new[] { EffectDefinition.RandomStrike(700, TargetTeam.Enemy, 3, true, EffectDefinition.Damage(DamageType.Pure, 40).WithStructurePercent(50)) });

    private static SimulationKernel<MatchState> Arena(FixedVector2 beta)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = beta;
            H(ctx.State, "alpha").Hp = Fixed.FromInt(4000);
        });
        return kernel;
    }

    private static List<DamageDealt> Strikes(SimulationKernel<MatchState> kernel) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();

    [Fact]
    public void EverySpirit_StrikesEveryPeriod_AndTheCasterHealsAtTheEnd()
    {
        var kernel = Arena(P(5300, 5000));
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Exorcism, H(ctx.State, "alpha").Id));
        // Her own spell meanwhile: not the spirits' damage, not healed back.
        At(kernel, 2 + 45, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Damage(DamageType.Pure, 200) }, 1, H(ctx.State, "beta"), P(0, 0)));
        double before = 0;
        At(kernel, 2 + 3 * 30 - 2, ctx => before = H(ctx.State, "alpha").Hp.ToDouble());

        kernel.RunTicks(2 + 3 * 30 + 3);

        List<DamageDealt> strikes = Strikes(kernel);
        Assert.Equal(10, strikes.Count);   // three spirits for three seconds, and her own spell
        Assert.Equal(4000, before, 0);   // not before the end
        Assert.Equal(4000 + 9 * 40 / 4, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }

    [Fact]
    public void Buildings_TakeHalf()
    {
        var kernel = Arena(P(9000, 9000));
        MatchUnit? tower = null;
        At(kernel, 1, ctx =>
        {
            tower = ctx.State.Units.Where(u => u.Kind == UnitKind.Structure && u.Team != H(ctx.State, "alpha").Team && ctx.State.IsVulnerable(u))
                .OrderBy(u => u.Id.Value).First();
            H(ctx.State, "alpha").Position = tower.Position + FixedVector2.FromInts(300, 0);
        });
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Exorcism, H(ctx.State, "alpha").Id));

        kernel.RunTicks(2 + 35);

        List<DamageDealt> strikes = Strikes(kernel);
        Assert.NotEmpty(strikes);
        Assert.All(strikes, s => Assert.Equal(tower!.Id, s.Target));
        Assert.All(strikes, s => Assert.Equal(20, s.Amount.ToDouble(), 0));
    }
}
