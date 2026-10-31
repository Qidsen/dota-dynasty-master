using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Side Gunner (Gyrocopter's Aghanim's Scepter; Dota 7.41f data): every 1.3 s a gunner strikes with Gyrocopter's attack the nearest enemy
/// within his attack range, whatever he does himself; nothing when none is in range.
/// </summary>
public class SideGunnerTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Gunner = new ModifierSpec("side_gunner", -1, dispellable: false, periodMs: 1300, periodicFromBearer: true,
        periodicEffects: new[] { EffectDefinition.InAttackRange(EffectDefinition.Area(0, AreaCenter.Caster, TargetTeam.Enemy, 1, EffectDefinition.Attack(100))) });

    [Theory]
    [InlineData(5400, 2)]   // within his 500: struck at 1.3 and 2.6 s
    [InlineData(5700, 0)]
    public void TheGunner_StrikesTheNearestEnemyInAttackRange_EveryInterval(int betaX, int strikes)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 500, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(betaX, 5000);
            foreach (string id in new[] { "alpha", "beta" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
        });
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Gunner, H(ctx.State, "alpha").Id));

        kernel.RunTicks(1 + 3 * 30);

        Assert.Equal(strikes, kernel.EventsOf<DamageDealt>().Count(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id));
    }
}
