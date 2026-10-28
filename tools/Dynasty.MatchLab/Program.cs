using System.Diagnostics;
using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

// Match Laboratory (headless): runs matches with a chosen ruleset and seeds and prints what happened.
// Usage: dotnet run --project tools/Dynasty.MatchLab -c Release -- [heroes|swapped|mirror|base|lab] [firstSeed] [count] [maxMinutes]
// MATCHLAB_DEATHS=1 prints what killed the heroes; MATCHLAB_PROFILE=1 prints time spent per system.
// MATCHLAB_PACK=packs/dota-7.41f.json plays on a ruleset pack (Dota heroes and items) instead of the base heroes.
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
    var roleOf = RoleOf();
    var runs = Enumerable.Range(1, n).AsParallel().AsOrdered().Select(i =>
    {
        var k = Match.Create(Rules(), (ulong)i, line);
        HeroAi.Attach(k);
        var audit = new QualityAudit();
        k.AddSystem(audit);
        k.RunUntilFinished(60L * 60 * 30);
        return (k, audit);
    }).ToList();

    var lh10 = roles.ToDictionary(r => r, _ => 0.0);
    var lh20 = roles.ToDictionary(r => r, _ => new List<double>());
    var nw10 = roles.ToDictionary(r => r, _ => 0.0);
    var xp10 = roles.ToDictionary(r => r, _ => 0.0);
    var xp20 = roles.ToDictionary(r => r, _ => new List<double>());
    var nw20 = roles.ToDictionary(r => r, _ => new List<double>());
    var killsPerMatch = new List<double>();
    var earned10 = roles.ToDictionary(r => r, _ => 0.0);
    var earned20 = roles.ToDictionary(r => r, _ => 0.0);
    var bySource = new Dictionary<(string, string), double>();
    var bySource20 = new Dictionary<(string, string), double>();
    double laneDied = 0, laneByHeroes = 0, neutralsDied = 0;
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
            nw10[role] += audit.NetWorthAt10.GetValueOrDefault(hero.Id.Value) / 2.0;
            xp10[role] += audit.XpAt10.GetValueOrDefault(hero.Id.Value) / 2.0;
            if (audit.XpAt20.TryGetValue(hero.Id.Value, out int x20)) xp20[role].Add(x20);
            if (audit.NetWorthAt20.TryGetValue(hero.Id.Value, out int w20))
            {
                nw20[role].Add(w20);
                lh20[role].Add(audit.LastHitsAt20[hero.Id.Value]);
            }
            deaths += hero.Hero.Deaths / 10.0;
        }

        killsPerMatch.Add(k.State.Heroes.Sum(h => h.Hero!.Kills));
        foreach (var r in k.Events.Records)
            if (r.Event is GoldEarned g && r.Tick.Value < 20 * 1800)
            {
                MatchUnit h = k.State.Heroes.First(x => x.Id == g.Hero);
                string role = roleOf[h.Hero!.Definition.Id];
                if (r.Tick.Value < 10 * 1800) earned10[role] += g.Amount / 2.0;
                earned20[role] += g.Amount / 2.0;
                if (r.Tick.Value < 10 * 1800) bySource[(role, g.Reason)] = bySource.GetValueOrDefault((role, g.Reason)) + g.Amount / 2.0;
                else bySource20[(role, g.Reason)] = bySource20.GetValueOrDefault((role, g.Reason)) + g.Amount / 2.0;
            }
        var heroIdsAll = k.State.Heroes.Select(h => h.Id).ToHashSet();
        foreach (var r in k.Events.Records)
            if (r.Event is UnitKilled uk && r.Tick.Value >= 10 * 1800 && r.Tick.Value < 20 * 1800 && uk.VictimTypeId != "" && !heroIdsAll.Contains(uk.Victim))
            {
                if (uk.VictimTeam == Team.Neutral) { if (uk.VictimTypeId.StartsWith("jungle") || uk.VictimTypeId.StartsWith("neutral_")) neutralsDied += 0.5; continue; }
                if (!(uk.VictimTypeId.StartsWith("melee") || uk.VictimTypeId.StartsWith("ranged") || uk.VictimTypeId.StartsWith("siege"))) continue;
                laneDied += 0.5;
                if (heroIdsAll.Contains(uk.Killer)) laneByHeroes += 0.5;
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
    Console.WriteLine($"kills per match {killsPerMatch.Average():0.0}");
    HeroRules economy = Rules().HeroRules!;
    int start = economy.StartingGold, passive = economy.GoldPerMinute;   // passive gold is credited directly, not as GoldEarned events
    Console.WriteLine("role       nw@10  nw@20 earn@10 earn@20 LH@10  LH@20  XP@10  XP@20   (net worth = gold + item cost; earned = start + passive + GoldEarned, like OpenDota gold_t; @20 over matches still running)");
    foreach (string r in roles)
        Console.WriteLine($"{r,-9} {nw10[r] / n,6:0} {(nw20[r].Count > 0 ? nw20[r].Average() : 0),6:0} {start + 10 * passive + earned10[r] / n,7:0} {start + 20 * passive + earned20[r] / n,7:0} {lh10[r] / n,5:0} {(lh20[r].Count > 0 ? lh20[r].Average() : 0),6:0} {xp10[r] / n,6:0} {(xp20[r].Count > 0 ? xp20[r].Average() : 0),6:0}");
    Console.WriteLine("gold earned by 10 min by source: " + string.Join("; ", roles.Select(r => r + ": " + string.Join(", ",
        bySource.Where(kv => kv.Key.Item1 == r).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Item2} {kv.Value / n:0}")))));
    Console.WriteLine("gold earned in minutes 10-20 by source: " + string.Join("; ", roles.Select(r => r + ": " + string.Join(", ",
        bySource20.Where(kv => kv.Key.Item1 == r).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Item2} {kv.Value / n:0}")))));
    Console.WriteLine($"minutes 10-20 per team: lane creeps died {laneDied / n:0}, last hit by heroes {laneByHeroes / Math.Max(1, laneDied):P0}; jungle creeps killed {neutralsDied / n:0}");
    Console.WriteLine("structures destroyed by minute (per match, both teams): " + string.Join("  ", new[] { 10, 15, 20, 25, 30 }.Select(m =>
        $"{m}: {runs.Sum(r => r.audit.Destroyed.Count(d => d.Tick < m * 1800L)) / (double)n:0.0}")) +
        "   T1 by 20: " + $"{runs.Sum(r => r.audit.Destroyed.Count(d => d.Tick < 20 * 1800L && d.PlacementId.EndsWith(".t1"))) / (double)n:0.0}");
    Console.WriteLine($"structures {structures / n:0.0} per match, grouped {grouped / Math.Max(1, structures):P0}");
    Console.WriteLine($"high ground {highGround / n:0.0} per match (first at {firstHigh / Math.Max(1, withHigh):0.0} min), last hit by heroes {byHeroes / Math.Max(1, highGround):P0}, grouped {highGrouped / Math.Max(1, highGround):P0}");
    return;
}

if (mode == "whydiag")
{
    // Which AI branch decides, per role, between two minutes (mirrored line-up, both teams): HeroAiSettings.DecisionTrace.
    int n = args.Length > 1 ? int.Parse(args[1]) : 16;
    int from = args.Length > 2 ? int.Parse(args[2]) : 10, to = args.Length > 3 ? int.Parse(args[3]) : 20;
    var roleOf = RoleOf();
    var counts = Enumerable.Range(1, n).AsParallel().Select(seed =>
    {
        var why = new Dictionary<(string, string), int>();
        var settings = new HeroAiSettings
        {
            DecisionTrace = (h, now, branch) =>
            {
                if (now < from * 1800L || now >= to * 1800L) return;
                var key = (roleOf[h.Hero!.Definition.Id], branch);
                why[key] = why.GetValueOrDefault(key) + 1;
            },
        };
        var k = Match.Create(Rules(), (ulong)seed, MirrorLineup());
        HeroAi.Attach(k, settings);
        k.RunUntilFinished(to * 1800L);
        return why;
    }).ToList();
    var total = counts.SelectMany(d => d).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
    foreach (var role in new[] { "carry", "mid", "offlane", "support4", "support5" })
    {
        double sum = total.Where(kv => kv.Key.Item1 == role).Sum(kv => kv.Value);
        Console.WriteLine(role + ": " + string.Join(", ", total.Where(kv => kv.Key.Item1 == role).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Item2} {kv.Value * 100 / sum:0}%")));
    }
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
        var k = Match.Create(Rules(), seed, MirrorLineup());
        var solo = new HeroAiSettings();
        if (Environment.GetEnvironmentVariable("MATCHLAB_SOLO") == "1")
            foreach (var h in k.State.Heroes.Where(h => h.Hero!.Definition.Id != heroId)) solo.Uncontrolled.Add(h.Hero!.Definition.Id);   // in a mirror the other side's copy plays on its own safe lane
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
        var k = Match.Create(Rules(), seed, MirrorLineup());
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
    RulesetContent rules = Rules();
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

if (mode == "skilldiag")
{
    // Elite (Dawn, q17) against poor (Dusk, q7) players on the mirrored line-up: where each team's hero time goes.
    int n = args.Length > 1 ? int.Parse(args[1]) : 8;
    var time = new Dictionary<(Team, string), long>();
    var structureDamage = new Dictionary<Team, double>();
    Dynasty.Core.Mathematics.Fixed R(int v) => Dynasty.Core.Mathematics.Fixed.FromInt(v);
    MatchLineup line = MirrorLineup();
    var runs = Enumerable.Range(1, n).AsParallel().AsOrdered().Select(i =>
    {
        ulong seed = (ulong)i;
        var settings = new HeroAiSettings();
        var random = new Dynasty.Core.Randomness.DeterministicRandom(seed * 31);
        foreach (var (team, picks, q) in new[] { (Team.Dawn, line.Dawn, 17), (Team.Dusk, line.Dusk, 7) })
            for (int j = 0; j < picks.Count; j++)
            {
                var player = Dynasty.Players.PlayerGenerator.Generate(random, picks[j].HeroId, j + 1, q, new[] { picks[j].HeroId });
                settings.SkillsOf(team)[picks[j].HeroId] = HeroSkill.FromPlayer(player, picks[j].HeroId);
            }

        var k = Match.Create(Rules(), seed, line);
        HeroAi.Attach(k, settings);
        var local = new Dictionary<(Team, string), long>();
        while (!k.IsFinished && k.CurrentTick.Value < 60L * 60 * 30)
        {
            k.RunUntilFinished(15);
            foreach (MatchUnit h in k.State.Heroes)
            {
                var home = k.State.Ruleset.Map.FountainOf(h.Team)!.Position;
                string c;
                if (h.Hero!.IsDead) c = "dead";
                else if (Dynasty.Core.Mathematics.FixedVector2.Distance(h.Position, home) < R(1500)) c = "base";
                else if (k.State.Heroes.Any(e => e.Team != h.Team && e.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(e.Position, h.Position) < R(1200))) c = "near enemy heroes";
                else if (k.State.Units.Any(u => u.Kind == UnitKind.Structure && u.Team != h.Team && u.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, h.Position) < R(1200))) c = "at enemy structure";
                else if (k.State.Units.Any(u => u.Kind == UnitKind.Creep && u.Team != h.Team && u.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, h.Position) < R(800))) c = "at enemy creeps";
                else c = "elsewhere";
                local[(h.Team, c)] = local.GetValueOrDefault((h.Team, c)) + 1;
            }
        }

        return (k, local);
    }).ToList();
    foreach (var (k, local) in runs)
    {
        foreach (var kv in local)
            time[kv.Key] = time.GetValueOrDefault(kv.Key) + kv.Value;
        var heroTeam = k.State.Heroes.ToDictionary(h => h.Id, h => h.Team);
        var structures = k.State.Units.Where(u => u.Kind == UnitKind.Structure).Select(u => u.Id).ToHashSet();
        foreach (var d in k.Events.Records.Select(r => r.Event).OfType<DamageDealt>())
            if (heroTeam.TryGetValue(d.Attacker, out Team t) && (structures.Contains(d.Target) || k.State.Find(d.Target) == null && !heroTeam.ContainsKey(d.Target) && false))
                structureDamage[t] = structureDamage.GetValueOrDefault(t) + d.Amount.ToDouble();
    }

    foreach (Team team in new[] { Team.Dawn, Team.Dusk })
    {
        long total = time.Where(kv => kv.Key.Item1 == team).Sum(kv => kv.Value);
        Console.WriteLine($"{(team == Team.Dawn ? "elite" : "poor ")}: " + string.Join(", ", time.Where(kv => kv.Key.Item1 == team).OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key.Item2} {kv.Value * 100.0 / total:0}%")) + $"; hero damage to living structures {structureDamage.GetValueOrDefault(team) / n:0} per match");
    }

    Console.WriteLine($"elite wins {runs.Count(r => r.k.State.Winner == Team.Dawn)}/{n}");
    return;
}

if (mode == "lanewatch")
{
    // One match, the Dawn hero's lane second by second: position, HP, order, nearest units.
    ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
    string heroId = args.Length > 2 ? args[2] : "ironvow";
    int from = args.Length > 3 ? int.Parse(args[3]) : 0, to = args.Length > 4 ? int.Parse(args[4]) : 240;
    var k = Match.Create(Rules(), seed, MirrorLineup());
    HeroAi.Attach(k);
    MatchUnit me = k.State.Heroes.Single(h => h.Team == Team.Dawn && h.Hero!.Definition.Id == heroId);
    int Dist(MatchUnit u) => Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, me.Position).ToIntFloor();
    string Near(IEnumerable<MatchUnit> units) { var l = units.Where(u => u.IsAlive).OrderBy(Dist).Take(1).ToList(); return l.Count == 0 ? "-" : $"{Dist(l[0])}"; }
    int lh = 0;
    k.RunUntilFinished(from * 30L);
    for (int sec = from; sec < to; sec++)
    {
        k.RunUntilFinished(30);
        var h = me.Hero!;
        var target = k.State.Find(h.Order.Target);
        string order = h.Order.Kind.ToString() + (h.Order.Kind == Dynasty.MatchSimulation.Heroes.HeroOrderKind.Attack ? "→" + (target?.Hero?.Definition.Id ?? target?.Kind.ToString() ?? "?") : "");
        string tower = Near(k.State.Units.Where(u => u.Kind == UnitKind.Structure && u.Team == Team.Dusk));
        string ownTower = Near(k.State.Units.Where(u => u.Kind == UnitKind.Structure && u.Team == Team.Dawn));
        Console.WriteLine($"{sec / 60}:{sec % 60:00} pos {me.Position.X.ToIntFloor(),5},{me.Position.Y.ToIntFloor(),5} hp {me.Hp.ToIntFloor(),4}/{me.MaxHp} {order,-22} LH {h.LastHits - lh,1} " +
            $"enemyCreep {Near(k.State.Units.Where(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk)),5} ownCreep {Near(k.State.Units.Where(u => u.Kind == UnitKind.Creep && u.Team == Team.Dawn)),5} " +
            $"enemyHero {Near(k.State.Heroes.Where(u => u.Team == Team.Dusk)),5} ally {Near(k.State.Heroes.Where(u => u.Team == Team.Dawn && u != me)),5} enemyTower {tower,5} ownTower {ownTower,5}");
        lh = h.LastHits;
    }

    return;
}

if (mode == "lhdiag")
{
    // Enemy creeps dying within 700 of the Dawn hero (alive) in the first 10 minutes: who took them.
    int n = args.Length > 1 ? int.Parse(args[1]) : 8;
    string heroId = args.Length > 2 ? args[2] : "ironvow";
    bool solo = Environment.GetEnvironmentVariable("MATCHLAB_SOLO") == "1";
    var takers = new Dictionary<string, int>();
    for (ulong seed = 1; seed <= (ulong)n; seed++)
    {
        var settings = new HeroAiSettings();
        if (solo)
            foreach (var id in new[] { "dawnmender", "emberwisp", "stonewarden", "thornkeeper" }) settings.Uncontrolled.Add(id);
        var k = Match.Create(Rules(), seed, MirrorLineup());
        HeroAi.Attach(k, settings);
        MatchUnit me = k.State.Heroes.Single(h => h.Team == Team.Dawn && h.Hero!.Definition.Id == heroId);
        int seen = 0;
        var lastPosition = new Dictionary<long, Dynasty.Core.Mathematics.FixedVector2>();
        for (long t = 0; t < 10L * 1800; t++)
        {
            lastPosition.Clear();
            foreach (MatchUnit u in k.State.Units)
                if (u.Kind == UnitKind.Creep && u.Team == Team.Dusk && u.LaneId != null)
                    lastPosition[u.Id.Value] = u.Position;
            k.RunUntilFinished(1);
            var records = k.Events.Records;
            for (; seen < records.Count; seen++)
            {
                if (records[seen].Event is not UnitKilled uk || uk.VictimTeam != Team.Dusk || me.Hero!.IsDead)
                    continue;
                if (!lastPosition.TryGetValue(uk.Victim.Value, out var at)) continue;   // lane creeps only
                if (Dynasty.Core.Mathematics.FixedVector2.Distance(at, me.Position) > Dynasty.Core.Mathematics.Fixed.FromInt(700)) continue;
                MatchUnit? killer = k.State.Find(uk.Killer);
                string who = uk.Killer == me.Id ? "ME" : killer?.Hero != null ? (killer.Team == Team.Dawn ? "ally hero" : "enemy hero (deny)") : killer?.Kind == UnitKind.Structure ? "tower" : "creep";
                takers[who] = takers.GetValueOrDefault(who) + 1;
            }
        }
    }

    foreach (var kv in takers.OrderByDescending(x => x.Value))
        Console.WriteLine($"{kv.Key,-20} {kv.Value / (double)n:0.0} per match");
    return;
}

if (mode == "skill")
{
    // Player skill on the same heroes: Dawn players of quality A vs Dusk players of quality B, and swapped.
    // MATCHLAB_EQUALIZE=Prop1,Prop2 gives the weaker players the stronger players' value of those HeroSkill
    // properties (ablation: how much of the gap each part of skill makes).
    int qa = args.Length > 1 ? int.Parse(args[1]) : 17;
    int qb = args.Length > 2 ? int.Parse(args[2]) : 7;
    int n = args.Length > 3 ? int.Parse(args[3]) : 16;
    string[] equalize = (Environment.GetEnvironmentVariable("MATCHLAB_EQUALIZE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    MatchLineup line = MirrorLineup();   // the same heroes on both sides: only the players differ
    int strongerWins = 0;
    foreach (bool swap in new[] { false, true })
    {
        int dawnQ = swap ? qb : qa, duskQ = swap ? qa : qb;
        Team stronger = dawnQ >= duskQ ? Team.Dawn : Team.Dusk;
        var runs = Enumerable.Range(1, n).AsParallel().AsOrdered().Select(i =>
        {
            ulong seed = (ulong)i;
            var settings = new HeroAiSettings();
            var random = new Dynasty.Core.Randomness.DeterministicRandom(seed * 31);
            foreach (var (team, picks, q) in new[] { (Team.Dawn, line.Dawn, dawnQ), (Team.Dusk, line.Dusk, duskQ) })
                for (int j = 0; j < picks.Count; j++)
                {
                    var player = Dynasty.Players.PlayerGenerator.Generate(random, picks[j].HeroId, j + 1, q, new[] { picks[j].HeroId });
                    settings.SkillsOf(team)[picks[j].HeroId] = HeroSkill.FromPlayer(player, picks[j].HeroId);
                }

            foreach (string property in equalize)
            {
                var info = typeof(HeroSkill).GetProperty(property) ?? throw new ArgumentException("No HeroSkill property " + property);
                foreach (var pick in line.Dawn)
                    info.SetValue(settings.SkillsOf(stronger.Opponent())[pick.HeroId], info.GetValue(settings.SkillsOf(stronger)[pick.HeroId]));
            }

            var k = Match.Create(Rules(), seed, line);
            HeroAi.Attach(k, settings);
            k.RunUntilFinished(60L * 60 * 30);
            return k;
        }).ToList();

        int Sum(Team team, Func<Dynasty.MatchSimulation.Heroes.HeroState, int> value) => runs.Sum(k => k.State.Heroes.Where(h => h.Team == team).Sum(h => value(h.Hero!)));
        int early(Team lost) => runs.Sum(k => k.Events.Records.Count(r => r.Tick.Value < 12 * 1800 && r.Event is StructureDestroyed sd && sd.Team == lost));
        int lh10(Team team) => runs.Sum(k => k.Events.Records.Count(r => r.Tick.Value < 10 * 1800 && r.Event is GoldEarned g && g.Reason == "last_hit" &&
                                                                       k.State.Heroes.Any(h => h.Id == g.Hero && h.Team == team)));
        (double Towers, double Creeps, double Heroes) Taken(Team team)
        {
            double towers = 0, creeps = 0, heroes = 0;
            foreach (var k in runs)
            {
                var victims = k.State.Heroes.Where(h => h.Team == team).Select(h => h.Id).ToHashSet();
                var heroIds = k.State.Heroes.Select(h => h.Id).ToHashSet();
                var structureIds = k.Events.Records.Select(r => r.Event).OfType<StructureDestroyed>().Select(e => e.Structure)
                    .Concat(k.State.Units.Where(u => u.Kind == UnitKind.Structure).Select(u => u.Id)).ToHashSet();
                foreach (var d in k.Events.Records.Select(r => r.Event).OfType<DamageDealt>())
                {
                    if (!victims.Contains(d.Target)) continue;
                    if (heroIds.Contains(d.Attacker)) heroes += d.Amount.ToDouble();
                    else if (structureIds.Contains(d.Attacker)) towers += d.Amount.ToDouble();
                    else creeps += d.Amount.ToDouble();
                }
            }

            return (towers / n, creeps / n, heroes / n);
        }

        int wins = runs.Count(k => k.State.Winner == Team.Dawn);
        strongerWins += stronger == Team.Dawn ? wins : n - wins;
        Console.WriteLine($"Dawn q{dawnQ} vs Dusk q{duskQ}: Dawn wins {wins}/{n}, kills {Sum(Team.Dawn, h => h.Kills)}:{Sum(Team.Dusk, h => h.Kills)}, " +
                          $"last hits {Sum(Team.Dawn, h => h.LastHits)}:{Sum(Team.Dusk, h => h.LastHits)}, structures lost before 12 min {early(Team.Dawn)}:{early(Team.Dusk)}, last hits by 10 min {lh10(Team.Dawn)}:{lh10(Team.Dusk)}");
        foreach (Team t in new[] { Team.Dawn, Team.Dusk })
            Console.WriteLine($"  {t} heroes take per match: from towers {Taken(t).Towers:0}, creeps and others {Taken(t).Creeps:0}, heroes {Taken(t).Heroes:0}");
    }

    Console.WriteLine($"stronger players win {strongerWins}/{2 * n}" + (equalize.Length > 0 ? $" (equalized: {string.Join(",", equalize)})" : ""));
    return;
}

if (mode == "profiles")
{
    // Draft profiles derived from the base ruleset (Dynasty.Drafting.HeroProfile).
    RulesetContent rules = Rules();
    Console.WriteLine($"{"hero",-14}{"roles",-26}{"ctrl",5}{"burst",6}{"mag%",5}{"init",5}{"sust",5}{"push",5}{"scale",6}{"tempo",6}{"dura",5}{"mob",5}{"rng",4}{"disp",5}");
    foreach (HeroDefinition hero in rules.Heroes)
    {
        var p = Dynasty.Drafting.HeroProfile.Of(rules, hero.Id);
        Console.WriteLine($"{hero.Id,-14}{string.Join(",", hero.Roles),-26}{p.Control,5}{p.Burst,6}{p.MagicalPercent,5}{p.Initiation,5}{p.Sustain,5}{p.Push,5}{p.Scaling,6}{p.Tempo,6}{p.Durability,5}{p.Mobility,5}{(p.Ranged ? "R" : "M"),4}{(p.Dispel ? "y" : ""),5}");
    }

    return;
}

if (mode == "towerdiag")
{
    // Who deals the damage to the outer (T1) towers before minute 15, and when they fall.
    int n = args.Length > 1 ? int.Parse(args[1]) : 16;
    var byKind = new Dictionary<string, double>();
    var falls = new List<double>();
    for (ulong seed = 1; seed <= (ulong)n; seed++)
    {
        var k = Match.Create(Rules(), seed, MirrorLineup());
        HeroAi.Attach(k);
        var t1Units = k.State.Units.Where(u => u.Kind == UnitKind.Structure && u.Placement != null && u.Placement.Id.EndsWith(".t1")).Select(u => u.Id).ToHashSet();
        k.RunUntilFinished(15 * 1800L);
        var types = k.Events.Records.Select(r => r.Event).OfType<CreepSpawned>().ToDictionary(e => e.Unit, e => e.CreepTypeId);
        var heroes = k.State.Heroes.Select(h => h.Id).ToHashSet();
        var t1 = k.State.Ruleset.Map.Structures.Where(p => p.Id.EndsWith(".t1")).Select(p => p.Id).ToHashSet();
        foreach (var r in k.Events.Records)
        {
            if (r.Event is StructureDestroyed sd && t1.Contains(sd.PlacementId)) falls.Add(r.Tick.Value / 1800.0);
            if (r.Event is DamageDealt d)
            {
                if (!t1Units.Contains(d.Target)) continue;
                string kind = heroes.Contains(d.Attacker) ? "hero" : types.TryGetValue(d.Attacker, out var ct) ? ct : "other";
                byKind[kind] = byKind.GetValueOrDefault(kind) + d.Amount.ToDouble() / n;
            }
        }
    }
    foreach (var kv in byKind.OrderByDescending(x => x.Value)) Console.WriteLine($"{kv.Key,-12} {kv.Value:0} damage to T1 towers per match (before 15 min)");
    Console.WriteLine($"T1 falls before 15 min: {falls.Count / (double)n:0.0} per match, at minutes: " + string.Join(" ", falls.OrderBy(x => x).Select(x => x.ToString("0.0"))));
    return;
}

if (mode == "dps")
{
    // Dawn heroes' level, attack damage per second, health, net worth and items every five minutes.
    var k = Match.Create(Rules(), args.Length > 1 ? ulong.Parse(args[1]) : 1, MirrorLineup());
    HeroAi.Attach(k);
    foreach (int minute in new[] { 5, 10, 15, 20, 25 })
    {
        k.RunUntilFinished(minute * 1800L - k.CurrentTick.Value);
        foreach (MatchUnit h in k.State.Heroes.Where(h => h.Team == Team.Dawn))
        {
            double avg = (h.AttackDamageMin + h.AttackDamageMax) / 2.0;
            double interval = h.Stats.AttackIntervalMs * 100.0 / (100 + h.AttackSpeedBonus) / 1000.0;
            Console.WriteLine($"{minute,2} {h.Hero!.Definition.Id,-12} lvl {h.Hero.Level,2} dmg {avg,5:0} int {interval:0.00}s dps {avg / interval,5:0} hp {h.MaxHp,5} nw {QualityAudit.NetWorth(h),5} items: {string.Join(",", h.Hero.Inventory.Select(i => i.Definition.Id))}");
        }
    }
    return;
}

if (mode == "trace")
{
    // Second by second: position, health, order and deciding branch of one Dawn hero, and the nearest camp.
    ulong seed = ulong.Parse(args[1]); string heroId = args[2]; int from = int.Parse(args[3]), to = int.Parse(args[4]);
    var k = Match.Create(Rules(), seed, MirrorLineup());
    MatchUnit hero = k.State.Heroes.Single(h => h.Team == Team.Dawn && h.Hero!.Definition.Id == heroId);
    string last = "";
    HeroAi.Attach(k, new HeroAiSettings { DecisionTrace = (h, now, branch) => { if (h == hero) last = branch; } });
    k.RunUntilFinished(from * 1800L);
    for (long t = from * 1800L; t < to * 1800L; t += 30)
    {
        k.RunUntilFinished(30);
        var camp = k.State.Ruleset.Map.Camps.OrderBy(c => Dynasty.Core.Mathematics.FixedVector2.DistanceSquared(c.Position, hero.Position)).First();
        int alive = k.State.Units.Count(u => u.Team == Team.Neutral && u.Kind == UnitKind.Creep && u.IsAlive && Dynasty.Core.Mathematics.FixedVector2.Distance(u.Position, camp.Position) < Dynasty.Core.Mathematics.Fixed.FromInt(700));
        var target = k.State.Find(hero.Hero!.Order.Target);
        Console.WriteLine($"{t / 1800}:{t % 1800 / 30:00} ({hero.Position.X.ToIntFloor(),5},{hero.Position.Y.ToIntFloor(),5}) hp{hero.Hp.ToIntFloor() * 100 / hero.MaxHp,3}% lh{hero.Hero.LastHits,3} {hero.Hero.Order.Kind}{(target != null ? "->" + target.TypeId : "")} | {last} | camp {camp.Id} alive {alive}");
    }
    return;
}

if (mode == "xpdiag")
{
    // Lane creep deaths between two minutes by how many enemy heroes share their XP (1500 radius): 0 = XP lost.
    int n = args.Length > 1 ? int.Parse(args[1]) : 16;
    int from = args.Length > 2 ? int.Parse(args[2]) : 0, to = args.Length > 3 ? int.Parse(args[3]) : 10;
    var buckets = Enumerable.Range(1, n).AsParallel().Select(seed =>
    {
        var k = Match.Create(Rules(), (ulong)seed, MirrorLineup());
        var branch = new Dictionary<long, string>();
        HeroAi.Attach(k, new HeroAiSettings { DecisionTrace = (h, now, b) => branch[h.Id.Value] = b });
        var counts = new int[6];
        var lostBy = new Dictionary<string, int>();
        var alive = new Dictionary<long, (Dynasty.Core.Mathematics.FixedVector2 Pos, Team Team)>();
        int radius = k.State.Ruleset.HeroRules!.XpShareRadius;
        for (long t = 0; t < to * 1800L && !k.IsFinished; t++)
        {
            k.RunUntilFinished(1);
            var now = k.State.Units.Where(u => u.Kind == UnitKind.Creep && u.IsAlive && u.LaneId != null && u.Team != Team.Neutral).ToDictionary(u => u.Id.Value, u => (u.Position, u.Team));
            if (t >= from * 1800L)
                foreach (var (id, (pos, team)) in alive)
                    if (!now.ContainsKey(id))
                    {
                        int near = k.State.Heroes.Count(h => h.Team != team && h.IsAlive &&
                            Dynasty.Core.Mathematics.FixedVector2.Distance(h.Position, pos) <= Dynasty.Core.Mathematics.Fixed.FromInt(radius));
                        counts[Math.Min(near, 5)]++;
                        if (near == 0)
                        {
                            string lane = k.State.Ruleset.Map.Lanes.OrderBy(l => l.Waypoints.Min(w => Dynasty.Core.Mathematics.FixedVector2.DistanceSquared(w, pos))).First().Id;
                            MatchUnit? core = k.State.Heroes.Where(h => h.Team != team && h.Hero!.Definition.Roles.Any(r => r == "carry" || r == "mid" || r == "offlane"))
                                .OrderBy(h => Dynasty.Core.Mathematics.FixedVector2.DistanceSquared(h.Position, pos)).FirstOrDefault();
                            string why = core == null ? "?" : core.Hero!.IsDead ? "nearest core dead" : "nearest core " + (branch.TryGetValue(core.Id.Value, out var b) ? b : "?");
                            lostBy[why] = lostBy.GetValueOrDefault(why) + 1;
                        }
                    }
            alive = now;
        }
        return (counts, lostBy);
    }).ToList();
    var sum = new int[6];
    foreach (var (c, _) in buckets) for (int i = 0; i < 6; i++) sum[i] += c[i];
    var lost = buckets.SelectMany(b => b.lostBy).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
    double total = sum.Sum();
    Console.WriteLine($"lane creep deaths minutes {from}-{to}: {total / n:0} per match; XP shared by 0 heroes {sum[0] / total:P0}, 1 {sum[1] / total:P0}, 2 {sum[2] / total:P0}, 3+ {(sum[3] + sum[4] + sum[5]) / total:P0}");
    Console.WriteLine("XP lost while the nearest enemy core was: " + string.Join(", ", lost.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value * 100.0 / lost.Values.Sum():0}%")));
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

RulesetContent ruleset = mode == "lab" ? BaseRuleset.CreateDawnSiegeLab() : Rules();
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
    var minibossKills = records.Select(r => r.Event).OfType<MinibossKilled>().ToList();
    var shrinesTaken = records.Select(r => r.Event).OfType<ShrineTaken>().ToList();
    int gateUses = records.Count(r => r.Event is TwinGateUsed);
    int aegisPicked = records.Count(r => r.Event is AegisPicked);
    int aegisDenied = records.Count(r => r.Event is AegisDenied);
    int lotuses = records.Count(r => r.Event is LotusPicked);
    int watchersOn = records.Count(r => r.Event is WatcherTaken);
    int outpostsTaken = records.Count(r => r.Event is OutpostCaptured);
    if (Environment.GetEnvironmentVariable("MATCHLAB_MINIBOSS") == "1")
    {
        var spawned = records.Select(r => r.Event).OfType<MinibossSpawned>().Select(e => e.Miniboss.Value).ToHashSet();
        var hits = records.Where(r => r.Event is DamageDealt d && spawned.Contains(d.Target.Value)).ToList();
        Console.WriteLine($"   miniboss: spawns {spawned.Count}, hits {hits.Count}, first hit at {(hits.Count > 0 ? hits[0].Tick.Value / 30 / 60 + ":" + (hits[0].Tick.Value / 30 % 60).ToString("00") : "-")}");
        foreach (var g in hits.GroupBy(h => h.Tick.Value / 30 / 30))
            Console.WriteLine($"     {g.Key * 30 / 60}:{g.Key * 30 % 60:00} hits {g.Count()} by {g.Select(h => ((DamageDealt)h.Event).Attacker.Value).Distinct().Count()} dealers, damage {g.Sum(h => ((DamageDealt)h.Event).Amount.ToIntFloor())}");
    }
    // The base rules' observer ward or the Dota pack's.
    var wards = records.Select(r => r.Event).OfType<UnitSummoned>().Count(s => s.CreepTypeId is "observer_ward" or "ward_observer_unit");
    Console.WriteLine($"   boss kills: Dawn {bossKills.Count(b => b.Team == Team.Dawn)}, Dusk {bossKills.Count(b => b.Team == Team.Dusk)}; miniboss kills: Dawn {minibossKills.Count(b => b.Team == Team.Dawn)}, Dusk {minibossKills.Count(b => b.Team == Team.Dusk)}; shrines: Dawn {shrinesTaken.Count(t => t.Team == Team.Dawn)}, Dusk {shrinesTaken.Count(t => t.Team == Team.Dusk)}; lotuses {lotuses}, watchers on {watchersOn}, outposts taken {outpostsTaken}, gate uses {gateUses}; aegis picked {aegisPicked}, denied {aegisDenied}; observer wards placed: {wards}");

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
    Console.WriteLine("   hero          team  lvl  K/D/A     LH/DN    gold   dmg     heal   casts  tal +2  items");
    foreach (MatchUnit hero in kernel.State.Heroes)
    {
        var h = hero.Hero!;
        string items = string.Join(",", h.Inventory.Select(i => i.Definition.Id));
        Console.WriteLine($"   {h.Definition.Name,-13} {hero.Team,-5} {h.Level,3}  {h.Kills,2}/{h.Deaths,2}/{h.Assists,2}  {h.LastHits,3}/{h.Denies,-3} {h.Gold.ToIntFloor(),6} {damage.GetValueOrDefault(hero.Id),7:0} {heals.GetValueOrDefault(hero.Id),6:0} {casts.GetValueOrDefault(hero.Id),6}  {h.Talents.Count,3} {h.AttributeBonusLevel,2}  {items}");
    }
}

Console.WriteLine();
Console.WriteLine($"Summary: Dawn {dawnWins}, Dusk {duskWins}, unfinished {count - dawnWins - duskWins}");
Console.WriteLine($"Hero kills: Dawn {dawnKills}, Dusk {duskKills}; structures lost: Dawn {dawnStructuresLost}, Dusk {duskStructuresLost}");

static MatchLineup MirrorLineup()
{
    // Identical heroes on both sides; Dusk lanes mirrored (top <-> bot) so matchups are symmetric.
    // Order: carry, support 5 (safe lane), mid, offlaner, support 4 (off lane) — RoleOf relies on it.
    // MATCHLAB_HEROES=a,b,c,d,e overrides the heroes (same order) to try other abilities on a pack.
    string[] ids = Environment.GetEnvironmentVariable("MATCHLAB_HEROES")?.Split(',') ?? (PackPath() != null
        ? new[] { "juggernaut", "lion", "nevermore", "mars", "earthshaker" }
        : new[] { "ironvow", "dawnmender", "emberwisp", "stonewarden", "thornkeeper" });
    string[] lanes = { "bot", "bot", "mid", "top", "top" };
    HeroPick[] dawn = ids.Select((id, i) => new HeroPick(id.Trim(), lanes[i])).ToArray();
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

static string? PackPath() => Environment.GetEnvironmentVariable("MATCHLAB_PACK");

/// <summary>The ruleset matches play on: the base heroes, or a ruleset pack applied to the base (MATCHLAB_PACK).</summary>
static RulesetContent Rules() => RulesCache.Value;

/// <summary>Role of each hero of the mirrored line-up, by its position in it.</summary>
static Dictionary<string, string> RoleOf()
{
    string[] roles = { "carry", "support5", "mid", "offlane", "support4" };
    return MirrorLineup().Dawn.Select((p, i) => (p.HeroId, roles[i])).ToDictionary(x => x.HeroId, x => x.Item2);
}

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
    public Dictionary<long, int> LastHitsAt20 { get; } = new();
    public Dictionary<long, int> NetWorthAt10 { get; } = new();
    public Dictionary<long, int> NetWorthAt20 { get; } = new();
    public Dictionary<long, int> XpAt10 { get; } = new();
    public Dictionary<long, int> XpAt20 { get; } = new();

    /// <summary>Unspent gold plus the full cost of carried items.</summary>
    public static int NetWorth(MatchUnit hero) => hero.Hero!.Gold.ToIntFloor() + hero.Hero.Inventory.Sum(i => i.Definition.Cost);
    public List<(string PlacementId, int Near, long Tick)> Destroyed { get; } = new();

    public static bool IsHighGround(string placementId) =>
        placementId.EndsWith(".t3") || placementId.EndsWith(".barracks") || placementId.Contains(".t4") || placementId.EndsWith(".ancient");

    public void Tick(SimulationContext<MatchState> context)
    {
        MatchState state = context.State;
        if (context.Tick.Value == 10 * 1800)
            foreach (MatchUnit hero in state.Heroes)
            {
                LastHitsAt10[hero.Id.Value] = hero.Hero!.LastHits;
                NetWorthAt10[hero.Id.Value] = NetWorth(hero);
                XpAt10[hero.Id.Value] = hero.Hero.Xp;
            }

        if (context.Tick.Value == 20 * 1800)
            foreach (MatchUnit hero in state.Heroes)
            {
                LastHitsAt20[hero.Id.Value] = hero.Hero!.LastHits;
                NetWorthAt20[hero.Id.Value] = NetWorth(hero);
                XpAt20[hero.Id.Value] = hero.Hero.Xp;
            }

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

static class RulesCache
{
    public static readonly RulesetContent Value = Load();

    private static RulesetContent Load()
    {
        string? pack = Environment.GetEnvironmentVariable("MATCHLAB_PACK");
        return pack == null ? BaseRuleset.Create() : Dynasty.Content.RulesetPack.Apply(BaseRuleset.Create(), File.ReadAllText(pack));
    }
}
