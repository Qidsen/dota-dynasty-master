using System.Collections.Generic;
using System.Linq;
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

        /// <summary>The bonus while the item is active (in the inventory); null in the backpack, the stash or on the courier.</summary>
        internal ActiveModifier? Bonus { get; set; }
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
            StashRules? stash = context.State.Ruleset.HeroRules?.Stash;
            if (unit.Hero != null && stash != null)
            {
                // Dota: carried when there is room (inventory, backpack), else in the stash.
                if (!Carry(context, unit, item) && !Store(context, unit, item))
                    return false;
                context.Emit(new Events.ItemReceived(unit.Id, item.Id));
                return true;
            }

            if (unit.Hero == null || unit.Hero.Inventory.Count >= InventorySize)
                return false;
            Put(context, unit, item);
            context.Emit(new Events.ItemReceived(unit.Id, item.Id));
            return true;
        }

        /// <summary>Carries a new item (inventory or backpack, then rearranged); false without room.</summary>
        internal static bool Carry(Dynasty.Core.Kernel.SimulationContext<MatchState> context, MatchUnit unit, ItemDefinition item)
        {
            HeroState hero = unit.Hero!;
            StashRules rules = context.State.Ruleset.HeroRules!.Stash!;
            if (hero.InventorySlots.Count + hero.BackpackSlots.Count >= InventorySize + rules.BackpackSlots)
                return false;
            hero.BackpackSlots.Add(NewSlot(context, item));
            Arrange(context, unit);
            return true;
        }

        /// <summary>Puts a new item in the stash; false when it is full.</summary>
        internal static bool Store(Dynasty.Core.Kernel.SimulationContext<MatchState> context, MatchUnit unit, ItemDefinition item)
        {
            HeroState hero = unit.Hero!;
            if (hero.StashSlots.Count >= context.State.Ruleset.HeroRules!.Stash!.StashSlots)
                return false;
            hero.StashSlots.Add(NewSlot(context, item));
            return true;
        }

        private static ItemSlot NewSlot(Dynasty.Core.Kernel.SimulationContext<MatchState> context, ItemDefinition item)
        {
            AbilityState? active = item.ActiveAbilityId != null ? new AbilityState(context.State.Ruleset.FindAbility(item.ActiveAbilityId)!, level: 1) : null;
            return new ItemSlot(item, active, null);
        }

        /// <summary>
        /// Keeps the most useful carried items in the inventory and the rest in the backpack: one of each consumable first (it is
        /// meant to be used; Dota stacks the copies in one slot), then the dearest; an item moved out of the inventory loses its
        /// bonus, one moved in gains it.
        /// </summary>
        internal static void Arrange(Dynasty.Core.Kernel.SimulationContext<MatchState> context, MatchUnit unit)
        {
            HeroState hero = unit.Hero!;
            var carried = hero.InventorySlots.Concat(hero.BackpackSlots).ToList();
            var firstConsumables = carried.Where(s => s.Definition.Charges > 0).GroupBy(s => s.Definition.Id).Select(g => g.First()).ToList();
            var all = carried
                .OrderByDescending(s => firstConsumables.Contains(s) ? 1 : 0).ThenByDescending(s => s.Definition.Cost).ThenBy(s => s.Definition.Id, System.StringComparer.Ordinal)
                .ToList();
            var active = all.Take(InventorySize).ToList();
            foreach (ItemSlot slot in hero.InventorySlots.Where(s => !active.Contains(s)).ToList())
            {
                if (slot.Bonus != null)
                    Combat.Modifiers.Remove(context, unit, slot.Bonus, "item_to_backpack");
                slot.Bonus = null;
            }

            foreach (ItemSlot slot in active.Where(s => !hero.InventorySlots.Contains(s)))
                Activate(context, unit, slot);

            hero.InventorySlots.Clear();
            hero.InventorySlots.AddRange(active);
            hero.BackpackSlots.Clear();
            hero.BackpackSlots.AddRange(all.Skip(InventorySize));
            HeroProgression.RefreshUpgrades(context, unit);
        }

        private static void Activate(Dynasty.Core.Kernel.SimulationContext<MatchState> context, MatchUnit unit, ItemSlot slot)
        {
            int oldMaxHp = unit.MaxHp;
            slot.Bonus = slot.Definition.Bonuses != null ? Combat.Modifiers.AddInstance(context, unit, slot.Definition.Bonuses, unit.Id) : null;
            if (unit.IsAlive && unit.MaxHp > oldMaxHp)
                unit.Hp += Dynasty.Core.Mathematics.Fixed.FromInt(unit.MaxHp - oldMaxHp);
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
