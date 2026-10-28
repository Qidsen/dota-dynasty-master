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
                if (hero == null)
                    continue;
                if (context.State.Ruleset.HeroRules?.Stash != null)
                    Courier(context, unit, hero);
                if (hero.ShopRequests.Count == 0)
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

        /// <summary>
        /// Dota's stash and courier: at the base the stash goes into the hero's hands; elsewhere a free courier takes what the hero has
        /// room for and brings it — along the ground below the flying level, straight above it — then goes back.
        /// </summary>
        private static void Courier(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero)
        {
            MatchState state = context.State;
            StashRules rules = state.Ruleset.HeroRules!.Stash!;
            long now = context.Tick.Value;
            if (hero.CourierArriveTick >= 0)
            {
                if (now < hero.CourierArriveTick)
                    return;
                foreach (ItemSlot slot in hero.CourierSlots.ToList())
                {
                    hero.CourierSlots.Remove(slot);
                    if (unit.IsAlive && !hero.IsDead && hero.InventorySlots.Count + hero.BackpackSlots.Count < HeroShop.InventorySize + rules.BackpackSlots)
                        hero.BackpackSlots.Add(slot);
                    else
                        hero.StashSlots.Add(slot);
                }

                hero.CourierArriveTick = -1;
                HeroShop.Arrange(context, unit);
                return;
            }

            int room = HeroShop.InventorySize + rules.BackpackSlots - hero.InventorySlots.Count - hero.BackpackSlots.Count;
            if (hero.StashSlots.Count == 0 || room <= 0 || hero.IsDead || !unit.IsAlive)
                return;
            if (InShop(state, unit))
            {
                foreach (ItemSlot slot in hero.StashSlots.Take(room).ToList())
                {
                    hero.StashSlots.Remove(slot);
                    hero.BackpackSlots.Add(slot);
                }

                HeroShop.Arrange(context, unit);
                return;
            }

            if (now < hero.CourierHomeTick)
                return;
            FountainPlacement? fountain = state.Ruleset.Map.FountainOf(unit.Team);
            if (fountain == null)
                return;
            Fixed distance = hero.Level >= rules.CourierFliesFromLevel ? FixedVector2.Distance(fountain.Position, unit.Position) : GroundDistance(state, fountain.Position, unit.Position);
            int speed = rules.CourierSpeed + rules.CourierSpeedPerLevel * hero.Level;
            long travel = context.TickRate.TicksFor((distance * Fixed.FromInt(1000) / Fixed.FromInt(speed)).ToIntFloor());
            foreach (ItemSlot slot in hero.StashSlots.Take(room).ToList())
            {
                hero.StashSlots.Remove(slot);
                hero.CourierSlots.Add(slot);
            }

            hero.CourierArriveTick = now + travel;
            hero.CourierHomeTick = now + 2 * travel;
        }

        /// <summary>The length of the walking path between two points (straight when there is none).</summary>
        private static Fixed GroundDistance(MatchState state, FixedVector2 from, FixedVector2 to)
        {
            IReadOnlyList<FixedVector2>? path = state.Pathfinder.FindPath(from, to);
            if (path == null)
                return FixedVector2.Distance(from, to);
            Fixed length = Fixed.Zero;
            FixedVector2 at = from;
            foreach (FixedVector2 point in path)
            {
                length += FixedVector2.Distance(at, point);
                at = point;
            }

            return length;
        }

        private static string? Buy(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, string itemId)
        {
            MatchState state = context.State;
            ItemDefinition? item = state.Ruleset.FindItem(itemId);
            if (item == null)
                return "unknown_item";
            if (!item.InShop)
                return "not_for_sale";
            if (state.Ruleset.HeroRules?.Stash != null)
                return BuyAnywhere(context, unit, hero, item);
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
            HeroShop.Put(context, unit, item);
            context.Emit(new ItemPurchased(unit.Id, item.Id, price, consumed.Select(s => s.Definition.Id).ToList()));
            return null;
        }

        /// <summary>
        /// Dota's shop with a stash: components come from the inventory, the backpack and the stash; the item goes to the hero at the
        /// base (alive) when there is room, else to the stash.
        /// </summary>
        private static string? BuyAnywhere(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, ItemDefinition item)
        {
            MatchState state = context.State;
            StashRules rules = state.Ruleset.HeroRules!.Stash!;
            var owned = hero.InventorySlots.Concat(hero.BackpackSlots).Concat(hero.StashSlots).ToList();
            var consumed = new List<ItemSlot>();
            foreach (string componentId in item.Components)
            {
                ItemSlot? slot = owned.FirstOrDefault(s => s.Definition.Id == componentId && !consumed.Contains(s));
                if (slot != null)
                    consumed.Add(slot);
            }

            int price = item.Cost - consumed.Sum(s => s.Definition.Cost);
            if (hero.Gold < Fixed.FromInt(price))
                return "gold";
            bool atBase = !hero.IsDead && unit.IsAlive && InShop(state, unit);
            int carried = hero.InventorySlots.Count + hero.BackpackSlots.Count - consumed.Count(s => !hero.StashSlots.Contains(s));
            int stashed = hero.StashSlots.Count - consumed.Count(s => hero.StashSlots.Contains(s));
            bool carry = atBase && carried < HeroShop.InventorySize + rules.BackpackSlots;
            if (!carry && stashed >= rules.StashSlots)
                return "stash_full";

            foreach (ItemSlot slot in consumed)
            {
                if (!hero.StashSlots.Remove(slot) && !hero.BackpackSlots.Remove(slot))
                    RemoveSlot(context, unit, hero, slot);
            }

            hero.Spend(Fixed.FromInt(price));
            if (!(carry && HeroShop.Carry(context, unit, item)))
                HeroShop.Store(context, unit, item);
            else
                HeroShop.Arrange(context, unit);
            if (consumed.Count > 0)
                HeroShop.Arrange(context, unit);
            context.Emit(new ItemPurchased(unit.Id, item.Id, price, consumed.Select(s => s.Definition.Id).ToList()));
            return null;
        }

        private static string? Sell(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, string itemId)
        {
            ItemSlot? slot = hero.InventorySlots.FirstOrDefault(s => s.Definition.Id == itemId) ?? hero.BackpackSlots.FirstOrDefault(s => s.Definition.Id == itemId);
            if (slot == null)
                return "not_owned";
            if (!InShop(context.State, unit))
                return "not_in_shop";

            if (!hero.BackpackSlots.Remove(slot))
                RemoveSlot(context, unit, hero, slot);
            if (context.State.Ruleset.HeroRules?.Stash != null)
                HeroShop.Arrange(context, unit);
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
