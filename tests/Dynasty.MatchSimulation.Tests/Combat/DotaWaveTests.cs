using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Tests.Abilities;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Dota 7.41f lane waves (Liquipedia «Lane Creeps», updated for 7.41): every 30 s 3 melee and 1 ranged; a siege creep from
/// the 11th wave on every 10th; a flagbearer from the 5th wave on every 2nd, in place of a melee creep; more creeps from set
/// times (+1 melee at 15:00, 30:00 and 45:00, +1 ranged at 40:00, +1 siege at 30:00 and 60:00). Here with a 1 s interval.
/// </summary>
public class DotaWaveTests
{
    private static SimulationKernel<MatchState> Lanes(params WaveEntry[] wave)
    {
        var draft = new RulesetDraft { IntervalMs = 1000 };
        draft.Creeps.Add(new CreepType("ranged", MeleeStats(), new Bounty(40, 40, 60)));
        draft.Creeps.Add(new CreepType("siege", MeleeStats(), new Bounty(40, 40, 60)));
        draft.Creeps.Add(new CreepType("flagbearer", MeleeStats(), new Bounty(40, 40, 60)));
        draft.DawnWave.Clear();
        draft.DuskWave.Clear();
        draft.DawnWave.AddRange(wave);
        draft.DuskWave.AddRange(wave);
        return draft.Start();
    }

    /// <summary>Dawn's creeps of each type spawned in the wave that came at the given second (wave 1 at 0 s).</summary>
    private static Dictionary<string, int> WaveAt(SimulationKernel<MatchState> kernel, int second) =>
        kernel.TimedEventsOf<CreepSpawned>()
            .Where(e => e.Tick == second * 30L && e.Event.Team == Team.Dawn)
            .GroupBy(e => e.Event.CreepTypeId).ToDictionary(g => g.Key, g => g.Count());

    [Fact]
    public void Entries_StartAtAWave_ComeEveryNthWave_AndJoinFromATime()
    {
        var kernel = Lanes(
            new WaveEntry("melee", 3),
            new WaveEntry("ranged", 1),
            new WaveEntry("siege", 1, firstWave: 11, everyWaves: 10),
            new WaveEntry("melee", 1, fromMs: 5000),
            new WaveEntry("siege", 1, firstWave: 11, everyWaves: 10, fromMs: 15000));
        kernel.RunTicks(30 * 25);

        Assert.Equal(new Dictionary<string, int> { ["melee"] = 3, ["ranged"] = 1 }, WaveAt(kernel, 0));                  // wave 1
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1 }, WaveAt(kernel, 5));                  // wave 6, from 5 s
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1, ["siege"] = 1 }, WaveAt(kernel, 10));  // wave 11
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1 }, WaveAt(kernel, 11));
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 4, ["ranged"] = 1, ["siege"] = 2 }, WaveAt(kernel, 20));  // wave 21, from 15 s
    }

    [Fact]
    public void AFlagbearer_TakesTheplaceOfAMeleeCreep()
    {
        var kernel = Lanes(
            new WaveEntry("melee", 3),
            new WaveEntry("ranged", 1),
            new WaveEntry("flagbearer", 1, firstWave: 5, everyWaves: 2, replacesCreepTypeId: "melee"));
        kernel.RunTicks(30 * 8);

        Assert.Equal(new Dictionary<string, int> { ["melee"] = 3, ["ranged"] = 1 }, WaveAt(kernel, 3));                       // wave 4
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 2, ["ranged"] = 1, ["flagbearer"] = 1 }, WaveAt(kernel, 4));   // wave 5
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 3, ["ranged"] = 1 }, WaveAt(kernel, 5));
        Assert.Equal(new Dictionary<string, int> { ["melee"] = 2, ["ranged"] = 1, ["flagbearer"] = 1 }, WaveAt(kernel, 6));   // wave 7
    }

    /// <summary>
    /// Every 7:30 lane creeps get stronger (Liquipedia «Lane Creeps», 7.41): a creep spawned after n cycles has n times its
    /// type's health and damage step (at most 30 times) and n times its gold and XP step (no limit; super melee +1.5 gold).
    /// Here a cycle is 2 s and the stats stop after 2 cycles.
    /// </summary>
    private static SimulationKernel<MatchState> Upgrading()
    {
        var draft = TestAbilities.Draft();   // without waves: one melee creep a side
        draft.IntervalMs = 1000;
        draft.DawnWave.Add(new WaveEntry("melee", 1));
        draft.DuskWave.Add(new WaveEntry("melee", 1));
        draft.CreepUpgrades = new CreepUpgradeRules(intervalMs: 2000, maxStatCycles: 2, new[]
        {
            new CreepUpgradeStep("melee", health: 12, damage: 1, goldHundredths: 150, xp: 8),
        });
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(5000, 9000));
        return kernel;
    }

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    [Fact]
    public void CreepsSpawnedLater_HaveMoreHealthAndDamage_UpToTheLimit_AndMoreBounty()
    {
        var kernel = Upgrading();
        var spawned = new Dictionary<int, MatchUnit>();
        var spawnHp = new Dictionary<int, int>();
        foreach (int second in new[] { 0, 2, 4, 6 })
            At(kernel, second * 30L + 1, ctx =>
            {
                spawned[second] = ctx.State.Units.Last(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk);
                spawnHp[second] = spawned[second].Hp.ToIntFloor();
            });
        int lastHitGold = 0;
        int xpBefore = 0;
        At(kernel, 6 * 30 + 2, ctx =>
        {
            H(ctx.State, "alpha").Position = spawned[6].Position;   // within the XP radius
            xpBefore = H(ctx.State, "alpha").Hero!.Xp;
            Dynasty.MatchSimulation.Combat.Damage.Deal(ctx, H(ctx.State, "alpha").Id, spawned[6], Dynasty.Core.Mathematics.Fixed.FromInt(99999), Dynasty.MatchSimulation.Combat.DamageType.Pure);
        });
        kernel.RunTicks(6 * 30 + 5);
        lastHitGold = kernel.EventsOf<GoldEarned>().Last(g => g.Reason == "last_hit").Amount;

        int baseHp = spawned[0].MaxHp, baseDamage = spawned[0].AttackDamageMin;
        Assert.Equal(new[] { baseHp, baseHp + 12, baseHp + 24, baseHp + 24 }, new[] { 0, 2, 4, 6 }.Select(t => spawned[t].MaxHp).ToArray());
        Assert.Equal(new[] { baseDamage, baseDamage + 1, baseDamage + 2, baseDamage + 2 }, new[] { 0, 2, 4, 6 }.Select(t => spawned[t].AttackDamageMin).ToArray());
        Assert.Equal(spawned[4].MaxHp, spawnHp[4]);   // spawned at full health
        Assert.Equal(40 + 4, lastHitGold);                                  // 40 + 3 × 1.5 = 44.5
        Assert.Equal(60 + 24, H(kernel.State, "alpha").Hero!.Xp - xpBefore);   // the type's 60 + 3 × 8
    }
}
