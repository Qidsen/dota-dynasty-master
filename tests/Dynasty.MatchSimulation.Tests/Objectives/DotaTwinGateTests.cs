using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Objectives;

/// <summary>
/// Dota's Twin Gates (game file `twin_gate_portal_warp`; Fandom «Twin Gate»; Valve 7.35, 7.40, 7.41e): a hero ordered to a gate walks
/// within 200 of it, pays 30 mana and channels for 4 s, then stands by the other gate; a stun, a root or another order breaks the
/// channel and gives the mana back; a rooted hero cannot start it.
/// </summary>
public class DotaTwinGateTests
{
    private static readonly FixedVector2 GateA = P(1500, 1500);
    private static readonly FixedVector2 GateB = P(8500, 8500);

    private static RulesetDraft WithGates()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.TwinGates = new TwinGateRules(new[] { GateA, GateB }, radius: 200, channelMs: 4000, manaCost: 30);
        return draft;
    }

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static void Order(SimulationKernel<MatchState> kernel)
    {
        At(kernel, 1, ctx =>
        {
            H(ctx.State, "alpha").Position = P(1800, 1500);
            H(ctx.State, "beta").Position = P(9500, 5000);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.UseGate(GateA));
        });
    }

    private static Fixed Mana(MatchState state) => H(state, "alpha").Hero!.Mana;

    [Fact]
    public void AHeroWalksToTheGate_PaysAndChannels_ThenStandsByTheOther()
    {
        var kernel = Start(WithGates());
        Order(kernel);
        Fixed manaBefore = Fixed.Zero;
        At(kernel, 1, ctx => manaBefore = Mana(ctx.State));
        FixedVector2 beforeEnd = default;
        At(kernel, 30 * 4, ctx => beforeEnd = H(ctx.State, "alpha").Position);

        kernel.RunTicks(30 * 6);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(FixedVector2.Distance(beforeEnd, GateA) < Fixed.FromInt(300), "still channeling by the first gate: " + beforeEnd);
        Assert.True(FixedVector2.Distance(alpha.Position, GateB) < Fixed.FromInt(400), "by the other gate: " + alpha.Position);
        Assert.Single(kernel.EventsOf<TwinGateUsed>());
        Assert.True(Mana(kernel.State) < manaBefore, "paid its mana");
    }

    [Fact]
    public void AStun_BreaksTheChannel_AndGivesTheManaBack()
    {
        var kernel = Start(WithGates());
        Order(kernel);
        Fixed manaBefore = Fixed.Zero;
        At(kernel, 1, ctx => manaBefore = Mana(ctx.State));
        At(kernel, 30 * 2, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("stun", 500, flags: ModifierFlags.Stunned), H(ctx.State, "beta").Id));
        Fixed manaAfter = Fixed.Zero;
        At(kernel, 30 * 2 + 2, ctx => manaAfter = Mana(ctx.State));

        kernel.RunTicks(30 * 6);

        Assert.Empty(kernel.EventsOf<TwinGateUsed>());
        Assert.True(manaAfter >= manaBefore, "the mana is given back");
        Assert.True(FixedVector2.Distance(H(kernel.State, "alpha").Position, GateA) < Fixed.FromInt(400));
    }

    [Fact]
    public void ARootedHero_CannotStartIt()
    {
        var kernel = Start(WithGates());
        Order(kernel);
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("root", 10000, flags: ModifierFlags.Rooted), H(ctx.State, "beta").Id));

        kernel.RunTicks(30 * 6);

        Assert.Empty(kernel.EventsOf<TwinGateUsed>());
    }

    [Fact]
    public void TheAi_TakesTheGate_WhenItSavesALongWalk()
    {
        // Alpha's lane is the top one, its tower by the far gate; alpha stands by the near gate.
        var draft = WithGates();
        draft.Lanes.Add(new LaneDefinition("top", new[] { P(1000, 9000), P(9000, 9000) }));
        draft.Placements.Add(new StructurePlacement("dawn.top.t1", Team.Dawn, "tower", P(8000, 8800), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "top") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(1500, 1900);
            H(ctx.State, "beta").Position = P(9500, 5000);
        });
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 };
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30 * 8);

        Assert.Contains(kernel.EventsOf<TwinGateUsed>(), u => u.Hero == H(kernel.State, "alpha").Id);
    }

    [Fact]
    public void Validation_AndIdentity()
    {
        var one = WithGates();
        one.TwinGates = new TwinGateRules(new[] { GateA }, 200, 4000, 30);
        Assert.Contains(one.Build().Validate(), e => e.Contains("Twin Gates"));
        var other = WithGates();
        other.TwinGates = new TwinGateRules(new[] { GateA, GateB }, 200, 4000, 75);
        Assert.NotEqual(other.Build().ToDescriptor().Fingerprint, WithGates().Build().ToDescriptor().Fingerprint);
    }
}
