using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Curse of the Oldgrowth (Nature's Prophet's Aghanim's Shard; Dota 7.41f data and the official description): enemy heroes around are
/// shown through the fog, and for every tree within 250 of them - Treants count as trees - take damage over time and are slowed.
/// </summary>
public class CurseOfTheOldgrowthTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Curse = EffectDefinition.Area(900, AreaCenter.Caster, TargetTeam.Enemy, true,
        EffectDefinition.Modifier(new ModifierSpec("curse", 7000, moveSpeedPercent: -7, revealed: true, perTreeRadius: 250, damagePerTreePerSecond: 15, periodMs: 1000)));

    private static SimulationKernel<MatchState> Arena(params FixedVector2[] trees)
    {
        const int cells = 100;
        var draft = new RulesetDraft().WithoutWaves();
        draft.Terrain = new MapTerrain(cells, cells, Enumerable.Repeat(true, cells * cells).ToArray(), trees: trees, treeSize: 64);
        draft.Creeps.Add(new CreepType("treant", new UnitStats(500, Fixed.Zero, 300, 0, 0, 0, 1000, 0, 0, 16, visionRange: 0), new Bounty(0, 0, 0)));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 100),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5600, 5000);
            H(ctx.State, "gamma").Position = P(9000, 9000);
            foreach (string id in new[] { "alpha", "beta" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), H(ctx.State, id).Id);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Curse }, 1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));
        return kernel;
    }

    private static Fixed Lost(MatchState state) => Fixed.FromInt(5000) - H(state, "beta").Hp;

    [Fact]
    public void TwoTreesNear_TwiceTheDamageAndSlow_AndHeIsSeenThroughTheFog()
    {
        var kernel = Arena(P(5800, 5000), P(5600, 5200), P(7000, 7000));   // the third is far

        kernel.RunTicks(2 + 3 * 30 + 1);

        Assert.Equal(3 * 2 * 15, Lost(kernel.State).ToDouble(), 0);
        Assert.Equal(300 * 86 / 100, H(kernel.State, "beta").MoveSpeed);
        Assert.True(kernel.State.IsVisibleTo(H(kernel.State, "alpha").Team, H(kernel.State, "beta")));   // 600 away, alpha sees 100
    }

    [Fact]
    public void ATreantCountsAsATree_NoTreesNoCurse()
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            MatchUnit treant = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"), ctx.State.Ruleset.CreepTypes.Single(c => c.Id == "treant"), ctx.Tick.Value, -1);
            treant.Position = P(5700, 5100);
            Modifiers.Apply(ctx, treant, ModifierSpec.Status("hold", -1, ModifierFlags.Rooted | ModifierFlags.Disarmed), treant.Id);
        });
        At(kernel, 40, ctx => ctx.State.Units.Single(u => u.TypeId == "treant").ExpiresTick = ctx.Tick.Value);   // gone after a pulse

        kernel.RunTicks(2 + 3 * 30 + 1);

        Assert.Equal(15, Lost(kernel.State).ToDouble(), 0);
        Assert.Equal(300, H(kernel.State, "beta").MoveSpeed);
    }
}
