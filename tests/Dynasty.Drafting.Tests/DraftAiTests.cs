using Dynasty.Drafting;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Drafting.Tests;

public class DraftAiTests
{
    private static readonly RulesetContent Ruleset = BaseRuleset.Create();

    /// <summary>Five players on positions 1–5 with an even, modest pool over every hero of their roles.</summary>
    private static TeamRoster Roster(string name, Action<Dictionary<int, Dictionary<string, int>>>? tweak = null)
    {
        var pools = new Dictionary<int, Dictionary<string, int>>();
        for (int position = 1; position <= 5; position++)
        {
            pools[position] = Ruleset.Heroes
                .Where(h => PositionPlan.Fit(h, position) >= 9)
                .ToDictionary(h => h.Id, _ => 50);
        }

        tweak?.Invoke(pools);
        return new TeamRoster(name, Enumerable.Range(1, 5).Select(p =>
            new RosterPlayer(name + p, name + " player " + p, p, pools[p].Select(e => new HeroPoolEntry(e.Key, e.Value, 50)).ToList())).ToList());
    }

    private static DraftSession Play(TeamRoster dawn, TeamRoster dusk, ulong seed = 1, DraftAiSettings? settings = null)
    {
        var session = DraftSession.Start(Ruleset, Team.Dawn);
        var dawnAi = new DraftAi(Ruleset, Team.Dawn, dawn, dusk, settings ?? new DraftAiSettings(), seed);
        var duskAi = new DraftAi(Ruleset, Team.Dusk, dusk, dawn, settings ?? new DraftAiSettings(), seed + 1000);
        while (!session.IsComplete)
        {
            DraftAi ai = session.TeamToAct == Team.Dawn ? dawnAi : duskAi;
            Assert.Equal(DraftResult.Accepted, session.Submit(session.TeamToAct!.Value, ai.Choose(session)));
        }

        return session;
    }

    [Fact]
    public void TwoAis_CompleteADraftWithPlayableTeams()
    {
        DraftSession session = Play(Roster("A"), Roster("B"));

        foreach (Team team in new[] { Team.Dawn, Team.Dusk })
        {
            PositionPlan plan = PositionPlan.For(Ruleset, team, session.PicksOf(team));
            for (int position = 1; position <= 5; position++)
                Assert.True(PositionPlan.Fit(Ruleset.FindHero(plan.HeroAt(position))!, position) > 0, $"{team} position {position}: {plan.HeroAt(position)}");
        }
    }

    [Fact]
    public void Ai_FirstPicksAPlayersSignatureHeroWhileItIsFree()
    {
        // (In a full draft the opponent rightly bans it - see the ban test.)
        TeamRoster dawn = Roster("A", pools => pools[1]["gunwright"] = 100);
        var session = DraftSession.Start(Ruleset, Team.Dawn);
        foreach (string ban in new[] { "ironvow", "emberwisp", "stonewarden", "frostweaver" })
            session.Submit(session.TeamToAct!.Value, ban);

        var ai = new DraftAi(Ruleset, Team.Dawn, dawn, Roster("B"), new DraftAiSettings(), 1);
        string pick = ai.Choose(session);

        Assert.True(pick == "gunwright", string.Join(" | ", ai.Rank(session).Take(5)));
    }

    [Fact]
    public void Ai_BansTheOpponentsSignatureHeroEarly()
    {
        TeamRoster dusk = Roster("B", pools => pools[2]["stormcaller"] = 100);

        DraftSession session = Play(Roster("A"), dusk);

        Assert.Contains("stormcaller", session.BansOf(Team.Dawn).Take(2));
    }

    /// <summary>Plays scripted steps: each team's picks and bans in the given order (Dawn first pick).</summary>
    private static DraftSession Scripted(string[] dawnBans, string[] duskBans, string[] dawnPicks, string[] duskPicks)
    {
        var session = DraftSession.Start(Ruleset, Team.Dawn);
        var queues = new Dictionary<(Team, DraftAction), Queue<string>>
        {
            [(Team.Dawn, DraftAction.Ban)] = new(dawnBans), [(Team.Dusk, DraftAction.Ban)] = new(duskBans),
            [(Team.Dawn, DraftAction.Pick)] = new(dawnPicks), [(Team.Dusk, DraftAction.Pick)] = new(duskPicks),
        };
        while (!session.IsComplete && queues[(session.TeamToAct!.Value, session.ActionToTake!.Value)].Count > 0)
            Assert.Equal(DraftResult.Accepted, session.Submit(session.TeamToAct.Value, queues[(session.TeamToAct.Value, session.ActionToTake.Value)].Dequeue()));
        return session;
    }

    [Fact]
    public void Ai_FillsMissingControlWithItsLastPick()
    {
        // Dawn holds carry, mid, offlane and hard support with little control; position 4 is open.
        DraftSession session = Scripted(
            new[] { "frostweaver", "voidseer", "tidebreaker", "plaguebringer" },
            new[] { "gunwright", "starborn", "gloomfang", "wildheart" },
            new[] { "ironvow", "emberwisp", "ashblade", "lumenpriest" },
            new[] { "duskrunner", "stormcaller", "siegewright", "bulwark", "dawnmender" });
        Assert.Equal(Team.Dawn, session.TeamToAct);
        Assert.Equal(DraftAction.Pick, session.ActionToTake);

        var ai = new DraftAi(Ruleset, Team.Dawn, Roster("A"), Roster("B"), new DraftAiSettings { MetaUncertaintyPercent = 0 }, 1);
        string pick = ai.Choose(session);

        int control = HeroProfile.Of(Ruleset, pick).Control;
        int best = new[] { "chainwarden", "thornkeeper", "windsinger", "spiritwarden" }.Where(session.IsAvailable).Max(h => HeroProfile.Of(Ruleset, h).Control);
        Assert.True(control >= best * 2 / 3, pick + " control " + control + " | " + string.Join(" | ", ai.Rank(session).Take(4)));
    }

    [Fact]
    public void Composition_RaisesTheControlAndInitiationTeamsEndUpWith()
    {
        int Drafted(int compositionWeight)
        {
            var settings = new DraftAiSettings { CompositionWeight = compositionWeight };
            int total = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                DraftSession session = Play(Roster("A"), Roster("B"), seed * 11, settings);
                total += session.PicksOf(Team.Dawn).Concat(session.PicksOf(Team.Dusk)).Sum(h => HeroProfile.Of(Ruleset, h).Control + HeroProfile.Of(Ruleset, h).Initiation);
            }

            return total;
        }

        int with = Drafted(2);
        int without = Drafted(0);
        Assert.True(with > without, $"control+initiation with composition {with}, without {without}");
    }

    [Fact]
    public void Counter_ControlBeatsMobileAssassinsAndSilenceBeatsCasters()
    {
        Assert.True(DraftAi.Counter(Ruleset, "frostweaver", "shadeblade") > DraftAi.Counter(Ruleset, "starborn", "shadeblade"));
        Assert.True(DraftAi.Counter(Ruleset, "lumenpriest", "thornkeeper") > DraftAi.Counter(Ruleset, "ironvow", "thornkeeper"));   // dispel vs disables
    }

    [Fact]
    public void MetaUncertainty_DifferentTeamsReadTheMetaDifferently_ButEachIsDeterministic()
    {
        var settings = new DraftAiSettings { MetaUncertaintyPercent = 40 };

        var drafts = Enumerable.Range(0, 6).Select(i => string.Join(",", Play(Roster("A"), Roster("B"), (ulong)(i * 7 + 1), settings).PicksOf(Team.Dawn))).ToList();

        Assert.True(drafts.Distinct().Count() > 1, "every seed drafted the same: " + drafts[0]);
        Assert.Equal(drafts[0], string.Join(",", Play(Roster("A"), Roster("B"), 1, settings).PicksOf(Team.Dawn)));
    }
}
