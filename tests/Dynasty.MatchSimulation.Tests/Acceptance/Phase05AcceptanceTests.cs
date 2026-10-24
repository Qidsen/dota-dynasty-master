using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.Drafting;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Acceptance;

/// <summary>
/// Phase 05 acceptance (spec §38: "the draft changes real match behaviour"): the same heroes play
/// differently when the team follows the game plan its draft calls for, and the whole chain
/// draft AI → positions → strategy → match runs.
/// </summary>
[Trait("Category", "Acceptance")]
public class Phase05AcceptanceTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();
    private static readonly string[] Opponent = { "starborn", "shadeblade", "frostweaver", "hivecaller", "thornkeeper" };
    private static readonly string[] PushDraft = { "duskrunner", "siegewright", "wildheart", "stormcaller", "spiritwarden" };
    private static readonly string[] LateDraft = { "gunwright", "ironvow", "bulwark", "lumenpriest", "dawnmender" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4 };

    private sealed class Run
    {
        public Run(SimulationKernel<MatchState> kernel, SiegePresence presence)
        {
            Kernel = kernel;
            Presence = presence;
        }

        public SimulationKernel<MatchState> Kernel { get; }
        public SiegePresence Presence { get; }
    }

    private static Run Play(string[] dawn, bool followPlan, ulong seed)
    {
        MatchLineup lineup = PositionPlan.Lineup(PositionPlan.For(Ruleset, Team.Dawn, dawn), PositionPlan.For(Ruleset, Team.Dusk, Opponent));
        var kernel = Match.Create(Ruleset, seed, lineup);
        var presence = new SiegePresence();
        kernel.AddSystem(presence);
        var settings = new HeroAiSettings();
        if (followPlan)
            settings.DawnStrategy = TeamStrategy.From(GamePlan.For(Ruleset, dawn));
        HeroAi.Attach(kernel, settings);
        kernel.RunUntilFinished(15L * 60 * 30);
        return new Run(kernel, presence);
    }

    private static List<Run> Runs(string[] dawn, bool followPlan) =>
        Seeds.AsParallel().AsOrdered().Select(seed => Play(dawn, followPlan, seed)).ToList();   // independent deterministic matches

    private static readonly Lazy<(List<Run> Plan, List<Run> Neutral)> Late = new(() => (Runs(LateDraft, true), Runs(LateDraft, false)));
    private static readonly Lazy<(List<Run> Plan, List<Run> Neutral)> Push = new(() => (Runs(PushDraft, true), Runs(PushDraft, false)));

    private static IEnumerable<T> Events<T>(Run run) => run.Kernel.Events.Records.Select(r => r.Event).OfType<T>();

    private static double DawnGold(List<Run> runs) => runs.Average(r =>
        Events<GoldEarned>(r).Where(e => r.Kernel.State.Heroes.Any(h => h.Id == e.Hero && h.Team == Team.Dawn)).Sum(e => e.Amount));

    private static double Kills(List<Run> runs) => runs.Average(r => Events<HeroKilled>(r).Count());

    [Fact]
    public void LateGameDraft_FightsLessAndFarmsMoreEarlyWhenItPlaysItsPlan()
    {
        var (plan, neutral) = Late.Value;

        Assert.True(Kills(plan) < Kills(neutral) * 0.9, $"kills by 15 min: plan {Kills(plan):0.0}, neutral {Kills(neutral):0.0}");
        Assert.True(DawnGold(plan) > DawnGold(neutral) * 1.04, $"Dawn gold by 15 min: plan {DawnGold(plan):0}, neutral {DawnGold(neutral):0}");
    }

    [Fact]
    public void PushDraft_SpendsMoreTimeAtEnemyStructuresBeforeMinute15WhenItPlaysItsPlan()
    {
        var (plan, neutral) = Push.Value;
        double Presence(List<Run> runs) => runs.Average(r => r.Presence.HeroSeconds);

        Assert.True(Presence(plan) > Presence(neutral) * 1.2, $"Dawn hero-seconds near Dusk structures, minutes 7-15: plan {Presence(plan):0}, neutral {Presence(neutral):0}");
    }

    [Fact]
    public void DraftedTeams_PlayCompleteMatches()
    {
        // The whole chain: two draft AIs, position plans, strategies from their game plans, a full match.
        TeamRoster dawnRoster = Roster("A");
        TeamRoster duskRoster = Roster("B");
        var session = DraftSession.Start(Ruleset, Team.Dawn);
        var dawnAi = new DraftAi(Ruleset, Team.Dawn, dawnRoster, duskRoster, new DraftAiSettings(), 3);
        var duskAi = new DraftAi(Ruleset, Team.Dusk, duskRoster, dawnRoster, new DraftAiSettings(), 4);
        while (!session.IsComplete)
            session.Submit(session.TeamToAct!.Value, (session.TeamToAct == Team.Dawn ? dawnAi : duskAi).Choose(session));

        MatchLineup lineup = PositionPlan.Lineup(PositionPlan.For(Ruleset, Team.Dawn, session.PicksOf(Team.Dawn)), PositionPlan.For(Ruleset, Team.Dusk, session.PicksOf(Team.Dusk)));
        var kernel = Match.Create(Ruleset, 9, lineup);
        HeroAi.Attach(kernel, new HeroAiSettings
        {
            DawnStrategy = TeamStrategy.From(GamePlan.For(Ruleset, session.PicksOf(Team.Dawn))),
            DuskStrategy = TeamStrategy.From(GamePlan.For(Ruleset, session.PicksOf(Team.Dusk))),
        });
        kernel.RunUntilFinished(60L * 60 * 30);

        Assert.NotNull(kernel.State.Winner);
    }

    private static TeamRoster Roster(string name) => new(name, Enumerable.Range(1, 5).Select(position =>
        new RosterPlayer(name + position, name + position, position,
            Ruleset.Heroes.Where(h => PositionPlan.Fit(h, position) >= 9).Select(h => new HeroPoolEntry(h.Id, 50, 50)).ToList())).ToList());

    /// <summary>Counts, once a second from minute 7 to 15, Dawn heroes within 1500 of a living Dusk structure.</summary>
    private sealed class SiegePresence : ISimulationSystem<MatchState>
    {
        public string Name => "test-siege-presence";
        public int HeroSeconds { get; private set; }

        public void Tick(SimulationContext<MatchState> context)
        {
            long tick = context.Tick.Value;
            if (tick % 30 != 0 || tick < 7 * 1800 || tick >= 15 * 1800)
                return;
            MatchState state = context.State;
            var structures = state.Units.Where(u => u.Kind == UnitKind.Structure && u.Team == Team.Dusk && u.IsAlive).ToList();
            HeroSeconds += state.Heroes.Count(h => h.Team == Team.Dawn && h.IsAlive &&
                                                   structures.Any(s => FixedVector2.Distance(h.Position, s.Position).ToIntFloor() <= 1500));
        }
    }
}
