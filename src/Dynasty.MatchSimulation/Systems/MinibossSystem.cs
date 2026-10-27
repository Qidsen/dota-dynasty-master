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
    /// Dota's Tormentor (<see cref="MinibossDefinition"/>): spawns on the side of the time of day and moves with it, keeps its barrier
    /// growing and regenerating, sends the reflected damage out in waves and burns the units around after a hit. The damage it
    /// takes is recorded by <see cref="Damage.Deal"/>; its death by the cleanup system (<see cref="Died"/>).
    /// </summary>
    internal sealed class MinibossSystem : ISimulationSystem<MatchState>
    {
        public string Name => "miniboss";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            MinibossDefinition? definition = state.Ruleset.Miniboss;
            if (definition == null)
                return;
            long now = context.Tick.Value;
            long timeMs = now * 1000 / context.TickRate.TicksPerSecond;
            int intervals = definition.IntervalsAt(timeMs);
            bool night = state.IsNight(now);

            MatchUnit? miniboss = state.Miniboss;
            if (miniboss == null)
            {
                if (state.NextMinibossSpawnTick >= 0 && now >= state.NextMinibossSpawnTick)
                {
                    miniboss = state.SpawnMiniboss(context.Ids.Allocate(), definition, definition.PositionAt(night));
                    miniboss.MinibossBarrier = Fixed.FromInt(definition.Barrier + definition.BarrierPerInterval * intervals);
                    miniboss.MinibossIntervals = intervals;
                    miniboss.MinibossSpawnTick = now;
                    context.Emit(new MinibossSpawned(miniboss.Id, definition.Id));
                }

                return;
            }

            if (!miniboss.IsAlive)
                return;

            // It stands away from the boss: it moves with the time of day.
            miniboss.Position = definition.PositionAt(night);
            miniboss.StartPosition = miniboss.Position;

            // The barrier's size grows with the time (and so does what is left of it); it regenerates up to its size.
            if (intervals != miniboss.MinibossIntervals)
            {
                miniboss.MinibossBarrier += Fixed.FromInt(definition.BarrierPerInterval * (intervals - miniboss.MinibossIntervals));
                miniboss.MinibossIntervals = intervals;
            }

            Fixed size = Fixed.FromInt(definition.Barrier + definition.BarrierPerInterval * intervals);
            // A second's regeneration spread over its ticks exactly: the remainder of the division carries over.
            long perSecond = (definition.BarrierRegen + definition.BarrierRegenPerInterval * Fixed.FromInt(intervals)).Raw + miniboss.MinibossRegenCarry;
            miniboss.MinibossRegenCarry = perSecond % context.TickRate.TicksPerSecond;
            miniboss.MinibossBarrier = Fixed.Min(size, miniboss.MinibossBarrier + Fixed.FromRaw(perSecond / context.TickRate.TicksPerSecond));

            long sinceSpawn = now - miniboss.MinibossSpawnTick;
            long reflectEvery = context.TickRate.TicksFor(definition.ReflectIntervalMs);
            if (sinceSpawn > 0 && reflectEvery > 0 && sinceSpawn % reflectEvery == 0)
                Reflect(context, state, definition, miniboss);

            long shineEvery = context.TickRate.TicksFor(definition.ShiningIntervalMs);
            bool hitLately = miniboss.MinibossLastHitTick >= 0 && now - miniboss.MinibossLastHitTick <= context.TickRate.TicksFor(definition.ShiningAfterHitMs);
            if (hitLately && sinceSpawn > 0 && shineEvery > 0 && sinceSpawn % shineEvery == 0)
            {
                Fixed damage = (definition.ShiningDamage + definition.ShiningDamagePerInterval * Fixed.FromInt(intervals)) * Fixed.FromInt(definition.ShiningIntervalMs) / Fixed.FromInt(1000);
                foreach (MatchUnit unit in state.Units.Where(u => u.Team != Team.Neutral && u.IsAlive && u.Kind != UnitKind.Structure && u.Kind != UnitKind.Rune &&
                                                                  Geometry.WithinRange(u.Position, miniboss.Position, definition.ShiningRadius)).ToList())
                    Damage.Deal(context, miniboss.Id, unit, damage, DamageType.Magical);
            }
        }

        /// <summary>Called by <see cref="Damage.Deal"/> for damage to the miniboss, before any reduction.</summary>
        internal static void Took(SimulationContext<MatchState> context, MatchUnit miniboss, MatchUnit? from, Fixed rawAmount, DamageType type)
        {
            MinibossDefinition definition = context.State.Ruleset.Miniboss!;
            int intervals = definition.IntervalsAt(context.Tick.Value * 1000 / context.TickRate.TicksPerSecond);
            Fixed percent = definition.ReflectPercent + definition.ReflectPercentPerInterval * Fixed.FromInt(intervals);
            miniboss.MinibossReflected ??= new Fixed[3];
            miniboss.MinibossReflected[(int)type] += rawAmount * percent / Fixed.FromInt(100);
            miniboss.MinibossLastHitTick = context.Tick.Value;
            if (from != null && from.Team != Team.Neutral && !miniboss.MinibossDealers.Contains(from.Id))
                miniboss.MinibossDealers.Add(from.Id);
        }

        /// <summary>
        /// The reflected damage of the wave, split evenly among the heroes within the radius and whoever dealt damage (illusions take
        /// twice their share and do not count); a dealer that is not a hero dies when no hero is among them.
        /// </summary>
        private static void Reflect(SimulationContext<MatchState> context, MatchState state, MinibossDefinition definition, MatchUnit miniboss)
        {
            Fixed[]? pools = miniboss.MinibossReflected;
            List<Dynasty.Core.Ids.EntityId> dealers = miniboss.MinibossDealers;
            if (pools == null || pools.All(p => p <= Fixed.Zero))
            {
                dealers.Clear();
                return;
            }

            var targets = state.Units.Where(u => u.Kind == UnitKind.Hero && u.IsAlive && Geometry.WithinRange(u.Position, miniboss.Position, definition.ReflectRadius)).ToList();
            foreach (Dynasty.Core.Ids.EntityId id in dealers)
            {
                MatchUnit? dealer = state.Find(id);
                if (dealer != null && dealer.IsAlive && !targets.Contains(dealer))
                    targets.Add(dealer);
            }

            int shares = targets.Count(t => t.Kind == UnitKind.Hero && !t.IllusionOf.IsValid);
            if (shares == 0)
            {
                foreach (MatchUnit dealer in targets.Where(t => t.Kind != UnitKind.Hero).ToList())
                    Damage.Execute(context, miniboss.Id, dealer);
            }
            else
            {
                for (int type = 0; type < pools.Length; type++)
                {
                    if (pools[type] <= Fixed.Zero)
                        continue;
                    Fixed share = pools[type] / Fixed.FromInt(shares);
                    foreach (MatchUnit target in targets.Where(t => t.IsAlive).ToList())
                        Damage.Deal(context, miniboss.Id, target, target.IllusionOf.IsValid ? share * Fixed.FromInt(2) : share, (DamageType)type);
                }
            }

            for (int type = 0; type < pools.Length; type++)
                pools[type] = Fixed.Zero;
            dealers.Clear();
        }

        /// <summary>Its death: the killing team's gold and item, the death modifier to the heroes around, the next spawn.</summary>
        internal static void Died(SimulationContext<MatchState> context, MatchUnit miniboss)
        {
            MatchState state = context.State;
            MinibossDefinition definition = state.Ruleset.Miniboss!;
            long now = context.Tick.Value;
            state.NextMinibossSpawnTick = now + context.TickRate.TicksFor(definition.RespawnMs);

            MatchUnit? killer = state.Find(miniboss.LastAttackerId);
            Team team = killer != null && killer.Team != Team.Neutral ? killer.Team : Team.Neutral;
            if (team != Team.Neutral)
            {
                List<MatchUnit> heroes = state.Heroes.Where(h => h.Team == team).ToList();
                foreach (MatchUnit hero in heroes)
                    HeroProgression.GrantGold(context, hero, definition.TeamGold, "miniboss");

                ItemDefinition? item = definition.RewardItemId != null ? state.Ruleset.FindItem(definition.RewardItemId) : null;
                MatchUnit? receiver = item == null ? null : heroes
                    .Where(h => !h.Hero!.Inventory.Any(s => s.Definition.Id == item.Id || (item.Upgrade != null && s.Definition.Upgrade == item.Upgrade)))
                    .OrderBy(h => h.Hero!.NetWorth).ThenBy(h => h.Id.Value).FirstOrDefault();
                if (receiver != null)
                    HeroShop.Give(context, receiver, item!);
                else if (item != null && definition.TeamGoldWithoutItem > 0)
                {
                    foreach (MatchUnit hero in heroes)
                        HeroProgression.GrantGold(context, hero, definition.TeamGoldWithoutItem, "miniboss");
                }
            }

            if (definition.DeathModifier != null)
            {
                foreach (MatchUnit hero in state.Heroes.Where(h => h.IsAlive && Geometry.WithinRange(h.Position, miniboss.Position, definition.DeathModifierRadius)).ToList())
                    Modifiers.Apply(context, hero, definition.DeathModifier, miniboss.Id);
            }

            context.Emit(new MinibossKilled(miniboss.Id, team, miniboss.LastAttackerId, state.NextMinibossSpawnTick));
        }
    }
}
