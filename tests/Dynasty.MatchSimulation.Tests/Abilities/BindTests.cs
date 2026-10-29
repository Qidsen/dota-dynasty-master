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
/// Soulbind (Dota 7.41f data and the official description; the project owner: the leash is the distance at binding times 1.3 plus
/// 50): the target is bound to its nearest allied hero within the radius; neither can go farther from the other; a unit-targeted
/// ability either of them receives is cast on the other too (not an item).
/// </summary>
public class BindTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static readonly EffectDefinition Soulbind = EffectDefinition.Bind(600, 130, 50, new ModifierSpec("soulbind", 6000));

    /// <summary>alpha and gamma against beta and delta; beta at the centre, delta where given; alpha has a nuke and an item nuke.</summary>
    private static SimulationKernel<MatchState> Arena(FixedVector2 delta, FixedVector2? epsilon = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 2000, 0, new[] { 1000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 100) }));
        draft.Abilities.Add(new AbilityDefinition("dagon", "Dagon", AbilityTargeting.Unit, TargetTeam.Enemy, 2000, 0, new[] { 1000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 300) }));
        draft.Items.Add(new ItemDefinition("dagon_item", "Dagon", 0, activeAbilityId: "dagon"));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta", "epsilon" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "nuke" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid"), ("epsilon", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 5000);
            H(ctx.State, "beta").Position = P(5000, 5000);
            H(ctx.State, "delta").Position = delta;
            H(ctx.State, "epsilon").Position = epsilon ?? P(9000, 9000);
            H(ctx.State, "gamma").Position = P(5000, 5300);   // alpha's ally, nearer than delta: not a partner
            HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("dagon_item")!);
        });
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"), new[] { Soulbind }, 1, H(ctx.State, "beta"), H(ctx.State, "beta").Position));
        return kernel;
    }

    private static bool Bound(MatchUnit unit) => unit.Modifiers.Any(m => m.Spec.Key == "soulbind");

    [Theory]
    [InlineData(400, true)]
    [InlineData(700, false)]   // beyond the bind radius
    public void Target_IsBoundToItsNearestAlliedHeroWithinTheRadius(int deltaAway, bool bound)
    {
        var kernel = Arena(P(5000 + deltaAway, 5000));

        kernel.RunTicks(4);

        Assert.True(Bound(H(kernel.State, "beta")));
        Assert.Equal(bound, Bound(H(kernel.State, "delta")));
        Assert.False(Bound(H(kernel.State, "gamma")));
    }

    [Fact]
    public void Target_IsBoundToTheNearestOfItsAllies()
    {
        var kernel = Arena(P(5500, 5000), P(4700, 5000));   // epsilon nearer than delta

        kernel.RunTicks(4);

        Assert.True(Bound(H(kernel.State, "epsilon")));
        Assert.False(Bound(H(kernel.State, "delta")));
    }

    [Fact]
    public void Leash_Ends_WhenThePartnerDies()
    {
        var kernel = Arena(P(5400, 5000));
        At(kernel, 4, ctx =>
        {
            Damage.Execute(ctx, H(ctx.State, "alpha").Id, H(ctx.State, "delta"));
            HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(2000, 5000)));
        });

        kernel.RunTicks(4 * 30);

        Assert.True(H(kernel.State, "beta").Position.X < Fixed.FromInt(4000));
    }

    [Fact]
    public void Bound_CannotWalkFartherThanTheLeash_AndThePartnerIsNotDragged()
    {
        var kernel = Arena(P(5400, 5000));   // 400 apart: leash 400 * 1.3 + 50 = 570
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(2000, 5000))));

        kernel.RunTicks(4 * 30);

        Fixed apart = FixedVector2.Distance(H(kernel.State, "beta").Position, H(kernel.State, "delta").Position);
        Assert.True(apart <= Fixed.FromInt(571) && apart > Fixed.FromInt(540), apart.ToString());
        Assert.Equal(P(5400, 5000), H(kernel.State, "delta").Position);
    }

    [Fact]
    public void Leash_Ends_WithTheBind()
    {
        var kernel = Arena(P(5400, 5000));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Move(P(2000, 5000))));

        kernel.RunTicks(9 * 30);   // 6 s bind, then 3 s of walking free

        Assert.True(FixedVector2.Distance(H(kernel.State, "beta").Position, H(kernel.State, "delta").Position) > Fixed.FromInt(1000));
    }

    [Theory]
    [InlineData("nuke", 100)]
    [InlineData("dagon", 0)]   // an item is not copied
    public void TargetedAbility_OnOne_IsCastOnTheOtherToo(string spell, int partnerDamage)
    {
        var kernel = Arena(P(5400, 5000));
        At(kernel, 5, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast(spell, H(ctx.State, "beta").Id)));

        kernel.RunTicks(10);

        MatchUnit delta = H(kernel.State, "delta");
        Assert.True(H(kernel.State, "beta").Hp < Fixed.FromInt(2000));
        Assert.Equal(partnerDamage, (Fixed.FromInt(delta.MaxHp) - delta.Hp).ToDouble(), 0);
    }
}
