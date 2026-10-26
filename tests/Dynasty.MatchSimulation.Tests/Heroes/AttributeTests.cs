using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Heroes;

/// <summary>
/// Dota's attributes (Liquipedia «Attributes»): every point of strength gives 22 health and 0.1 health
/// regeneration, of agility 1/6 armor and 1 attack speed, of intelligence 12 mana, 0.05 mana regeneration
/// and 0.1% magic resistance; the primary attribute adds 1 attack damage per point, a universal hero
/// 0.45 per point of every attribute. Attributes grow by their gain every level.
/// </summary>
public class AttributeTests
{
    private static readonly int[] DotaXp = { 0, 240, 640, 1160, 1760, 2440, 3200, 4000, 4900, 5900, 7000 };

    private static MatchUnit H(MatchState state) => state.Units.Single(u => u.Hero != null);

    /// <summary>Fractional stats carry the fixed-point error of their per-point factors (0.1, 1/6, 0.05).</summary>
    private static void Near(double expected, Fixed actual) => Assert.InRange(actual.ToDouble(), expected - 0.001, expected + 0.001);

    /// <summary>Base 120 health, 75 mana, 0.5 health regeneration, 0 armor, 25% magic resistance, 30–34 damage, 1.7 s base attack time.</summary>
    private static HeroDefinition Hero(PrimaryAttribute primary) => new(
        "alpha", "alpha", new[] { "carry" },
        new UnitStats(120, Fixed.Zero, 300, 30, 34, 150, 1700, 300, 800, 24, magicResistPercent: 25, visionRange: 1200),
        hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 75, manaPerLevel: 0,
        hpRegenPerSecond: Fixed.FromRatio(1, 2), manaRegenPerSecond: Fixed.Zero,
        attributes: new HeroAttributes(primary, strength: 25, agility: 15, intelligence: 18,
            strengthGain: Fixed.FromInt(3), agilityGain: Fixed.FromRatio(3, 2), intelligenceGain: Fixed.FromRatio(9, 5)));

    private static SimulationKernel<MatchState> Start(PrimaryAttribute primary, int xp = 0, ModifierSpec? modifier = null)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.XpTable.Clear();
        draft.XpTable.AddRange(DotaXp);
        draft.Heroes[0] = Hero(primary);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            if (xp > 0)
                HeroProgression.GrantXp(ctx, H(ctx.State), xp);
            if (modifier != null)
                Modifiers.Apply(ctx, H(ctx.State), modifier, EntityId.None);
        }));
        kernel.Step();
        return kernel;
    }

    [Fact]
    public void Strength_GivesHealthAndRegeneration_AndDamageAsPrimary()
    {
        MatchUnit alpha = H(Start(PrimaryAttribute.Strength).State);

        Assert.Equal(25, alpha.Strength.ToIntFloor());
        Assert.Equal(120 + 22 * 25, alpha.MaxHp);
        Assert.Equal(Fixed.FromInt(alpha.MaxHp), alpha.Hp);   // spawns at full health
        Near(0.5 + 2.5, alpha.HpRegenPerSecond);
        Assert.Equal(30 + 25, alpha.AttackDamageMin);
        Assert.Equal(34 + 25, alpha.AttackDamageMax);
    }

    [Fact]
    public void Agility_GivesArmorAndAttackSpeed()
    {
        MatchUnit alpha = H(Start(PrimaryAttribute.Agility).State);

        Near(2.5, alpha.Armor);
        Assert.Equal(15, alpha.AttackSpeedBonus);
        Assert.Equal(30 + 15, alpha.AttackDamageMin);
    }

    [Fact]
    public void Intelligence_GivesManaRegenerationAndMagicResistance()
    {
        MatchUnit alpha = H(Start(PrimaryAttribute.Intelligence).State);

        Assert.Equal(75 + 12 * 18, alpha.Hero!.MaxMana);
        Assert.Equal(Fixed.FromInt(alpha.Hero.MaxMana), alpha.Hero.Mana);
        Near(0.9, alpha.ManaRegenPerSecond);
        Assert.Equal(26, alpha.MagicResistPercent);   // 25 + 1.8, rounded down
        Assert.Equal(30 + 18, alpha.AttackDamageMin);
    }

    [Fact]
    public void Universal_GetsDamageFromEveryAttribute()
    {
        MatchUnit alpha = H(Start(PrimaryAttribute.Universal).State);

        Assert.Equal(30 + (25 + 15 + 18) * 45 / 100, alpha.AttackDamageMin);   // 0.45 per point: 26
    }

    [Fact]
    public void Attributes_GrowEveryLevel()
    {
        MatchUnit alpha = H(Start(PrimaryAttribute.Strength, xp: 7000).State);   // level 11

        Assert.Equal(11, alpha.Hero!.Level);
        Assert.Equal(Fixed.FromInt(25 + 30), alpha.Strength);
        Assert.Equal(Fixed.FromInt(15 + 15), alpha.Agility);
        Assert.Equal(Fixed.FromInt(18 + 18), alpha.Intelligence);
        Assert.Equal(120 + 22 * 55, alpha.MaxHp);
        Assert.Equal(30 + 55, alpha.AttackDamageMin);
        Assert.Equal(30, alpha.AttackSpeedBonus);
    }

    [Fact]
    public void AttributeBonuses_FromModifiers_AddToTheAttributes()
    {
        MatchUnit alpha = H(Start(PrimaryAttribute.Strength,
            modifier: new ModifierSpec("item.belt", -1, strengthBonus: 10, agilityBonus: 6, intelligenceBonus: 12)).State);

        Assert.Equal(120 + 22 * 35, alpha.MaxHp);
        Assert.Equal(30 + 35, alpha.AttackDamageMin);
        Assert.Equal(21, alpha.AttackSpeedBonus);
        Assert.Equal(75 + 12 * 30, alpha.Hero!.MaxMana);
    }

    [Fact]
    public void BaseAttackSpeed_OtherThan100_IsKept()
    {
        var draft = new RulesetDraft().WithoutWaves();
        HeroDefinition basic = Hero(PrimaryAttribute.Agility);
        draft.Heroes[0] = new HeroDefinition(basic.Id, basic.Name, basic.Roles, basic.BaseStats, 0, Fixed.Zero, 0, 75, 0, Fixed.Zero, Fixed.Zero,
            attributes: new HeroAttributes(PrimaryAttribute.Agility, 25, 15, 18, Fixed.Zero, Fixed.Zero, Fixed.Zero, baseAttackSpeed: 115));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        kernel.Step();

        Assert.Equal(15 + 15, H(kernel.State).AttackSpeedBonus);
    }
}
