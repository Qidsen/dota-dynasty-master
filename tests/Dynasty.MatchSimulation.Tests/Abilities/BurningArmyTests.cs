using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>Burning Army (Clinkz's Aghanim's Scepter; Dota 7.41f data): a row of archers at the point, across the way from Clinkz to it.</summary>
public class BurningArmyTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    [Fact]
    public void TheArchers_StandInARowAcrossTheCast_EvenlyOverItsLength()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Creeps.Add(new CreepType("archer", new UnitStats(1000, Fixed.Zero, 0, 5, 5, 500, 1700, 300, 1000, 16, visionRange: 800), new Bounty(20, 20, 20)));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(9000, 9000);
        }));
        kernel.AddSystem(new LambdaSystem(2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Summon("archer", 20000, true, new[] { 4 }, Array.Empty<int>(), Array.Empty<EffectDefinition>()).InRow(900) },
            1, H(ctx.State, "alpha"), P(5600, 5000))));

        kernel.RunTicks(4);

        List<FixedVector2> archers = kernel.State.Units.Where(u => u.TypeId == "archer" && u.IsAlive).Select(u => u.Position).OrderBy(p => p.Y).ToList();
        Assert.Equal(new[] { 4550, 4850, 5150, 5450 }, archers.Select(p => (int)System.Math.Round(p.Y.ToDouble())).ToArray());
        Assert.All(archers, p => Assert.Equal(5600, p.X.ToDouble(), 0));
    }
}
