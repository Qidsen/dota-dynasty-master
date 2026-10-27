using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's rune timings (Fandom «Runes», 2025-09; Valve 7.40): bounty runes at 0:00 at all four spots (bounty and power spots),
/// then every 4 minutes at the bounty spots, never gone, 40 + 6 per 4 minutes passed when they appeared to every hero of the
/// team; water runes at 2:00 and 4:00 at the power spots (+40 health, +80 mana); power runes from 6:00 every 2 minutes at one
/// random power spot, no type twice in a cycle nor twice in a row, gone when the next comes. Seconds for minutes here.
/// </summary>
public class DotaRuneTests
{
    private static readonly FixedVector2 PowerTop = P(3000, 7000), PowerBottom = P(7000, 3000), BountyDawn = P(2600, 4300), BountyDusk = P(7400, 5700);

    private static readonly RuneType Bounty = new("bounty", teamGold: 40, teamGoldPerInterval: 6, teamGoldIntervalMs: 4000);
    private static readonly RuneType Water = new("water", effects: new[] { EffectDefinition.Heal(40), EffectDefinition.RestoreMana(80) });

    private static RuneType Power(string id) => new(id, new ModifierSpec(id, 10000, damageBonus: 10));

    private static SimulationKernel<MatchState> Arena(Action<SimulationContext<MatchState>>? setup = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        var powers = new[] { Power("haste"), Power("arcane"), Power("shield") };
        draft.Runes = new RuneRules(Array.Empty<FixedVector2>(), 0, 1000, Array.Empty<RuneType>(), pickupRadius: 100, schedules: new[]
        {
            new RuneSchedule("bounty_first", new[] { PowerTop, PowerBottom, BountyDawn, BountyDusk }, 0, 0, 0, new[] { Bounty }, RunePlacement.EverySpot, noRepeat: false, stay: true),
            new RuneSchedule("bounty", new[] { BountyDawn, BountyDusk }, 4000, 4000, 0, new[] { Bounty }, RunePlacement.EverySpot, noRepeat: false, stay: true),
            new RuneSchedule("water", new[] { PowerTop, PowerBottom }, 2000, 2000, 4000, new[] { Water }, RunePlacement.EverySpot, noRepeat: false, stay: false),
            new RuneSchedule("power", new[] { PowerTop, PowerBottom }, 6000, 2000, 0, powers, RunePlacement.OneRandomSpot, noRepeat: true, stay: false),
        });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 9500);
            H(ctx.State, "gamma").Position = P(5000, 9300);
            H(ctx.State, "beta").Position = P(5000, 500);
            setup?.Invoke(ctx);
        }));
        return kernel;
    }

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static List<(long Second, string Type, FixedVector2 At)> Spawns(SimulationKernel<MatchState> kernel) =>
        kernel.TimedEventsOf<RuneSpawned>().Select(e => (e.Tick / 30, e.Event.RuneTypeId, e.Event.Position)).ToList();

    [Fact]
    public void Runes_ComeOnTheirTimings_AtTheirSpots()
    {
        var kernel = Arena();
        kernel.RunTicks(30 * 16 + 1);
        var spawns = Spawns(kernel);

        Assert.Equal(4, spawns.Count(s => s.Second == 0 && s.Type == "bounty"));
        Assert.Equal(new long[] { 4, 8, 12, 16 }, spawns.Where(s => s.Second > 0 && s.Type == "bounty").Select(s => s.Second).Distinct().ToArray());
        Assert.True(spawns.Where(s => s.Type == "bounty" && s.Second > 0).All(s => s.At == BountyDawn || s.At == BountyDusk));
        Assert.Equal(new long[] { 2, 2, 4, 4 }, spawns.Where(s => s.Type == "water").Select(s => s.Second).ToArray());
        var power = spawns.Where(s => s.Type != "bounty" && s.Type != "water").ToList();
        Assert.Equal(new long[] { 6, 8, 10, 12, 14, 16 }, power.Select(s => s.Second).ToArray());   // one at a time
        Assert.True(power.All(s => s.At == PowerTop || s.At == PowerBottom));
        Assert.Equal(3, power.Take(3).Select(s => s.Type).Distinct().Count());   // a cycle without a repeat
        Assert.Equal(3, power.Skip(3).Take(3).Select(s => s.Type).Distinct().Count());
        Assert.True(power.Zip(power.Skip(1), (a, b) => a.Type != b.Type).All(x => x), "never twice in a row");
    }

    [Fact]
    public void Runes_NotTaken_AreGoneWhenTheNextCome_ExceptBountyRunes()
    {
        var kernel = Arena();
        kernel.RunTicks(30 * 12 + 1);
        var runes = kernel.State.Units.Where(u => u.Kind == UnitKind.Rune).Select(u => u.TypeId).ToList();

        Assert.Equal(1, runes.Count(t => t != "bounty" && t != "water"));   // only the latest power rune
        Assert.Equal(0, runes.Count(t => t == "water"));                     // the 4 s ones gone when the power runes came at 6 s
        Assert.Equal(4 + 2 * 3, runes.Count(t => t == "bounty"));           // all stay
    }

    [Fact]
    public void ABountyRune_GivesEveryHeroOfTheTeam_GoldByWhenItAppeared()
    {
        double before = 0, gammaBefore = 0, betaBefore = 0;
        var kernel = Arena();
        kernel.AddSystem(new LambdaSystem(30 * 8 + 5, ctx =>   // the 8 s bounty runes: 40 + 2 × 6
        {
            before = H(ctx.State, "alpha").Hero!.Gold.ToDouble();
            gammaBefore = H(ctx.State, "gamma").Hero!.Gold.ToDouble();
            betaBefore = H(ctx.State, "beta").Hero!.Gold.ToDouble();
            foreach (MatchUnit old in ctx.State.Units.Where(u => u.Kind == UnitKind.Rune && u.Position == BountyDawn && u.RuneSpawnTick < 30 * 8).ToList())
                ctx.State.Remove(old);   // only the newest at the spot
            H(ctx.State, "alpha").Position = BountyDawn;
        }));
        kernel.RunTicks(30 * 8 + 7);

        Assert.Equal(52, System.Math.Floor(H(kernel.State, "alpha").Hero!.Gold.ToDouble() - before));
        Assert.Equal(52, System.Math.Floor(H(kernel.State, "gamma").Hero!.Gold.ToDouble() - gammaBefore));
        Assert.Equal(0, System.Math.Floor(H(kernel.State, "beta").Hero!.Gold.ToDouble() - betaBefore));
    }

    [Fact]
    public void AWaterRune_RestoresHealthAndMana()
    {
        int hp = 0, mana = 0;
        var kernel = Arena();
        kernel.AddSystem(new LambdaSystem(30 * 2 + 2, ctx =>
        {
            MatchUnit alpha = H(ctx.State, "alpha");
            alpha.Hp = Fixed.FromInt(100);
            alpha.Hero!.Mana = Fixed.FromInt(10);
            alpha.Position = PowerTop;
        }));
        kernel.AddSystem(new LambdaSystem(30 * 2 + 4, ctx =>
        {
            hp = H(ctx.State, "alpha").Hp.ToIntFloor();
            mana = H(ctx.State, "alpha").Hero!.Mana.ToIntFloor();
        }));
        kernel.RunTicks(30 * 2 + 5);

        Assert.InRange(hp, 140, 142);     // + a little regeneration
        Assert.InRange(mana, 90, 92);
    }

    [Fact]
    public void PowerRunes_NeverRepeatWithinACycle_OverManyCycles()
    {
        var kernel = Arena();
        kernel.RunTicks(30 * 60 + 1);   // 28 power runes: 9 full cycles of three
        var power = Spawns(kernel).Where(s => s.Type != "bounty" && s.Type != "water").Select(s => s.Type).ToList();

        for (int cycle = 0; cycle + 3 <= power.Count; cycle += 3)
            Assert.Equal(3, power.Skip(cycle).Take(3).Distinct().Count());
    }
}
