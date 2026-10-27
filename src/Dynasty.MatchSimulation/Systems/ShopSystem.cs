using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Carries out queued purchases and sales. Shopping is possible inside the team's fountain
    /// area, or while dead (the hero respawns there). Owned components are consumed by recipes.
    /// </summary>
    internal sealed class ShopSystem : ISimulationSystem<MatchState>
    {
        public string Name => "shop";

        public void Tick(SimulationContext<MatchState> context)
        {
            foreach (MatchUnit unit in context.State.Units)
            {
                HeroState? hero = unit.Hero;
                if (hero == null || hero.ShopRequests.Count == 0)
                    continue;

                List<ShopRequest> requests = hero.ShopRequests.ToList();
                hero.ShopRequests.Clear();
                foreach (ShopRequest request in requests)
                {
                    string? failure = request.Sell ? Sell(context, unit, hero, request.ItemId) : Buy(context, unit, hero, request.ItemId);
                    if (failure != null)
                        context.Emit(new ItemPurchaseFailed(unit.Id, request.ItemId, failure));
                }
            }
        }

        private static bool InShop(MatchState state, MatchUnit unit)
        {
            if (unit.Hero!.IsDead)
                return true;
            FountainPlacement? fountain = state.Ruleset.Map.FountainOf(unit.Team);
            return fountain != null && Geometry.WithinRange(unit.Position, fountain.Position, fountain.Radius);
        }

        private static string? Buy(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, string itemId)
        {
            MatchState state = context.State;
            ItemDefinition? item = state.Ruleset.FindItem(itemId);
            if (item == null)
                return "unknown_item";
            if (!InShop(state, unit))
                return "not_in_shop";

            // Each listed component consumes one owned copy, if any.
            var consumed = new List<ItemSlot>();
            foreach (string componentId in item.Components)
            {
                ItemSlot? owned = hero.InventorySlots.FirstOrDefault(s => s.Definition.Id == componentId && !consumed.Contains(s));
                if (owned != null)
                    consumed.Add(owned);
            }

            int price = item.Cost - consumed.Sum(s => s.Definition.Cost);
            if (hero.Gold < Fixed.FromInt(price))
                return "gold";
            if (hero.InventorySlots.Count - consumed.Count >= HeroShop.InventorySize)
                return "inventory_full";

            foreach (ItemSlot slot in consumed)
                RemoveSlot(context, unit, hero, slot);

            hero.Spend(Fixed.FromInt(price));
            int oldMaxHp = unit.MaxHp;
            ActiveModifier? bonus = item.Bonuses != null ? Modifiers.AddInstance(context, unit, item.Bonuses, unit.Id) : null;
            if (unit.IsAlive && unit.MaxHp > oldMaxHp)
                unit.Hp += Fixed.FromInt(unit.MaxHp - oldMaxHp);

            AbilityState? active = item.ActiveAbilityId != null ? new AbilityState(state.Ruleset.FindAbility(item.ActiveAbilityId)!, level: 1) : null;
            hero.InventorySlots.Add(new ItemSlot(item, active, bonus));
            HeroProgression.RefreshUpgrades(context, unit);
            context.Emit(new ItemPurchased(unit.Id, item.Id, price, consumed.Select(s => s.Definition.Id).ToList()));
            return null;
        }

        private static string? Sell(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, string itemId)
        {
            ItemSlot? slot = hero.InventorySlots.FirstOrDefault(s => s.Definition.Id == itemId);
            if (slot == null)
                return "not_owned";
            if (!InShop(context.State, unit))
                return "not_in_shop";

            RemoveSlot(context, unit, hero, slot);
            int refund = slot.Definition.Cost / 2;
            hero.Gold += Fixed.FromInt(refund);
            context.Emit(new ItemSold(unit.Id, itemId, refund));
            return null;
        }

        private static void RemoveSlot(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, ItemSlot slot)
        {
            hero.InventorySlots.Remove(slot);
            if (slot.Bonus != null)
                Modifiers.Remove(context, unit, slot.Bonus, "item_removed");
            HeroProgression.RefreshUpgrades(context, unit);
        }
    }
}
