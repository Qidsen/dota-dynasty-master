using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Abilities;

/// <summary>
/// Morphling's Attribute Shift (data of 7.41f; the official notes: it cannot convert stats from items, levelling Agility Gain levels
/// Strength Gain too; 10 mana a second while shifting per rdy.gg and dotacoach for 7.41): two toggles moving points of the base
/// attributes one way or the other, one point a period, never below one base point; turning one on turns the other off.
/// </summary>
public class AttributeShiftTests
{
    private static MatchUnit H(MatchState state, string id) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == id);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static HeroDefinition Morphling() => new(
        "alpha", "alpha", new[] { "carry" },
        new UnitStats(120, Fixed.Zero, 300, 30, 34, 150, 1700, 300, 800, 24, magicResistPercent: 25, visionRange: 1200),
        hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 300, manaPerLevel: 0,
        hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
        abilityIds: new[] { "to_agility", "to_strength" },
        attributes: new HeroAttributes(PrimaryAttribute.Agility, strength: 6, agility: 20, intelligence: 18,
            strengthGain: Fixed.Zero, agilityGain: Fixed.Zero, intelligenceGain: Fixed.Zero));

    private static ModifierSpec Shift(string key, int direction) =>
        new ModifierSpec(key, -1, periodMs: 250, manaCostPerSecond: Fixed.FromInt(10), attributeShift: direction);

    private static SimulationKernel<MatchState> Start(Action<SimulationContext<MatchState>>? setup = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Heroes[0] = Morphling();
        draft.Abilities.Add(new AbilityDefinition("to_agility", "Agility Gain", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0,
            new[] { 0, 0 }, new[] { 0, 0 }, new[] { 1, 3 }, Array.Empty<EffectDefinition>(), toggle: new[] { Shift("to_agility", 1), Shift("to_agility", 1) }));
        draft.Abilities.Add(new AbilityDefinition("to_strength", "Strength Gain", AbilityTargeting.NoTarget, TargetTeam.Ally, 0, 0,
            new[] { 0, 0 }, new[] { 0, 0 }, new[] { 1, 3 }, Array.Empty<EffectDefinition>(), toggle: new[] { Shift("to_strength", -1), Shift("to_strength", -1) },
            levelsWith: "to_agility"));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(9000, 9000);
            setup?.Invoke(ctx);
        });
        return kernel;
    }

    private static void Cast(SimulationKernel<MatchState> kernel, long tick, string ability) =>
        At(kernel, tick, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.CastNoTarget(ability)));

    [Fact]
    public void Shifting_MovesOnePointAPeriod_AndCostsManaEverySecond()
    {
        var kernel = Start();
        Cast(kernel, 2, "to_agility");
        Fixed manaAtStart = Fixed.Zero;
        At(kernel, 2, ctx => manaAtStart = H(ctx.State, "alpha").Hero!.Mana);

        kernel.RunTicks(2 + 30 + 1);   // one second: four points at one per 0.25 s

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.InRange(alpha.Agility.ToIntFloor(), 23, 25);
        Assert.Equal(26, alpha.Strength.ToIntFloor() + alpha.Agility.ToIntFloor());   // moved, not made
        Assert.InRange((manaAtStart - alpha.Hero!.Mana).ToDouble(), 5, 12.5);   // about 10 mana a second, less its regeneration
    }

    [Fact]
    public void Shifting_StopsAtOneBasePoint_AndNeverTakesItemStats()
    {
        var kernel = Start(ctx => Modifiers.Apply(ctx, H(ctx.State, "alpha"), new ModifierSpec("item_strength", -1, strengthBonus: 10), H(ctx.State, "alpha").Id));
        Cast(kernel, 2, "to_agility");

        kernel.RunTicks(2 + 4 * 30);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(1 + 10, alpha.Strength.ToIntFloor());   // the base 6 down to 1; the item's 10 stay
        Assert.Equal(25, alpha.Agility.ToIntFloor());
    }

    [Fact]
    public void TurningOneOn_TurnsTheOtherOff_AndShiftsBack()
    {
        var kernel = Start();
        Cast(kernel, 2, "to_agility");
        Cast(kernel, 2 + 30, "to_strength");

        kernel.RunTicks(2 + 30 + 2 * 30);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.DoesNotContain(alpha.Modifiers, m => m.Spec.Key == "to_agility");
        Assert.Contains(alpha.Modifiers, m => m.Spec.Key == "to_strength");
        Assert.InRange(alpha.Strength.ToIntFloor(), 9, 11);   // 4 points out, then 8 back
    }

    [Fact]
    public void LevellingOne_LevelsTheOtherToo_ForOneSkillPoint()
    {
        var kernel = Start(ctx => H(ctx.State, "alpha").Hero!.Level = 3);

        kernel.RunTicks(3);

        HeroState hero = H(kernel.State, "alpha").Hero!;
        Assert.Equal(2, hero.Ability("to_agility")!.Level);
        Assert.Equal(2, hero.Ability("to_strength")!.Level);
        Assert.Equal(1, hero.UnspentSkillPoints + hero.AttributeBonusLevel);   // 3 points: 2 into the pair, 1 left (or into the attribute bonus)
    }

    /// <summary>The AI shifts into strength when hurt and into agility when healthy, and stops when there is nothing left to shift.</summary>
    [Fact]
    public void Ai_ShiftsToStrengthWhenHurt_ToAgilityWhenHealthy()
    {
        var kernel = Start();
        var settings = new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0, LaningPhaseMinutes = 0 };
        settings.Uncontrolled.Add("beta");
        HeroAi.Attach(kernel, settings);
        bool agilityWhenHealthy = false, strengthWhenHurt = false;
        At(kernel, 30, ctx => agilityWhenHealthy = H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "to_agility"));
        At(kernel, 31, ctx => H(ctx.State, "alpha").Hp = Fixed.FromInt(H(ctx.State, "alpha").MaxHp / 4));
        At(kernel, 60, ctx => strengthWhenHurt = H(ctx.State, "alpha").Modifiers.Any(m => m.Spec.Key == "to_strength"));

        kernel.RunTicks(61);

        Assert.True(agilityWhenHealthy);
        Assert.True(strengthWhenHurt);
    }
}
