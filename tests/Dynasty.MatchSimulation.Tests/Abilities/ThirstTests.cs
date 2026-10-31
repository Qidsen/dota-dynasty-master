using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Thirst (Dota 7.41f data and official notes): movement speed for every enemy hero below full health, growing to the most at 25%,
/// adding up over the heroes, above the usual speed limit; not for illusions; enemy heroes below 25% are seen, invisible or not.
/// </summary>
public class ThirstTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Thirst = new ModifierSpec("thirst", -1, thirstMoveSpeedPercent: 40, thirstFullBelowHealthPercent: 25, revealsBelowHealthPercent: 25);

    private static SimulationKernel<MatchState> Arena(int alphaSpeed = 300)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(1000, Fixed.Zero, id == "alpha" ? alphaSpeed : 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(2000, 2000);
            H(ctx.State, "gamma").Position = P(2000, 2400);
            H(ctx.State, "beta").Position = P(9000, 9000);   // far out of sight
            H(ctx.State, "delta").Position = P(9000, 8000);
        });
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), Thirst, H(ctx.State, "alpha").Id));
        return kernel;
    }

    [Theory]
    [InlineData(1000, 1000, 300, 300)]   // nobody hurt
    [InlineData(625, 1000, 300, 360)]    // half way to 25%: half of 40%
    [InlineData(250, 250, 300, 540)]     // two at 25%: 40% each
    [InlineData(100, 100, 400, 720)]     // above the usual 550 limit
    public void MoveSpeed_GrowsWithTheEnemyHeroesHealthMissing(int betaHp, int deltaHp, int baseSpeed, int speed)
    {
        var kernel = Arena(baseSpeed);
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(betaHp);
            H(ctx.State, "delta").Hp = Fixed.FromInt(deltaHp);
        });

        kernel.RunTicks(5);

        Assert.Equal(speed, H(kernel.State, "alpha").MoveSpeed);
    }

    [Fact]
    public void Illusions_DoNotCount()
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            MatchUnit image = ctx.State.SpawnIllusion(ctx.Ids.Allocate(), H(ctx.State, "beta"), H(ctx.State, "beta"), ctx.Tick.Value, ctx.Tick.Value + 3000);
            image.Hp = Fixed.FromInt(100);
        });

        kernel.RunTicks(5);

        Assert.Equal(300, H(kernel.State, "alpha").MoveSpeed);
    }

    [Theory]
    [InlineData(200, true)]    // below 25%: seen across the map, invisible or not
    [InlineData(300, false)]
    public void EnemyHeroes_BelowTheThreshold_AreSeen(int betaHp, bool seen)
    {
        var kernel = Arena();
        At(kernel, 2, ctx =>
        {
            H(ctx.State, "beta").Hp = Fixed.FromInt(betaHp);
            Modifiers.Apply(ctx, H(ctx.State, "beta"), ModifierSpec.Status("invisible", -1, ModifierFlags.Invisible), H(ctx.State, "beta").Id);
        });

        kernel.RunTicks(10);

        Assert.Equal(seen, kernel.State.IsVisibleTo(H(kernel.State, "alpha").Team, H(kernel.State, "beta")));
    }

    [Fact]
    public void AnIllusionBelowTheThreshold_IsNotSeen()
    {
        var kernel = Arena();
        MatchUnit? image = null;
        At(kernel, 2, ctx =>
        {
            image = ctx.State.SpawnIllusion(ctx.Ids.Allocate(), H(ctx.State, "beta"), H(ctx.State, "beta"), ctx.Tick.Value, ctx.Tick.Value + 3000);
            image.Position = P(9000, 7000);
            image.Hp = Fixed.FromInt(50);
        });

        kernel.RunTicks(10);

        Assert.False(kernel.State.IsVisibleTo(H(kernel.State, "alpha").Team, image!));
    }
}
