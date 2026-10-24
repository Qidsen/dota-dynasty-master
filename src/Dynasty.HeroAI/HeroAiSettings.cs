using System.Collections.Generic;
using System.Linq;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.HeroAI
{
    /// <summary>
    /// How a hero tends to play. Same mechanics, different decisions (spec §10). Later phases
    /// derive these from the professional player controlling the hero.
    /// </summary>
    public sealed class HeroPersonality
    {
        /// <summary>Multiplies the perceived odds of a fight; above 1 takes riskier fights.</summary>
        public double Aggression { get; set; } = 1.0;

        public int RetreatHpPercent { get; set; } = 30;
        public int ReturnHpPercent { get; set; } = 90;

        /// <summary>0..1: how long the hero keeps farming before joining team pushes.</summary>
        public double Greed { get; set; } = 0.5;

        /// <summary>Default personality by role tags.</summary>
        public static HeroPersonality ForRoles(IReadOnlyList<string> roles)
        {
            if (roles.Contains("carry") || roles.Contains("late"))
                return new HeroPersonality { Aggression = 0.9, Greed = 0.8, RetreatHpPercent = 35 };
            if (roles.Contains("support") || roles.Contains("healer"))
                return new HeroPersonality { Aggression = 1.0, Greed = 0.1 };
            if (roles.Contains("initiator") || roles.Contains("offlane"))
                return new HeroPersonality { Aggression = 1.1, Greed = 0.3, RetreatHpPercent = 25 };
            return new HeroPersonality { Aggression = 1.05, Greed = 0.5 };
        }
    }

    public sealed class HeroAiSettings
    {
        /// <summary>Base re-think interval in ticks (a stand-in for reaction time).</summary>
        public int DecisionIntervalTicks { get; set; } = 3;

        /// <summary>Random extra ticks added to each decision interval (plausible reaction variance).</summary>
        public int ReactionJitterTicks { get; set; } = 3;

        /// <summary>± percent error applied when a hero judges fight odds (plausible misjudgement).</summary>
        public int MisjudgementPercent { get; set; } = 15;

        /// <summary>From this match minute on, heroes keep enough gold for a buyback when shopping.</summary>
        public int BuybackReserveFromMinute { get; set; } = 25;

        /// <summary>A dead hero buys back to defend only if its respawn is at least this far away.</summary>
        public int BuybackMinWaitSeconds { get; set; } = 15;

        /// <summary>
        /// Until this match minute heroes stay in their lanes: a team pushes only with a three-hero
        /// numbers advantage, and only heroes already nearby rotate to defend.
        /// </summary>
        public int LaningPhaseMinutes { get; set; } = 10;

        /// <summary>
        /// From this match minute on, a team of four or more takes the boss without a numbers
        /// advantage, as long as no enemy hero is seen near the pit.
        /// </summary>
        public int BossAtEvenNumbersFromMinute { get; set; } = 20;

        /// <summary>Radius around a hero within which allies and visible enemies count toward a fight.</summary>
        public int EngageRange { get; set; } = 1200;

        /// <summary>From this match minute a team of three or more pushes even without a numbers advantage.</summary>
        public int EvenPushFromMinute { get; set; } = 15;

        /// <summary>Dawn's game plan from its draft (no adjustment by default).</summary>
        public TeamStrategy DawnStrategy { get; set; } = new TeamStrategy();

        /// <summary>Dusk's game plan from its draft (no adjustment by default).</summary>
        public TeamStrategy DuskStrategy { get; set; } = new TeamStrategy();

        public TeamStrategy StrategyFor(Team team) => team == Team.Dawn ? DawnStrategy : DuskStrategy;

        /// <summary>Laning phase length for a team: the default shifted by its strategy.</summary>
        public int LaningPhaseFor(Team team) => System.Math.Max(0, LaningPhaseMinutes + StrategyFor(team).LaningShiftMinutes);

        /// <summary>Minute a team starts pushing at even numbers: the default shifted by its strategy.</summary>
        public int EvenPushFor(Team team) => System.Math.Max(0, EvenPushFromMinute + StrategyFor(team).EvenPushShiftMinutes);

        private readonly Dictionary<string, HeroSkill> _dawnSkills = new Dictionary<string, HeroSkill>();
        private readonly Dictionary<string, HeroSkill> _duskSkills = new Dictionary<string, HeroSkill>();

        /// <summary>
        /// A team's player skills by hero ID (<see cref="HeroSkill.FromPlayer"/>); per team, since both
        /// sides may play the same hero. Heroes not listed play the standard way with this object's
        /// reaction and misjudgement values.
        /// </summary>
        public Dictionary<string, HeroSkill> SkillsOf(Team team) => team == Team.Dawn ? _dawnSkills : _duskSkills;

        public HeroSkill SkillFor(Team team, string heroId) =>
            SkillsOf(team).TryGetValue(heroId, out HeroSkill? skill)
                ? skill
                : new HeroSkill { DecisionIntervalTicks = DecisionIntervalTicks, ReactionJitterTicks = ReactionJitterTicks, MisjudgementPercent = MisjudgementPercent };

        /// <summary>Per hero ID; heroes not listed use <see cref="HeroPersonality.ForRoles"/>.</summary>
        public Dictionary<string, HeroPersonality> Personalities { get; } = new Dictionary<string, HeroPersonality>();

        /// <summary>Hero IDs the AI must leave alone (scripted or human-controlled).</summary>
        public HashSet<string> Uncontrolled { get; } = new HashSet<string>();

        public HeroPersonality PersonalityFor(HeroDefinition hero) =>
            Personalities.TryGetValue(hero.Id, out HeroPersonality? p) ? p : HeroPersonality.ForRoles(hero.Roles);
    }

    /// <summary>
    /// The AI's window on the match: exactly the team's fog-of-war knowledge from the Vision
    /// module (spec §9). The AI must not look at units through any other path.
    /// </summary>
    public sealed class TeamPerception
    {
        private readonly MatchState _state;
        private readonly Team _team;

        private TeamPerception(MatchState state, Team team)
        {
            _state = state;
            _team = team;
        }

        public static TeamPerception Build(MatchState state, Team team, HeroAiSettings settings) => new TeamPerception(state, team);

        public bool CanSee(MatchUnit unit) => _state.IsVisibleTo(_team, unit);
    }

    /// <summary>
    /// How a team plays its draft (spec §22: the draft changes the match's actions, not a pre-set
    /// win chance). Adjustments to the shared AI settings, built from the team's <see cref="Dynasty.Drafting.GamePlan"/>.
    /// </summary>
    public sealed class TeamStrategy
    {
        /// <summary>Added to the laning phase: a push line-up leaves lanes early, a late-game one farms longer.</summary>
        public int LaningShiftMinutes { get; set; }

        /// <summary>Added to the minute the team starts pushing at even numbers.</summary>
        public int EvenPushShiftMinutes { get; set; }

        /// <summary>Scales every hero's fight odds and gank appetite (100 = unchanged).</summary>
        public int AggressionPercent { get; set; } = 100;

        /// <summary>How far heroes look for isolated enemies to gank.</summary>
        public int GankRange { get; set; } = 3000;

        public Dynasty.Drafting.GameArchetype Archetype { get; set; } = Dynasty.Drafting.GameArchetype.Balanced;

        /// <summary>
        /// Ticks before the heroes act on a new team plan (communication: teamwork, leadership, a common
        /// language; see <see cref="TeamCommunication"/>). Until then each keeps doing what it was doing.
        /// </summary>
        public int CallDelayTicks { get; set; }

        /// <summary>Summoners call their units at an enemy structure their creeps are already hitting.</summary>
        public bool SiegeWithSummons { get; set; }

        /// <summary>Until this minute greedy carries skip ganks and only take clearly favourable fights.</summary>
        public int ProtectCarriesUntilMinute { get; set; }

        public static TeamStrategy From(Dynasty.Drafting.GamePlan plan)
        {
            switch (plan.Archetype)
            {
                case Dynasty.Drafting.GameArchetype.Push:
                    return new TeamStrategy { Archetype = plan.Archetype, LaningShiftMinutes = -3, EvenPushShiftMinutes = -4, AggressionPercent = 105, SiegeWithSummons = true };
                case Dynasty.Drafting.GameArchetype.Teamfight:
                    return new TeamStrategy { Archetype = plan.Archetype, LaningShiftMinutes = -1, EvenPushShiftMinutes = -2, AggressionPercent = 108 };
                case Dynasty.Drafting.GameArchetype.LateGame:
                    return new TeamStrategy
                    {
                        Archetype = plan.Archetype, LaningShiftMinutes = 3, EvenPushShiftMinutes = 9, AggressionPercent = 90, GankRange = 2500,
                        ProtectCarriesUntilMinute = plan.PowerPeakMinute - 5,
                    };
                case Dynasty.Drafting.GameArchetype.Pickoff:
                    return new TeamStrategy { Archetype = plan.Archetype, LaningShiftMinutes = -1, AggressionPercent = 105, GankRange = 4000 };
                default:
                    return new TeamStrategy { Archetype = plan.Archetype };
            }
        }
    }
}
