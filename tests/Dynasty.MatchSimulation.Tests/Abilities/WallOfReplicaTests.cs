using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Wall of Replica (Dota 7.41f data and official notes): a wall across the way from Dark Seer to the point; an enemy hero touching it is
/// damaged, slowed and gets a replica fighting for Dark Seer - once per wall; heroes off the wall are untouched.
/// </summary>
public class WallOfReplicaTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Wall = EffectDefinition.Summon("wall", 30000, true, new[] { 1 }, Array.Empty<int>(),
        EffectDefinition.Modifier(new ModifierSpec("wall", -1, ModifierFlags.Invulnerable | ModifierFlags.Rooted, dispellable: false, wallHalfLength: 650,
            trampleMarker: "wall_struck",
            onTrample: new[]
            {
                EffectDefinition.IfHero(
                    EffectDefinition.Damage(DamageType.Pure, 40),
                    EffectDefinition.Modifier(new ModifierSpec("wall_slow", 1000, moveSpeedPercent: -40)),
                    EffectDefinition.Modifier(new ModifierSpec("wall_struck", 30000, dispellable: false)),
                    EffectDefinition.Illusion(30000, new[] { new ModifierSpec("replica", -1, attackDamagePercent: -20, incomingDamagePercent: 100) }, 1)),
            })));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("wall", new UnitStats(1, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16, visionRange: 0), new Bounty(0, 0, 0)));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5800, 5500);    // on the wall: it runs north-south through (5800, 5000)
            H(ctx.State, "gamma").Position = P(6200, 5000);   // beyond it, off it
            foreach (string id in new[] { "alpha", "beta", "gamma" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Wall }, 1, H(ctx.State, "alpha"), P(5800, 5000)));
        return kernel;
    }

    private static List<MatchUnit> Replicas(MatchState state) => state.Units.Where(u => u.IsAlive && u.IllusionOf.IsValid).ToList();

    [Fact]
    public void AnEnemyHeroOnTheWall_IsDamagedSlowedAndReplicated_Once_ForDarkSeer()
    {
        var kernel = Arena();

        kernel.RunTicks(2 + 30);

        double fromWall = kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, "beta").Id).Sum(d => d.Amount.ToDouble());
        Assert.Equal(40, fromWall, 0);   // once, though still on it (the replica's own attacks aside)
        Assert.Contains(H(kernel.State, "beta").Modifiers, m => m.Spec.Key == "wall_slow");
        MatchUnit replica = Assert.Single(Replicas(kernel.State));
        Assert.Equal(H(kernel.State, "beta").Id, replica.IllusionOf);
        Assert.Equal(H(kernel.State, "alpha").Team, replica.Team);
        Assert.Equal(5000, H(kernel.State, "gamma").Hp.ToDouble(), 0);
    }
}
