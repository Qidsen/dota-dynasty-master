using System.Diagnostics;
using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

// Match Laboratory (headless): runs matches with a chosen ruleset and seeds and prints what happened.
// Usage: dotnet run --project tools/Dynasty.MatchLab -c Release -- [heroes|swapped|base|lab] [firstSeed] [count] [maxMinutes]
//   heroes  — base ruleset, 5v5 with the basic hero AI (default)
//   swapped — same, with the two lineups exchanged between sides (separates side bias from draft strength)
//   base   — base ruleset, creeps only
//   lab    — Dawn-siege lab ruleset, creeps only

string mode = args.Length > 0 ? args[0] : "heroes";
ulong firstSeed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
int count = args.Length > 2 ? int.Parse(args[2]) : 1;
int maxMinutes = args.Length > 3 ? int.Parse(args[3]) : 60;

RulesetContent ruleset = mode == "lab" ? BaseRuleset.CreateDawnSiegeLab() : BaseRuleset.Create();
MatchLineup lineup = mode == "heroes" ? DefaultLineup(swapped: false)
    : mode == "swapped" ? DefaultLineup(swapped: true)
    : mode == "mirror" ? MirrorLineup()
    : MatchLineup.Empty;
int dawnKills = 0, duskKills = 0, dawnStructuresLost = 0, duskStructuresLost = 0;
Console.WriteLine($"Ruleset {ruleset.ToDescriptor()}  mode {mode}  seeds {firstSeed}..{firstSeed + (ulong)count - 1}  limit {maxMinutes} min");

int dawnWins = 0, duskWins = 0;
for (ulong seed = firstSeed; seed < firstSeed + (ulong)count; seed++)
{
    SimulationKernel<MatchState> kernel = Match.Create(ruleset, seed, lineup);
    if (lineup.Dawn.Count + lineup.Dusk.Count > 0)
        HeroAi.Attach(kernel);
    var pushes = new GroupedPushCounter();
    kernel.AddSystem(pushes);

    int tps = kernel.Config.TickRate.TicksPerSecond;
    var clock = Stopwatch.StartNew();
    long ticks = kernel.RunUntilFinished((long)maxMinutes * 60 * tps);
    clock.Stop();

    var records = kernel.Events.Records;
    string Time(long tick) => $"{tick / tps / 60:00}:{tick / tps % 60:00}";
    if (kernel.State.Winner == Team.Dawn) dawnWins++;
    if (kernel.State.Winner == Team.Dusk) duskWins++;

    Console.WriteLine();
    Console.WriteLine($"── seed {seed} ─ {(kernel.State.Winner is Team w ? $"{w} wins at {Time(ticks)}" : $"no winner after {Time(ticks)}")}");
    Console.WriteLine($"   {ticks} ticks in {clock.ElapsedMilliseconds} ms ({ticks * 1000 / Math.Max(1, clock.ElapsedMilliseconds)} ticks/s), {records.Count} events, checksum {kernel.Events.Checksum:x16}");

    var structures = records.Where(r => r.Event is StructureDestroyed).ToList();
    dawnStructuresLost += structures.Count(r => ((StructureDestroyed)r.Event).Team == Team.Dawn);
    duskStructuresLost += structures.Count(r => ((StructureDestroyed)r.Event).Team == Team.Dusk);
    if (Environment.GetEnvironmentVariable("MATCHLAB_DEATHS") == "1")
    {
        // Diagnostics: who killed each hero, and where.
        var kindOf = new Dictionary<long, string>();
        foreach (var record in records)
        {
            if (record.Event is CreepSpawned cs) kindOf[cs.Unit.Value] = "creep";
            if (record.Event is UnitSummoned us) kindOf[us.Unit.Value] = "summon";
        }
        foreach (MatchUnit u in kernel.State.Units) kindOf[u.Id.Value] = u.Kind.ToString().ToLowerInvariant();
        foreach (var team in new[] { Team.Dawn, Team.Dusk })
        {
            var heroIds = kernel.State.Heroes.Where(h => h.Team == team).Select(h => h.Id).ToHashSet();
            var deaths = records.Select(r => r.Event).OfType<HeroKilled>().Where(k => heroIds.Contains(k.Victim)).ToList();
            string causes = string.Join(", ", deaths.GroupBy(d => kindOf.GetValueOrDefault(d.Killer.Value, d.Killer.IsValid ? "?" : "none")).Select(g => g.Key + "=" + g.Count()));
            Console.WriteLine($"   {team} deaths {deaths.Count}: {causes}");
        }
    }

    dawnKills += kernel.State.Heroes.Where(h => h.Team == Team.Dawn).Sum(h => h.Hero!.Kills);
    duskKills += kernel.State.Heroes.Where(h => h.Team == Team.Dusk).Sum(h => h.Hero!.Kills);
    Console.WriteLine($"   structures lost: Dawn {structures.Count(r => ((StructureDestroyed)r.Event).Team == Team.Dawn)}, Dusk {structures.Count(r => ((StructureDestroyed)r.Event).Team == Team.Dusk)}; taken by 3+ heroes together: {pushes.Grouped}");
    var bossKills = records.Select(r => r.Event).OfType<BossKilled>().ToList();
    var wards = records.Select(r => r.Event).OfType<UnitSummoned>().Count(s => s.CreepTypeId == "observer_ward");
    Console.WriteLine($"   boss kills: Dawn {bossKills.Count(b => b.Team == Team.Dawn)}, Dusk {bossKills.Count(b => b.Team == Team.Dusk)}; observer wards placed: {wards}");

    if (!kernel.State.Heroes.Any())
    {
        foreach (var record in structures)
            Console.WriteLine($"   {Time(record.Tick.Value)}  {((StructureDestroyed)record.Event).PlacementId} destroyed");
        continue;
    }

    var casts = records.Select(r => r.Event).OfType<AbilityCast>().GroupBy(c => c.Caster).ToDictionary(g => g.Key, g => g.Count());
    var heals = records.Select(r => r.Event).OfType<HealApplied>().GroupBy(h => h.Source).ToDictionary(g => g.Key, g => g.Sum(h => h.Amount.ToDouble()));
    var damage = records.Select(r => r.Event).OfType<DamageDealt>()
        .GroupBy(d => d.Attacker).ToDictionary(g => g.Key, g => g.Sum(d => d.Amount.ToDouble()));
    Console.WriteLine("   hero          team  lvl  K/D/A     LH/DN    gold   dmg     heal   casts  items");
    foreach (MatchUnit hero in kernel.State.Heroes)
    {
        var h = hero.Hero!;
        string items = string.Join(",", h.Inventory.Select(i => i.Definition.Id));
        Console.WriteLine($"   {h.Definition.Name,-13} {hero.Team,-5} {h.Level,3}  {h.Kills,2}/{h.Deaths,2}/{h.Assists,2}  {h.LastHits,3}/{h.Denies,-3} {h.Gold.ToIntFloor(),6} {damage.GetValueOrDefault(hero.Id),7:0} {heals.GetValueOrDefault(hero.Id),6:0} {casts.GetValueOrDefault(hero.Id),6}  {items}");
    }
}

Console.WriteLine();
Console.WriteLine($"Summary: Dawn {dawnWins}, Dusk {duskWins}, unfinished {count - dawnWins - duskWins}");
Console.WriteLine($"Hero kills: Dawn {dawnKills}, Dusk {duskKills}; structures lost: Dawn {dawnStructuresLost}, Dusk {duskStructuresLost}");

static MatchLineup MirrorLineup()
{
    // Identical heroes on both sides; Dusk lanes mirrored (top <-> bot) so matchups are symmetric.
    HeroPick[] dawn =
    {
        new("ironvow", "bot"), new("dawnmender", "bot"), new("emberwisp", "mid"),
        new("stonewarden", "top"), new("thornkeeper", "top"),
    };
    HeroPick[] dusk = dawn.Select(p => new HeroPick(p.HeroId, p.LaneId == "top" ? "bot" : p.LaneId == "bot" ? "top" : p.LaneId)).ToArray();
    return new MatchLineup(dawn, dusk);
}

static MatchLineup DefaultLineup(bool swapped)
{
    // Lane names are swapped along with the sides so each lineup keeps its safe/off lane roles.
    string Lane(string lane) => !swapped || lane == "mid" ? lane : lane == "top" ? "bot" : "top";
    HeroPick[] a =
    {
        new("ironvow", "bot"), new("dawnmender", "bot"), new("emberwisp", "mid"),
        new("stonewarden", "top"), new("thornkeeper", "top"),
    };
    HeroPick[] b =
    {
        new("starborn", "top"), new("shadeblade", "top"), new("frostweaver", "mid"),
        new("hivecaller", "bot"), new("siegewright", "bot"),
    };
    HeroPick[] Relane(HeroPick[] picks) => picks.Select(p => new HeroPick(p.HeroId, Lane(p.LaneId))).ToArray();
    return swapped ? new MatchLineup(Relane(b), Relane(a)) : new MatchLineup(a, b);
}

/// <summary>Counts structures destroyed with three or more enemy heroes within 1500 (a grouped push).</summary>
sealed class GroupedPushCounter : ISimulationSystem<MatchState>
{
    private int _seen;

    public string Name => "matchlab-grouped-pushes";
    public int Grouped { get; private set; }

    public void Tick(SimulationContext<MatchState> context)
    {
        MatchState state = context.State;
        var destroyed = state.DestroyedStructures.ToList();
        for (; _seen < destroyed.Count; _seen++)
        {
            StructurePlacement placement = state.Ruleset.Map.Structures.Single(s => s.Id == destroyed[_seen]);
            Dynasty.Core.Mathematics.Fixed reach = Dynasty.Core.Mathematics.Fixed.FromInt(1500);
            int near = state.Heroes.Count(h => h.Team != placement.Team && h.IsAlive &&
                                               Dynasty.Core.Mathematics.FixedVector2.DistanceSquared(h.Position, placement.Position) <= reach * reach);
            if (near >= 3)
                Grouped++;
        }
    }
}
