using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using Dynasty.Core.Kernel;
using Dynasty.Core.Randomness;
using Dynasty.Drafting;
using Dynasty.Players;
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
        /// Two original demo teams of professional players (Northwind a little stronger than Ember Pact),
        /// one per position, each with a hero pool over the heroes that suit the position and one
        /// signature hero (mastery 90+). Deterministic.
        /// </summary>
        public static (IReadOnlyList<PlayerProfile> Dawn, IReadOnlyList<PlayerProfile> Dusk) Players()
        {
            RulesetContent ruleset = BaseRuleset.Create();
            return (Team(ruleset, "northwind", DawnHandles, 15, 11), Team(ruleset, "emberpact", DuskHandles, 12, 23));
        }

        /// <summary>The demo teams as the draft sees them.</summary>
        public static (TeamRoster Dawn, TeamRoster Dusk) Rosters()
        {
            var (dawn, dusk) = Players();
            return (new TeamRoster("Northwind", dawn.Select((p, i) => RosterPlayer.From(p, i + 1)).ToList()),
                    new TeamRoster("Ember Pact", dusk.Select((p, i) => RosterPlayer.From(p, i + 1)).ToList()));
        }

        private static IReadOnlyList<PlayerProfile> Team(RulesetContent ruleset, string team, string[] handles, int quality, ulong seed)
        {
            DeterministicRandom random = new RandomStreams(seed).Get("roster");
            var players = new List<PlayerProfile>();
            for (int position = 1; position <= 5; position++)
            {
                var suited = ruleset.Heroes.Where(h => PositionPlan.Fit(h, position) >= 9).Select(h => h.Id).ToList();
                PlayerProfile generated = PlayerGenerator.Generate(random, team + "." + position, position, quality, suited, handles[position - 1]);
                string signature = suited[random.NextInt(0, suited.Count)];
                var pool = generated.HeroPool.Select(h => h.HeroId != signature ? h :
                    new HeroPoolRecord(h.HeroId, random.NextInt(90, 101), 80, 85, h.PatchFamiliarity, 80, 90, h.GamesPlayed, h.Wins)).ToList();
                players.Add(new PlayerProfile(generated.Id, generated.Handle, generated.Name, generated.Nationality, generated.BirthYear, generated.Languages,
                    generated.Region, generated.PositionProficiency, generated.Attributes, generated.Hidden, pool));
            }

            return players;
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

    /// <summary>What a probe run plays.</summary>
    public enum ProbeKind
    {
        /// <summary>The demo line-ups for a fixed number of ticks.</summary>
        Demo,

        /// <summary>The demo line-ups to the end of the match (late game, high ground, ancient).</summary>
        Full,

        /// <summary>Two AI teams draft, then their players play the match to the end (drafting, player skill, strategies).</summary>
        Drafted,
    }

    /// <summary>
    /// Plays matches headless and reports their event checksums, so the same build of the core can be
    /// compared across runtimes (.NET, Unity Mono, Unity IL2CPP). Different checksums for the same
    /// kind, seed, tick count and ruleset mean the simulation is not deterministic there.
    /// </summary>
    public static class DeterminismProbe
    {
        public static readonly ulong[] DefaultSeeds = { 1, 2, 3 };
        public const long DefaultTicks = 9000;   // five minutes: laning, fights, first objectives
        private const long MatchCap = 60L * 60 * 30;

        /// <summary>Three five-minute demo matches, two full demo matches, two drafted matches with players.</summary>
        public static List<ProbeResult> RunDefault()
        {
            var results = new List<ProbeResult>();
            foreach (ulong seed in DefaultSeeds)
                results.Add(Run(ProbeKind.Demo, seed, DefaultTicks));
            results.Add(Run(ProbeKind.Full, 4, MatchCap));
            results.Add(Run(ProbeKind.Full, 5, MatchCap));
            results.Add(Run(ProbeKind.Drafted, 6, MatchCap));
            results.Add(Run(ProbeKind.Drafted, 7, MatchCap));
            return results;
        }

        public static ProbeResult Run(ulong seed, long ticks) => Run(ProbeKind.Demo, seed, ticks);

        /// <summary>Plays one run for at most <paramref name="ticks"/> ticks (a match may end earlier).</summary>
        public static ProbeResult Run(ProbeKind kind, ulong seed, long ticks)
        {
            RulesetContent ruleset = BaseRuleset.Create();
            SimulationKernel<MatchState> kernel;
            ulong draftChecksum = 0;
            if (kind == ProbeKind.Drafted)
            {
                DraftWatch draft = DraftWatch.Start(ruleset, seed, 1.0);
                draft.Finish();
                draftChecksum = draft.Session.Events.Checksum;
                kernel = draft.StartMatch(seed).Kernel;
            }
            else
            {
                kernel = Match.Create(ruleset, seed, DemoMatch.Lineup);
                HeroAi.Attach(kernel);
            }

            kernel.RunUntilFinished(ticks);
            ulong checksum = kernel.Events.Checksum ^ (draftChecksum * 0x9E3779B97F4A7C15UL);
            return new ProbeResult(kind, seed, kernel.CurrentTick.Value, checksum, ruleset.ToDescriptor().ToString());
        }

        /// <summary>Plays a reference result again (same kind, seed and tick count).</summary>
        public static ProbeResult Replay(ProbeResult reference) => Run(reference.Kind, reference.Seed, reference.Ticks);

        /// <summary>One line per result: kind, seed, ticks, checksum (hex), ruleset.</summary>
        public static string Format(IEnumerable<ProbeResult> results)
        {
            var text = new StringBuilder();
            foreach (ProbeResult r in results)
                text.Append(r.Kind.ToString()).Append(' ')
                    .Append(r.Seed.ToString(CultureInfo.InvariantCulture)).Append(' ')
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
                string[] parts = line.Split(new[] { ' ' }, 5);
                if (parts.Length != 5)
                    throw new FormatException("Bad probe line: " + line);
                results.Add(new ProbeResult(
                    (ProbeKind)Enum.Parse(typeof(ProbeKind), parts[0]),
                    ulong.Parse(parts[1], CultureInfo.InvariantCulture),
                    long.Parse(parts[2], CultureInfo.InvariantCulture),
                    ulong.Parse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    parts[4]));
            }

            return results;
        }
    }

    public sealed class ProbeResult
    {
        public ProbeResult(ProbeKind kind, ulong seed, long ticks, ulong checksum, string ruleset)
        {
            Kind = kind;
            Seed = seed;
            Ticks = ticks;
            Checksum = checksum;
            Ruleset = ruleset;
        }

        public ProbeKind Kind { get; }
        public ulong Seed { get; }
        public long Ticks { get; }
        public ulong Checksum { get; }
        public string Ruleset { get; }
    }
}
