using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>Trample (Dota 7.41f data and the official description): for every so many units the Beast travels, enemies around take damage.</summary>
public class TrampleTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Trample = new ModifierSpec("trample", 10000, ModifierFlags.Disarmed, everyDistance: 140,
        onDistance: new[] { EffectDefinition.Area(1000, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.Damage(DamageType.Pure, 30)) });

    private static SimulationKernel<MatchState> Arena(bool walks)
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
            H(ctx.State, "beta").Position = P(5000, 5100);
        });
        At(kernel, 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Trample, H(ctx.State, "alpha").Id));
        if (walks)
            At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Move(P(6000, 5000))));
        return kernel;
    }

    [Theory]
    [InlineData(true, 3)]    // 300 a second for 1.5 s: 450 units, three steps of 140
    [InlineData(false, 0)]   // standing: nothing
    public void EnemiesAround_TakeDamage_ForEveryStepTravelled(bool walks, int pulses)
    {
        var kernel = Arena(walks);

        kernel.RunTicks(3 + 45);

        Assert.Equal(pulses, kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id));
    }
}
