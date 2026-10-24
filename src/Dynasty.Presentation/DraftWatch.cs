using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Randomness;
using Dynasty.Drafting;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Presentation
{
    /// <summary>
    /// A draft between two AI teams watched step by step (one ban or pick per interval), with the
    /// reasoning behind each choice and both game plans at the end; then the match is started with
    /// the drafted heroes on their positions and each team playing its plan.
    /// </summary>
    public sealed class DraftWatch
    {
        private readonly RulesetContent _ruleset;
        private readonly DraftAi _dawnAi;
        private readonly DraftAi _duskAi;
        private readonly double _secondsPerStep;
        private double _elapsed;

        private DraftWatch(RulesetContent ruleset, DraftSession session, TeamRoster dawn, TeamRoster dusk, DraftAi dawnAi, DraftAi duskAi, double secondsPerStep)
        {
            _ruleset = ruleset;
            Session = session;
            DawnRoster = dawn;
            DuskRoster = dusk;
            _dawnAi = dawnAi;
            _duskAi = duskAi;
            _secondsPerStep = secondsPerStep;
        }

        public static DraftWatch Start(RulesetContent ruleset, ulong seed, double secondsPerStep)
        {
            if (secondsPerStep <= 0)
                throw new ArgumentOutOfRangeException(nameof(secondsPerStep));
            var (dawn, dusk) = DemoMatch.Rosters();
            Team firstPick = seed % 2 == 0 ? Team.Dusk : Team.Dawn;   // the coin flip
            var session = DraftSession.Start(ruleset, firstPick);
            var dawnAi = new DraftAi(ruleset, Team.Dawn, dawn, dusk, new DraftAiSettings(), seed * 2 + 1);
            var duskAi = new DraftAi(ruleset, Team.Dusk, dusk, dawn, new DraftAiSettings(), seed * 2 + 2);
            return new DraftWatch(ruleset, session, dawn, dusk, dawnAi, duskAi, secondsPerStep);
        }

        public DraftSession Session { get; }
        public TeamRoster DawnRoster { get; }
        public TeamRoster DuskRoster { get; }

        /// <summary>The last ban or pick and why (its score parts).</summary>
        public DraftCandidate? LastChoice { get; private set; }

        /// <summary>The runners-up of the last step, best first.</summary>
        public IReadOnlyList<DraftCandidate> LastAlternatives { get; private set; } = Array.Empty<DraftCandidate>();

        public Team? LastTeam { get; private set; }

        public GamePlan? DawnPlan => Session.IsComplete ? GamePlan.For(_ruleset, Session.PicksOf(Team.Dawn)) : null;
        public GamePlan? DuskPlan => Session.IsComplete ? GamePlan.For(_ruleset, Session.PicksOf(Team.Dusk)) : null;

        public void Update(double realSeconds)
        {
            _elapsed += realSeconds;
            while (_elapsed >= _secondsPerStep && !Session.IsComplete)
            {
                _elapsed -= _secondsPerStep;
                Step();
            }
        }

        public void Finish()
        {
            while (!Session.IsComplete)
                Step();
        }

        /// <summary>Starts the match: drafted heroes on their positions, each team playing its game plan.</summary>
        public MatchSession StartMatch(ulong seed)
        {
            if (!Session.IsComplete)
                throw new InvalidOperationException("The draft is not finished.");
            MatchLineup lineup = PositionPlan.Lineup(
                PositionPlan.For(_ruleset, Team.Dawn, Session.PicksOf(Team.Dawn)),
                PositionPlan.For(_ruleset, Team.Dusk, Session.PicksOf(Team.Dusk)));
            var settings = new HeroAiSettings
            {
                DawnStrategy = TeamStrategy.From(DawnPlan!),
                DuskStrategy = TeamStrategy.From(DuskPlan!),
            };
            return MatchSession.Start(_ruleset, seed, lineup, settings);
        }

        private void Step()
        {
            Team team = Session.TeamToAct!.Value;
            DraftAi ai = team == Team.Dawn ? _dawnAi : _duskAi;
            List<DraftCandidate> ranking = ai.Rank(Session);
            string choice = ai.Choose(Session);
            LastChoice = ranking.First(c => c.HeroId == choice);
            LastAlternatives = ranking.Where(c => c.HeroId != choice).Take(3).ToList();
            LastTeam = team;
            Session.Submit(team, choice);
        }
    }
}
