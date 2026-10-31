using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Stampede (Dota 7.41f data and the official description; the project owner: speed 550, each enemy trampled once a cast): every allied
/// hero runs at the top speed; an enemy coming within the trample radius of any of them takes damage by Centaur's strength and is
/// slowed, once.
/// </summary>
public class StampedeTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Stampede = EffectDefinition.Area(30000, AreaCenter.Caster, TargetTeam.Ally, true, EffectDefinition.Modifier(
        new ModifierSpec("stampede", 4000, fixedMoveSpeed: 550, trampleRadius: 105, trampleMarker: "stampede_trampled", onTrample: new[]
        {
            EffectDefinition.ScaledDamage(DamageType.Pure, DamageScaling.CasterStrengthPercent, 20000),
            EffectDefinition.Modifier(new ModifierSpec("stampede_slow", 3000, moveSpeedPercent: -100)),
            EffectDefinition.Modifier(new ModifierSpec("stampede_trampled", 4000)),
        })));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                attributes: id == "alpha"
                    ? new HeroAttributes(PrimaryAttribute.Strength, strength: 30, agility: 0, intelligence: 0, strengthGain: Fixed.Zero, agilityGain: Fixed.Zero, intelligenceGain: Fixed.Zero)
                    : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(2000, 2000);
            H(ctx.State, "gamma").Position = P(5000, 5000);   // far from Centaur, near beta
            H(ctx.State, "beta").Position = P(5080, 5000);
            H(ctx.State, "delta").Position = P(9000, 9000);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Stampede }, 1, H(ctx.State, "alpha"), P(0, 0)));
        return kernel;
    }

    private static List<DamageDealt> Trampled(SimulationKernel<MatchState> kernel, string hero) =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Target == H(kernel.State, hero).Id && d.Attacker == H(kernel.State, "alpha").Id).ToList();

    [Fact]
    public void EveryAlliedHero_RunsAtTopSpeed()
    {
        var kernel = Arena();

        kernel.RunTicks(4);

        Assert.Equal(550, H(kernel.State, "alpha").MoveSpeed);
        Assert.Equal(550, H(kernel.State, "gamma").MoveSpeed);
        Assert.Equal(300, H(kernel.State, "delta").MoveSpeed);   // not an enemy
    }

    [Fact]
    public void AnEnemyNearAnAlly_IsTrampledOnce_ByCentaursStrength_AndSlowed()
    {
        var kernel = Arena();
        // A second pass: beta goes away and comes back to gamma.
        At(kernel, 30, ctx => H(ctx.State, "beta").Position = P(5600, 5000));
        At(kernel, 40, ctx => H(ctx.State, "beta").Position = P(5080, 5000));

        kernel.RunTicks(60);

        List<DamageDealt> hits = Trampled(kernel, "beta");
        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Single(hits);
        Assert.Equal(alpha.Strength.ToDouble() * 2, hits[0].Amount.ToDouble(), 0);
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "stampede_slow");
        Assert.Empty(Trampled(kernel, "delta"));   // far from everyone
    }

    [Fact]
    public void Allies_AreNotTrampled()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(4950, 5000));   // two stampeding allies side by side

        kernel.RunTicks(10);

        Assert.DoesNotContain(kernel.EventsOf<DamageDealt>(), d => d.Target == H(kernel.State, "gamma").Id);
    }
}
