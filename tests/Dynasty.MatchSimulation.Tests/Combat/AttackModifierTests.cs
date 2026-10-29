using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Combat;

/// <summary>
/// Attack modifiers of Dota heroes (data of 7.41f and the official notes): Mortal Strike's critical strike on a cooldown, Chaos Strike's
/// critical strike of a random strength with lifesteal on it, Fervor's stacks on one target dropped on a new one, Arcane Orb and
/// Impetus: an attack that spends mana and deals damage by the mana left (after spending) or by the distance (capped).
/// </summary>
public class AttackModifierTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    /// <summary>Dota stacking rules; heroes with 50 attack damage exactly, no armor, no regeneration, 1000 mana.</summary>
    private static SimulationKernel<MatchState> Arena(int betaX = 5150)
    {
        var draft = new RulesetDraft { DotaStacking = true }.WithoutWaves();
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta", "gamma", "delta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(5000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 1000, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("gamma", "mid") }, new[] { ("beta", "mid"), ("delta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(betaX, 5000);
            H(ctx.State, "delta").Position = P(5000, 5150);
            H(ctx.State, "gamma").Position = P(3000, 5000);
        });
        return kernel;
    }

    private static void Give(SimulationKernel<MatchState> kernel, ModifierSpec spec) =>
        At(kernel, 1, ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), spec, H(ctx.State, "alpha").Id));

    private static void Hit(SimulationKernel<MatchState> kernel, long tick, string target = "beta") =>
        At(kernel, tick, ctx => AttackSystem.Strike(ctx, H(ctx.State, "alpha"), H(ctx.State, target), 100));

    private static List<Fixed> Hits(SimulationKernel<MatchState> kernel, string target = "beta") =>
        kernel.EventsOf<DamageDealt>().Where(d => d.Attacker == H(kernel.State, "alpha").Id && d.Target == H(kernel.State, target).Id).Select(d => d.Amount).ToList();

    [Fact]
    public void CritOnACooldown_EveryHitWhenReady_NoneUntilItIsBack()
    {
        var kernel = Arena();
        Give(kernel, new ModifierSpec("mortal", -1, critChanceHundredths: 10000, critMultiplierPercent: 200, procCooldownMs: 1000));
        Hit(kernel, 2);
        Hit(kernel, 3);
        Hit(kernel, 2 + 31);

        kernel.RunTicks(2 + 32);

        Assert.Equal(new[] { Fixed.FromInt(100), Fixed.FromInt(50), Fixed.FromInt(100) }, Hits(kernel));
    }

    [Fact]
    public void CritOfARandomStrength_FallsBetweenItsBounds_AndHealsByItsLifesteal()
    {
        var kernel = Arena();
        Give(kernel, new ModifierSpec("chaos", -1, critChanceHundredths: 10000, critMultiplierPercent: 120, critMultiplierMaxPercent: 270, critLifestealPercent: 50));
        At(kernel, 1, ctx => H(ctx.State, "alpha").Hp = Fixed.FromInt(1000));
        for (long tick = 2; tick < 42; tick++)
            Hit(kernel, tick);

        kernel.RunTicks(42);

        List<Fixed> hits = Hits(kernel);
        Assert.All(hits, h => Assert.InRange(h.ToDouble(), 60, 135));
        Assert.True(hits.Distinct().Count() > 5, "the strength varies");
        Assert.Equal(1000 + hits.Sum(h => h.ToDouble()) / 2, H(kernel.State, "alpha").Hp.ToDouble(), 0);
    }

    [Fact]
    public void Stacks_GrowOnOneTarget_AndDropOnANewOne()
    {
        var kernel = Arena();
        var stack = new ModifierSpec("fervor_stack", -1, attackSpeedBonus: 20, maxStacks: 10);
        Give(kernel, new ModifierSpec("fervor", -1, onHit: new[] { EffectDefinition.OnCaster(EffectDefinition.Modifier(stack)) }, resetStacksKey: "fervor_stack"));
        int onBeta = 0, onDelta = 0;
        for (long tick = 2; tick < 6; tick++)
            Hit(kernel, tick);
        At(kernel, 6, ctx => onBeta = H(ctx.State, "alpha").FindModifier("fervor_stack", H(ctx.State, "alpha").Id)?.Stacks ?? 0);
        Hit(kernel, 7, "delta");
        At(kernel, 8, ctx => onDelta = H(ctx.State, "alpha").FindModifier("fervor_stack", H(ctx.State, "alpha").Id)?.Stacks ?? 0);

        kernel.RunTicks(9);

        Assert.Equal(4, onBeta);
        Assert.Equal(1, onDelta);
    }

    [Fact]
    public void ManaPercentOrb_SpendsAShareOfTheMana_ThenDamagesByTheManaLeft()
    {
        var kernel = Arena();
        var orb = EffectDefinition.ScaledDamage(DamageType.Pure, DamageScaling.CasterCurrentManaPercent, 1000);   // 10% of the mana
        Give(kernel, new ModifierSpec("orb", -1, onHit: new[] { orb }, onHitManaCostPercent: 20));
        Hit(kernel, 2);

        kernel.RunTicks(3);

        Assert.Equal(800, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 2);
        Assert.Contains(Hits(kernel), h => System.Math.Abs(h.ToDouble() - 80) < 0.05);   // 10% of the 800 left (fixed-point percent)
    }

    [Theory]
    [InlineData(5600, 60)]    // 600 away: 10% of it
    [InlineData(7500, 175)]   // 2500 away: capped at 1750
    public void DistanceOrb_DamagesByTheDistance_Capped_ForItsManaCost(int betaX, int expected)
    {
        var kernel = Arena(betaX);
        var impetus = EffectDefinition.CappedScaledDamage(DamageType.Pure, DamageScaling.DistanceToTargetPercent, 1750, 1000);
        Give(kernel, new ModifierSpec("impetus", -1, onHit: new[] { impetus }, onHitManaCost: 40));
        Hit(kernel, 2);

        kernel.RunTicks(3);

        Assert.Equal(960, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 2);
        Assert.Contains(Hits(kernel), h => System.Math.Abs(h.ToDouble() - expected) < 0.05);
    }

    [Fact]
    public void ManaCostOrb_DoesNotProc_WithoutTheMana()
    {
        var kernel = Arena(5600);
        var impetus = EffectDefinition.CappedScaledDamage(DamageType.Pure, DamageScaling.DistanceToTargetPercent, 1750, 1000);
        Give(kernel, new ModifierSpec("impetus", -1, onHit: new[] { impetus }, onHitManaCost: 40));
        At(kernel, 1, ctx => H(ctx.State, "alpha").Hero!.Mana = Fixed.FromInt(30));
        Hit(kernel, 2);

        kernel.RunTicks(3);

        Assert.Equal(new[] { Fixed.FromInt(50) }, Hits(kernel));   // the attack alone
        Assert.Equal(30, H(kernel.State, "alpha").Hero!.Mana.ToDouble(), 2);
    }
}
