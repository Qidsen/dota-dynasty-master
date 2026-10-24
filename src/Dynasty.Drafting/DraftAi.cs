using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Randomness;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting
{
    public sealed class DraftAiSettings
    {
        /// <summary>
        /// How unsure the team is about each hero's real strength in the current patch (spec §22):
        /// a fixed per-team error of up to this percent on the perceived meta value. Analysts reduce it later.
        /// </summary>
        public int MetaUncertaintyPercent { get; set; } = 20;

        public int ComfortWeight { get; set; } = 3;
        public int CompositionWeight { get; set; } = 2;
        public int CounterWeight { get; set; } = 2;
        public int SynergyWeight { get; set; } = 1;
        public int MetaWeight { get; set; } = 1;
    }

    /// <summary>
    /// Drafting AI for one team (spec §22). Picks weigh the comfort of the player who would play the
    /// hero, what the hero adds to the composition (control, initiation, sustain, damage mix, a win
    /// condition), counters to the enemy's picks, synergy with its own, and the team's — imperfect —
    /// reading of the meta. Bans take away the opponent's signature heroes and threats to its own
    /// picks. Integer scores and a deterministic per-team RNG: the same inputs give the same draft.
    /// </summary>
    public sealed class DraftAi
    {
        /// <summary>A team wants at least this much summed initiation and control.</summary>
        public const int WantedInitiation = 60;

        public const int WantedControl = 120;

        private readonly RulesetContent _ruleset;
        private readonly Team _team;
        private readonly TeamRoster _own;
        private readonly TeamRoster _opponent;
        private readonly DraftAiSettings _settings;
        private readonly Dictionary<string, HeroProfile> _profiles;
        private readonly Dictionary<string, int> _perceivedMeta = new Dictionary<string, int>();

        public DraftAi(RulesetContent ruleset, Team team, TeamRoster own, TeamRoster opponent, DraftAiSettings settings, ulong seed)
        {
            _ruleset = ruleset;
            _team = team;
            _own = own;
            _opponent = opponent;
            _settings = settings;
            _profiles = ruleset.Heroes.ToDictionary(h => h.Id, h => HeroProfile.Of(h, ruleset.Abilities));

            DeterministicRandom random = new RandomStreams(seed).Get("draft.meta");
            foreach (HeroDefinition hero in ruleset.Heroes)
            {
                int truth = Strength(_profiles[hero.Id]);
                int error = settings.MetaUncertaintyPercent <= 0 ? 0 : random.NextInt(-settings.MetaUncertaintyPercent, settings.MetaUncertaintyPercent + 1);
                _perceivedMeta[hero.Id] = truth * (100 + error) / 100;
            }
        }

        /// <summary>The hero to ban or pick now; the session must be waiting for this team.</summary>
        public string Choose(DraftSession session)
        {
            if (session.TeamToAct != _team)
                throw new InvalidOperationException("It is not " + _team + "'s turn.");

            IReadOnlyList<string> ownPicks = session.PicksOf(_team);
            IReadOnlyList<string> enemyPicks = session.PicksOf(_team.Opponent());
            Func<string, int> score = session.ActionToTake == DraftAction.Ban
                ? (Func<string, int>)(h => BanScore(h, ownPicks, enemyPicks))
                : h => PickScore(h, ownPicks, enemyPicks);

            string best = session.AvailableHeroes[0];
            int bestScore = int.MinValue;
            foreach (string hero in session.AvailableHeroes)   // ruleset order breaks ties
            {
                int value = score(hero);
                if (value > bestScore)
                {
                    best = hero;
                    bestScore = value;
                }
            }

            return best;
        }

        /// <summary>How much hero <paramref name="a"/> troubles hero <paramref name="b"/> (profile-based).</summary>
        public static int Counter(RulesetContent ruleset, string a, string b) =>
            Counter(HeroProfile.Of(ruleset, a), HeroProfile.Of(ruleset, b));

        internal static int Counter(HeroProfile a, HeroProfile b)
        {
            int value = 0;
            value += a.Control * b.Mobility / 20;                         // disables pin down divers
            value += a.Dispel ? b.Control / 2 : 0;                         // dispels undo disables
            value += a.Burst * Math.Max(0, 80 - b.Durability) / 100;      // burst kills the fragile
            value += a.Sustain * b.Burst / 300;                            // healing outlasts attrition
            value += a.Ranged && !b.Ranged ? 5 : 0;                        // lane harass
            return value;
        }

        internal static int Synergy(HeroProfile a, HeroProfile b)
        {
            int value = 0;
            value += (a.Control * b.Burst + b.Control * a.Burst) / 200;   // lock down, then burst
            value += (a.Initiation * b.Burst + b.Initiation * a.Burst) / 300;
            value += a.Push * b.Push / 100;                                // a pushing line-up
            value += (a.Sustain * b.Durability + b.Sustain * a.Durability) / 300;
            return value;
        }

        /// <summary>Every available hero with the parts of its score for the current step, best first (explainability).</summary>
        public List<DraftCandidate> Rank(DraftSession session)
        {
            IReadOnlyList<string> ownPicks = session.PicksOf(_team);
            IReadOnlyList<string> enemyPicks = session.PicksOf(_team.Opponent());
            return session.AvailableHeroes
                .Select(h => session.ActionToTake == DraftAction.Ban ? BanCandidate(h, ownPicks) : PickCandidate(h, ownPicks, enemyPicks))
                .OrderByDescending(c => c.Total)
                .ToList();
        }

        private int PickScore(string hero, IReadOnlyList<string> ownPicks, IReadOnlyList<string> enemyPicks) =>
            PickCandidate(hero, ownPicks, enemyPicks).Total;

        private DraftCandidate PickCandidate(string hero, IReadOnlyList<string> ownPicks, IReadOnlyList<string> enemyPicks)
        {
            HeroProfile profile = _profiles[hero];
            int comfort = BestOpenSlot(hero, ownPicks);
            if (comfort == int.MinValue)
                return new DraftCandidate(hero, int.MinValue, 0, 0, 0, 0, 0);   // no free position this hero could play

            var team = ownPicks.Select(h => _profiles[h]).ToList();
            // Needs matter more as the line-up takes shape: a third at the first pick, five thirds at the last.
            int composition = (CompositionValue(team.Append(profile).ToList()) - CompositionValue(team)) * (team.Count + 1) / (3 * CompositionScale);
            int counter = enemyPicks.Sum(e => Counter(profile, _profiles[e])) - enemyPicks.Sum(e => Counter(_profiles[e], profile));
            int synergy = team.Sum(o => Synergy(profile, o));
            int meta = _perceivedMeta[hero] / 4;
            int total = _settings.ComfortWeight * comfort
                        + _settings.CompositionWeight * composition
                        + _settings.CounterWeight * counter
                        + _settings.SynergyWeight * synergy
                        + _settings.MetaWeight * meta;
            return new DraftCandidate(hero, total, comfort, composition, counter, synergy, meta);
        }

        /// <summary>Brings composition gains (hundreds of points) onto the scale of the other parts.</summary>
        private const int CompositionScale = 4;

        private int BanScore(string hero, IReadOnlyList<string> ownPicks, IReadOnlyList<string> enemyPicks) =>
            BanCandidate(hero, ownPicks).Total;

        private DraftCandidate BanCandidate(string hero, IReadOnlyList<string> ownPicks)
        {
            HeroProfile profile = _profiles[hero];
            // What the opponent would get from it: their best player's comfort on it, its meta value,
            // and how hard it counters what we already have.
            int theirComfort = 0;
            foreach (RosterPlayer player in _opponent.Players)
            {
                if (PositionPlan.Fit(_ruleset.FindHero(hero)!, player.Position) < 5)
                    continue;
                theirComfort = Math.Max(theirComfort, Comfort(player, hero));
            }

            int threat = ownPicks.Sum(o => Counter(profile, _profiles[o]));
            int meta = _perceivedMeta[hero] / 4;
            int total = _settings.ComfortWeight * theirComfort + _settings.CounterWeight * threat + _settings.MetaWeight * meta;
            return new DraftCandidate(hero, total, theirComfort, 0, threat, 0, meta);
        }

        /// <summary>
        /// Best value of placing the hero on a position still open after the best assignment of the
        /// current picks: role fit plus the comfort of the player on that position.
        /// </summary>
        private int BestOpenSlot(string hero, IReadOnlyList<string> ownPicks)
        {
            HeroDefinition definition = _ruleset.FindHero(hero)!;
            int best = int.MinValue;
            foreach (int position in OpenPositions(ownPicks))
            {
                int fit = PositionPlan.Fit(definition, position);
                if (fit < 5)
                    continue;
                best = Math.Max(best, fit * 3 + Comfort(_own.At(position), hero));
            }

            return best;
        }

        private IEnumerable<int> OpenPositions(IReadOnlyList<string> picks)
        {
            if (picks.Count == 0)
                return Enumerable.Range(1, 5);

            var definitions = picks.Select(p => _ruleset.FindHero(p)!).ToList();
            int[]? bestAssignment = null;
            int bestScore = int.MinValue;
            foreach (int[] positions in Arrangements(Enumerable.Range(1, 5).ToArray(), picks.Count))
            {
                int score = 0;
                for (int i = 0; i < positions.Length; i++)
                    score += PositionPlan.Fit(definitions[i], positions[i]) * 3 + Comfort(_own.At(positions[i]), picks[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAssignment = positions;
                }
            }

            return Enumerable.Range(1, 5).Except(bestAssignment!);
        }

        private static int Comfort(RosterPlayer player, string hero)
        {
            HeroPoolEntry? entry = player.Entry(hero);
            return entry == null ? 0 : entry.Mastery * 3 / 4 + entry.Confidence / 10;
        }

        /// <summary>
        /// Concave value of a line-up: needs met (initiation, control, sustain, a win condition) count
        /// fully, surplus only a little, and a mixed damage profile is preferred.
        /// </summary>
        private static int CompositionValue(List<HeroProfile> team)
        {
            if (team.Count == 0)
                return 0;
            int value = 0;
            value += Need(team.Sum(p => p.Initiation), WantedInitiation, 3);
            value += Need(team.Sum(p => p.Control), WantedControl, 3);
            value += Need(team.Sum(p => p.Sustain), 80, 1);
            value += Need(team.Sum(p => p.Burst), 250, 1);
            value += Need(Math.Max(team.Sum(p => p.Scaling), team.Sum(p => p.Push) * 2), 220, 1);   // late game or a push plan
            value += Need(team.Sum(p => p.Durability), 300, 1);

            int burst = team.Sum(p => p.Burst);
            if (burst > 0)
            {
                int magical = team.Sum(p => p.Burst * p.MagicalPercent) / burst;
                value -= Math.Max(0, Math.Abs(magical - 55) - 25);   // all-magic or all-physical is easy to itemise against
            }

            return value;
        }

        private static int Need(int have, int wanted, int weight) =>
            have <= wanted ? have * weight : wanted * weight + (have - wanted) * weight / 4;

        /// <summary>Overall perceived strength of a hero, the "meta" a team reads.</summary>
        private static int Strength(HeroProfile p) =>
            p.Control + p.Burst + p.Initiation + p.Sustain + p.Push + p.Scaling + p.Tempo / 2 + p.Durability / 2;

        private static IEnumerable<int[]> Arrangements(int[] items, int count)
        {
            if (count == 0)
            {
                yield return new int[0];
                yield break;
            }

            for (int i = 0; i < items.Length; i++)
            {
                int[] rest = items.Where((_, j) => j != i).ToArray();
                foreach (int[] tail in Arrangements(rest, count - 1))
                    yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }
    }

    /// <summary>
    /// One hero's score for a draft step and its parts. For a pick: comfort (role fit and the
    /// player's mastery), composition gain, counters, synergy, perceived meta. For a ban: the
    /// opponent's comfort on it, its threat to our picks (in <see cref="Counter"/>) and meta.
    /// </summary>
    public sealed class DraftCandidate
    {
        public DraftCandidate(string heroId, int total, int comfort, int composition, int counter, int synergy, int meta)
        {
            HeroId = heroId;
            Total = total;
            Comfort = comfort;
            Composition = composition;
            Counter = counter;
            Synergy = synergy;
            Meta = meta;
        }

        public string HeroId { get; }
        public int Total { get; }
        public int Comfort { get; }
        public int Composition { get; }
        public int Counter { get; }
        public int Synergy { get; }
        public int Meta { get; }

        public override string ToString() => $"{HeroId}: {Total} (comfort {Comfort}, composition {Composition}, counter {Counter}, synergy {Synergy}, meta {Meta})";
    }
}
