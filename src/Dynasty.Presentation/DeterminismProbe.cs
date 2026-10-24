using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using Dynasty.Core.Kernel;
using Dynasty.Core.Randomness;
using Dynasty.Drafting;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Presentation
{
    /// <summary>The lineup the demo viewer and the determinism probe play (the base ruleset's reference teams), and two demo rosters for drafting.</summary>
    public static class DemoMatch
    {
        private static readonly string[] DawnHandles = { "Kestrel", "Vanta", "Bastion", "Lumen", "Moss" };
        private static readonly string[] DuskHandles = { "Cinder", "Hollow", "Rook", "Wisp", "Thistle" };

        /// <summary>
        /// Two original demo teams. Each player covers the heroes that suit their position with a
        /// seeded mastery (35–80) and one signature hero (90+). Deterministic.
        /// </summary>
        public static (TeamRoster Dawn, TeamRoster Dusk) Rosters()
        {
            RulesetContent ruleset = BaseRuleset.Create();
            return (Roster(ruleset, "Northwind", DawnHandles, 11), Roster(ruleset, "Ember Pact", DuskHandles, 23));
        }

        private static TeamRoster Roster(RulesetContent ruleset, string name, string[] handles, ulong seed)
        {
            DeterministicRandom random = new RandomStreams(seed).Get("roster");
            var players = new List<RosterPlayer>();
            for (int position = 1; position <= 5; position++)
            {
                var suited = ruleset.Heroes.Where(h => PositionPlan.Fit(h, position) >= 9).Select(h => h.Id).ToList();
                string signature = suited[random.NextInt(0, suited.Count)];
                var pool = suited.Select(id => new HeroPoolEntry(id, id == signature ? random.NextInt(90, 101) : random.NextInt(35, 81), random.NextInt(40, 91))).ToList();
                players.Add(new RosterPlayer(name + "." + position, handles[position - 1], position, pool));
            }

            return new TeamRoster(name, players);
        }

        public static readonly MatchLineup Lineup = new MatchLineup(
            new[]
            {
                new HeroPick("ironvow", "bot"), new HeroPick("dawnmender", "bot"), new HeroPick("emberwisp", "mid"),
                new HeroPick("stonewarden", "top"), new HeroPick("thornkeeper", "top"),
            },
            new[]
            {
                new HeroPick("starborn", "top"), new HeroPick("shadeblade", "top"), new HeroPick("frostweaver", "mid"),
                new HeroPick("hivecaller", "bot"), new HeroPick("siegewright", "bot"),
            });
    }

    /// <summary>
    /// Plays demo matches headless and reports their event checksums, so the same build of the core
    /// can be compared across runtimes (.NET, Unity Mono, Unity IL2CPP). Different checksums for the
    /// same seed, tick count and ruleset mean the simulation is not deterministic there.
    /// </summary>
    public static class DeterminismProbe
    {
        public static readonly ulong[] DefaultSeeds = { 1, 2, 3 };
        public const long DefaultTicks = 9000;   // five minutes: laning, fights, first objectives

        public static ProbeResult Run(ulong seed, long ticks)
        {
            RulesetContent ruleset = BaseRuleset.Create();
            SimulationKernel<MatchState> kernel = Match.Create(ruleset, seed, DemoMatch.Lineup);
            HeroAi.Attach(kernel);
            kernel.RunTicks(ticks);
            return new ProbeResult(seed, kernel.CurrentTick.Value, kernel.Events.Checksum, ruleset.ToDescriptor().ToString());
        }

        public static List<ProbeResult> RunDefault()
        {
            var results = new List<ProbeResult>();
            foreach (ulong seed in DefaultSeeds)
                results.Add(Run(seed, DefaultTicks));
            return results;
        }

        /// <summary>One line per result: seed, ticks, checksum (hex), ruleset.</summary>
        public static string Format(IEnumerable<ProbeResult> results)
        {
            var text = new StringBuilder();
            foreach (ProbeResult r in results)
                text.Append(r.Seed.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(r.Ticks.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(r.Checksum.ToString("x16", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(r.Ruleset).Append('\n');
            return text.ToString();
        }

        public static List<ProbeResult> Parse(string text)
        {
            var results = new List<ProbeResult>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                string[] parts = line.Split(new[] { ' ' }, 4);
                if (parts.Length != 4)
                    throw new FormatException("Bad probe line: " + line);
                results.Add(new ProbeResult(
                    ulong.Parse(parts[0], CultureInfo.InvariantCulture),
                    long.Parse(parts[1], CultureInfo.InvariantCulture),
                    ulong.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    parts[3]));
            }

            return results;
        }
    }

    public sealed class ProbeResult
    {
        public ProbeResult(ulong seed, long ticks, ulong checksum, string ruleset)
        {
            Seed = seed;
            Ticks = ticks;
            Checksum = checksum;
            Ruleset = ruleset;
        }

        public ulong Seed { get; }
        public long Ticks { get; }
        public ulong Checksum { get; }
        public string Ruleset { get; }
    }
}
