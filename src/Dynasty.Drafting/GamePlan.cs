using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting
{
    /// <summary>What a line-up wants to do in the match.</summary>
    public enum GameArchetype
    {
        Balanced,

        /// <summary>Take structures early with summons and siege before the enemy scales.</summary>
        Push,

        /// <summary>Group and win five-on-five fights with initiation, control and area damage.</summary>
        Teamfight,

        /// <summary>Farm, avoid even fights early, win late with scaling carries.</summary>
        LateGame,

        /// <summary>Catch heroes alone with mobility and burst.</summary>
        Pickoff,
    }

    public enum DraftStrength
    {
        StrongInitiation,
        HeavyControl,
        BigSustain,
        StrongPush,
        LateGameScaling,
        HighBurst,
        Mobile,
    }

    public enum DraftWeakness
    {
        NoInitiation,
        LittleControl,
        NoSustain,
        MostlyMagicalDamage,
        MostlyPhysicalDamage,
        WeakLateGame,
        Fragile,
    }

    /// <summary>
    /// Post-draft analysis of five heroes (spec §22): the line-up's archetype, strengths and
    /// weaknesses, and the minute it is strongest. Derived from <see cref="HeroProfile"/> totals.
    /// The team AI turns it into a match strategy, so the draft changes how the match is played,
    /// not a pre-set win chance.
    /// </summary>
    public sealed class GamePlan
    {
        private GamePlan(IReadOnlyList<string> heroes)
        {
            Heroes = heroes;
        }

        public IReadOnlyList<string> Heroes { get; }
        public GameArchetype Archetype { get; private set; }

        /// <summary>The match minute the line-up is strongest relative to an average one (10–45).</summary>
        public int PowerPeakMinute { get; private set; }

        public IReadOnlyList<DraftStrength> Strengths { get; private set; } = Array.Empty<DraftStrength>();
        public IReadOnlyList<DraftWeakness> Weaknesses { get; private set; } = Array.Empty<DraftWeakness>();

        // Totals, for display.
        public int Initiation { get; private set; }
        public int Control { get; private set; }
        public int Burst { get; private set; }
        public int Sustain { get; private set; }
        public int Push { get; private set; }
        public int Scaling { get; private set; }
        public int Mobility { get; private set; }
        public int Durability { get; private set; }
        public int MagicalPercent { get; private set; }

        public static GamePlan For(RulesetContent ruleset, IReadOnlyList<string> heroes)
        {
            var profiles = heroes.Select(h => HeroProfile.Of(ruleset, h)).ToList();
            var plan = new GamePlan(heroes.ToArray())
            {
                Initiation = profiles.Sum(p => p.Initiation),
                Control = profiles.Sum(p => p.Control),
                Burst = profiles.Sum(p => p.Burst),
                Sustain = profiles.Sum(p => p.Sustain),
                Push = profiles.Sum(p => p.Push),
                Scaling = profiles.Sum(p => p.Scaling),
                Mobility = profiles.Sum(p => p.Mobility),
                Durability = profiles.Sum(p => p.Durability),
            };
            plan.MagicalPercent = plan.Burst == 0 ? 0 : profiles.Sum(p => p.Burst * p.MagicalPercent) / plan.Burst;
            plan.PowerPeakMinute = Math.Max(10, Math.Min(45, 10 + plan.Scaling / 10 - plan.Push / 20));

            // Each archetype's score in percent of a "clearly that" line-up; the best one wins if it is clear enough.
            var scores = new List<(GameArchetype Archetype, int Score)>
            {
                (GameArchetype.Push, plan.Push * 100 / 300),
                (GameArchetype.LateGame, plan.Scaling * 100 / 300),
                (GameArchetype.Teamfight, (plan.Initiation + plan.Control / 2) * 100 / 300),
                (GameArchetype.Pickoff, (plan.Mobility * 2 + plan.Burst / 4) * 100 / 300),
            };
            (GameArchetype archetype, int score) = scores.OrderByDescending(s => s.Score).First();   // stable: list order breaks ties
            plan.Archetype = score >= 80 ? archetype : GameArchetype.Balanced;

            var strengths = new List<DraftStrength>();
            if (plan.Initiation >= 150) strengths.Add(DraftStrength.StrongInitiation);
            if (plan.Control >= 200) strengths.Add(DraftStrength.HeavyControl);
            if (plan.Sustain >= 150) strengths.Add(DraftStrength.BigSustain);
            if (plan.Push >= 250) strengths.Add(DraftStrength.StrongPush);
            if (plan.Scaling >= 300) strengths.Add(DraftStrength.LateGameScaling);
            if (plan.Burst >= 450) strengths.Add(DraftStrength.HighBurst);
            if (plan.Mobility >= 80) strengths.Add(DraftStrength.Mobile);
            plan.Strengths = strengths;

            var weaknesses = new List<DraftWeakness>();
            if (plan.Initiation < 30) weaknesses.Add(DraftWeakness.NoInitiation);
            if (plan.Control < 80) weaknesses.Add(DraftWeakness.LittleControl);
            if (plan.Sustain < 60) weaknesses.Add(DraftWeakness.NoSustain);
            if (plan.Burst > 0 && plan.MagicalPercent > 80) weaknesses.Add(DraftWeakness.MostlyMagicalDamage);
            if (plan.Burst > 0 && plan.MagicalPercent < 20) weaknesses.Add(DraftWeakness.MostlyPhysicalDamage);
            if (plan.Scaling < 130) weaknesses.Add(DraftWeakness.WeakLateGame);
            if (plan.Durability < 250) weaknesses.Add(DraftWeakness.Fragile);
            plan.Weaknesses = weaknesses;
            return plan;
        }
    }
}
