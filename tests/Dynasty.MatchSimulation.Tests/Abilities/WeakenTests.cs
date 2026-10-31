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
/// Dota 7.41f data and official notes: Natural Order removes a share of the base armor and base magic resistance (bonuses stay);
/// Corrosive Weaponry reduces attack damage by fractions of a percent per stack; Eye of the Storm strikes the enemy with the lowest
/// health within its radius.
/// </summary>
public class WeakenTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena(bool agileBeta = false)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.FromInt(5), 300, 100, 100, 150, 1500, 300, 800, 24, 25, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                attributes: agileBeta && id == "beta"
                    ? new HeroAttributes(PrimaryAttribute.Agility, strength: 0, agility: 30, intelligence: 0, strengthGain: Fixed.Zero, agilityGain: Fixed.Zero, intelligenceGain: Fixed.Zero)
                    : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5150, 5000);
            H(ctx.State, "delta").Position = P(5400, 5000);
            H(ctx.State, "gamma").Position = P(2000, 2000);
        });
        return kernel;
    }

    [Theory]
    [InlineData(100, 3, 10)]   // base 5 armor and 25% resistance gone, the bonuses (+3, +10%) stay
    [InlineData(40, 6, 25)]    // 5 * 0.6 + 3; 25 * 0.6 + 10
    public void NaturalOrder_RemovesAShareOfTheBaseArmorAndResistance(int percent, int armor, int resist)
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            Modifiers.Apply(ctx, beta, new ModifierSpec("bonus", 10000, armorBonus: Fixed.FromInt(3), magicResistBonus: 10), beta.Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("natural_order", 10000, baseArmorReductionPercent: percent, baseMagicResistReductionPercent: percent),
                H(ctx.State, "alpha").Id);
        });

        kernel.RunTicks(3);

        Assert.Equal(armor, H(kernel.State, "beta").Armor.ToDouble(), 1);
        Assert.Equal(resist, H(kernel.State, "beta").MagicResistPercent);
    }

    /// <summary>The armor from agility is base armor too (the white number in Dota; bonus armor stays).</summary>
    [Fact]
    public void NaturalOrder_RemovesTheArmorFromAgilityToo()
    {
        var kernel = Arena(agileBeta: true);
        At(kernel, 1, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            Modifiers.Apply(ctx, beta, new ModifierSpec("bonus", 10000, armorBonus: Fixed.FromInt(3)), beta.Id);
            Modifiers.Apply(ctx, beta, new ModifierSpec("natural_order", 10000, baseArmorReductionPercent: 100), H(ctx.State, "alpha").Id);
        });

        kernel.RunTicks(3);

        Assert.True(H(kernel.State, "beta").Agility > Fixed.Zero);
        Assert.Equal(3, H(kernel.State, "beta").Armor.ToDouble(), 1);
    }

    private static double FirstHitOfBeta(int attackDamageHundredths)
    {
        var kernel = Arena();
        At(kernel, 1, ctx =>
        {
            MatchUnit beta = H(ctx.State, "beta");
            Modifiers.Apply(ctx, beta, new ModifierSpec("corrosion", 10000, attackDamageHundredths: attackDamageHundredths), H(ctx.State, "alpha").Id);
        });
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "beta"), HeroOrder.Attack(H(ctx.State, "alpha").Id)));
        kernel.RunTicks(2 * 30);
        return kernel.EventsOf<DamageDealt>().First(d => d.Attacker == H(kernel.State, "beta").Id).Amount.ToDouble();
    }

    [Fact]
    public void CorrosiveWeaponry_ReducesAttackDamageByAFractionOfAPercent()
    {
        Assert.Equal(0.975, FirstHitOfBeta(-250) / FirstHitOfBeta(0), 3);
    }

    [Fact]
    public void Area_LowestHealthFirst_TakesTheWeakestEnemy_NotTheNearest()
    {
        var kernel = Arena();
        At(kernel, 1, ctx => H(ctx.State, "delta").Hp = Fixed.FromInt(500));   // farther, weaker
        At(kernel, 2, ctx => Effects.Run(ctx, H(ctx.State, "alpha"),
            new[] { EffectDefinition.Area(500, AreaCenter.Caster, TargetTeam.Enemy, 1, lowestHealthFirst: true, EffectDefinition.Damage(DamageType.Pure, 60)) },
            1, H(ctx.State, "alpha"), H(ctx.State, "alpha").Position));

        kernel.RunTicks(4);

        Assert.Equal(440, H(kernel.State, "delta").Hp.ToDouble(), 0);
        Assert.Equal(2000, H(kernel.State, "beta").Hp.ToDouble(), 0);
    }
}
