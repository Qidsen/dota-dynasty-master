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

        /// <summary>Radius around a hero within which allies and visible enemies count toward a fight.</summary>
        public int EngageRange { get; set; } = 1200;

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
}
