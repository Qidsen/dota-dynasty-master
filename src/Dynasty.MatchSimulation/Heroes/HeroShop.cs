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
            Charges = definition.Charges;
        }

        public ItemDefinition Definition { get; }

        /// <summary>Remaining uses of a consumable; 0 for non-consumables.</summary>
        public int Charges { get; internal set; }

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

        /// <summary>Puts an item into the hero's inventory for free (Dota's Roshan drops); false if there is no room.</summary>
        public static bool Give(Dynasty.Core.Kernel.SimulationContext<MatchState> context, MatchUnit unit, ItemDefinition item)
        {
            if (unit.Hero == null || unit.Hero.Inventory.Count >= InventorySize)
                return false;
            Put(context, unit, item);
            context.Emit(new Events.ItemReceived(unit.Id, item.Id));
            return true;
        }

        /// <summary>The item's bonus (its health added to the current health), its active, and its slot.</summary>
        internal static void Put(Dynasty.Core.Kernel.SimulationContext<MatchState> context, MatchUnit unit, ItemDefinition item)
        {
            HeroState hero = unit.Hero!;
            int oldMaxHp = unit.MaxHp;
            Combat.ActiveModifier? bonus = item.Bonuses != null ? Combat.Modifiers.AddInstance(context, unit, item.Bonuses, unit.Id) : null;
            if (unit.IsAlive && unit.MaxHp > oldMaxHp)
                unit.Hp += Dynasty.Core.Mathematics.Fixed.FromInt(unit.MaxHp - oldMaxHp);

            AbilityState? active = item.ActiveAbilityId != null ? new AbilityState(context.State.Ruleset.FindAbility(item.ActiveAbilityId)!, level: 1) : null;
            hero.InventorySlots.Add(new ItemSlot(item, active, bonus));
            HeroProgression.RefreshUpgrades(context, unit);
        }

        /// <summary>Requests a purchase; it is validated and carried out by the shop system on the next tick.</summary>
        public static void QueuePurchase(MatchUnit hero, string itemId) => hero.Hero!.ShopRequests.Add(new ShopRequest(itemId, sell: false));

        public static void QueueSale(MatchUnit hero, string itemId) => hero.Hero!.ShopRequests.Add(new ShopRequest(itemId, sell: true));
    }
}
