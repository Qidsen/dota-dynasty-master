using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Spotlight (Ringmaster's Aghanim's Shard; Dota 7.41f data and the official description): enemies in the light miss attacks by a chance
/// and are seen; illusions in it fade, losing a share of their max health a second; heroes are not burnt.
/// </summary>
public class SpotlightTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Light = EffectDefinition.Zone(450, AreaCenter.Point, TargetTeam.Enemy, 8000, 0, false,
        EffectDefinition.Modifier(new ModifierSpec("spotlight", 300, missChanceHundredths: 3000, revealed: true)),
        EffectDefinition.IfIllusion(EffectDefinition.Modifier(new ModifierSpec("spotlight_fade", 300, damagePerSecondMaxHealthPercent: Fixed.FromInt(30),
            damageType: DamageType.Pure, periodMs: 100))));

    [Fact]
    public void EnemiesInTheLight_MissAndAreSeen_IllusionsFade_HeroesDoNot()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(1000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 100),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        MatchUnit? image = null;
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(6000, 5000);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, "beta").Id);
        });
        At(kernel, 1, ctx =>
        {
            image = ctx.State.SpawnIllusion(ctx.Ids.Allocate(), H(ctx.State, "beta"), H(ctx.State, "beta"), ctx.Tick.Value, -1);
            image.Position = P(6100, 5000);
            Modifiers.Apply(ctx, image, ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), image.Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Light }, 1, H(ctx.State, "alpha"), P(6000, 5000)));

        kernel.RunTicks(2 + 30 + 2);

        MatchUnit beta = H(kernel.State, "beta");
        Assert.Equal(3000, beta.MissChanceHundredths);
        Assert.True(kernel.State.IsVisibleTo(H(kernel.State, "alpha").Team, beta));
        Assert.Equal(1000, beta.Hp.ToDouble(), 0);
        Assert.InRange(image!.Hp.ToDouble(), 600, 750);   // about 30% of 1000 in a second
    }
}
