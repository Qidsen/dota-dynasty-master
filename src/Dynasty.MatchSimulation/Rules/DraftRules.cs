using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;

namespace Dynasty.MatchSimulation.Rules
{
    public enum DraftAction
    {
        Ban,
        Pick,
    }

    /// <summary>Who acts: the side that won the first pick, or the other one.</summary>
    public enum DraftSide
    {
        First,
        Second,
    }

    public sealed class DraftStep
    {
        public DraftStep(DraftSide side, DraftAction action)
        {
            Side = side;
            Action = action;
        }

        public DraftSide Side { get; }
        public DraftAction Action { get; }
    }

    /// <summary>
    /// Versioned pick/ban format of a ruleset (spec §22): the order of bans and picks and the heroes
    /// that are not available. Part of the ruleset fingerprint, so a patch can change it.
    /// </summary>
    public sealed class DraftRules
    {
        public DraftRules(IReadOnlyList<DraftStep> steps, IReadOnlyList<string>? disabledHeroes = null)
        {
            Steps = steps.ToArray();
            DisabledHeroes = (disabledHeroes ?? Array.Empty<string>()).ToArray();
        }

        public IReadOnlyList<DraftStep> Steps { get; }

        /// <summary>Heroes that cannot be picked or banned under these rules.</summary>
        public IReadOnlyList<string> DisabledHeroes { get; }

        /// <summary>
        /// Captains-mode style: bans 2+2, picks first-second-second-first, bans 2+2,
        /// picks second-first-first-second, last picks first-second. Four bans and five picks per side.
        /// </summary>
        public static DraftRules CaptainsMode()
        {
            const DraftSide F = DraftSide.First;
            const DraftSide S = DraftSide.Second;
            var steps = new List<DraftStep>();
            void Add(DraftAction action, params DraftSide[] sides)
            {
                foreach (DraftSide side in sides)
                    steps.Add(new DraftStep(side, action));
            }

            Add(DraftAction.Ban, F, S, F, S);
            Add(DraftAction.Pick, F, S, S, F);
            Add(DraftAction.Ban, F, S, F, S);
            Add(DraftAction.Pick, S, F, F, S);
            Add(DraftAction.Pick, F, S);
            return new DraftRules(steps);
        }

        internal void Validate(List<string> errors, IReadOnlyList<HeroDefinition> heroes)
        {
            foreach (DraftSide side in new[] { DraftSide.First, DraftSide.Second })
            {
                if (Steps.Count(s => s.Side == side && s.Action == DraftAction.Pick) != MatchLineup.MaxHeroesPerTeam)
                    errors.Add("Draft rules must give each side exactly five picks.");
            }

            foreach (string hero in DisabledHeroes.Where(h => heroes.All(d => d.Id != h)))
                errors.Add("Draft rules disable unknown hero '" + hero + "'.");

            int available = heroes.Count(h => !DisabledHeroes.Contains(h.Id));
            if (available < Steps.Count)
                errors.Add("Draft rules need " + Steps.Count + " heroes, but only " + available + " are available.");
        }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Steps.Count);
            foreach (DraftStep step in Steps)
            {
                w.WriteInt64((int)step.Side);
                w.WriteInt64((int)step.Action);
            }

            w.WriteInt64(DisabledHeroes.Count);
            foreach (string hero in DisabledHeroes)
                w.WriteString(hero);
        }
    }
}
