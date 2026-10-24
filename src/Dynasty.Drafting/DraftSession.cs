using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Events;
using Dynasty.Core.Hashing;
using Dynasty.Core.Time;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting
{
    public enum DraftResult
    {
        Accepted,
        NotYourTurn,
        UnknownHero,

        /// <summary>Already picked or banned, or disabled by the draft rules.</summary>
        Unavailable,

        /// <summary>The draft is over.</summary>
        Complete,
    }

    /// <summary>
    /// One pick/ban phase under a ruleset's <see cref="DraftRules"/> (spec §22). Turn-based and
    /// deterministic: every accepted ban or pick is a domain event, so a draft can be replayed and
    /// checksummed like a match. Timers and reserve time are not modelled yet.
    /// </summary>
    public sealed class DraftSession
    {
        private readonly List<string> _available;
        private readonly Dictionary<Team, List<string>> _picks = new Dictionary<Team, List<string>> { { Team.Dawn, new List<string>() }, { Team.Dusk, new List<string>() } };
        private readonly Dictionary<Team, List<string>> _bans = new Dictionary<Team, List<string>> { { Team.Dawn, new List<string>() }, { Team.Dusk, new List<string>() } };
        private readonly HashSet<string> _known;

        private DraftSession(RulesetContent ruleset, DraftRules rules, Team firstPick)
        {
            Ruleset = ruleset;
            Rules = rules;
            FirstPick = firstPick;
            _known = new HashSet<string>(ruleset.Heroes.Select(h => h.Id));
            _available = ruleset.Heroes.Select(h => h.Id).Where(id => !rules.DisabledHeroes.Contains(id)).ToList();
        }

        public static DraftSession Start(RulesetContent ruleset, Team firstPick)
        {
            DraftRules rules = ruleset.Draft ?? throw new InvalidOperationException("Ruleset '" + ruleset.Id + "' has no draft rules.");
            if (firstPick != Team.Dawn && firstPick != Team.Dusk)
                throw new ArgumentOutOfRangeException(nameof(firstPick));
            return new DraftSession(ruleset, rules, firstPick);
        }

        public RulesetContent Ruleset { get; }
        public DraftRules Rules { get; }
        public Team FirstPick { get; }
        public EventLog Events { get; } = new EventLog();

        /// <summary>Index of the next step; equals the number of steps taken.</summary>
        public int StepIndex { get; private set; }

        public bool IsComplete => StepIndex >= Rules.Steps.Count;

        public Team? TeamToAct => IsComplete ? (Team?)null : TeamOf(Rules.Steps[StepIndex].Side);

        public DraftAction? ActionToTake => IsComplete ? (DraftAction?)null : Rules.Steps[StepIndex].Action;

        /// <summary>Heroes still free to pick or ban, in ruleset order.</summary>
        public IReadOnlyList<string> AvailableHeroes => _available;

        public IReadOnlyList<string> PicksOf(Team team) => _picks[team];

        public IReadOnlyList<string> BansOf(Team team) => _bans[team];

        public bool IsAvailable(string heroId) => _available.Contains(heroId);

        public Team TeamOf(DraftSide side) => side == DraftSide.First ? FirstPick : FirstPick.Opponent();

        public DraftResult Submit(Team team, string heroId)
        {
            if (IsComplete)
                return DraftResult.Complete;
            if (team != TeamToAct)
                return DraftResult.NotYourTurn;
            if (!_known.Contains(heroId))
                return DraftResult.UnknownHero;
            if (!_available.Contains(heroId))
                return DraftResult.Unavailable;

            DraftAction action = Rules.Steps[StepIndex].Action;
            _available.Remove(heroId);
            var tick = new SimTick(StepIndex);
            if (action == DraftAction.Ban)
            {
                _bans[team].Add(heroId);
                Events.Append(tick, new DraftBanCommitted(team, heroId, StepIndex));
            }
            else
            {
                _picks[team].Add(heroId);
                Events.Append(tick, new DraftPickCommitted(team, heroId, StepIndex));
            }

            StepIndex++;
            if (IsComplete)
                Events.Append(new SimTick(StepIndex), new DraftCompleted(_picks[Team.Dawn], _picks[Team.Dusk]));
            return DraftResult.Accepted;
        }
    }

    public sealed class DraftBanCommitted : IDomainEvent
    {
        public DraftBanCommitted(Team team, string heroId, int step)
        {
            Team = team;
            HeroId = heroId;
            Step = step;
        }

        public Team Team { get; }
        public string HeroId { get; }
        public int Step { get; }
        public string Type => "draft.ban_committed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64((int)Team);
            hash.AddString(HeroId);
            hash.AddInt64(Step);
        }
    }

    public sealed class DraftPickCommitted : IDomainEvent
    {
        public DraftPickCommitted(Team team, string heroId, int step)
        {
            Team = team;
            HeroId = heroId;
            Step = step;
        }

        public Team Team { get; }
        public string HeroId { get; }
        public int Step { get; }
        public string Type => "draft.pick_committed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64((int)Team);
            hash.AddString(HeroId);
            hash.AddInt64(Step);
        }
    }

    public sealed class DraftCompleted : IDomainEvent
    {
        public DraftCompleted(IReadOnlyList<string> dawn, IReadOnlyList<string> dusk)
        {
            Dawn = dawn.ToArray();
            Dusk = dusk.ToArray();
        }

        public IReadOnlyList<string> Dawn { get; }
        public IReadOnlyList<string> Dusk { get; }
        public string Type => "draft.completed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Dawn.Count);
            foreach (string hero in Dawn)
                hash.AddString(hero);
            hash.AddInt64(Dusk.Count);
            foreach (string hero in Dusk)
                hash.AddString(hero);
        }
    }
}
