using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting
{
    /// <summary>
    /// Five drafted heroes on positions 1–5 and their lanes: 1 carry and 5 hard support in the safe
    /// lane, 2 mid, 3 offlaner and 4 soft support in the off lane. Chosen by the best total role fit
    /// over all 120 assignments (ties broken by the order the heroes were given).
    /// </summary>
    public sealed class PositionPlan
    {
        private readonly string[] _byPosition;   // index 0 = position 1

        private PositionPlan(Team team, string[] byPosition)
        {
            Team = team;
            _byPosition = byPosition;
        }

        public Team Team { get; }

        public static PositionPlan For(RulesetContent ruleset, Team team, IReadOnlyList<string> heroes)
        {
            if (heroes.Count != MatchLineup.MaxHeroesPerTeam)
                throw new ArgumentException("A position plan needs exactly five heroes.", nameof(heroes));

            var definitions = heroes.Select(id => ruleset.FindHero(id) ?? throw new ArgumentException("Unknown hero '" + id + "'.", nameof(heroes))).ToArray();
            int[]? best = null;
            int bestScore = int.MinValue;
            foreach (int[] order in Permutations(5))
            {
                int score = 0;
                for (int position = 0; position < 5; position++)
                    score += Fit(definitions[order[position]], position + 1);
                if (score > bestScore)
                {
                    best = order;
                    bestScore = score;
                }
            }

            return new PositionPlan(team, best!.Select(i => heroes[i]).ToArray());
        }

        public static MatchLineup Lineup(PositionPlan dawn, PositionPlan dusk)
        {
            if (dawn.Team != Team.Dawn || dusk.Team != Team.Dusk)
                throw new ArgumentException("Plans must be Dawn's and Dusk's.");
            return new MatchLineup(dawn.Picks(), dusk.Picks());
        }

        /// <summary>The hero on a position (1–5).</summary>
        public string HeroAt(int position) => _byPosition[position - 1];

        public int PositionOf(string heroId) => Array.IndexOf(_byPosition, heroId) + 1;

        public string LaneOf(string heroId) => LaneFor(Team, PositionOf(heroId));

        /// <summary>Safe lane = bottom for Dawn, top for Dusk; the off lane is the other side lane.</summary>
        public static string LaneFor(Team team, int position)
        {
            string safe = team == Team.Dawn ? "bot" : "top";
            string off = team == Team.Dawn ? "top" : "bot";
            return position == 2 ? "mid" : position == 1 || position == 5 ? safe : off;
        }

        private HeroPick[] Picks() => _byPosition.Select((hero, i) => new HeroPick(hero, LaneFor(Team, i + 1))).ToArray();

        /// <summary>How well a hero's role tags suit a position.</summary>
        internal static int Fit(HeroDefinition hero, int position)
        {
            bool Has(string role) => hero.Roles.Contains(role);
            switch (position)
            {
                case 1: return (Has("carry") ? 10 : 0) + (Has("late") ? 3 : 0) - (Has("support") ? 8 : 0);
                case 2: return (Has("mid") ? 10 : 0) + (Has("nuker") || Has("assassin") ? 2 : 0) - (Has("support") ? 6 : 0);
                case 3: return (Has("offlane") ? 10 : 0) + (Has("initiator") ? 2 : 0) - (Has("support") && !Has("offlane") ? 4 : 0);
                case 4: return (Has("support") ? 9 : 0) + (Has("initiator") ? 2 : 0) - (Has("carry") ? 6 : 0);
                default: return (Has("support") ? 10 : 0) + (Has("healer") ? 2 : 0) - (Has("carry") ? 8 : 0);
            }
        }

        private static IEnumerable<int[]> Permutations(int n)
        {
            var items = Enumerable.Range(0, n).ToArray();
            return Permute(items, 0);
        }

        private static IEnumerable<int[]> Permute(int[] items, int start)
        {
            if (start == items.Length)
            {
                yield return (int[])items.Clone();
                yield break;
            }

            for (int i = start; i < items.Length; i++)
            {
                (items[start], items[i]) = (items[i], items[start]);
                foreach (int[] p in Permute(items, start + 1))
                    yield return p;
                (items[start], items[i]) = (items[i], items[start]);
            }
        }
    }
}
