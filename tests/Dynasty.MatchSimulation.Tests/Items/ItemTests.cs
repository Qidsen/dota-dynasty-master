using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Items;

public class ItemTests
{
    private static readonly AbilityDefinition Zap = new("zap", "Zap", AbilityTargeting.Unit, TargetTeam.Enemy, 600, 0,
        new[] { 15000 }, new[] { 0 }, new[] { 1 },
        new[] { EffectDefinition.Modifier(ModifierSpec.Status("zap.stun", 1000, ModifierFlags.Stunned)) });

    private static RulesetDraft Shop()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Abilities.Add(Zap);
        draft.Items.AddRange(new[]
        {
            new ItemDefinition("blade", "Blade", 500, new ModifierSpec("blade", -1, damageBonus: 20)),
            new ItemDefinition("plate", "Plate", 400, new ModifierSpec("plate", -1, armorBonus: Fixed.FromInt(5))),
            new ItemDefinition("orb", "Orb", 300, new ModifierSpec("orb", -1, maxHpBonus: 200)),
            new ItemDefinition("pebble", "Pebble", 10),
            new ItemDefinition("crest", "Crest", 1500, new ModifierSpec("crest", -1, damageBonus: 30, armorBonus: Fixed.FromInt(8)), new[] { "blade", "plate" }),
            new ItemDefinition("wand", "Wand", 600, activeAbilityId: "zap"),
        });
        return draft;
    }

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) =>
        draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static void Buy(SimulationKernel<MatchState> kernel, long tick, params string[] items) =>
        At(kernel, tick, ctx =>
        {
            foreach (string item in items)
                HeroShop.QueuePurchase(H(ctx.State, "alpha"), item);
        });

    [Fact]
    public void Purchase_AtTheFountainSpendsGoldAndGrantsBonuses()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "blade");

        kernel.RunTicks(2);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.InRange(alpha.Hero!.Gold.ToDouble(), 100, 100.5);
        Assert.Equal(70, alpha.AttackDamageMin);
        Assert.Equal(new[] { "blade" }, alpha.Hero.Inventory.Select(i => i.Definition.Id));
        ItemPurchased bought = Assert.Single(kernel.EventsOf<ItemPurchased>());
        Assert.Equal(500, bought.Cost);
    }

    [Fact]
    public void Purchase_FailsWithoutEnoughGold()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "crest");

        kernel.RunTicks(2);

        Assert.Empty(H(kernel.State, "alpha").Hero!.Inventory);
        Assert.Equal("gold", Assert.Single(kernel.EventsOf<ItemPurchaseFailed>()).Reason);
    }

    [Fact]
    public void Purchase_FailsAwayFromTheShop()
    {
        var kernel = Start(Shop());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(3000, 3000));
        Buy(kernel, 0, "blade");

        kernel.RunTicks(2);

        Assert.Equal("not_in_shop", Assert.Single(kernel.EventsOf<ItemPurchaseFailed>()).Reason);
    }

    [Fact]
    public void Purchase_FailsWhenTheInventoryIsFull()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "pebble", "pebble", "pebble", "pebble", "pebble", "pebble", "pebble");

        kernel.RunTicks(2);

        Assert.Equal(6, H(kernel.State, "alpha").Hero!.Inventory.Count);
        Assert.Equal("inventory_full", Assert.Single(kernel.EventsOf<ItemPurchaseFailed>()).Reason);
    }

    [Fact]
    public void Recipe_ConsumesOwnedComponentsAndChargesTheRemainder()
    {
        var kernel = Start(Shop());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(5000));
        Buy(kernel, 0, "blade", "plate", "crest");

        kernel.RunTicks(2);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(new[] { "crest" }, alpha.Hero!.Inventory.Select(i => i.Definition.Id));
        Assert.Equal(600, kernel.EventsOf<ItemPurchased>().Single(p => p.ItemId == "crest").Cost);
        Assert.Equal(80, alpha.AttackDamageMin);   // 50 + 30: component bonuses are gone
        Assert.Equal(Fixed.FromInt(10), alpha.Armor);
        Assert.InRange(alpha.Hero.Gold.ToDouble(), 3500, 3500.5);
    }

    [Fact]
    public void IdenticalItems_StackTheirBonuses()
    {
        var kernel = Start(Shop());
        At(kernel, 0, ctx => H(ctx.State, "alpha").Hero!.Gold = Fixed.FromInt(5000));
        Buy(kernel, 0, "blade", "blade");

        kernel.RunTicks(2);

        Assert.Equal(90, H(kernel.State, "alpha").AttackDamageMin);
    }

    [Fact]
    public void MaxHpItem_RaisesMaxAndCurrentHp()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "orb");

        kernel.RunTicks(2);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Equal(800, alpha.MaxHp);
        Assert.Equal(Fixed.FromInt(800), alpha.Hp);
    }

    [Fact]
    public void ActiveItem_IsCastThroughTheAbilityPipeline()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "wand");
        At(kernel, 2, ctx => H(ctx.State, "beta").Position = H(ctx.State, "alpha").Position + FixedVector2.FromInts(300, 0));
        At(kernel, 4, ctx => HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Cast("zap", H(ctx.State, "beta").Id)));   // after vision saw beta

        kernel.RunTicks(7);

        Assert.True(H(kernel.State, "beta").IsStunned);
        Assert.Equal("zap", Assert.Single(kernel.EventsOf<AbilityCast>()).AbilityId);
    }

    [Fact]
    public void Selling_RefundsHalfAndRemovesBonuses()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "blade");
        At(kernel, 2, ctx => HeroShop.QueueSale(H(ctx.State, "alpha"), "blade"));

        kernel.RunTicks(4);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.Empty(alpha.Hero!.Inventory);
        Assert.Equal(50, alpha.AttackDamageMin);
        Assert.Equal(250, Assert.Single(kernel.EventsOf<ItemSold>()).Refund);
        Assert.InRange(alpha.Hero.Gold.ToDouble(), 350, 350.5);
    }

    [Fact]
    public void Items_AreKeptThroughDeath()
    {
        var kernel = Start(Shop());
        Buy(kernel, 0, "blade");
        At(kernel, 2, ctx => Damage.Deal(ctx, H(ctx.State, "beta").Id, H(ctx.State, "alpha"), Fixed.FromInt(9999), DamageType.Pure));

        kernel.RunTicks(300);

        MatchUnit alpha = H(kernel.State, "alpha");
        Assert.True(alpha.IsAlive);
        Assert.Equal(70, alpha.AttackDamageMin);
    }

    [Fact]
    public void Validation_RejectsUnknownComponentsAndUnderpricedRecipes()
    {
        var draft = Shop();
        draft.Items.Add(new ItemDefinition("ghost", "Ghost", 100, components: new[] { "mist" }));
        draft.Items.Add(new ItemDefinition("cheap", "Cheap", 100, components: new[] { "blade" }));

        IReadOnlyList<string> errors = draft.Build().Validate();

        Assert.Contains(errors, e => e.Contains("mist"));
        Assert.Contains(errors, e => e.Contains("cheap"));
    }
}
