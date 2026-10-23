using System.Collections.Generic;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Heroes
{
    /// <summary>One carried item: its permanent bonus modifier and, for active items, its castable ability.</summary>
    public sealed class ItemSlot
    {
        internal ItemSlot(ItemDefinition definition, AbilityState? active, ActiveModifier? bonus)
        {
            Definition = definition;
            Active = active;
            Bonus = bonus;
        }

        public ItemDefinition Definition { get; }

        /// <summary>Castable ability of an active item (always level 1); null for passive items.</summary>
        public AbilityState? Active { get; }

        internal ActiveModifier? Bonus { get; }
    }

    internal readonly struct ShopRequest
    {
        public ShopRequest(string itemId, bool sell)
        {
            ItemId = itemId;
            Sell = sell;
        }

        public string ItemId { get; }
        public bool Sell { get; }
    }

    public static class HeroShop
    {
        public const int InventorySize = 6;

        /// <summary>Requests a purchase; it is validated and carried out by the shop system on the next tick.</summary>
        public static void QueuePurchase(MatchUnit hero, string itemId) => hero.Hero!.ShopRequests.Add(new ShopRequest(itemId, sell: false));

        public static void QueueSale(MatchUnit hero, string itemId) => hero.Hero!.ShopRequests.Add(new ShopRequest(itemId, sell: true));
    }
}
