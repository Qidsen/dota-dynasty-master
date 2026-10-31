using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Dota 7.41f data and official descriptions. Dead in the Water: the hero moves freely within the chain's length of the anchor and
/// beyond it only at the drag speed, dragging the anchor. Planar Pocket: unit-targeted abilities its caster's enemies cast on the
/// pocketed hero go to the caster instead, until the caster is too far from it.
/// </summary>
public class AnchorAndPocketTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 2000, 0, new[] { 5000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 100) }));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: new[] { "nuke" }));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "gamma").Position = P(5300, 5000);
            H(ctx.State, "beta").Position = P(5900, 5000);
            H(ctx.State, "delta").Position = P(5900, 5400);
        });
        return kernel;
    }

    private static Fixed Lost(MatchState state, string hero) => Fixed.FromInt(2000) - H(state, hero).Hp;

    [Theory]
    [InlineData(100, 520, 550)]   // 350 freely (~1.2 s at 300), then 100 a second for the rest of the 3 s
    [InlineData(300, 860, 910)]   // a drag speed as fast as walking: no hold
    public void Anchor_TheHeroGoesBeyondTheChainOnlyAtTheDragSpeed(int dragSpeed, int least, int most)
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), new ModifierSpec("anchor", 10000, anchorChain: 350, anchorDragSpeed: dragSpeed), H(ctx.State, "alpha").Id));
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(9000, 5000))));

        kernel.RunTicks(2 + 3 * 30);

        Fixed went = H(kernel.State, "beta").Position.X - Fixed.FromInt(5900);
        Assert.True(went > Fixed.FromInt(least) && went < Fixed.FromInt(most), went.ToString());
    }

    [Fact]
    public void Anchor_IsDragged_SoWalkingBackIsFreeAgain()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "beta"), new ModifierSpec("anchor", 10000, anchorChain: 350, anchorDragSpeed: 100), H(ctx.State, "alpha").Id));
        At(kernel, 2, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(9000, 5000))));
        Fixed turned = Fixed.Zero;
        At(kernel, 2 + 3 * 30, ctx =>
        {
            turned = H(ctx.State, "beta").Position.X;
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(1000, 5000)));
        });

        kernel.RunTicks(2 + 3 * 30 + 30);

        // Back towards the dragged anchor at full speed: about 300 in a second.
        Fixed back = turned - H(kernel.State, "beta").Position.X;
        Assert.True(back > Fixed.FromInt(280), back.ToString());
    }

    private static readonly ModifierSpec Pocket = new ModifierSpec("planar_pocket", 6000, redirectsSpells: true, breakDistance: 900);

    [Theory]
    [InlineData("gamma", "beta", "alpha")]    // an enemy's spell on alpha's pocketed ally goes to alpha
    [InlineData("beta", "gamma", "beta")]     // alpha's ally casting on a pocketed enemy: not redirected
    public void Pocket_RedirectsTheCastersEnemiesSpells(string pocketed, string caster, string hit)
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, pocketed), Pocket, H(ctx.State, "alpha").Id));
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, caster), HeroOrder.Cast("nuke", H(ctx.State, pocketed).Id)));

        kernel.RunTicks(8);

        Assert.Equal(100, Lost(kernel.State, hit).ToDouble(), 0);
        if (hit != pocketed)
            Assert.Equal(0, Lost(kernel.State, pocketed).ToDouble(), 0);
    }

    [Fact]
    public void Pocket_Breaks_WhenTheCasterIsTooFar()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "gamma"), Pocket, H(ctx.State, "alpha").Id));
        At(kernel, 2, ctx => H(ctx.State, "alpha").Position = P(1000, 1000));
        At(kernel, 5, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Cast("nuke", H(ctx.State, "gamma").Id)));

        kernel.RunTicks(10);

        Assert.Equal(100, Lost(kernel.State, "gamma").ToDouble(), 0);
        Assert.Equal(0, Lost(kernel.State, "alpha").ToDouble(), 0);
    }
}
