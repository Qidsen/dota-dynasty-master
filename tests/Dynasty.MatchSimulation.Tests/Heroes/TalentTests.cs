using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Tests.Abilities;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Heroes;

/// <summary>
/// Dota's skill points and talents (docs/DOTA_PACK.md, wave 3): skill points for abilities and the attribute bonus;
/// separate talent points (one of two talents per tier, the skipped ones later); talents, Aghanim's Scepter and
/// Shard switch abilities to their upgraded variants.
/// </summary>
public class TalentTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>Bolt (100 magical) with a variant for talent "alpha.bolt" (300) and for the scepter (500), and both (700).</summary>
    private static readonly AbilityDefinition UpgradableBolt = new("ubolt", "Upgradable Bolt", AbilityTargeting.Unit, TargetTeam.Enemy,
        castRange: 600, castPointMs: 0, cooldownMs: new[] { 1000 }, manaCost: new[] { 0 }, requiredHeroLevel: new[] { 1 },
        effects: new[] { EffectDefinition.Damage(DamageType.Pure, 100) },
        variants: new[]
        {
            new AbilityVariant(new[] { "alpha.bolt" }, Variant(300)),
            new AbilityVariant(new[] { "scepter" }, Variant(500)),
            new AbilityVariant(new[] { "alpha.bolt", "scepter" }, Variant(700)),
        });

    private static AbilityDefinition Variant(int damage) => new("ubolt", "Upgradable Bolt", AbilityTargeting.Unit, TargetTeam.Enemy,
        castRange: 600, castPointMs: 0, cooldownMs: new[] { 1000 }, manaCost: new[] { 0 }, requiredHeroLevel: new[] { 1 },
        effects: new[] { EffectDefinition.Damage(DamageType.Pure, damage) });

    private static readonly TalentDefinition[] AlphaTalents =
    {
        new("alpha.armor", "+5 Armor", 2, new ModifierSpec("talent.alpha.armor", -1, armorBonus: Fixed.FromInt(5))),
        new("alpha.bolt", "+200 Bolt Damage", 2),
    };

    private static SimulationKernel<MatchState> Arena(RulesetDraft draft)
    {
        draft.Abilities.Add(UpgradableBolt);
        draft.Heroes[0] = Hero("alpha", abilities: new[] { "ubolt" }, talents: AlphaTalents);
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 3000);
            H(ctx.State, "beta").Position = P(5200, 3000);
        }));
        return kernel;
    }

    private static void LevelTo(SimulationKernel<MatchState> kernel, long tick, int level) =>
        kernel.AddSystem(new LambdaSystem(tick, ctx => HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.HeroRules!.XpForLevel[level - 1] - H(ctx.State, "alpha").Hero!.Xp)));

    [Fact]
    public void OnlyListedLevelsGiveSkillPoints()
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.SkillPointLevels = new List<int> { 1, 3, 4 };
        var kernel = Arena(draft);
        LevelTo(kernel, 1, 3);
        kernel.RunTicks(3);

        HeroState alpha = H(kernel.State, "alpha").Hero!;
        Assert.Equal((3, 1), (alpha.Level, alpha.AbilityLevel("ubolt")));
        Assert.Equal(1, alpha.UnspentSkillPoints);   // levels 1 and 3 gave points; ubolt has one level
    }

    /// <summary>Dota 7.40+: talents use their own points (10, 15, 20, 25 and then 27–30 for the ones skipped), not skill points.</summary>
    [Fact]
    public void TalentPoints_AreSeparate_FirstTalentOfTheTier_ThenTheOther()
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.TalentPointLevels = new List<int> { 2, 4 };
        var kernel = Arena(draft);
        Fixed armorBefore = Fixed.Zero, armorAtTwo = Fixed.Zero;
        List<string> talentsAtThree = new();
        kernel.AddSystem(new LambdaSystem(1, ctx => armorBefore = H(ctx.State, "alpha").Armor));
        LevelTo(kernel, 1, 2);
        kernel.AddSystem(new LambdaSystem(3, ctx => armorAtTwo = H(ctx.State, "alpha").Armor));
        LevelTo(kernel, 3, 3);
        kernel.AddSystem(new LambdaSystem(5, ctx => talentsAtThree = H(ctx.State, "alpha").Hero!.Talents.ToList()));
        LevelTo(kernel, 5, 4);
        kernel.RunTicks(8);

        HeroState alpha = H(kernel.State, "alpha").Hero!;
        Assert.True(armorAtTwo - armorBefore >= Fixed.FromInt(5));   // the first talent of the tier: +5 armor (plus the level's growth)
        Assert.Equal(new[] { "alpha.armor" }, talentsAtThree);
        Assert.Equal(new[] { "alpha.armor", "alpha.bolt" }, alpha.Talents);
        Assert.Equal(2, kernel.EventsOf<TalentLearned>().Count());
        Assert.Equal(4 - 1, alpha.UnspentSkillPoints);   // talents took no skill point (ubolt took one)
    }

    /// <summary>A skill point no ability can use goes to the attribute bonus (+2 to every attribute, 7 levels in Dota).</summary>
    [Fact]
    public void SpareSkillPoints_GoToTheAttributeBonus_UpToItsLevels()
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.AttributeBonusLevels = 2;
        draft.AttributeBonusPerLevel = 2;
        var kernel = Arena(draft);
        LevelTo(kernel, 1, 4);
        kernel.RunTicks(3);

        HeroState alpha = H(kernel.State, "alpha").Hero!;
        Assert.Equal(2, alpha.AttributeBonusLevel);
        Assert.Equal(2, H(kernel.State, "alpha").Modifiers.Single(m => m.Spec.Key == "attribute_bonus").Stacks);
        Assert.Equal(1, alpha.UnspentSkillPoints);   // 4 points: ubolt, two attribute bonus levels, one left
    }

    [Theory]
    [InlineData(false, false, 100)]
    [InlineData(true, false, 300)]
    [InlineData(false, true, 500)]
    [InlineData(true, true, 700)]
    public void Upgrades_SwitchTheAbilityToItsVariant(bool talent, bool scepter, int damage)
    {
        RulesetDraft draft = TestAbilities.Draft();
        draft.TalentPointLevels = new List<int> { 2, 3 };   // level 2: the tier's first talent; level 3: the other one
        draft.Items.Add(new ItemDefinition("scepter", "Scepter", 100, upgrade: "scepter"));
        var kernel = Arena(draft);
        if (talent)
            LevelTo(kernel, 1, 3);
        if (scepter)
        {
            // Bought at the fountain, then back to the arena.
            kernel.AddSystem(new LambdaSystem(1, ctx =>
            {
                H(ctx.State, "alpha").Position = ctx.State.Ruleset.Map.FountainOf(Team.Dawn)!.Position;
                HeroShop.QueuePurchase(H(ctx.State, "alpha"), "scepter");
            }));
            kernel.AddSystem(new LambdaSystem(5, ctx => H(ctx.State, "alpha").Position = P(5000, 3000)));
        }

        kernel.AddSystem(new LambdaSystem(20, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("ubolt", H(ctx.State, "beta").Id))));
        kernel.RunTicks(25);

        Assert.Equal(damage, kernel.EventsOf<DamageDealt>().Single(d => d.Target == H(kernel.State, "beta").Id).Amount.ToIntFloor());
    }
}
