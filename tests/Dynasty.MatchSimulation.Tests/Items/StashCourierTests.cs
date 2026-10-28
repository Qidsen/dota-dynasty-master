using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Items;

/// <summary>
/// Dota's stash, backpack and courier (Fandom «Inventory», «Courier»): an item can be bought anywhere — in the base shop it goes to
/// the hero (the inventory, then the 3-slot backpack), elsewhere to the 6-slot stash, which the courier brings to the hero: at
/// 290 + 10 per hero level, flying (a straight line) from hero level 4. Items in the backpack give nothing; the hero keeps its
/// most valuable items in the inventory. Recipes take components from anywhere the hero owns them.
/// </summary>
public class StashCourierTests
{
    private static RulesetDraft WithShop()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.StartingGold = 20000;
        draft.Stash = new StashRules(backpackSlots: 3, stashSlots: 6, courierSpeed: 290, courierSpeedPerLevel: 10, courierFliesFromLevel: 4);
        draft.Items.Add(new ItemDefinition("blade", "Blade", 400, new ModifierSpec("item.blade", -1, damageBonus: 10)));
        draft.Items.Add(new ItemDefinition("ring", "Ring", 100, new ModifierSpec("item.ring", -1, armorBonus: Fixed.One)));
        draft.Items.Add(new ItemDefinition("sword", "Sword", 1000, new ModifierSpec("item.sword", -1, damageBonus: 40), components: new[] { "blade", "blade" }));
        return draft;
    }

    private static MatchUnit H(MatchState state, string heroId) => state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static void At(SimulationKernel<MatchState> kernel, long tick, Action<SimulationContext<MatchState>> action) =>
        kernel.AddSystem(new LambdaSystem(tick, action));

    private static SimulationKernel<MatchState> Start(RulesetDraft draft) => draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());

    private static IEnumerable<string> Ids(IEnumerable<ItemSlot> slots) => slots.Select(s => s.Definition.Id);

    [Fact]
    public void BoughtAwayFromTheBase_AnItemGoesToTheStash_AndTheCourierBringsIt()
    {
        var kernel = Start(WithShop());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(200 + 2900, 5000));   // 2900 from the fountain: 10 s at level 1
        At(kernel, 2, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "blade"));
        bool inStash = false, carriedEarly = true;
        At(kernel, 4, ctx => inStash = Ids(H(ctx.State, "alpha").Hero!.Stash).Contains("blade") || H(ctx.State, "alpha").Hero!.CourierCarrying.Any());
        At(kernel, 30 * 9, ctx => carriedEarly = Ids(H(ctx.State, "alpha").Hero!.Inventory).Contains("blade"));

        kernel.RunTicks(30 * 11);

        Assert.True(inStash);
        Assert.False(carriedEarly);
        Assert.Contains("blade", Ids(H(kernel.State, "alpha").Hero!.Inventory));
        Assert.Empty(H(kernel.State, "alpha").Hero!.Stash);
    }

    [Fact]
    public void TheCourierFlies_FasterAtHigherHeroLevels()
    {
        var kernel = Start(WithShop());
        At(kernel, 1, ctx =>
        {
            HeroProgression.GrantXp(ctx, H(ctx.State, "alpha"), 1500);   // level 4 or more: 330+ a second
            H(ctx.State, "alpha").Position = P(200 + 2900, 5000);
        });
        At(kernel, 2, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "blade"));

        kernel.RunTicks(30 * 9);

        Assert.True(H(kernel.State, "alpha").Hero!.Level >= 4);
        Assert.Contains("blade", Ids(H(kernel.State, "alpha").Hero!.Inventory));
    }

    [Fact]
    public void AFullInventory_SpillsIntoTheBackpack_WhereItemsGiveNothing_AndTheBestStayActive()
    {
        var kernel = Start(WithShop());
        At(kernel, 2, ctx =>
        {
            foreach (string id in new[] { "ring", "ring", "ring", "ring", "ring", "ring", "blade" })
                HeroShop.QueuePurchase(H(ctx.State, "alpha"), id);   // at the fountain
        });

        kernel.RunTicks(5);

        HeroState hero = H(kernel.State, "alpha").Hero!;
        Assert.Equal(6, hero.Inventory.Count);
        Assert.Contains("blade", Ids(hero.Inventory));
        Assert.Equal(new[] { "ring" }, Ids(hero.Backpack));
        Assert.Equal(5, H(kernel.State, "alpha").Modifiers.Count(m => m.Spec.Key == "item.ring"));
    }

    [Fact]
    public void ARecipe_TakesItsComponentsFromWhereverTheHeroOwnsThem()
    {
        var kernel = Start(WithShop());
        At(kernel, 2, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "blade"));   // at the fountain: carried
        At(kernel, 4, ctx =>
        {
            H(ctx.State, "alpha").Position = P(6000, 5000);
            HeroShop.QueuePurchase(H(ctx.State, "alpha"), "blade");   // away: to the stash
            HeroShop.QueuePurchase(H(ctx.State, "alpha"), "sword");   // one blade carried, one in the stash
        });

        kernel.RunTicks(7);

        HeroState hero = H(kernel.State, "alpha").Hero!;
        Assert.Contains("sword", Ids(hero.Stash.Concat(hero.CourierCarrying)));
        Assert.DoesNotContain("blade", Ids(hero.Inventory.Concat(hero.Backpack).Concat(hero.Stash).Concat(hero.CourierCarrying)));
        Assert.Equal(200, kernel.EventsOf<ItemPurchased>().Single(p => p.ItemId == "sword").Cost);
    }

    [Fact]
    public void NetWorth_CountsTheStashAndTheCourier()
    {
        var kernel = Start(WithShop());
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(6000, 5000));
        At(kernel, 2, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "blade"));

        kernel.RunTicks(10);

        HeroState hero = H(kernel.State, "alpha").Hero!;
        Assert.Empty(hero.Inventory);
        Assert.Equal(hero.Gold + Fixed.FromInt(400), hero.NetWorth);
    }

    [Fact]
    public void TheAi_BuysItsBuildAwayFromTheBase_AndCountsWhatTheCourierBrings()
    {
        var draft = WithShop();
        draft.Heroes[0] = Hero("alpha", itemBuild: new[] { "sword" });
        var kernel = Start(draft);
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(4000, 5000));
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });

        kernel.RunTicks(30 * 3);

        Assert.Equal(new[] { "blade", "blade", "sword" }, kernel.EventsOf<ItemPurchased>().Select(p => p.ItemId).Where(i => i != "tp_scroll" && i != "salve"));
    }

    [Fact]
    public void OneCopyOfAConsumable_StaysActive_TheRestGoesToTheBackpack()
    {
        var draft = Heroes.TeleportTests.WithTeleport(WithShop());
        var kernel = Start(draft);
        At(kernel, 2, ctx =>
        {
            foreach (string id in new[] { "tp_scroll", "tp_scroll", "tp_scroll", "blade", "blade", "blade", "blade", "blade" })
                HeroShop.QueuePurchase(H(ctx.State, "alpha"), id);
        });

        kernel.RunTicks(5);

        HeroState hero = H(kernel.State, "alpha").Hero!;
        Assert.Equal(1, hero.Inventory.Count(i => i.Definition.Id == "tp_scroll"));
        Assert.Equal(5, hero.Inventory.Count(i => i.Definition.Id == "blade"));
    }

    [Fact]
    public void TheAi_DoesNotBuyAnotherScroll_WhileOneIsOnItsWay()
    {
        var draft = Heroes.TeleportTests.WithTeleport(WithShop());
        var kernel = Start(draft);
        At(kernel, 0, ctx => H(ctx.State, "alpha").Position = P(6000, 5000));
        HeroAi.Attach(kernel, new HeroAiSettings { ReactionJitterTicks = 0, MisjudgementPercent = 0 });

        kernel.RunTicks(30 * 5);

        Assert.Single(kernel.EventsOf<ItemPurchased>(), p => p.ItemId == "tp_scroll");
    }

    [Fact]
    public void WithoutTheseRules_TheShopIsOnlyAtTheFountain()
    {
        var draft = WithShop();
        draft.Stash = null;
        var kernel = Start(draft);
        At(kernel, 1, ctx => H(ctx.State, "alpha").Position = P(6000, 5000));
        At(kernel, 2, ctx => HeroShop.QueuePurchase(H(ctx.State, "alpha"), "blade"));

        kernel.RunTicks(5);

        Assert.Contains(kernel.EventsOf<ItemPurchaseFailed>(), f => f.Reason == "not_in_shop");
    }
}
