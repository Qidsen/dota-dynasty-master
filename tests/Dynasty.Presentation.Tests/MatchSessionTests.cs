using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class MatchSessionTests
{
    private static readonly MatchLineup Lineup = new(
        new[]
        {
            new HeroPick("ironvow", "bot"), new HeroPick("dawnmender", "bot"), new HeroPick("emberwisp", "mid"),
            new HeroPick("stonewarden", "top"), new HeroPick("thornkeeper", "top"),
        },
        new[]
        {
            new HeroPick("starborn", "top"), new HeroPick("shadeblade", "top"), new HeroPick("frostweaver", "mid"),
            new HeroPick("hivecaller", "bot"), new HeroPick("siegewright", "bot"),
        });

    private static MatchSession Start(ulong seed = 4) => MatchSession.Start(BaseRuleset.Create(), seed, Lineup);

    [Fact]
    public void Update_RunsTheTicksTheClockAllowsAndKeepsTheLastTwoFrames()
    {
        MatchSession session = Start();

        session.Update(1.0);

        Assert.Equal(30, session.Current.Tick);
        Assert.Equal(29, session.Previous.Tick);
    }

    /// <summary>
    /// A frame computes the match only within its time budget: at a speed the computer cannot keep up with, the ticks it has no
    /// time for are dropped (the match runs slower, smoothly), not run in one long frame that skips a minute of the match.
    /// </summary>
    [Fact]
    public void Update_RunsOnlyWhatFitsTheFrameBudget_AndDropsTheRest()
    {
        MatchSession session = Start();
        session.Clock.SetSpeed(64);

        session.Update(1.0, budgetMilliseconds: 0);   // no time at all: still one tick, so the match moves
        Assert.Equal(1, session.Current.Tick);

        session.Update(1.0 / 60);   // the dropped ticks do not come back in the next frame
        Assert.Equal(1 + 32, session.Current.Tick);
    }

    [Fact]
    public void WatchingDoesNotChangeTheMatch_WhateverTheFrameSizesAndSpeeds()
    {
        // Same seed, same number of ticks: a headless run and a "watched" run with ragged frames,
        // speed changes, pauses and single steps must produce the same event stream.
        MatchSession session = Start(seed: 11);
        var frames = new[] { 0.016, 0.033, 0.5, 0.004, 0.25, 1.7 };
        int i = 0;
        while (session.Kernel.CurrentTick.Value < 3000)
        {
            if (i % 7 == 3) session.Clock.SetSpeed(16);
            if (i % 7 == 5) session.Clock.SetSpeed(2);
            if (i % 11 == 6) { session.Clock.Pause(); session.Clock.StepOnce(); }
            if (i % 11 == 8) session.Clock.Resume();
            session.Update(frames[i++ % frames.Length]);
        }

        long ticks = session.Kernel.CurrentTick.Value;
        var headless = Match.Create(BaseRuleset.Create(), 11, Lineup);
        HeroAi.Attach(headless);
        headless.RunTicks(ticks);
        Assert.Equal(headless.Events.Checksum, session.Kernel.Events.Checksum);
    }

    [Fact]
    public void Viewpoint_ChangesWhatTheFramesShow()
    {
        MatchSession session = Start();
        session.Update(1.0);
        session.Clock.SetSpeed(MatchClock.MaxSpeed);
        for (int i = 0; i < 4; i++)
            session.Update(1.0);   // a few minutes in

        session.Viewpoint = Viewpoint.Dawn;

        Assert.Equal(Viewpoint.Dawn, session.Current.Viewpoint);
        Assert.DoesNotContain(session.Current.Units, u => u.Team == Team.Dusk && !u.SeenByDawn);
        Assert.Same(session.Current, session.Previous);   // no stale observer frame to interpolate from
    }

    [Fact]
    public void Feed_ReportsHeroKillsWithNamesOnce()
    {
        MatchSession session = Start();
        session.Clock.SetSpeed(MatchClock.MaxSpeed);
        var feed = new List<FeedItem>();
        while (session.Kernel.EventsOf<HeroKilled>().Count() < 3)
        {
            session.Update(1.0);
            feed.AddRange(session.TakeFeed());
        }

        feed.AddRange(session.TakeFeed());
        var kills = feed.Where(f => f.Kind == FeedKind.HeroKilled).ToList();
        Assert.Equal(session.Kernel.EventsOf<HeroKilled>().Count(), kills.Count);
        string[] names = session.Kernel.State.Heroes.Select(h => h.Hero!.Definition.Name).ToArray();
        Assert.All(kills, k => Assert.Contains(k.Subject, names));
        Assert.Empty(session.TakeFeed());
    }

    [Fact]
    public void Session_StopsAtTheEndOfTheMatch()
    {
        MatchSession session = Start(seed: 1);
        session.Clock.SetSpeed(MatchClock.MaxSpeed);
        var feed = new List<FeedItem>();
        for (int i = 0; i < 60 * 60 && !session.IsFinished; i++)
        {
            session.Update(1.0);
            feed.AddRange(session.TakeFeed());
        }

        Assert.True(session.IsFinished);
        long end = session.Kernel.CurrentTick.Value;
        session.Update(5.0);
        Assert.Equal(end, session.Kernel.CurrentTick.Value);
        Assert.NotNull(session.Current.Winner);
        Assert.Contains(feed, f => f.Kind == FeedKind.MatchFinished && f.Team == session.Current.Winner);
    }

    [Fact]
    public void Poses_InterpolateBetweenThePreviousAndCurrentFrame()
    {
        MatchSession session = Start();
        session.Update(2.0);
        session.Update(1.0 / 60);   // half a tick: no new tick, alpha 0.5

        UnitFrame now = session.Current.Units.First(u => u.Kind == UnitKind.Hero);
        UnitFrame before = session.Previous.Units.First(u => u.Id == now.Id);
        UnitPose pose = session.Poses().Single(p => p.Id == now.Id);

        Assert.Equal((before.X + now.X) / 2, pose.X, 2);
        Assert.Equal((before.Y + now.Y) / 2, pose.Y, 2);
    }
}
