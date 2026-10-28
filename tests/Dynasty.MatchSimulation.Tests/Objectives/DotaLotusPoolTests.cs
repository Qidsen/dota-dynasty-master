using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Lotus Pools (Fandom «Lotus Pool» and «Healing Lotus»; Valve 7.38, 7.41; game file `ability_lotus_pool`): a pool grows a
/// Healing Lotus every 3 minutes, up to 6; a hero within 350 picks them one after another — the first in 1.5 s, each next one in a
/// row 30% faster, at least 0.3 s —, each going to a random hero of the team in range; heroes of both teams in range run the
/// countdown back second for second; nobody in range resets it and the run. Here a lotus every 10 s, up to 3.
/// </summary>
public class DotaLotusPoolTests
{
    private static readonly FixedVector2 Pool = P(5000, 2000);

    private static RulesetDraft WithPool()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Add(Hero("delta"));
        draft.Items.Add(new ItemDefinition("healing_lotus", "Healing Lotus", 0, inShop: false));
        draft.LotusPools = new LotusPoolRules(new[] { Pool }, firstMs: 10000, intervalMs: 10000, max: 3, radius: 350, firstPickMs: 1500, nextPickPercent: 70,
            minPickMs: 300, itemId: "healing_lotus");
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static void Put(SimulationKernel<MatchState> kernel, long tick, string hero, FixedVector2 at) => At(kernel, tick, ctx => H(ctx.State, hero).Position = at);

    private static int Lotuses(MatchState state, string hero) => H(state, hero).Hero!.Inventory.Count(i => i.Definition.Id == "healing_lotus");

    [Fact]
    public void APoolGrowsALotusEveryInterval_UpToItsMax()
    {
        var kernel = Start(WithPool());
        int at15 = -1;
        At(kernel, 30 * 15, ctx => at15 = ctx.State.LotusCount(0));

        kernel.RunTicks(30 * 60);

        Assert.Equal(1, at15);
        Assert.Equal(3, kernel.State.LotusCount(0));
    }

    [Fact]
    public void AHeroPicksThemOneAfterAnother_EachNextFaster()
    {
        var kernel = Start(WithPool());
        Put(kernel, 30 * 30, "alpha", P(5100, 2000));   // three lotuses by then

        kernel.RunTicks(30 * 30 + 45 + 1);   // the first at 1.5 s
        Assert.Equal(1, Lotuses(kernel.State, "alpha"));
        kernel.RunTicks(32);                 // the second 1.05 s later
        Assert.Equal(2, Lotuses(kernel.State, "alpha"));
        kernel.RunTicks(23);                 // the third 0.735 s later
        Assert.Equal(3, Lotuses(kernel.State, "alpha"));
        Assert.Equal(0, kernel.State.LotusCount(0));
        Assert.Equal(3, kernel.EventsOf<LotusPicked>().Count());
    }

    [Fact]
    public void LeavingStartsTheRunOver()
    {
        var kernel = Start(WithPool());
        Put(kernel, 30 * 30, "alpha", P(5100, 2000));
        Put(kernel, 30 * 30 + 50, "alpha", P(1000, 8000));    // after the first
        Put(kernel, 30 * 30 + 52, "alpha", P(5100, 2000));

        kernel.RunTicks(30 * 30 + 52 + 40);   // 1.33 s since: not yet 1.5 again

        Assert.Equal(1, Lotuses(kernel.State, "alpha"));
    }

    [Fact]
    public void BothTeamsInRange_RunTheCountdownBack()
    {
        var kernel = Start(WithPool());
        Put(kernel, 30 * 10, "alpha", P(5100, 2000));
        Put(kernel, 30 * 10 + 30, "gamma", P(4900, 2000));   // 1 s counted, then contested 1 s: back to 0
        Put(kernel, 30 * 10 + 60, "gamma", P(1000, 8000));

        kernel.RunTicks(30 * 10 + 60 + 44);   // 1.4 s alone again
        Assert.Equal(0, Lotuses(kernel.State, "alpha"));
        kernel.RunTicks(4);
        Assert.Equal(1, Lotuses(kernel.State, "alpha"));
    }

    [Fact]
    public void ALotusGoesToARandomHeroOfTheTeamInRange()
    {
        var pickers = new HashSet<string>();
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var kernel = WithPool().StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") }, seed);
            Put(kernel, 30 * 30, "alpha", P(5100, 2000));
            Put(kernel, 30 * 30, "beta", P(4900, 2000));

            kernel.RunTicks(30 * 34);

            Assert.Equal(3, Lotuses(kernel.State, "alpha") + Lotuses(kernel.State, "beta"));
            foreach (LotusPicked picked in kernel.EventsOf<LotusPicked>())
                pickers.Add(kernel.State.Find(picked.Hero)!.Hero!.Definition.Id);
        }

        Assert.Equal(new[] { "alpha", "beta" }, pickers.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void TheTeamsNearestHero_PicksTheLotuses_AndEatsOneWhenHurt()
    {
        var draft = WithPool();
        draft.Items.Clear();
        draft.Abilities.Add(new AbilityDefinition("item_healing_lotus", "Healing Lotus", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0,
            new[] { 5000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Heal(125) }));
        draft.Items.Add(new ItemDefinition("healing_lotus", "Healing Lotus", 0, activeAbilityId: "item_healing_lotus", charges: 1, inShop: false));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 2500);
            H(ctx.State, "beta").Position = P(1500, 5000);
            H(ctx.State, "gamma").Position = P(9500, 9300);
            H(ctx.State, "delta").Position = P(9500, 9500);
        });
        At(kernel, 30 * 25, ctx => H(ctx.State, "alpha").Hp -= Fixed.FromInt(200));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("gamma");
        settings.Uncontrolled.Add("delta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 30);

        Assert.All(kernel.EventsOf<LotusPicked>(), p => Assert.Equal(H(kernel.State, "alpha").Id, p.Hero));
        Assert.NotEmpty(kernel.EventsOf<LotusPicked>());
        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "item_healing_lotus" && c.Caster == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void Validation_AndIdentity()
    {
        var unknown = WithPool();
        unknown.LotusPools = new LotusPoolRules(new[] { Pool }, 10000, 10000, 3, 350, 1500, 70, 300, "mango");
        Assert.Contains(unknown.Build().Validate(), e => e.Contains("mango"));
        var outside = WithPool();
        outside.LotusPools = new LotusPoolRules(new[] { P(50000, 2000) }, 10000, 10000, 3, 350, 1500, 70, 300, "healing_lotus");
        Assert.Contains(outside.Build().Validate(), e => e.Contains("Lotus"));

        var other = WithPool();
        other.LotusPools = new LotusPoolRules(new[] { Pool }, 10000, 10000, 6, 350, 1500, 70, 300, "healing_lotus");
        Assert.NotEqual(other.Build().ToDescriptor().Fingerprint, WithPool().Build().ToDescriptor().Fingerprint);
    }
}
