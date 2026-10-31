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
/// Abilities Aghanim's Scepter or Shard grant (Lightning Hands, Nosedive, Friendly Shadow…): the hero has them, at their one level,
/// only while it carries the upgrade; they take no skill points.
/// </summary>
public class GrantedTests
{
    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Arena()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Items.Add(new ItemDefinition("aghanims_shard", "Aghanim's Shard", 1400, upgrade: "shard"));
        draft.Abilities.Add(new AbilityDefinition("nuke", "Nuke", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 5000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 100) }));
        draft.Abilities.Add(new AbilityDefinition("bolt", "Bolt", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 5000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 10) }));
        draft.Abilities.Add(new AbilityDefinition("gift", "Gift", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 5000 }, new[] { 0 }, new[] { 1 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 50) }, grantedBy: "shard"));
        draft.Abilities.Add(new AbilityDefinition("aura", "Aura", AbilityTargeting.Passive, TargetTeam.Ally, 0, 0, new[] { 0 }, new int[0], new[] { 1 },
            Array.Empty<EffectDefinition>(), passiveModifiers: new[] { new ModifierSpec("gifted_armor", -1, armorBonus: Fixed.FromInt(10)) }, grantedBy: "shard"));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "nuke", "bolt", "gift", "aura" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(5000, 5000);
            H(ctx.State, "beta").Position = P(5500, 5000);
        });
        return kernel;
    }

    private static AbilityState Ability(MatchState state, string id) => H(state, "alpha").Hero!.Abilities.Single(a => a.Definition.Id == id);

    [Fact]
    public void WithoutTheUpgrade_TheAbilityIsNotThere_AndTakesNoSkillPoint()
    {
        var kernel = Arena();
        At(kernel, 3, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("gift", H(ctx.State, "beta").Id)));

        kernel.RunTicks(10);

        Assert.Equal(0, Ability(kernel.State, "gift").Level);
        Assert.Equal(1, Ability(kernel.State, "nuke").Level);   // the one skill point went to the ability that needs one
        Assert.DoesNotContain(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "gift");
        Assert.Equal(0, H(kernel.State, "alpha").Armor.ToDouble(), 1);
    }

    [Fact]
    public void WithTheUpgrade_ItIsThereAtItsLevel_AndGoesWithTheUpgrade()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("aghanims_shard")!));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("gift", H(ctx.State, "beta").Id)));
        double armorWith = 0;
        At(kernel, 8, ctx => armorWith = H(ctx.State, "alpha").Armor.ToDouble());

        kernel.RunTicks(10);

        Assert.Equal(1, Ability(kernel.State, "gift").Level);
        Assert.Equal(1, Ability(kernel.State, "nuke").Level);
        Assert.Contains(kernel.EventsOf<AbilityCast>(), c => c.AbilityId == "gift");
        Assert.Equal(10, armorWith, 1);   // a granted passive works too
    }

    [Fact]
    public void WithTheUpgradeGone_TheAbilityAndItsPassiveGo()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("aghanims_shard")!));
        At(kernel, 5, ctx =>
        {
            HeroState hero = H(ctx.State, "alpha").Hero!;
            hero.InventorySlots.Remove(hero.InventorySlots.Single(s => s.Definition.Id == "aghanims_shard"));
            HeroProgression.RefreshUpgrades(ctx, H(ctx.State, "alpha"));
        });

        kernel.RunTicks(8);

        Assert.Equal(0, Ability(kernel.State, "gift").Level);
        Assert.Equal(0, H(kernel.State, "alpha").Armor.ToDouble(), 1);
    }

    /// <summary>With the upgrade the hero's next skill point still goes to an ability that needs one.</summary>
    [Fact]
    public void WithTheUpgrade_TheSkillPointsAreStillFree()
    {
        var kernel = Arena();
        At(kernel, 2, ctx => HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("aghanims_shard")!));
        At(kernel, 4, ctx =>
        {
            H(ctx.State, "alpha").Hero!.Level = 2;
            HeroProgression.LearnSkills(ctx, H(ctx.State, "alpha"));
        });

        kernel.RunTicks(6);

        Assert.Equal((1, 1), (Ability(kernel.State, "nuke").Level, Ability(kernel.State, "bolt").Level));   // both points went to abilities
    }

    /// <summary>A granted ability that grows with another (Lightning Hands with Arc Lightning) is at that ability's level.</summary>
    [Fact]
    public void AGrantedAbility_LevelledWithAnother_FollowsItsLevel()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Items.Add(new ItemDefinition("aghanims_shard", "Aghanim's Shard", 1400, upgrade: "shard"));
        draft.Abilities.Add(new AbilityDefinition("arc", "Arc", AbilityTargeting.Unit, TargetTeam.Enemy, 1000, 0, new[] { 5000, 5000, 5000 }, new[] { 0, 0, 0 }, new[] { 1, 2, 3 },
            new[] { EffectDefinition.Damage(DamageType.Pure, 100, 200, 300) }));
        draft.Abilities.Add(new AbilityDefinition("hands", "Hands", AbilityTargeting.Passive, TargetTeam.Ally, 0, 0, new[] { 0, 0, 0 }, new int[0], new[] { 1, 2, 3 },
            Array.Empty<EffectDefinition>(), passiveModifiers: new[]
            {
                new ModifierSpec("hands", -1, armorBonus: Fixed.FromInt(10)), new ModifierSpec("hands", -1, armorBonus: Fixed.FromInt(20)),
                new ModifierSpec("hands", -1, armorBonus: Fixed.FromInt(30)),
            }, levelsWith: "arc", grantedBy: "shard"));
        draft.Heroes.Clear();
        foreach (string id in new[] { "alpha", "beta" })
            draft.Heroes.Add(new HeroDefinition(id, id, new[] { "carry" }, new UnitStats(2000, Fixed.Zero, 300, 50, 50, 150, 1500, 300, 800, 24, 0, visionRange: 1800),
                hpPerLevel: 0, armorPerLevel: Fixed.Zero, damagePerLevel: 0, baseMana: 500, manaPerLevel: 0, hpRegenPerSecond: Fixed.Zero, manaRegenPerSecond: Fixed.Zero,
                abilityIds: id == "alpha" ? new[] { "arc", "hands" } : null));
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        At(kernel, 0, ctx => H(ctx.State, "beta").Position = P(9000, 9000));
        void LevelTo(SimulationContext<MatchState> ctx, int level)
        {
            H(ctx.State, "alpha").Hero!.Level = level;
            HeroProgression.LearnSkills(ctx, H(ctx.State, "alpha"));
        }

        var armor = new List<double>();
        var hands = new List<int>();
        void Note(SimulationContext<MatchState> ctx)
        {
            armor.Add(H(ctx.State, "alpha").Armor.ToDouble());
            hands.Add(Ability(ctx.State, "hands").Level);
        }

        At(kernel, 2, ctx => LevelTo(ctx, 2));   // arc 2, no shard yet
        At(kernel, 3, Note);
        At(kernel, 4, ctx => HeroShop.Give(ctx, H(ctx.State, "alpha"), ctx.State.Ruleset.FindItem("aghanims_shard")!));
        At(kernel, 5, Note);
        At(kernel, 6, ctx => LevelTo(ctx, 3));   // arc 3, with the shard
        At(kernel, 7, Note);

        kernel.RunTicks(9);

        Assert.Equal(new[] { 0, 2, 3 }, hands);
        Assert.Equal(new[] { 0.0, 20.0, 30.0 }, armor.Select(a => System.Math.Round(a)));
    }
}
