using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Wukong's Command (Dota 7.41f data and the official description; the project owner: each soldier strikes the nearest enemy hero in its
/// reach): soldiers stand in two rings around the point; every 1.1 s each strikes with Monkey King's attack the nearest enemy hero within
/// its reach, never creeps; when Monkey King leaves the outer ring they are gone.
/// </summary>
public class WukongsCommandTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Soldier = new ModifierSpec("soldier", -1, ModifierFlags.Invulnerable | ModifierFlags.Rooted, dispellable: false,
        endsWithOwnerModifier: "wukong", periodMs: 1100,
        periodicEffects: new[] { EffectDefinition.NearestHeroes(300, AreaCenter.Target, TargetTeam.Enemy, 1, EffectDefinition.Attack(100)) });

    private static EffectDefinition[] Command(int innerCount, int outerCount) => new[]
    {
        EffectDefinition.OnCaster(EffectDefinition.Modifier(new ModifierSpec("wukong", 14000, armorBonus: Fixed.FromInt(20), dispellable: false, anchorRadius: 750))),
        EffectDefinition.Summon("soldier", 14000, true, new[] { innerCount }, Array.Empty<int>(), EffectDefinition.Modifier(Soldier)).InRing(300),
        EffectDefinition.Summon("soldier", 14000, true, new[] { outerCount }, Array.Empty<int>(), EffectDefinition.Modifier(Soldier)).InRing(750),
    };

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("soldier", new UnitStats(1, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16, visionRange: 0), new Bounty(0, 0, 0)));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5000, 5300);   // on the inner ring
            H(ctx.State, "gamma").Position = P(9000, 9000);
            foreach (string id in new[] { "alpha", "beta" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
        });
        return kernel;
    }

    private static List<MatchUnit> Soldiers(MatchState state) => state.Units.Where(u => u.TypeId == "soldier" && u.IsAlive).ToList();

    [Fact]
    public void TheSoldiers_StandInTwoRingsAroundThePoint()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Command(5, 9), 1, H(ctx.State, "alpha"), P(5000, 5000)));

        kernel.RunTicks(4);

        List<double> distances = Soldiers(kernel.State).Select(s => FixedVector2.Distance(s.Position, P(5000, 5000)).ToDouble()).OrderBy(d => d).ToList();
        Assert.Equal(14, distances.Count);
        Assert.All(distances.Take(5), d => Assert.Equal(300, d, 0));
        Assert.All(distances.Skip(5), d => Assert.Equal(750, d, 0));
        Assert.Equal(5, Soldiers(kernel.State).Select(s => s.Position).Take(5).Distinct().Count());   // spread round the ring
    }

    [Fact]
    public void EverySoldierInReach_StrikesTheNearestEnemyHero_WithMonkeyKingsAttack()
    {
        var kernel = Arena();
        MatchUnit? creep = null;
        At(kernel, 1, ctx =>
        {
            creep = ctx.State.SpawnCreep(ctx.Ids.Allocate(), H(ctx.State, "beta").Team, ctx.State.Ruleset.CreepTypes.First(), ctx.State.Ruleset.Map.Lanes.First(), ctx.Tick.Value);
            creep.Position = P(5000, 4700);
            Modifiers.Apply(ctx, creep, ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), creep.Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Command(5, 9), 1, H(ctx.State, "alpha"), P(5000, 5000)));
        int soldiersNearBeta = 0;
        At(kernel, 3, ctx => soldiersNearBeta = Soldiers(ctx.State).Count(s => FixedVector2.Distance(s.Position, H(ctx.State, "beta").Position) <= Fixed.FromInt(300 + 24)));

        kernel.RunTicks(2 + 34);   // the first strikes 1.1 s after the cast

        List<DamageDealt> hits = kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id).ToList();
        Assert.True(soldiersNearBeta >= 1);
        Assert.Equal(soldiersNearBeta, hits.Count(d => d.Target == H(kernel.State, "beta").Id));
        Assert.DoesNotContain(hits, d => d.Target == creep!.Id);
    }

    [Fact]
    public void LeavingTheOuterRing_EndsIt()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), Command(5, 9), 1, H(ctx.State, "alpha"), P(5000, 5000)));
        At(kernel, 10, ctx => H(ctx.State, "alpha").Position = P(6000, 5000));

        kernel.RunTicks(14);

        Assert.Empty(Soldiers(kernel.State));
        Assert.DoesNotContain(H(kernel.State, "alpha").Modifiers, m => m.Spec.Key == "wukong");
    }
}
