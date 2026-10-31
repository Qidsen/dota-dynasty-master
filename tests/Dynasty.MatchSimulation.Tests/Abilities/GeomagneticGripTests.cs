using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Geomagnetic Grip on an ally (Dota 7.41f data and official notes): the ally is pulled to Earth Spirit and the enemies on its way are
/// silenced; those off the way are not.
/// </summary>
public class GeomagneticGripTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition[] Grip =
    {
        EffectDefinition.LineArea(180, TargetTeam.Enemy, EffectDefinition.Modifier(ModifierSpec.Status("grip_silence", 2000, ModifierFlags.Silenced))),
        EffectDefinition.Pull(AreaCenter.Caster, 1400),
    };

    [Fact]
    public void TheAlly_IsPulledIn_AndEnemiesOnItsWayAreSilenced()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "gamma", "beta", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "gamma").Position = P(5700, 5000);
            H(ctx.State, "beta").Position = P(5350, 5100);    // on the way
            H(ctx.State, "delta").Position = P(5350, 5500);   // off it
            foreach (string id in new[] { "beta", "delta", "gamma" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted), H(ctx.State, id).Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Grip, 1, H(ctx.State, "gamma"), H(ctx.State, "gamma").Position));

        kernel.RunTicks(4);

        Assert.True(FixedVector2.Distance(H(kernel.State, "gamma").Position, P(5000, 5000)) < Fixed.FromInt(60));
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "grip_silence");
        Assert.DoesNotContain(H(kernel.State, "delta").Modifiers, m => m.Spec.Key == "grip_silence");
        Assert.DoesNotContain(H(kernel.State, "gamma").Modifiers, m => m.Spec.Key == "grip_silence");
    }
}
