using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Shrines of Wisdom (Valve 7.38, 7.41; game file `ability_xp_fountain`; the project owner on 2026-10-28): every 7 minutes
/// each shrine fills with experience — 200 at the first activation of the match, 300 more at each later one, by the clock; what is
/// not taken stays and adds up, as Wisdom Runes «never despawn» (Fandom «Runes»; 7.38: «identical to Wisdom Runes»). A hero standing
/// within 300 for 3 s takes it all: a random hero of its team in range and their teammate with the least experience get it.
/// Heroes of both teams in range run the countdown back second for second down to zero; nobody in range resets it. Here the
/// interval is 10 s.
/// </summary>
public class DotaShrineTests
{
    private static readonly FixedVector2 Shrine = P(5000, 2000);

    private static RulesetDraft WithShrine()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Add(Hero("delta"));
        draft.Shrines = new ShrineRules(new[] { Shrine }, firstMs: 10000, intervalMs: 10000, radius: 300, captureMs: 3000, xp: 200, xpPerInterval: 300);
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid") }, new[] { ("delta", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Keeps the heroes where they are put (no AI here) from the given tick.</summary>
    private static void Put(SimulationKernel<MatchState> kernel, long tick, string hero, FixedVector2 at) => At(kernel, tick, ctx => H(ctx.State, hero).Position = at);

    private static int Xp(MatchState state, string hero) => H(state, hero).Hero!.Xp;

    [Fact]
    public void AHeroStandingThreeSeconds_TakesIt_ForARandomHeroInRangeAndTheTeammateWithTheLeastExperience()
    {
        var kernel = Start(WithShrine());
        Put(kernel, 1, "alpha", P(5100, 2000));
        Put(kernel, 1, "beta", P(1000, 8000));
        Put(kernel, 1, "gamma", P(1000, 8200));
        Put(kernel, 1, "delta", P(9000, 9000));
        At(kernel, 2, ctx => HeroProgression.GrantXp(ctx, H(ctx.State, "gamma"), 300));   // beta has the least

        kernel.RunTicks(30 * 13 + 1);   // active at 10 s, taken at 13 s

        var taken = Assert.Single(kernel.EventsOf<ShrineTaken>());
        Assert.Equal((Team.Dawn, 200), (taken.Team, taken.Xp));
        Assert.Equal(200, Xp(kernel.State, "alpha"));
        Assert.Equal(200, Xp(kernel.State, "beta"));
        Assert.Equal(300, Xp(kernel.State, "gamma"));
    }

    [Fact]
    public void NotBeforeItsTime_NorBeforeThreeSeconds()
    {
        var kernel = Start(WithShrine());
        Put(kernel, 1, "alpha", P(5100, 2000));

        kernel.RunTicks(30 * 13 - 2);

        Assert.Empty(kernel.EventsOf<ShrineTaken>());
    }

    [Fact]
    public void WhatIsNotTaken_AddsUp_AtTheValueOfTheTime()
    {
        var kernel = Start(WithShrine());
        Put(kernel, 1, "alpha", P(1000, 8000));
        Put(kernel, 30 * 21, "alpha", P(5100, 2000));   // after the second activation

        kernel.RunTicks(30 * 25);

        Assert.Equal(2 * 500, Assert.Single(kernel.EventsOf<ShrineTaken>()).Xp);
    }

    [Fact]
    public void BothTeamsInRange_RunTheCountdownBack()
    {
        var kernel = Start(WithShrine());
        Put(kernel, 1, "alpha", P(5100, 2000));
        Put(kernel, 1, "delta", P(9000, 9000));
        Put(kernel, 30 * 12, "delta", P(4900, 2000));   // 2 s counted, then contested for 2 s: back to 0
        Put(kernel, 30 * 14, "delta", P(9000, 9000));

        kernel.RunTicks(30 * 16 + 15);   // 2 more seconds alone: 2 of 3

        Assert.Empty(kernel.EventsOf<ShrineTaken>());
        kernel.RunTicks(30 + 1);
        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<ShrineTaken>()).Team);
    }

    [Fact]
    public void LeavingTheRange_ResetsTheCountdown()
    {
        var kernel = Start(WithShrine());
        Put(kernel, 1, "alpha", P(5100, 2000));
        Put(kernel, 30 * 12, "alpha", P(1000, 8000));
        Put(kernel, 30 * 12 + 2, "alpha", P(5100, 2000));

        kernel.RunTicks(30 * 14 + 30);   // back at 12 s, only 2.9 s since

        Assert.Empty(kernel.EventsOf<ShrineTaken>());
    }

    [Fact]
    public void TheTeamsNearestHero_GoesToTakeAFilledShrine()
    {
        // Two against two (no push on numbers); the enemy stands far away, uncontrolled.
        var kernel = WithShrine().StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 2500);
            H(ctx.State, "beta").Position = P(1500, 5000);
            H(ctx.State, "gamma").Position = P(9500, 9300);
            H(ctx.State, "delta").Position = P(9500, 9500);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("gamma");
        settings.Uncontrolled.Add("delta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 30);   // filled at 10 s; alpha is at its lane by then, 1600 away

        ShrineTaken taken = Assert.Single(kernel.EventsOf<ShrineTaken>());
        Assert.Equal((Team.Dawn, H(kernel.State, "alpha").Id), (taken.Team, taken.Taker));
    }

    [Fact]
    public void Validation_AndIdentity()
    {
        var outside = WithShrine();
        outside.Shrines = new ShrineRules(new[] { P(50000, 2000) }, 10000, 10000, 300, 3000, 200, 300);
        Assert.Contains(outside.Build().Validate(), e => e.Contains("Shrine"));
        var never = WithShrine();
        never.Shrines = new ShrineRules(new[] { Shrine }, 10000, 0, 300, 3000, 200, 300);
        Assert.Contains(never.Build().Validate(), e => e.Contains("Shrine"));

        var other = WithShrine();
        other.Shrines = new ShrineRules(new[] { Shrine }, 10000, 10000, 300, 3000, 200, 280);
        Assert.NotEqual(other.Build().ToDescriptor().Fingerprint, WithShrine().Build().ToDescriptor().Fingerprint);
    }
}
