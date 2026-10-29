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
/// Proximity mines (Dota 7.41f data and official notes on Proximity Mines): a mine goes off when an enemy has stood within its radius
/// for the threshold; the explosion deals full damage within the inner radius, falling to a share at the edge; the mine is gone.
/// </summary>
public class ProximityTests
{
    private static readonly FixedVector2 MineAt = P(5000, 5000);

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>400 pure, full within 150, half at the 500 edge.</summary>
    private static readonly ModifierSpec Mine = new ModifierSpec("land_mine", -1, proximityRadius: 500, proximityDelayMs: 1000,
        onProximity: new[] { EffectDefinition.Area(500, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.FalloffDamage(DamageType.Pure, 150, 500, 50, 400)) });

    /// <summary>alpha's mine at the centre; beta (enemy) and gamma (alpha's ally) stand where given.</summary>
    private static SimulationKernel<MatchState> Field(FixedVector2 beta, FixedVector2 gamma, Action<MatchUnit>? onMine = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("mine", new UnitStats(20, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 16), new Bounty(15, 15, 0)));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(2000, 2000);
            H(ctx.State, "beta").Position = beta;
            H(ctx.State, "gamma").Position = gamma;
            MatchUnit mine = ctx.State.SpawnSummon(ctx.Ids.Allocate(), H(ctx.State, "alpha"), ctx.State.Ruleset.FindCreepType("mine")!, ctx.Tick.Value, -1);
            mine.Position = MineAt;
            Modifiers.Apply(ctx, mine, Mine, H(ctx.State, "alpha").Id);
            onMine?.Invoke(mine);
        });
        return kernel;
    }

    private static Fixed Lost(MatchUnit unit) => Fixed.FromInt(2000) - unit.Hp;

    private static bool MineStands(MatchState state) => state.Units.Any(u => u.TypeId == "mine" && u.IsAlive);

    [Theory]
    [InlineData(100, 400)]   // within the full-damage radius
    [InlineData(150, 400)]
    [InlineData(325, 300)]   // half way from 150 to 500: 75%
    [InlineData(490, 206)]   // near the edge: 400 * (100 - 50 * 340 / 350)%
    public void Mine_GoesOff_AfterAnEnemyStoodNearItForTheThreshold_DamageFallingWithDistance(int distance, int expected)
    {
        var kernel = Field(MineAt + FixedVector2.FromInts(distance, 0), P(1000, 1000));

        kernel.RunTicks(25);
        Assert.True(MineStands(kernel.State));   // not yet: under a second
        Assert.Equal(0, Lost(H(kernel.State, "beta")).ToDouble(), 0);

        kernel.RunTicks(10);
        Assert.False(MineStands(kernel.State));
        Assert.Equal(expected, Lost(H(kernel.State, "beta")).ToDouble(), 0);
    }

    [Fact]
    public void Mine_DoesNotGoOff_ForAnAlly()
    {
        var kernel = Field(P(9000, 9000), MineAt + FixedVector2.FromInts(100, 0));

        kernel.RunTicks(3 * 30);

        Assert.True(MineStands(kernel.State));
        Assert.Equal(0, Lost(H(kernel.State, "gamma")).ToDouble(), 0);
    }

    [Fact]
    public void Mine_Threshold_StartsAgain_WhenTheEnemyLeaves()
    {
        var kernel = Field(MineAt + FixedVector2.FromInts(300, 0), P(1000, 1000));
        At(kernel, 20, ctx => H(ctx.State, "beta").Position = MineAt + FixedVector2.FromInts(900, 0));   // out after 2/3 of a second
        At(kernel, 25, ctx => H(ctx.State, "beta").Position = MineAt + FixedVector2.FromInts(300, 0));   // back

        kernel.RunTicks(45);   // 2/3 + 2/3 of a second within, never a whole second at once
        Assert.True(MineStands(kernel.State));

        kernel.RunTicks(20);
        Assert.False(MineStands(kernel.State));
    }
}
