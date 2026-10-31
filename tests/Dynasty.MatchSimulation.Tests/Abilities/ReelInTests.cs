using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Reel In (Dota 7.41f data and the official description): while Naga channels, the units her Ensnare holds within the radius are
/// pulled towards her, up to 100 from her; others are not.
/// </summary>
public class ReelInTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly ModifierSpec Net = new ModifierSpec("ensnare", 5000, ModifierFlags.Rooted);

    /// <summary>100 every 0.5 s (200 a second), for up to 5 s.</summary>
    private static readonly AbilityDefinition ReelIn = new AbilityDefinition("reel_in", "Reel In", AbilityTargeting.NoTarget, TargetTeam.Enemy, 0, 0,
        new[] { 8000 }, new[] { 0 }, new[] { 1 }, Array.Empty<EffectDefinition>(),
        channel: new ChannelSpec(new[] { 5000 }, 500,
            new[] { EffectDefinition.Area(1600, AreaCenter.Caster, TargetTeam.Enemy, EffectDefinition.IfTargetHas("ensnare", EffectDefinition.PullUpTo(100, 100))) },
            Array.Empty<EffectDefinition>(), null));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(ReelIn);
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "reel_in" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid"), ("gamma", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5700, 5000);
            H(ctx.State, "gamma").Position = P(5000, 5700);
            foreach (string id in new[] { "beta", "gamma" })
                Modifiers.Apply(ctx, H(ctx.State, id), ModifierSpec.Status("hold", -1, ModifierFlags.Stunned), H(ctx.State, id).Id);
        });
        return kernel;
    }

    [Fact]
    public void TheEnsnared_ArePulledIn_UpTo100FromNaga_OthersStay()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), Net, H(ctx.State, "alpha").Id));
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget("reel_in")));
        double afterASecond = 0;
        At(kernel, 2 + 31, ctx => afterASecond = H(ctx.State, "beta").Position.X.ToDouble());

        kernel.RunTicks(2 + 5 * 30);

        Assert.Equal(5700 - 3 * 100, afterASecond, 0);   // at 0, 0.5 and 1 s
        Assert.Equal(5100, H(kernel.State, "beta").Position.X.ToDouble(), 0);
        Assert.Equal(P(5000, 5700), H(kernel.State, "gamma").Position);
    }

    /// <summary>The AI reels in an enemy it has netted that is not yet close, not one it has not netted nor one already close.</summary>
    [Theory]
    [InlineData(true, 700, true)]
    [InlineData(false, 700, false)]
    [InlineData(true, 250, false)]
    public void Ai_ReelsInTheNetted(bool netted, int distance, bool reels)
    {
        var kernel = Arena();
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "gamma").Position = P(9000, 9000);
            H(ctx.State, "beta").Position = P(5000 + distance, 5000);
            H(ctx.State, "beta").Hp = Fixed.FromInt(1500);
            if (netted)
                Modifiers.Apply(ctx, H(ctx.State, "beta"), Net, H(ctx.State, "alpha").Id);
        });
        var settings = new Dynasty.HeroAI.HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        settings.Uncontrolled.Add("gamma");
        Dynasty.HeroAI.HeroAi.Attach(kernel, settings);

        kernel.RunTicks(30);

        Assert.Equal(reels, kernel.EventsOf<AbilityCast>().Any(c => c.AbilityId == "reel_in"));
    }
}
