using Dynasty.Core.Kernel;
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
}
