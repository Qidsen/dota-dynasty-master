using System.Diagnostics;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

// Match Laboratory (headless): runs matches with a chosen ruleset and seeds and prints what happened.
// Usage: dotnet run --project tools/Dynasty.MatchLab -- [base|lab] [firstSeed] [count] [maxMinutes]

string rulesetName = args.Length > 0 ? args[0] : "lab";
ulong firstSeed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
int count = args.Length > 2 ? int.Parse(args[2]) : 1;
int maxMinutes = args.Length > 3 ? int.Parse(args[3]) : 60;

RulesetContent ruleset = rulesetName == "base" ? BaseRuleset.Create() : BaseRuleset.CreateDawnSiegeLab();
Console.WriteLine($"Ruleset {ruleset.ToDescriptor()}  seeds {firstSeed}..{firstSeed + (ulong)count - 1}  limit {maxMinutes} min");

for (ulong seed = firstSeed; seed < firstSeed + (ulong)count; seed++)
{
    SimulationKernel<MatchState> kernel = Match.Create(ruleset, seed);
    int tps = kernel.Config.TickRate.TicksPerSecond;
    var clock = Stopwatch.StartNew();
    long ticks = kernel.RunUntilFinished((long)maxMinutes * 60 * tps);
    clock.Stop();

    var records = kernel.Events.Records;
    string Time(long tick) => TimeSpan.FromSeconds(tick / (double)tps).ToString(@"mm\:ss");

    Console.WriteLine();
    Console.WriteLine($"── seed {seed} ─ {(kernel.State.Winner is Team w ? $"{w} wins at {Time(ticks)}" : $"no winner after {Time(ticks)}")}");
    Console.WriteLine($"   {ticks} ticks in {clock.ElapsedMilliseconds} ms ({ticks * 1000 / Math.Max(1, clock.ElapsedMilliseconds)} ticks/s), {records.Count} events, checksum {kernel.Events.Checksum:x16}");

    var kills = records.Select(r => r.Event).OfType<UnitKilled>().GroupBy(k => k.VictimTeam).ToDictionary(g => g.Key, g => g.Count());
    Console.WriteLine($"   creeps lost: Dawn {kills.GetValueOrDefault(Team.Dawn)}, Dusk {kills.GetValueOrDefault(Team.Dusk)}");

    foreach (var record in records.Where(r => r.Event is StructureDestroyed))
        Console.WriteLine($"   {Time(record.Tick.Value)}  {((StructureDestroyed)record.Event).PlacementId} destroyed");
}
