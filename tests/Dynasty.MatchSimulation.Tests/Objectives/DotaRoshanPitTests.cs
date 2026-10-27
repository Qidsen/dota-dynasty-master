using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Navigation;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's two Roshan pits (Valve 7.38, 7.41; the project owner on 2026-10-28: «every 5 minutes, from the very start»): he lives in
/// the top pit by day and the bottom one by night, walks to the other at every change of day and night along a path around the
/// terrain, and respawns in the pit of the time. Here day and night last 30 s; the pits are on either side of a wall.
/// </summary>
public class DotaRoshanPitTests
{
    private static readonly FixedVector2 DayPit = P(3000, 2500);
    private static readonly FixedVector2 NightPit = P(7000, 2500);

    private static RulesetDraft TwoPits(int respawnMs = 480000)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.DayNight = new DayNightRules(dayMs: 30000, nightMs: 30000);
        draft.Obstacles.Add(ObstacleDefinition.Rectangle("wall", P(4900, 0), P(5100, 4000)));
        draft.Creeps.Add(new CreepType("roshan", new UnitStats(6000, Fixed.FromInt(30), 270, 75, 75, 150, 2000, 600, 150, 60, visionRange: 1400)));
        draft.Boss = new BossDefinition("roshan", "roshan", DayPit, leashRadius: 600, firstSpawnMs: 0, respawnMinMs: respawnMs, respawnMaxMs: respawnMs,
            teamGold: 0, reward: null, provokeRadius: 0, nightPosition: NightPit);
        return draft;
    }

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Roshan's position every tick.</summary>
    private sealed class Tracker : ISimulationSystem<MatchState>
    {
        public List<FixedVector2> Positions { get; } = new();

        public string Name => "test-tracker";

        public void Tick(SimulationContext<MatchState> context)
        {
            if (context.State.Boss != null)
                Positions.Add(context.State.Boss.Position);
        }
    }

    [Fact]
    public void Roshan_WalksAroundTheTerrainToTheNightPit_AndBackByDay()
    {
        var kernel = TwoPits().StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        var tracker = new Tracker();
        kernel.AddSystem(tracker);
        FixedVector2 atDusk = default, atNight = default;
        At(kernel, 30 * 29, ctx => atDusk = ctx.State.Boss!.Position);
        At(kernel, 30 * 59, ctx => atNight = ctx.State.Boss!.Position);

        kernel.RunTicks(30 * 89);

        Assert.True(FixedVector2.Distance(atDusk, DayPit) < Fixed.FromInt(100), "by day in the top pit: " + atDusk);
        Assert.True(FixedVector2.Distance(atNight, NightPit) < Fixed.FromInt(100), "by night in the bottom pit: " + atNight);
        Assert.True(FixedVector2.Distance(kernel.State.Boss!.Position, DayPit) < Fixed.FromInt(100), "back by day: " + kernel.State.Boss.Position);
        NavGrid grid = NavGrid.FromMap(kernel.State.Ruleset.Map);
        Assert.All(tracker.Positions, p => Assert.True(grid.IsWalkable(p), "walked through the wall at " + p));
    }

    [Fact]
    public void Roshan_RespawnsInThePitOfTheTime()
    {
        var kernel = TwoPits(respawnMs: 5000).StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 30 * 32, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, ctx.State.Boss!, Fixed.FromInt(99999), DamageType.Pure));

        kernel.RunTicks(30 * 38 + 2);   // dead at 32 s, back at 37 s: night

        Assert.Equal(NightPit, kernel.State.Boss!.Position);
    }

    [Fact]
    public void Validation_RejectsANightPitOutsideTheMap_AndTheNightPitIsPartOfTheIdentity()
    {
        var outside = TwoPits();
        outside.Boss = new BossDefinition("roshan", "roshan", DayPit, 600, 0, 1000, 1000, 0, null, nightPosition: P(20000, 2500));
        var onePit = TwoPits();
        onePit.Boss = new BossDefinition("roshan", "roshan", DayPit, 600, 0, 480000, 480000, 0, null, provokeRadius: 0);

        Assert.Contains(outside.Build().Validate(), e => e.Contains("night"));
        Assert.NotEqual(onePit.Build().ToDescriptor().Fingerprint, TwoPits().Build().ToDescriptor().Fingerprint);
    }
}
