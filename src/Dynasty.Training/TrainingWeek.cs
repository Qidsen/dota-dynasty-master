using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Players;
using Attribute = Dynasty.Players.Attribute;

namespace Dynasty.Training
{
    /// <summary>What a week's hours go into (spec §16).</summary>
    public enum TrainingFocus
    {
        Mechanics,
        Heroes,
        Macro,
        Teamfights,
        Vision,
        Communication,
        Mental,
        Scrims,
        Rest,
    }

    /// <summary>A player's hours for one week, and the heroes they practise.</summary>
    public sealed class WeeklyPlan
    {
        /// <summary>16 hours a day at most, including rest.</summary>
        public const int MaxHours = 112;

        public WeeklyPlan(IReadOnlyDictionary<TrainingFocus, int> hours, IReadOnlyList<string>? practicedHeroes = null)
        {
            if (hours.Values.Any(h => h < 0) || hours.Values.Sum() > MaxHours)
                throw new ArgumentException("A week has at most " + MaxHours + " planned hours, none negative.", nameof(hours));
            Hours = ((TrainingFocus[])Enum.GetValues(typeof(TrainingFocus))).ToDictionary(f => f, f => hours.TryGetValue(f, out int h) ? h : 0);
            PracticedHeroes = (practicedHeroes ?? Array.Empty<string>()).ToArray();
        }

        public IReadOnlyDictionary<TrainingFocus, int> Hours { get; }
        public IReadOnlyList<string> PracticedHeroes { get; }

        /// <summary>Hours of actual training (everything but rest).</summary>
        public int TrainingHours => Hours.Where(h => h.Key != TrainingFocus.Rest).Sum(h => h.Value);
    }

    /// <summary>Who trains the player and where (1–20 each).</summary>
    public sealed class TrainingEnvironment
    {
        public TrainingEnvironment(int coachQuality, int facilities)
        {
            if (coachQuality < 1 || coachQuality > 20 || facilities < 1 || facilities > 20)
                throw new ArgumentOutOfRangeException(nameof(coachQuality), "Coach quality and facilities are 1..20.");
            CoachQuality = coachQuality;
            Facilities = facilities;
        }

        public int CoachQuality { get; }
        public int Facilities { get; }
    }

    /// <summary>
    /// A player's physical and mental state between weeks: fatigue, burnout risk and the progress
    /// built up toward each attribute's next point (0–99). Progress is what keeps growth gradual.
    /// </summary>
    public sealed class PlayerCondition
    {
        private readonly int[] _progress;

        public PlayerCondition(int fatigue, int burnoutRisk, int[] progress)
        {
            Fatigue = Math.Max(0, Math.Min(100, fatigue));
            BurnoutRisk = Math.Max(0, Math.Min(100, burnoutRisk));
            _progress = progress;
        }

        public static PlayerCondition Fresh => new PlayerCondition(0, 0, new int[Attributes.All.Count]);

        /// <summary>0–100; tired players learn less and play worse.</summary>
        public int Fatigue { get; }

        public int BurnoutRisk { get; }

        public int Progress(Attribute attribute) => _progress[(int)attribute];

        internal int[] CopyProgress() => (int[])_progress.Clone();
    }

    /// <summary>
    /// One week of training (spec §16). Each focus feeds its attributes at a rate set by the player's
    /// learning speed and professionalism, the coach, the facilities and the player's energy; growth
    /// slows near and stops at the hidden potential; an attribute rises only once 100 points of
    /// progress are built up, so nothing jumps. Long weeks tire the player and raise burnout risk;
    /// rest recovers. Practised heroes get sharper, the rest fade. Deterministic integer arithmetic.
    /// </summary>
    public static class TrainingWeek
    {
        private static readonly Dictionary<TrainingFocus, Attribute[]> Feeds = new Dictionary<TrainingFocus, Attribute[]>
        {
            [TrainingFocus.Mechanics] = Attributes.All.Where(a => Attributes.GroupOf(a) == AttributeGroup.Mechanics).ToArray(),
            [TrainingFocus.Macro] = new[] { Attribute.MapAwareness, Attribute.FarmingPatterns, Attribute.Rotations, Attribute.ObjectiveControl, Attribute.LateGameDecisions, Attribute.Adaptability },
            [TrainingFocus.Teamfights] = new[] { Attribute.Initiation, Attribute.TargetPriority, Attribute.Movement, Attribute.SpellAccuracy },
            [TrainingFocus.Vision] = new[] { Attribute.Vision, Attribute.MapAwareness },
            [TrainingFocus.Communication] = new[] { Attribute.Teamwork, Attribute.Leadership },
            [TrainingFocus.Mental] = new[] { Attribute.Confidence, Attribute.PressureHandling, Attribute.TiltResistance, Attribute.Consistency },
        };

        /// <summary>The product of the rate factors at average values (learning 12, coach 10, facilities 10, professionalism 12, full energy).</summary>
        private const long AverageRate = 17L * 15 * 20 * 17 * 100;

        public static (PlayerProfile Player, PlayerCondition Condition) Apply(PlayerProfile player, PlayerCondition condition, WeeklyPlan plan, TrainingEnvironment environment)
        {
            int energy = 100 - condition.Fatigue;
            long rate = (5L + player.Hidden.LearningSpeed) * (5 + environment.CoachQuality) * (10 + environment.Facilities) * (5 + player.Hidden.Professionalism) * energy;

            // Points per attribute this week, in hundredths: hours × rate, shared across the focus's attributes.
            var points = new long[Attributes.All.Count];
            foreach (var feed in Feeds)
            {
                int hours = plan.Hours[feed.Key];
                foreach (Attribute attribute in feed.Value)
                    points[(int)attribute] += hours * 300L * rate / (AverageRate * feed.Value.Length);
            }

            int scrims = plan.Hours[TrainingFocus.Scrims];
            foreach (Attribute attribute in Attributes.All.Where(a => Attributes.GroupOf(a) != AttributeGroup.Psychology))
                points[(int)attribute] += scrims * 100L * rate / (AverageRate * 6);   // a little of everything

            AttributeSet attributes = player.Attributes;
            int[] progress = condition.CopyProgress();
            foreach (Attribute attribute in Attributes.All)
            {
                int value = attributes[attribute];
                int gap = player.Hidden.Potential - value;
                if (gap <= 0)
                {
                    progress[(int)attribute] = 0;
                    continue;
                }

                long gained = points[(int)attribute] * Math.Min(100, gap * 25) / 100 / 100;   // slower near the ceiling
                long total = progress[(int)attribute] + gained;
                if (total >= 100)
                {
                    attributes = attributes.With(attribute, value + 1);   // at most one point a week
                    total = value + 1 >= player.Hidden.Potential ? 0 : Math.Min(99, total - 100);
                }

                progress[(int)attribute] = (int)total;
            }

            int training = plan.TrainingHours;
            int rest = plan.Hours[TrainingFocus.Rest];
            int fatigue = condition.Fatigue + (training > 40 ? (training - 40) * 2 : -(40 - training) / 2) - rest / 2 - 5;
            fatigue = Math.Max(0, Math.Min(100, fatigue));
            int burnout = fatigue > 60
                ? condition.BurnoutRisk + fatigue * player.Hidden.BurnoutProneness / 100
                : condition.BurnoutRisk * 8 / 10;

            var heroes = PracticeHeroes(player, plan, rate);
            var trained = new PlayerProfile(player.Id, player.Handle, player.Name, player.Nationality, player.BirthYear, player.Languages, player.Region,
                player.PositionProficiency, attributes, player.Hidden, heroes);
            return (trained, new PlayerCondition(fatigue, burnout, progress));
        }

        private static List<HeroPoolRecord> PracticeHeroes(PlayerProfile player, WeeklyPlan plan, long rate)
        {
            var practiced = new HashSet<string>(plan.PracticedHeroes);
            int hours = plan.Hours[TrainingFocus.Heroes] + plan.Hours[TrainingFocus.Scrims] / 2;
            int share = practiced.Count == 0 ? 0 : hours / practiced.Count;
            var heroes = new List<HeroPoolRecord>();
            foreach (HeroPoolRecord hero in player.HeroPool)
            {
                bool practicing = practiced.Contains(hero.HeroId) && share > 0;
                int recent = practicing ? Math.Min(100, hero.RecentPractice + share * 2) : Math.Max(0, hero.RecentPractice - 10);
                int mastery = practicing ? Math.Min(100, hero.Mastery + (int)(share * rate / (AverageRate * 8))) : hero.Mastery;
                heroes.Add(new HeroPoolRecord(hero.HeroId, mastery, recent, hero.Confidence, hero.PatchFamiliarity, hero.MatchupKnowledge, hero.RoleFamiliarity,
                    hero.GamesPlayed, hero.Wins));
            }

            return heroes;
        }
    }
}
