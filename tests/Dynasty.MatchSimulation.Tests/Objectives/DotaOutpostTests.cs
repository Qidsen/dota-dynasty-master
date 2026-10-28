using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Outposts (Fandom «Outpost»; Valve 7.24: they start owned by the side they stand on, 7.33: no experience and no vision any
/// more, 7.36: teleporting to one takes as long as a Town Portal): an Outpost cannot be attacked; enemy heroes within 300 capture
/// it in 6 s divided by how many of them are there; its owner's heroes can teleport to it.
/// </summary>
public class DotaOutpostTests
{
    private static readonly FixedVector2 Outpost = P(5000, 2000);

    private static RulesetDraft WithOutpost()
    {
        RulesetDraft draft = Heroes.TeleportTests.WithTeleport(new RulesetDraft().WithoutWaves());
        draft.Heroes.Add(Hero("delta"));
        draft.Outposts = new OutpostRules(new[] { Outpost }, new[] { Team.Dusk }, radius: 300, captureMs: 6000);
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static MatchUnit O(MatchState state) => state.Units.Single(u => u.Kind == UnitKind.Outpost);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static void Put(SimulationKernel<MatchState> kernel, long tick, string hero, FixedVector2 at) => At(kernel, tick, ctx => H(ctx.State, hero).Position = at);

    [Fact]
    public void ItStartsWithItsSide_AndCannotBeAttacked()
    {
        var kernel = Start(WithOutpost());
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "alpha").Id, O(ctx.State), Fixed.FromInt(99999), DamageType.Pure));

        kernel.RunTicks(5);

        Assert.Equal(Team.Dusk, O(kernel.State).Team);
        Assert.False(kernel.State.IsVulnerable(O(kernel.State)));
        Assert.True(O(kernel.State).IsAlive);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 3)]
    public void EnemyHeroesAround_CaptureIt_InSixSecondsDividedByTheirNumber(int heroes, int seconds)
    {
        var kernel = Start(WithOutpost());
        Put(kernel, 1, "alpha", P(5100, 2000));
        if (heroes == 2)
            Put(kernel, 1, "beta", P(4900, 2000));
        Team before = Team.Neutral;
        At(kernel, 1 + 30 * seconds - 2, ctx => before = O(ctx.State).Team);

        kernel.RunTicks(1 + 30 * seconds + 2);

        Assert.Equal(Team.Dusk, before);
        Assert.Equal(Team.Dawn, O(kernel.State).Team);
        Assert.Equal(Team.Dawn, Assert.Single(kernel.EventsOf<OutpostCaptured>()).Team);
    }

    [Fact]
    public void ItsOwnersHeroes_TeleportToIt()
    {
        var kernel = Start(WithOutpost());
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "gamma").Position = P(9000, 9000);
            HeroShop.Give(ctx, H(ctx.State, "gamma"), ctx.State.Ruleset.FindItem("tp_scroll")!);
        });
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "gamma"), HeroOrder.Cast("teleport", O(ctx.State).Id)));

        kernel.RunTicks(30 * 6);

        Assert.True(FixedVector2.Distance(H(kernel.State, "gamma").Position, Outpost) < Fixed.FromInt(600), "at " + H(kernel.State, "gamma").Position);
    }

    [Fact]
    public void SpellsOnAlliedUnits_CannotTargetIt_LikeABuilding()
    {
        RulesetDraft draft = WithOutpost();
        draft.Abilities.Add(new AbilityDefinition("mend", "Mend", AbilityTargeting.Unit, TargetTeam.Ally, 20000, 0,
            new[] { 1000 }, new[] { 0 }, new[] { 1 }, new[] { EffectDefinition.Heal(100) }));
        draft.Heroes[draft.Heroes.FindIndex(h => h.Id == "gamma")] = Hero("gamma", abilities: new[] { "mend" });
        var kernel = Start(draft);
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "gamma"), HeroOrder.Cast("mend", O(ctx.State).Id)));

        kernel.RunTicks(5);

        Assert.Contains(kernel.EventsOf<AbilityCastFailed>(), f => f.AbilityId == "mend");
    }

    [Fact]
    public void AHeroNearby_CapturesTheEnemysOutpost()
    {
        var kernel = WithOutpost().StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid") }, new[] { ("gamma", "mid"), ("delta", "mid") });
        Put(kernel, 0, "alpha", P(4200, 2400));
        Put(kernel, 0, "beta", P(1200, 9000));
        Put(kernel, 0, "gamma", P(9000, 9000));
        Put(kernel, 0, "delta", P(9200, 9000));
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("gamma");
        settings.Uncontrolled.Add("delta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 15);

        Assert.Equal(Team.Dawn, O(kernel.State).Team);
    }

    /// <summary>
    /// Back at the base with the lane front far away (the waves fight at mid-lane) and no own tower worth it, a hero teleports to
    /// the own Outpost nearest the front.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BackAtTheBase_AHeroTeleportsToTheOwnOutpostNearTheFront(bool outpost)
    {
        RulesetDraft draft = Heroes.TeleportTests.WithTeleport();
        draft.DawnWave.Add(new WaveEntry("melee", 1));
        draft.DuskWave.Add(new WaveEntry("melee", 1));
        if (outpost)
            draft.Outposts = new OutpostRules(new[] { P(5500, 5300) }, new[] { Team.Dawn }, 300, 6000);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("tp_scroll")!);
            H(ctx.State, "beta").Position = P(9500, 9500);
        });
        At(kernel, 30 * 20, ctx => H(ctx.State, "alpha").Position = P(300, 5000));   // back home with the waves at mid-lane
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 25);   // a 3 s channel

        Assert.Equal(outpost, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "teleport"));
    }

    [Fact]
    public void Validation_AndIdentity()
    {
        var outside = WithOutpost();
        outside.Outposts = new OutpostRules(new[] { P(50000, 2000) }, new[] { Team.Dusk }, 300, 6000);
        Assert.Contains(outside.Build().Validate(), e => e.Contains("Outpost"));
        var unowned = WithOutpost();
        unowned.Outposts = new OutpostRules(new[] { Outpost }, Array.Empty<Team>(), 300, 6000);
        Assert.Contains(unowned.Build().Validate(), e => e.Contains("Outpost"));
        var other = WithOutpost();
        other.Outposts = new OutpostRules(new[] { Outpost }, new[] { Team.Dawn }, 300, 6000);
        Assert.NotEqual(other.Build().ToDescriptor().Fingerprint, WithOutpost().Build().ToDescriptor().Fingerprint);
    }
}
