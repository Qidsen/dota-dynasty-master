using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Ball Lightning (Dota 7.41f data and the official description): a further share of the max mana to start, then a cost for every 100
/// units travelled (flat plus a share of the max mana); the travel stops where the mana runs out.
/// </summary>
public class BallLightningTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>10% of 1000 max mana to start (100), then 10 + 1% of 1000 (20) for every 100 units.</summary>
    private static readonly EffectDefinition Ball = EffectDefinition.ManaTravel(1000, 10, 100);

    private static SimulationKernel<MatchState> Arena(int mana)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 5000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(mana);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Ball }, 1, H(ctx.State, "alpha"), P(4000, 5000)));
        return kernel;
    }

    [Fact]
    public void TravelsAllTheWay_PayingByTheDistance()
    {
        var kernel = Arena(1000);

        kernel.RunTicks(4);

        Assert.Equal(P(4000, 5000), H(kernel.State, "alpha").Position);
        Assert.Equal(1000 - 100 - 10 * 20, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 0);
    }

    [Fact]
    public void StopsWhereTheManaRunsOut()
    {
        var kernel = Arena(200);   // 100 to start, then 100 more: 500 units

        kernel.RunTicks(4);

        Assert.Equal(3500, H(kernel.State, "alpha").Position.X.ToDouble(), 0);
        Assert.Equal(0, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 0);
    }

    [Fact]
    public void WithoutManaToStart_GoesNowhere()
    {
        var kernel = Arena(50);

        kernel.RunTicks(4);

        Assert.Equal(P(3000, 5000), H(kernel.State, "alpha").Position);
        Assert.Equal(50, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 0);
    }
}
