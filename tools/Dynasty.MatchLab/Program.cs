using System.Diagnostics;
using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

// Match Laboratory (headless): runs matches with a chosen ruleset and seeds and prints what happened.
// Usage: dotnet run --project tools/Dynasty.MatchLab -c Release -- [heroes|swapped|mirror|base|lab] [firstSeed] [count] [maxMinutes]
// MATCHLAB_DEATHS=1 prints what killed the heroes; MATCHLAB_PROFILE=1 prints time spent per system.
//   heroes  — base ruleset, 5v5 with the basic hero AI (default)
//   swapped — same, with the two lineups exchanged between sides (separates side bias from draft strength)
//   base   — base ruleset, creeps only
//   lab    — Dawn-siege lab ruleset, creeps only
//   probe [file] — determinism reference checksums of the demo match (see DeterminismProbe)
//   profiles — draft profiles of the base heroes (control, burst, initiation, sustain, push, scaling...)

string mode = args.Length > 0 ? args[0] : "heroes";
if (mode == "quality")
{
    // AI quality metrics on the mirrored line-up (no draft bias), averaged over both teams and N seeds.
    int n = args.Length > 1 ? int.Parse(args[1]) : 16;
    MatchLineup line = MirrorLineup();
    string[] roles = { "carry", "mid", "offlane", "support4", "support5" };
    var roleOf = new Dictionary<string, string> { ["ironvow"] = "carry", ["emberwisp"] = "mid", ["stonewarden"] = "offlane", ["thornkeeper"] = "support4", ["dawnmender"] = "support5" };
    var runs = Enumerable.Range(1, n).AsParallel().AsOrdered().Select(i =>
    {
        var k = Match.Create(BaseRuleset.Create(), (ulong)i, line);
        HeroAi.Attach(k);
        var audit = new QualityAudit();
        k.AddSystem(audit);
        k.RunUntilFinished(60L * 60 * 30);
        return (k, audit);
    }).ToList();

    var lh10 = roles.ToDictionary(r => r, _ => 0.0);
    var lhAll = roles.ToDictionary(r => r, _ => 0.0);
    double deaths = 0, minutes = 0, structures = 0, grouped = 0, highGround = 0, byHeroes = 0, highGrouped = 0, firstHigh = 0, withHigh = 0;
    foreach (var (k, audit) in runs)
    {
        minutes += k.CurrentTick.Value / 1800.0;
        foreach (MatchUnit hero in k.State.Heroes)
        {
            string role = roleOf[hero.Hero!.Definition.Id];
            lhAll[role] += hero.Hero.LastHits / 2.0;
            lh10[role] += audit.LastHitsAt10.GetValueOrDefault(hero.Id.Value) / 2.0;
            deaths += hero.Hero.Deaths / 10.0;
        }

        var heroIds = k.State.Heroes.Select(h => h.Id).ToHashSet();
        var killers = k.Events.Records.Select(r => r.Event).OfType<StructureDestroyed>().ToDictionary(e => e.PlacementId, e => e.Killer);
        foreach (var (placementId, near, tick) in audit.Destroyed)
        {
            structures++;
            if (near >= 3) grouped++;
            if (!QualityAudit.IsHighGround(placementId)) continue;
            highGround++;
            if (near >= 3) highGrouped++;
            if (heroIds.Contains(killers[placementId])) byHeroes++;
        }

        long first = audit.Destroyed.Where(d => QualityAudit.IsHighGround(d.PlacementId)).Select(d => d.Tick).DefaultIfEmpty(-1).First();
        if (first >= 0) { firstHigh += first / 1800.0; withHigh++; }
    }

    Console.WriteLine($"quality over {n} mirrored matches: length {minutes / n:0.0} min, deaths per hero {deaths / n:0.0}");
    Console.WriteLine("last hits @10 / match: " + string.Join("  ", roles.Select(r => $"{r} {lh10[r] / n:0}/{lhAll[r] / n:0}")));
    Console.WriteLine($"structures {structures / n:0.0} per match, grouped {grouped / Math.Max(1, structures):P0}");
    Console.WriteLine($"high ground {highGround / n:0.0} per match (first at {firstHigh / Math.Max(1, withHigh):0.0} min), last hit by heroes {byHeroes / Math.Max(1, highGround):P0}, grouped {highGrouped / Math.Max(1, highGround):P0}");
    return;
}

if (mode == "lanediag")
{
    // Where the Dawn carry's first ten minutes go, and who takes the enemy creeps of its lane.
    int n = args.Length > 1 ? int.Parse(args[1]) : 8;
    string heroId = args.Length > 2 ? args[2] : "ironvow";
    var time = new Dictionary<string, int>();
    var kills = new Dictionary<string, int>();
    int trips = 0; string prev = "base";
    var damage = new Dictionary<string, long>();
    for (ulong seed = 1; seed <= (ulong)n; seed++)
    {
        var k = Match.Create(BaseRuleset.Create(), seed, MirrorLineup());
        var solo = new HeroAiSettings();
        if (Environment.GetEnvironmentVariable("MATCHLAB_SOLO") == "1")
            foreach (var id in new[] { "dawnmender", "emberwisp", "stonewarden", "thornkeeper" }) solo.Uncontrolled.Add(id);
        HeroAi.Attach(k, solo);
        MatchUnit carry = k.State.Heroes.Single(h => h.Team == Team.Dawn && h.Hero!.Definition.Id == heroId);
        var home = k.State.Ruleset.Map.FountainOf(Team.Dawn)!.Position;
        for (int t = 0; t < 10 * 1800; t += 15)
        {
            k.RunUntilFinished(15);
            string c;
            if (carry.Hero!.IsDead) c = "dead";
            else if (Dynasty.Core.Mathematics.FixedVector2.Distance(carry.Position, home) < Dynasty.Core.Mathematics.Fixed.FromInt(1500)) c = "base";
            else
            {
                var target = k.State.Find(carry.Hero.Order.Target);
                c = carry.Hero.Order.Kind == Dynasty.MatchSimulation.Heroes.HeroOrderKind.Attack ? "attack-" + (target?.Kind.ToString() ?? "?") : carry.Hero.Order.Kind.ToString().ToLowerInvariant();
                if (carry.Hero.Order.Kind == Dynasty.MatchSimulation.Heroes.HeroOrderKind.Move) c += Dynasty.Core.Mathematics.FixedVector2.Distance(carry.Hero.Order.Point, home) < Dynasty.Core.Mathematics.Fixed.FromInt(300) ? "-home" : $"-hp{(carry.Hp * Dynasty.Core.Mathematics.Fixed.FromInt(100) / Dynasty.Core.Mathematics.Fixed.FromInt(carry.MaxHp)).ToIntFloor() / 25 * 25}";
                bool creepsNear = k.State.Units.Any(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk && u.IsAlive &&
                    Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, carry.Position) < Dynasty.Core.Mathematics.Fixed.FromInt(800));
                c += creepsNear ? " (enemy creeps near)" : "";
            }
            time[c] = time.GetValueOrDefault(c) + 1;
            if (c == "base" && prev != "base") trips++;
            prev = c;
        }

        var creepIds = k.Events.Records.Select(r => r.Event).OfType<CreepSpawned>().Select(e => e.Unit).ToHashSet();
        foreach (var e in k.Events.Records.Where(r => r.Tick.Value < 10 * 1800).Select(r => r.Event).OfType<DamageDealt>().Where(e => e.Target == carry.Id))
        {
            MatchUnit? source = k.State.Find(e.Attacker);
            string who = source?.Hero != null ? "hero " + source.Hero.Definition.Id : creepIds.Contains(e.Attacker) ? "creep" : source?.Kind == UnitKind.Structure ? "tower" : "other";
            damage[who] = damage.GetValueOrDefault(who) + e.Amount.ToIntFloor();
        }

        var spawned = k.Events.Records.Select(r => r.Event).OfType<CreepSpawned>().Where(e => e.Team == Team.Dusk && e.LaneId == carry.Hero!.AssignedLane).Select(e => e.Unit).ToHashSet();
        foreach (var e in k.Events.Records.Where(r => r.Tick.Value < 10 * 1800).Select(r => r.Event).OfType<UnitKilled>().Where(e => spawned.Contains(e.Victim)))
        {
            MatchUnit? killer = k.State.Find(e.Killer);
            string who = e.Killer == carry.Id ? "carry" : killer?.Hero != null ? (killer.Team == Team.Dawn ? "ally " : "enemy ") + killer.Hero.Definition.Id
                : killer?.Kind == UnitKind.Structure ? "tower" : "creep/other";
            kills[who] = kills.GetValueOrDefault(who) + 1;
        }
    }

    foreach (var kv in time.OrderByDescending(x => x.Value))
        Console.WriteLine($"{kv.Key,-40} {kv.Value * 100.0 / time.Values.Sum():0.0}%");
    Console.WriteLine($"trips to base per match (first 10 min): {trips / (double)n:0.0}");
    Console.WriteLine("damage taken (first 10 min, per match): " + string.Join(", ", damage.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value / n}")));
    Console.WriteLine("enemy lane creeps killed by (first 10 min, per match):");
    foreach (var kv in kills.OrderByDescending(x => x.Value))
        Console.WriteLine($"  {kv.Key,-30} {kv.Value / (double)n:0.0}");
    return;
}

if (mode == "carrydiag")
{
    // Where the Dawn carry's time goes between two match minutes (mirrored line-up), and its last hits there.
    int n = args.Length > 1 ? int.Parse(args[1]) : 8;
    int from = args.Length > 2 ? int.Parse(args[2]) : 10, to = args.Length > 3 ? int.Parse(args[3]) : 30;
    var time = new Dictionary<string, int>();
    double lastHits = 0;
    Dynasty.Core.Mathematics.Fixed R(int v) => Dynasty.Core.Mathematics.Fixed.FromInt(v);
    for (ulong seed = 1; seed <= (ulong)n; seed++)
    {
        var k = Match.Create(BaseRuleset.Create(), seed, MirrorLineup());
        HeroAi.Attach(k);
        MatchUnit carry = k.State.Heroes.Single(h => h.Team == Team.Dawn && h.Hero!.Definition.Id == "ironvow");
        var home = k.State.Ruleset.Map.FountainOf(Team.Dawn)!.Position;
        k.RunUntilFinished(from * 1800L);
        int lhFrom = carry.Hero!.LastHits;
        for (long t = from * 1800L; t < to * 1800L && !k.IsFinished; t += 15)
        {
            k.RunUntilFinished(15);
            var pos = carry.Position;
            string c;
            if (carry.Hero.IsDead) c = "dead";
            else if (Dynasty.Core.Mathematics.FixedVector2.Distance(pos, home) < R(1500)) c = "base";
            else
            {
                int allies = k.State.Heroes.Count(h => h != carry && h.Team == Team.Dawn && h.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(h.Position, pos) < R(1200));
                bool enemyHeroes = k.State.Heroes.Any(h => h.Team == Team.Dusk && h.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(h.Position, pos) < R(1200));
                bool laneCreeps = k.State.Units.Any(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk && u.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, pos) < R(800));
                bool neutrals = k.State.Units.Any(u => u.Kind == UnitKind.Creep && u.Team == Team.Neutral && u.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, pos) < R(800));
                c = enemyHeroes ? "near enemy heroes" : laneCreeps ? "at enemy lane creeps" : neutrals ? "at neutral camp" : "walking/other";
                c += allies >= 2 ? " (grouped)" : "";
            }
            time[c] = time.GetValueOrDefault(c) + 1;
        }

        lastHits += carry.Hero.LastHits - lhFrom;
    }

    foreach (var kv in time.OrderByDescending(x => x.Value))
        Console.WriteLine($"{kv.Key,-40} {kv.Value * 100.0 / time.Values.Sum():0.0}%");
    Console.WriteLine($"carry last hits in minutes {from}-{to}: {lastHits / n:0.0} per match");
    return;
}

if (mode == "pushplan")
{
    // Backlog B-023: does the push draft take more structures when it plays its plan? Same seeds, plan on and off.
    int n = args.Length > 1 ? int.Parse(args[1]) : 16;
    RulesetContent rules = BaseRuleset.Create();
    string[] push = { "duskrunner", "siegewright", "wildheart", "stormcaller", "spiritwarden" };
    string[] opponent = { "starborn", "shadeblade", "frostweaver", "hivecaller", "thornkeeper" };
    foreach (bool plan in new[] { false, true })
    {
        var runs = Enumerable.Range(1, n).AsParallel().AsOrdered().Select(i =>
        {
            MatchLineup lineup = Dynasty.Drafting.PositionPlan.Lineup(Dynasty.Drafting.PositionPlan.For(rules, Team.Dawn, push), Dynasty.Drafting.PositionPlan.For(rules, Team.Dusk, opponent));
            var k = Match.Create(rules, (ulong)i, lineup);
            var settings = new HeroAiSettings();
            if (plan)
                settings.DawnStrategy = TeamStrategy.From(Dynasty.Drafting.GamePlan.For(rules, push));
            HeroAi.Attach(k, settings);
            k.RunUntilFinished(60L * 60 * 30);
            return k;
        }).ToList();
        double At(SimulationKernel<MatchState> k, long minute) => k.Events.Records.Count(r => r.Tick.Value < minute * 1800 && r.Event is StructureDestroyed d && d.Team == Team.Dusk);
        Console.WriteLine($"push draft, plan {(plan ? "on " : "off")}: Dusk structures taken by 15 min {runs.Average(k => At(k, 15)):0.0}, by 20 min {runs.Average(k => At(k, 20)):0.0}, " +
                          $"whole match {runs.Average(k => At(k, 999)):0.0}; Dawn wins {runs.Count(k => k.State.Winner == Team.Dawn)}/{n}, length {runs.Average(k => k.CurrentTick.Value / 1800.0):0.0} min");
    }

    return;
}

if (mode == "skill")
{
    // Player skill on the same heroes: Dawn players of quality A vs Dusk players of quality B, and swapped.
    int qa = args.Length > 1 ? int.Parse(args[1]) : 17;
    int qb = args.Length > 2 ? int.Parse(args[2]) : 7;
    int n = args.Length > 3 ? int.Parse(args[3]) : 16;
    MatchLineup line = MirrorLineup();   // the same heroes on both sides: only the players differ
    foreach (bool swap in new[] { false, true })
    {
        int dawnQ = swap ? qb : qa, duskQ = swap ? qa : qb;
        int sWins = 0, dawnK = 0, duskK = 0, dawnLh = 0, duskLh = 0;
        for (ulong seed = 1; seed <= (ulong)n; seed++)
        {
            var settings = new HeroAiSettings();
            var random = new Dynasty.Core.Randomness.DeterministicRandom(seed * 31);
            foreach (var (team, picks, q) in new[] { (Team.Dawn, line.Dawn, dawnQ), (Team.Dusk, line.Dusk, duskQ) })
                for (int i = 0; i < picks.Count; i++)
                {
                    var player = Dynasty.Players.PlayerGenerator.Generate(random, picks[i].HeroId, i + 1, q, new[] { picks[i].HeroId });
                    settings.SkillsOf(team)[picks[i].HeroId] = HeroSkill.FromPlayer(player, picks[i].HeroId);
                }

            var k = Match.Create(BaseRuleset.Create(), seed, line);
            HeroAi.Attach(k, settings);
            k.RunUntilFinished(60L * 60 * 30);
            if (k.State.Winner == Team.Dawn) sWins++;
            dawnK += k.State.Heroes.Where(h => h.Team == Team.Dawn).Sum(h => h.Hero!.Kills);
            duskK += k.State.Heroes.Where(h => h.Team == Team.Dusk).Sum(h => h.Hero!.Kills);
            dawnLh += k.State.Heroes.Where(h => h.Team == Team.Dawn).Sum(h => h.Hero!.LastHits);
            duskLh += k.State.Heroes.Where(h => h.Team == Team.Dusk).Sum(h => h.Hero!.LastHits);
        }

        Console.WriteLine($"Dawn q{dawnQ} vs Dusk q{duskQ}: Dawn wins {sWins}/{n}, kills {dawnK}:{duskK}, last hits {dawnLh}:{duskLh}");
    }

    return;
}

if (mode == "profiles")
{
    // Draft profiles derived from the base ruleset (Dynasty.Drafting.HeroProfile).
    RulesetContent rules = BaseRuleset.Create();
    Console.WriteLine($"{"hero",-14}{"roles",-26}{"ctrl",5}{"burst",6}{"mag%",5}{"init",5}{"sust",5}{"push",5}{"scale",6}{"tempo",6}{"dura",5}{"mob",5}{"rng",4}{"disp",5}");
    foreach (HeroDefinition hero in rules.Heroes)
    {
        var p = Dynasty.Drafting.HeroProfile.Of(rules, hero.Id);
        Console.WriteLine($"{hero.Id,-14}{string.Join(",", hero.Roles),-26}{p.Control,5}{p.Burst,6}{p.MagicalPercent,5}{p.Initiation,5}{p.Sustain,5}{p.Push,5}{p.Scaling,6}{p.Tempo,6}{p.Durability,5}{p.Mobility,5}{(p.Ranged ? "R" : "M"),4}{(p.Dispel ? "y" : ""),5}");
    }

    return;
}

if (mode == "probe")
{
    // Reference checksums for the runtime determinism check (tools/sync-unity.ps1 writes them into the Unity project).
    string report = Dynasty.Presentation.DeterminismProbe.Format(Dynasty.Presentation.DeterminismProbe.RunDefault());
    if (args.Length > 1)
        File.WriteAllText(args[1], report);
    Console.Write(report);
    return;
}

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
    var profile = Environment.GetEnvironmentVariable("MATCHLAB_PROFILE") == "1" ? new SystemProfile() : null;
    kernel.Profiler = profile;

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

    if (profile != null)
        Console.WriteLine("   time per system: " + profile.Report());

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

/// <summary>Accumulates wall-clock time per simulation system.</summary>
sealed class SystemProfile : ISimulationProfiler
{
    private readonly Dictionary<string, long> _elapsed = new();

    public void SystemTicked(string system, long elapsedStopwatchTicks) =>
        _elapsed[system] = _elapsed.GetValueOrDefault(system) + elapsedStopwatchTicks;

    public string Report()
    {
        long total = Math.Max(1, _elapsed.Values.Sum());
        return string.Join(", ", _elapsed.OrderByDescending(e => e.Value)
            .Select(e => $"{e.Key} {e.Value * 100.0 / total:0.0}% ({e.Value * 1000 / Stopwatch.Frequency} ms)"));
    }
}

/// <summary>Quality metrics: last hits at minute 10 and every destroyed structure with the number of enemy heroes within 1500.</summary>
sealed class QualityAudit : ISimulationSystem<MatchState>
{
    private int _seen;

    public string Name => "matchlab-quality";
    public Dictionary<long, int> LastHitsAt10 { get; } = new();
    public List<(string PlacementId, int Near, long Tick)> Destroyed { get; } = new();

    public static bool IsHighGround(string placementId) =>
        placementId.EndsWith(".t3") || placementId.EndsWith(".barracks") || placementId.Contains(".t4") || placementId.EndsWith(".ancient");

    public void Tick(SimulationContext<MatchState> context)
    {
        MatchState state = context.State;
        if (context.Tick.Value == 10 * 1800)
            foreach (MatchUnit hero in state.Heroes)
                LastHitsAt10[hero.Id.Value] = hero.Hero!.LastHits;

        var destroyed = state.DestroyedStructures.ToList();
        for (; _seen < destroyed.Count; _seen++)
        {
            StructurePlacement placement = state.Ruleset.Map.Structures.Single(s => s.Id == destroyed[_seen]);
            Dynasty.Core.Mathematics.Fixed reach = Dynasty.Core.Mathematics.Fixed.FromInt(1500);
            int near = state.Heroes.Count(h => h.Team != placement.Team && h.IsAlive &&
                                               Dynasty.Core.Mathematics.FixedVector2.DistanceSquared(h.Position, placement.Position) <= reach * reach);
            Destroyed.Add((placement.Id, near, context.Tick.Value));
        }
    }
}
