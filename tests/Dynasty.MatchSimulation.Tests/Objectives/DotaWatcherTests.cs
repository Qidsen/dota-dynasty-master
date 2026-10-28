using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Watchers (game file `npc_dota_lantern` and `ability_lamp_use`; Fandom «Watcher»; Valve 7.39c, 7.40): a hero within 200
/// for 1 s turns a ready Watcher on for its team for 7 minutes — the team sees around it, 800 by day and 450 by night —, then it is
/// ready again; a hero of the other team doing the same to a Watcher that is on turns it off for 2 minutes, when nobody can take
/// it. Here on for 10 s and off for 5 s; day and night 30 s.
/// </summary>
public class DotaWatcherTests
{
    private static readonly FixedVector2 Watcher = P(5000, 2000);

    private static RulesetDraft WithWatcher()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Add(Hero("delta"));
        draft.DayNight = new DayNightRules(dayMs: 30000, nightMs: 30000);
        draft.Watchers = new WatcherRules(new[] { Watcher }, radius: 200, channelMs: 1000, onMs: 10000, offMs: 5000, dayVision: 800, nightVision: 450);
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static void Put(SimulationKernel<MatchState> kernel, long tick, string hero, FixedVector2 at) => At(kernel, tick, ctx => H(ctx.State, hero).Position = at);

    /// <summary>Everyone far apart; delta sits 700 from the Watcher, beyond every hero's own sight from where they stand.</summary>
    private static void Spread(SimulationKernel<MatchState> kernel)
    {
        Put(kernel, 0, "alpha", P(1000, 9000));
        Put(kernel, 0, "beta", P(1200, 9000));
        Put(kernel, 0, "gamma", P(9000, 9000));
        Put(kernel, 0, "delta", P(5700, 2000));
    }

    [Fact]
    public void AHeroStandingASecond_TurnsItOnForItsTeam_WhichSeesAroundIt()
    {
        var kernel = Start(WithWatcher());
        Spread(kernel);
        Put(kernel, 30, "alpha", P(5100, 2000));
        Put(kernel, 30 * 3, "alpha", P(1000, 9000));   // walks away once it is on
        bool seenBefore = true;
        Team ownerMidway = Team.Dawn;
        At(kernel, 20, ctx => seenBefore = ctx.State.IsVisibleTo(Team.Dawn, H(ctx.State, "delta")));
        At(kernel, 30 + 25, ctx => ownerMidway = ctx.State.WatcherOwner(0));   // 0.8 s into the channel

        kernel.RunTicks(30 * 5);

        Assert.False(seenBefore);
        Assert.Equal(Team.Neutral, ownerMidway);
        Assert.Equal(Team.Dawn, kernel.State.WatcherOwner(0));
        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<WatcherTaken>()).Team);
        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "delta")));
    }

    [Fact]
    public void ItSeesLessAtNight_AndIsReadyAgainAfterItsTime()
    {
        var kernel = Start(WithWatcher());
        Spread(kernel);
        Put(kernel, 30 * 25, "alpha", P(5100, 2000));
        Put(kernel, 30 * 27, "alpha", P(1000, 9000));
        bool seenAtNight = true;
        At(kernel, 30 * 31, ctx => seenAtNight = ctx.State.IsVisibleTo(Team.Dawn, H(ctx.State, "delta")));   // 700 > 450

        kernel.RunTicks(30 * 37 + 2);   // on from 26 s to 36 s

        Assert.False(seenAtNight);
        Assert.Equal(Team.Neutral, kernel.State.WatcherOwner(0));
        Assert.False(kernel.State.WatcherOff(0));
    }

    [Fact]
    public void TheOtherTeam_TurnsItOff_AndNobodyTakesItWhileOff()
    {
        var kernel = Start(WithWatcher());
        Spread(kernel);
        Put(kernel, 30, "alpha", P(5100, 2000));
        Put(kernel, 30 * 3, "alpha", P(1000, 9000));
        Put(kernel, 30 * 3, "delta", P(4900, 2000));   // turns it off by 4 s and stays
        bool offAt5 = false;
        Team ownerAt5 = Team.Dawn;
        At(kernel, 30 * 5, ctx =>
        {
            offAt5 = ctx.State.WatcherOff(0);
            ownerAt5 = ctx.State.WatcherOwner(0);
        });

        kernel.RunTicks(30 * 8);
        Assert.True(offAt5);
        Assert.Equal(Team.Neutral, ownerAt5);
        Assert.Single(kernel.EventsOf<WatcherTaken>());   // delta standing there does not take it while off

        kernel.RunTicks(30 * 3);   // off until 9 s; delta takes it a second later
        Assert.Equal(Team.Dusk, kernel.State.WatcherOwner(0));
    }

    [Fact]
    public void AHeroPassingBy_TakesAReadyWatcher()
    {
        var draft = WithWatcher();
        var kernel = Start(draft);
        Put(kernel, 0, "alpha", P(4300, 2300));
        Put(kernel, 0, "beta", P(1200, 9000));
        Put(kernel, 0, "gamma", P(9000, 9000));
        Put(kernel, 0, "delta", P(9200, 9000));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("gamma");
        settings.Uncontrolled.Add("delta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 10);

        Assert.Equal(Team.Dawn, kernel.State.WatcherOwner(0));
    }

    [Fact]
    public void Validation_AndIdentity()
    {
        var outside = WithWatcher();
        outside.Watchers = new WatcherRules(new[] { P(50000, 2000) }, 200, 1000, 10000, 5000, 800, 450);
        Assert.Contains(outside.Build().Validate(), e => e.Contains("Watcher"));
        var other = WithWatcher();
        other.Watchers = new WatcherRules(new[] { Watcher }, 200, 1000, 10000, 5000, 800, 800);
        Assert.NotEqual(other.Build().ToDescriptor().Fingerprint, WithWatcher().Build().ToDescriptor().Fingerprint);
    }
}
