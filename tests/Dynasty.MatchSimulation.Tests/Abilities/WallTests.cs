using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Ring walls (Dota 7.41f data and official notes): after the formation time a circular barrier stands for the level's duration;
/// enemies can't pass it either way, by walking or by being pushed; allies and debuff-immune units pass (Kinetic Field, the
/// movement of Arena of Blood).
/// </summary>
public class WallTests
{
    private const int Radius = 350;
    private static readonly FixedVector2 Center = P(5000, 5000);

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>alpha and gamma against beta; beta and gamma stand at <paramref name="start"/> and walk to <paramref name="destination"/> from tick 2.</summary>
    private static SimulationKernel<MatchState> Arena(FixedVector2 start, FixedVector2 destination)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(3000, 3000);
            H(ctx.State, "beta").Position = start;
            H(ctx.State, "gamma").Position = start + FixedVector2.FromInts(0, 40);
        });
        At(kernel, 2, ctx =>
        {
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(destination));
            HeroOrders.Issue(H(ctx.State, "gamma"), HeroOrder.Move(destination));
        });
        return kernel;
    }

    private static void Wall(SimulationKernel<MatchState> kernel, long tick, int formationMs = 0, int durationMs = 10000) =>
        At(kernel, tick, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Wall(Radius, AreaCenter.Point, TargetTeam.Enemy, formationMs, durationMs) },
            1, H(ctx.State, "alpha"), Center));

    private static Fixed FromCenter(MatchUnit unit) => FixedVector2.Distance(unit.Position, Center);

    [Fact]
    public void Enemy_Inside_CannotWalkOut()
    {
        var kernel = Arena(Center, P(5000, 7000));
        Wall(kernel, 1);

        kernel.RunTicks(4 * 30);

        Assert.True(FromCenter(H(kernel.State, "beta")) < Fixed.FromInt(Radius));
        Assert.True(FromCenter(H(kernel.State, "beta")) > Fixed.FromInt(Radius - 30));   // it walked up to the wall
    }

    [Fact]
    public void Enemy_Outside_CannotWalkIn()
    {
        var kernel = Arena(P(5000, 6000), Center);
        Wall(kernel, 1);

        kernel.RunTicks(5 * 30);

        Assert.True(FromCenter(H(kernel.State, "beta")) > Fixed.FromInt(Radius));
    }

    [Fact]
    public void Ally_PassesTheWall()
    {
        var kernel = Arena(Center, P(5000, 7000));
        Wall(kernel, 1);

        kernel.RunTicks(4 * 30);

        Assert.True(FromCenter(H(kernel.State, "gamma")) > Fixed.FromInt(Radius + 100));
    }

    [Fact]
    public void DebuffImmuneEnemy_PassesTheWall()
    {
        var kernel = Arena(Center, P(5000, 7000));
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("bkb", 10000, ModifierFlags.DebuffImmune), H(ctx.State, "beta").Id));
        Wall(kernel, 1);

        kernel.RunTicks(4 * 30);

        Assert.True(FromCenter(H(kernel.State, "beta")) > Fixed.FromInt(Radius + 100));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1000, false)]   // still forming while it walks out
    public void Wall_StandsOnlyAfterItsFormationTime(int formationMs, bool held)
    {
        var kernel = Arena(P(5000, 5300), P(5000, 7000));
        Wall(kernel, 1, formationMs);

        kernel.RunTicks(4 * 30);

        Assert.Equal(held, FromCenter(H(kernel.State, "beta")) < Fixed.FromInt(Radius));
    }

    [Fact]
    public void Wall_FallsAfterItsDuration()
    {
        var kernel = Arena(Center, P(5000, 7000));
        Wall(kernel, 1, durationMs: 1000);

        kernel.RunTicks(4 * 30);

        Assert.True(FromCenter(H(kernel.State, "beta")) > Fixed.FromInt(Radius + 100));
    }

    [Fact]
    public void Enemy_PushedFromInside_StopsAtTheWall()
    {
        var kernel = Arena(Center, Center);
        Wall(kernel, 1);
        At(kernel, 10, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { EffectDefinition.Knockback(1000) }, 1, H(ctx.State, "beta"), Center));

        kernel.RunTicks(12);

        Assert.True(FromCenter(H(kernel.State, "beta")) < Fixed.FromInt(Radius));
    }
}
