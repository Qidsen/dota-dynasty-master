using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>Passive gold, HP/mana regeneration, fountain healing and respawns.</summary>
    internal sealed class HeroEconomySystem : ISimulationSystem<MatchState>
    {
        public string Name => "hero-economy";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            HeroRules? rules = state.Ruleset.HeroRules;
            if (rules == null)
                return;

            int tps = context.TickRate.TicksPerSecond;
            Fixed passiveGold = Fixed.FromRatio(rules.GoldPerMinute, 60L * tps);
            long now = context.Tick.Value;

            foreach (MatchUnit unit in state.Units)
            {
                HeroState? hero = unit.Hero;
                if (hero == null)
                    continue;

                hero.Gold += passiveGold;

                if (hero.BuybackRequested)
                {
                    hero.BuybackRequested = false;
                    TryBuyback(context, unit, hero, rules, now);
                }

                if (hero.IsDead)
                {
                    if (now >= hero.RespawnTick)
                        Respawn(context, unit, hero);
                    continue;
                }

                // Killed this tick (e.g. by damage over time) but not yet processed by cleanup:
                // regeneration must not bring the hero back.
                if (!unit.IsAlive)
                    continue;

                Fixed hpRegen = unit.HpRegenPerSecond / Fixed.FromInt(tps);
                Fixed manaRegen = unit.ManaRegenPerSecond / Fixed.FromInt(tps);
                FountainPlacement? fountain = state.Ruleset.Map.FountainOf(unit.Team);
                if (fountain != null && Geometry.WithinRange(unit.Position, fountain.Position, fountain.Radius))
                {
                    hpRegen += Fixed.FromRatio((long)unit.MaxHp * rules.FountainHealPercentPerSecond, 100L * tps);
                    manaRegen += Fixed.FromRatio((long)hero.MaxMana * rules.FountainHealPercentPerSecond, 100L * tps);
                }

                unit.Hp = Fixed.Min(Fixed.FromInt(unit.MaxHp), unit.Hp + hpRegen);
                hero.Mana = Fixed.Min(Fixed.FromInt(hero.MaxMana), hero.Mana + manaRegen);
            }
        }

        private static void Respawn(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero)
        {
            Revive(context, unit, hero);
            context.Emit(new HeroRespawned(unit.Id));
        }

        private static void TryBuyback(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, HeroRules rules, long now)
        {
            string? failure = !rules.BuybackEnabled ? "disabled"
                : !hero.IsDead ? "not_dead"
                : now < hero.BuybackReadyTick ? "cooldown"
                : hero.Gold < Fixed.FromInt(rules.BuybackCost(hero.Level)) ? "gold"
                : null;
            if (failure != null)
            {
                context.Emit(new BuybackFailed(unit.Id, failure));
                return;
            }

            int cost = rules.BuybackCost(hero.Level);
            hero.Gold -= Fixed.FromInt(cost);
            hero.BuybackReadyTick = now + context.TickRate.TicksFor(rules.BuybackCooldownMs);
            Revive(context, unit, hero);
            context.Emit(new HeroBoughtBack(unit.Id, cost));
        }

        private static void Revive(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero)
        {
            hero.RespawnTick = -1;
            unit.Position = context.State.Ruleset.Map.FountainOf(unit.Team)!.Position;
            unit.Hp = Fixed.FromInt(unit.MaxHp);
            hero.Mana = Fixed.FromInt(hero.MaxMana);
            unit.LastActiveTick = context.Tick.Value;
        }
    }

    /// <summary>
    /// Carries out hero orders: walks paths found by A*, chases attack targets until in range
    /// (the attack itself is started by the attack system), and drops orders that became impossible.
    /// </summary>
    internal sealed class HeroOrderSystem : ISimulationSystem<MatchState>
    {
        /// <summary>A chase path is recomputed when the target drifts this far from where it was planned to.</summary>
        private static readonly Fixed RepathDistance = Fixed.FromInt(200);

        public string Name => "hero-orders";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            foreach (MatchUnit unit in state.Units)
            {
                HeroState? hero = unit.Hero;
                if (hero == null || hero.IsDead)
                    continue;

                HeroOrder order = hero.Order;
                switch (order.Kind)
                {
                    case HeroOrderKind.Move:
                        if (!unit.CanMove)
                            break;
                        if (WalkTo(state, unit, hero, order.Point))
                            unit.LastActiveTick = now;
                        if (unit.Position == order.Point)
                            HeroOrders.Issue(unit, HeroOrder.Idle);
                        break;

                    case HeroOrderKind.Attack:
                        // A hero cannot keep chasing what its team no longer sees.
                        MatchUnit? target = state.Find(order.Target);
                        if (target == null || !state.CanAttack(unit, target) || !state.IsVisibleTo(unit.Team, target))
                        {
                            HeroOrders.Issue(unit, HeroOrder.Idle);
                            break;
                        }

                        unit.TargetId = target.Id;
                        if (unit.CanMove && !Geometry.WithinEdgeRangeOfStart(unit, target, unit.AttackRange) && Chase(state, unit, hero, target, unit.AttackRange))
                            unit.LastActiveTick = now;
                        break;

                    case HeroOrderKind.Cast:
                        if (unit.CanMove && !hero.IsCasting && MoveIntoCastRange(state, unit, hero, order))
                            unit.LastActiveTick = now;
                        break;
                }
            }
        }

        /// <summary>Walks toward a cast order's target or point until the ability's cast range is reached.</summary>
        private static bool MoveIntoCastRange(MatchState state, MatchUnit unit, HeroState hero, HeroOrder order)
        {
            AbilityState? ability = hero.Ability(order.AbilityId!);
            if (ability == null || AbilitySystem.InCastRange(unit, ability.Definition, order, state))
                return false;

            if (ability.Definition.Targeting == AbilityTargeting.Unit)
            {
                MatchUnit? target = state.Find(order.Target);
                return target != null && Chase(state, unit, hero, target, ability.Definition.CastRange);
            }

            return WalkTo(state, unit, hero, order.Point);
        }

        private static bool Chase(MatchState state, MatchUnit unit, HeroState hero, MatchUnit target, int range)
        {
            if (state.Nav.HasLineOfSight(unit.Position, target.StartPosition))
            {
                hero.Path = null;
                return MovementSystem.Approach(unit, target, range);
            }

            if (hero.Path != null && FixedVector2.Distance(hero.PathGoal, target.StartPosition) > RepathDistance)
                hero.Path = null;
            return WalkTo(state, unit, hero, target.StartPosition);
        }

        /// <summary>Follows (computing if needed) an A* path to the point. Returns true if the hero moved.</summary>
        private static bool WalkTo(MatchState state, MatchUnit unit, HeroState hero, FixedVector2 point)
        {
            if (hero.Path == null)
            {
                hero.Path = state.Pathfinder.FindPath(unit.Position, point);
                hero.PathIndex = 0;
                hero.PathGoal = point;
                if (hero.Path == null)
                    return false;
            }

            IReadOnlyList<FixedVector2> path = hero.Path;
            while (hero.PathIndex < path.Count && unit.Position == path[hero.PathIndex])
                hero.PathIndex++;
            if (hero.PathIndex >= path.Count)
            {
                hero.Path = null;
                return false;
            }

            unit.Position = FixedVector2.MoveTowards(unit.Position, path[hero.PathIndex], unit.StepPerTick);
            // Count the waypoint as reached now, before separation may nudge the hero off it.
            if (unit.Position == path[hero.PathIndex])
                hero.PathIndex++;
            return true;
        }
    }

    /// <summary>Gold, XP and scoreboard consequences of deaths. Called by the cleanup system.</summary>
    internal static class Rewards
    {
        public static void CreepDied(SimulationContext<MatchState> context, MatchUnit creep, CreepType? type)
        {
            MatchState state = context.State;
            if (state.Ruleset.HeroRules == null)
                return;

            MatchUnit? killer = state.Find(creep.LastAttackerId);
            bool denied = killer != null && killer.Team == creep.Team;
            if (killer?.Hero != null)
            {
                if (denied)
                {
                    killer.Hero.Denies++;
                }
                else
                {
                    killer.Hero.LastHits++;
                    Bounty bounty = type?.Bounty ?? Bounty.None;
                    if (bounty.GoldMax > 0)
                        HeroProgression.GrantGold(context, killer, context.Random("economy").NextInt(bounty.GoldMin, bounty.GoldMax + 1), "last_hit");
                }
            }

            // Denied creeps give no XP to the enemy. Neutral creeps reward the killer's team.
            if (denied || type == null || type.Bounty.Xp <= 0)
                return;
            if (creep.Team != Team.Neutral)
                ShareXp(context, creep, creep.Team.Opponent(), type.Bounty.Xp);
            else if (killer != null && killer.Team != Team.Neutral)
                ShareXp(context, creep, killer.Team, type.Bounty.Xp);
        }

        public static void HeroDied(SimulationContext<MatchState> context, MatchUnit victim)
        {
            MatchState state = context.State;
            HeroRules rules = state.Ruleset.HeroRules!;
            HeroState hero = victim.Hero!;
            long now = context.Tick.Value;

            hero.Deaths++;
            hero.RespawnTick = now + context.TickRate.TicksFor(rules.RespawnBaseMs + rules.RespawnPerLevelMs * hero.Level);
            hero.Gold = Fixed.Max(Fixed.Zero, hero.Gold - Fixed.FromInt(rules.GoldLossPerLevel * hero.Level));

            MatchUnit? killer = state.Find(victim.LastAttackerId);
            int bounty = 0;
            if (killer?.Hero != null && killer.Team != victim.Team)
            {
                killer.Hero.Kills++;
                bounty = rules.HeroKillGoldBase + rules.HeroKillGoldPerLevel * hero.Level;
                HeroProgression.GrantGold(context, killer, bounty, "hero_kill");
            }

            long window = context.TickRate.TicksFor(rules.AssistWindowMs);
            var assists = new List<EntityId>();
            foreach (KeyValuePair<long, long> entry in hero.RecentHeroAttackers)
            {
                if (now - entry.Value > window || (killer != null && entry.Key == killer.Id.Value))
                    continue;
                MatchUnit? helper = state.Find(new EntityId(entry.Key));
                if (helper?.Hero == null)
                    continue;
                helper.Hero.Assists++;
                assists.Add(helper.Id);
            }

            int killXp = rules.HeroKillXpBase + rules.HeroKillXpPerLevel * hero.Level + (int)((long)hero.Xp * rules.HeroKillXpPercentOfVictim / 100);
            ShareXp(context, victim, victim.Team.Opponent(), killXp, killer?.Hero != null && killer.Team != victim.Team ? killer : null);

            hero.RecentHeroAttackers.Clear();
            hero.Path = null;
            HeroOrders.Issue(victim, HeroOrder.Idle);
            victim.PendingAttackTick = -1;
            victim.ClearTemporaryModifiers();
            context.Emit(new HeroKilled(victim.Id, victim.LastAttackerId, assists, bounty, hero.RespawnTick));
        }

        public static void StructureDestroyed(SimulationContext<MatchState> context, MatchUnit structure)
        {
            HeroRules? rules = context.State.Ruleset.HeroRules;
            if (rules == null || rules.StructureTeamGold <= 0)
                return;

            foreach (MatchUnit hero in context.State.Heroes.Where(h => h.Team != structure.Team).ToList())
                HeroProgression.GrantGold(context, hero, rules.StructureTeamGold, "structure");
        }

        /// <summary>
        /// Living heroes of the receiving team within the share radius split the XP equally (remainder dropped);
        /// a hero killer always shares, wherever it stands.
        /// </summary>
        private static void ShareXp(SimulationContext<MatchState> context, MatchUnit dead, Team receivingTeam, int xp, MatchUnit? killer = null)
        {
            int radius = context.State.Ruleset.HeroRules!.XpShareRadius;
            List<MatchUnit> receivers = context.State.Heroes
                .Where(h => h.Team == receivingTeam && h.IsAlive && (h == killer || Geometry.WithinRange(h.Position, dead.Position, radius)))
                .ToList();
            if (receivers.Count == 0)
                return;

            int share = xp / receivers.Count;
            foreach (MatchUnit receiver in receivers)
                HeroProgression.GrantXp(context, receiver, share);
        }
    }
}
