using System;
using Dynasty.Players;

namespace Dynasty.HeroAI
{
    /// <summary>
    /// How well one hero is played: the skill of the player controlling it, turned into AI
    /// parameters (spec §14: mechanics, macro, psychology). The defaults are the AI's standard
    /// play, so a hero without a player behaves exactly as before. Swapping the player changes
    /// the hero's decisions — not a hidden power bonus.
    /// </summary>
    public sealed class HeroSkill
    {
        /// <summary>Base re-think interval in ticks (reaction).</summary>
        public int DecisionIntervalTicks { get; set; } = 3;

        /// <summary>Random extra ticks per decision (reaction variance, consistency).</summary>
        public int ReactionJitterTicks { get; set; } = 3;

        /// <summary>± percent error when judging fight odds (map awareness, adaptability, composure).</summary>
        public int MisjudgementPercent { get; set; } = 15;

        /// <summary>Share of creeps whose last-hit timing the player gets wrong and misses (last-hit skill), percent.</summary>
        public int LastHitMissPercent { get; set; }

        /// <summary>Chance per decision to let a good cast moment pass (spell accuracy).</summary>
        public int SpellHesitationPercent { get; set; }

        /// <summary>How far a point-targeted cast can land from where it was meant (spell accuracy).</summary>
        public int PointCastErrorUnits { get; set; }

        /// <summary>Chance to hit the team's focus target rather than whoever is closest (target priority).</summary>
        public int FocusDisciplinePercent { get; set; } = 100;

        /// <summary>Chance per fight decision to fumble — stand still instead of acting (micro, movement), percent.</summary>
        public int FightFumblePercent { get; set; }

        /// <summary>
        /// Chance per decision, after the laning phase and with no enemy hero in sight, to shove the wave
        /// in front of the hero instead of waiting for last hits (objective control), percent.
        /// </summary>
        public int WavePushPercent { get; set; } = 100;

        /// <summary>Waits for two or more enemies before spending an area ultimate (initiation).</summary>
        public bool InitiationPatience { get; set; } = true;

        /// <summary>Scales the hero's fight appetite (confidence), percent.</summary>
        public int AggressionPercent { get; set; } = 100;

        /// <summary>Added to the HP percent at which the hero retreats (awareness and discipline: the careless stay too long).</summary>
        public int RetreatShiftPercent { get; set; }

        /// <summary>Backs off from the enemy half when several enemy heroes are missing from the map (map awareness).</summary>
        public bool DangerSense { get; set; }

        /// <summary>
        /// How much two or more deaths in five minutes unsettle the player (low tilt resistance), 0 = not at all:
        /// a tilted hero fights more recklessly and misjudges more.
        /// </summary>
        public int TiltSusceptibility { get; set; }

        /// <summary>
        /// The skill of <paramref name="player"/> on <paramref name="heroId"/>: attributes, scaled by the
        /// player's readiness on that hero (an unfamiliar hero counts as low readiness). A tired player
        /// (<paramref name="fatigue"/> 0–100, from training) reacts later, misjudges and fumbles more.
        /// </summary>
        public static HeroSkill FromPlayer(PlayerProfile player, string heroId, int fatigue = 0)
        {
            fatigue = System.Math.Max(0, System.Math.Min(100, fatigue));
            int readiness = player.Hero(heroId)?.Readiness ?? 25;
            AttributeSet a = player.Attributes;
            int Effective(Players.Attribute attribute) =>
                System.Math.Max(Attributes.Min, System.Math.Min(Attributes.Max, a[attribute] * (60 + 40 * readiness / 100) / 100));
            int Gap(Players.Attribute attribute) => Attributes.Max - Effective(attribute);

            int composure = (a[Players.Attribute.MapAwareness] + a[Players.Attribute.Adaptability] + a[Players.Attribute.PressureHandling]) / 3;
            return new HeroSkill
            {
                DecisionIntervalTicks = 2 + Gap(Players.Attribute.Reaction) / 4,
                ReactionJitterTicks = 1 + Gap(Players.Attribute.Reaction) / 4 + (Attributes.Max - a[Players.Attribute.Consistency]) / 6 + fatigue / 25,
                MisjudgementPercent = 4 + (Attributes.Max - composure) * 2 + fatigue / 10,
                LastHitMissPercent = Gap(Players.Attribute.LastHit) * 3,
                SpellHesitationPercent = Gap(Players.Attribute.SpellAccuracy) * 2,
                PointCastErrorUnits = Gap(Players.Attribute.SpellAccuracy) * 15,
                FocusDisciplinePercent = 50 + a[Players.Attribute.TargetPriority] * 5 / 2,
                FightFumblePercent = (Gap(Players.Attribute.Micro) + Gap(Players.Attribute.Movement)) * 3 / 2 + fatigue / 10,
                WavePushPercent = System.Math.Min(100, Effective(Players.Attribute.ObjectiveControl) * 5),
                InitiationPatience = Effective(Players.Attribute.Initiation) >= 10,
                AggressionPercent = 85 + a[Players.Attribute.Confidence],
                RetreatShiftPercent = (a[Players.Attribute.MapAwareness] + a[Players.Attribute.Discipline]) / 2 - 12,
                DangerSense = Effective(Players.Attribute.MapAwareness) >= 12,
                TiltSusceptibility = System.Math.Max(0, 12 - a[Players.Attribute.TiltResistance]),
            };
        }
    }
}
