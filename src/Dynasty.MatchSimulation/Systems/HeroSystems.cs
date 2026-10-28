using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
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
            long now = context.Tick.Value;
            EconomyRules? economy = rules.Economy;
            // Dota: reliable periodic gold by the match time's table; otherwise the hero rules' gold per minute.
            Fixed passiveGold = economy != null
                ? Fixed.FromRatio(economy.PeriodicGoldAt(now * 1000 / tps), 100L * 60 * tps)
                : Fixed.FromRatio(rules.GoldPerMinute, 60L * tps);

            foreach (MatchUnit unit in state.Units)
            {
                HeroState? hero = unit.Hero;
                if (hero == null)
                    continue;

                hero.Gold += passiveGold;
                if (economy != null)
                    hero.ReliableGold += passiveGold;

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
            if (hero.Reincarnating)
            {
                // Dota's Aegis: back where it died, with full health and mana.
                hero.Reincarnating = false;
                hero.RespawnTick = -1;
                unit.Hp = Fixed.FromInt(unit.MaxHp);
                hero.Mana = Fixed.FromInt(hero.MaxMana);
                unit.LastActiveTick = context.Tick.Value;
                context.Emit(new UnitRevived(unit.Id, "reincarnation"));
                return;
            }

            Revive(context, unit, hero);
            context.Emit(new HeroRespawned(unit.Id));
        }

        private static void TryBuyback(SimulationContext<MatchState> context, MatchUnit unit, HeroState hero, HeroRules rules, long now)
        {
            EconomyRules? economy = rules.Economy;
            int cost = economy != null
                ? (Fixed.FromInt(economy.BuybackBaseGold) + hero.NetWorth / Fixed.FromInt(economy.BuybackNetWorthDivisor)).ToIntFloor()
                : rules.BuybackCost(hero.Level);
            string? failure = !rules.BuybackEnabled && economy == null ? "disabled"
                : !hero.IsDead ? "not_dead"
                : hero.Reincarnating ? "reincarnating"
                : now < hero.BuybackReadyTick ? "cooldown"
                : hero.Gold < Fixed.FromInt(cost) ? "gold"
                : null;
            if (failure != null)
            {
                context.Emit(new BuybackFailed(unit.Id, failure));
                return;
            }

            hero.Spend(Fixed.FromInt(cost), reliableFirst: economy != null);   // Dota: a buyback takes reliable gold first
            hero.BuybackReadyTick = now + context.TickRate.TicksFor(rules.BuybackCooldownMs);
            if (economy != null)
                hero.RespawnPenaltyMs = economy.BuybackRespawnPenaltyMs;
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
    /// <summary>
    /// Taunt and fear override what a unit was told to do: a taunted hero is ordered to attack the taunting unit
    /// (a creep or summon targets it), a feared hero is ordered to run straight away from the source. The source
    /// is that of the most recent such modifier.
    /// </summary>
    internal sealed class ForcedActionSystem : ISimulationSystem<MatchState>
    {
        /// <summary>A feared hero runs toward a point this far away from the source, renewed every few ticks.</summary>
        private static readonly Fixed FleeDistance = Fixed.FromInt(400);

        public string Name => "forced-actions";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            foreach (MatchUnit unit in state.Units)
            {
                if ((unit.Flags & (ModifierFlags.Feared | ModifierFlags.Taunted)) == 0 || !unit.IsAlive)
                    continue;
                ActiveModifier forcing = unit.Modifiers.Where(m => (m.Spec.Flags & (ModifierFlags.Feared | ModifierFlags.Taunted)) != 0)
                    .OrderByDescending(m => m.AppliedTick).First();
                MatchUnit? source = state.Find(forcing.Source);
                if (source == null || !source.IsAlive)
                    continue;

                if ((forcing.Spec.Flags & ModifierFlags.Taunted) != 0)
                {
                    if (unit.Hero == null)
                        unit.TargetId = source.Id;
                    else if (unit.Hero.Order.Kind != HeroOrderKind.Attack || unit.Hero.Order.Target != source.Id)
                        HeroOrders.Issue(unit, HeroOrder.Attack(source.Id));
                }
                else if (unit.Hero != null && (unit.Hero.Order.Kind != HeroOrderKind.Move || context.Tick.Value % 10 == 0))
                {
                    FixedVector2 away = unit.Position - source.Position;
                    Fixed length = away.Length;
                    FixedVector2 direction = length > Fixed.Zero ? away * (Fixed.One / length) : new FixedVector2(Fixed.One, Fixed.Zero);
                    HeroOrders.Issue(unit, HeroOrder.Move(unit.Position + direction * FleeDistance));
                }
            }
        }
    }

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

                    case HeroOrderKind.UseGate:
                        // The twin gate system channels it once within range.
                        Rules.TwinGateRules? gates = state.Ruleset.TwinGates;
                        if (gates == null)
                        {
                            HeroOrders.Issue(unit, HeroOrder.Idle);
                            break;
                        }

                        FixedVector2 gate = gates.Route(order.Point).From;
                        if (unit.CanMove && hero.GateChannelEndTick < 0 && !Geometry.WithinRange(unit.Position, gate, gates.Radius) &&
                            WalkTo(state, unit, hero, FixedVector2.MoveTowards(gate, unit.Position, Fixed.FromInt(gates.Radius / 2))))
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
            Fixed rolled = Fixed.Zero;
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
                    if (bounty.GoldMax > 0 && creep.BountyGoldBonus > Fixed.Zero)
                        HeroProgression.GrantGold(context, killer, rolled = Fixed.FromInt(context.Random("economy").NextInt(bounty.GoldMin, bounty.GoldMax + 1)) + creep.BountyGoldBonus, "last_hit");
                    else if (bounty.GoldMax > 0)
                    {
                        int gold = context.Random("economy").NextInt(bounty.GoldMin, bounty.GoldMax + 1);
                        rolled = Fixed.FromInt(gold);
                        HeroProgression.GrantGold(context, killer, gold, "last_hit");
                    }
                }
            }

            if (type?.AreaBounty != null && killer != null && !denied && killer.Team != Team.Neutral)
                AreaBountyPaid(context, creep, type, killer, rolled);

            if (type == null || type.Bounty.Xp <= 0)
                return;
            EconomyRules? economy = state.Ruleset.HeroRules.Economy;
            if (economy != null)
            {
                // Dota: a denied lane creep gives its enemies part of its XP; a neutral creep's XP goes to the heroes of both teams nearby.
                if (creep.Team == Team.Neutral)
                {
                    if (killer != null && killer.Team != Team.Neutral)
                        ShareXp(context, creep, null, type.Bounty.Xp + creep.BountyXpBonus);
                }
                else
                {
                    int xp = type.Bounty.Xp + creep.BountyXpBonus;
                    ShareXp(context, creep, creep.Team.Opponent(), denied ? xp * economy.DeniedCreepXpPercent / 100 : xp);
                }

                return;
            }

            // Denied creeps give no XP to the enemy. Neutral creeps reward the killer's team.
            if (denied)
                return;
            if (creep.Team != Team.Neutral)
                ShareXp(context, creep, creep.Team.Opponent(), type.Bounty.Xp + creep.BountyXpBonus);
            else if (killer != null && killer.Team != Team.Neutral)
                ShareXp(context, creep, killer.Team, type.Bounty.Xp + creep.BountyXpBonus);
        }

        public static void HeroDied(SimulationContext<MatchState> context, MatchUnit victim)
        {
            MatchState state = context.State;
            HeroRules rules = state.Ruleset.HeroRules!;
            HeroState hero = victim.Hero!;
            long now = context.Tick.Value;
            if (rules.Economy != null)
            {
                DotaHeroDied(context, victim, rules, rules.Economy);
                return;
            }

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

        /// <summary>
        /// A hero's death in Dota's economy. The bounty (base + level + the victim's streak + first blood) goes to the enemy
        /// hero that killed it (summons and illusions kill for their owner); a death to enemy creeps or towers goes to the
        /// enemy heroes that damaged or debuffed the victim within the kill credit window, or, if none did, is split among
        /// all enemy heroes without ending the streak or counting as first blood. A death to its own team or to neutrals
        /// gives nothing. Assist gold and XP go to the enemy heroes near the victim or that damaged or debuffed it.
        /// The victim loses net worth / divisor of unreliable gold and respawns by the level table.
        /// </summary>
        private static void DotaHeroDied(SimulationContext<MatchState> context, MatchUnit victim, HeroRules rules, EconomyRules economy)
        {
            MatchState state = context.State;
            HeroState hero = victim.Hero!;
            long now = context.Tick.Value;
            Team enemies = victim.Team.Opponent();
            hero.Deaths++;

            MatchUnit? killerUnit = state.Find(victim.LastAttackerId);
            MatchUnit? killerHero = killerUnit?.Hero != null ? killerUnit
                : killerUnit != null && killerUnit.Owner.IsValid ? state.Find(killerUnit.Owner) : null;
            if (killerHero?.Hero == null || killerHero.Team != enemies)
                killerHero = null;
            long window = context.TickRate.TicksFor(economy.KillCreditWindowMs);
            List<MatchUnit> recent = hero.RecentHeroAttackers
                .Where(e => now - e.Value <= window)
                .Select(e => state.Find(new EntityId(e.Key)))
                .Where(u => u?.Hero != null && u.Team == enemies)
                .Select(u => u!)
                .ToList();

            bool byEnemy = killerUnit != null && killerUnit.Team == enemies;
            List<MatchUnit> enemyHeroes = state.Heroes.Where(h => h.Team == enemies).ToList();
            List<MatchUnit> credited = !byEnemy ? new List<MatchUnit>()
                : killerHero != null ? new List<MatchUnit> { killerHero }
                : recent.Count > 0 ? recent
                : enemyHeroes;
            bool heroKill = byEnemy && (killerHero != null || recent.Count > 0);

            // Everything is measured before the death costs the victim its gold.
            Fixed victimNetWorth = hero.NetWorth;
            Fixed victimTeamNetWorth = Fixed.Zero, killerTeamNetWorth = Fixed.Zero;
            foreach (MatchUnit h in state.Heroes)
            {
                if (h.Team == victim.Team)
                    victimTeamNetWorth += h.Hero!.NetWorth;
                else if (h.Team == enemies)
                    killerTeamNetWorth += h.Hero!.NetWorth;
            }

            int streak = hero.KillStreak;
            int bounty = economy.KillGoldBase + economy.KillGoldPerLevel * hero.Level +
                         (heroKill ? economy.StreakGoldFor(streak) : 0) + (heroKill && !state.FirstBloodTaken ? economy.FirstBloodGold : 0);
            if (credited.Count > 0)
            {
                Fixed share = Fixed.FromInt(bounty) / Fixed.FromInt(credited.Count);
                foreach (MatchUnit receiver in credited)
                    HeroProgression.GrantGold(context, receiver, share, "hero_kill");
            }

            if (heroKill)
            {
                if (credited.Count == 1)
                {
                    credited[0].Hero!.Kills++;
                    credited[0].Hero!.KillStreak++;
                }

                state.FirstBloodTaken = true;
                hero.KillStreak = 0;
            }

            // Assist gold and XP: enemy heroes near the victim, or that damaged or debuffed it, and the killer wherever it stands.
            var assists = new List<EntityId>();
            if (byEnemy)
            {
                List<MatchUnit> receivers = enemyHeroes
                    .Where(h => h.IsAlive && (h == killerHero || recent.Contains(h) || Geometry.WithinRange(h.Position, victim.Position, economy.AssistRadius)))
                    .ToList();
                if (receivers.Count > 0)
                {
                    Fixed n = Fixed.FromInt(receivers.Count);
                    if (heroKill && killerTeamNetWorth > Fixed.Zero)
                    {
                        Fixed f = Fixed.FromInt(economy.AssistGoldBase) +
                                  (Fixed.FromInt(economy.AssistGoldPool) + victimNetWorth * Fixed.FromRatio(economy.AssistGoldNetWorthPermille, 1000)) / n;
                        Fixed assistGold = f * victimTeamNetWorth / killerTeamNetWorth;
                        foreach (MatchUnit receiver in receivers)
                            HeroProgression.GrantGold(context, receiver, assistGold, "assist");
                    }

                    int capLevel = System.Math.Min(economy.HeroXpCapLevel, rules.MaxLevel);
                    int countedXp = System.Math.Min(hero.Xp, rules.XpForLevel[capLevel - 1]);
                    Fixed baseXp = Fixed.FromInt(rules.HeroKillXpBase + rules.HeroKillXpPerLevel * hero.Level) +
                                   Fixed.FromRatio((long)countedXp * rules.HeroKillXpPercentOfVictim, 100);
                    Fixed xp = baseXp / n + (heroKill ? economy.StreakXpFor(streak, hero.Level) : Fixed.Zero);
                    foreach (MatchUnit receiver in receivers)
                    {
                        HeroProgression.GrantXp(context, receiver, xp.ToIntFloor());
                        if (receiver != killerHero && recent.Contains(receiver))
                        {
                            receiver.Hero!.Assists++;
                            assists.Add(receiver.Id);
                        }
                    }
                }
            }

            Fixed loss = victimNetWorth / Fixed.FromInt(economy.DeathGoldLossNetWorthDivisor);
            hero.Gold -= Fixed.Min(loss, Fixed.Max(Fixed.Zero, hero.Gold - hero.ReliableGold));

            int respawnMs = economy.RespawnMsAt(hero.Level) + hero.RespawnPenaltyMs;
            hero.RespawnPenaltyMs = 0;
            if (killerUnit != null && killerUnit.Team == Team.Neutral)
                respawnMs = System.Math.Max(respawnMs, economy.NeutralRespawnMinMs);
            hero.RespawnTick = now + context.TickRate.TicksFor(respawnMs);

            hero.RecentHeroAttackers.Clear();
            hero.Path = null;
            HeroOrders.Issue(victim, HeroOrder.Idle);
            victim.PendingAttackTick = -1;
            victim.ClearTemporaryModifiers();
            context.Emit(new HeroKilled(victim.Id, victim.LastAttackerId, assists, bounty, hero.RespawnTick));
        }

        /// <summary>
        /// Dota's flagbearer: the killing player (the hero, or a summon's owner; always) and every enemy hero within the radius
        /// get the base gold plus a percent of its bounty (the rolled one when a hero took it, else the middle of its range).
        /// </summary>
        private static void AreaBountyPaid(SimulationContext<MatchState> context, MatchUnit creep, CreepType type, MatchUnit killer, Fixed rolled)
        {
            AreaBounty area = type.AreaBounty!;
            if (rolled == Fixed.Zero)
                rolled = Fixed.FromRatio(type.Bounty.GoldMin + type.Bounty.GoldMax, 2) + creep.BountyGoldBonus;
            Fixed gold = Fixed.FromInt(area.BaseGold) + rolled * Fixed.FromInt(area.BountyPercent) / Fixed.FromInt(100);
            MatchUnit? player = killer.Hero != null ? killer : context.State.Find(killer.Owner);
            foreach (MatchUnit hero in context.State.Heroes.Where(h => h.Team == killer.Team && h.IsAlive && h.IllusionOf.IsValid == false).ToList())
            {
                if (hero == player || Geometry.WithinRange(hero.Position, creep.Position, area.Radius))
                    HeroProgression.GrantGold(context, hero, gold, "creep_area_bounty");
            }
        }

        public static void StructureDestroyed(SimulationContext<MatchState> context, MatchUnit structure)
        {
            HeroRules? rules = context.State.Ruleset.HeroRules;
            if (rules == null)
                return;
            StructureBounty? bounty = context.State.Ruleset.FindStructureType(structure.TypeId)?.Bounty;
            if (bounty != null)
            {
                DotaStructureDestroyed(context, structure, bounty);
                return;
            }

            if (rules.StructureTeamGold <= 0)
                return;

            foreach (MatchUnit hero in context.State.Heroes.Where(h => h.Team != structure.Team).ToList())
                HeroProgression.GrantGold(context, hero, rules.StructureTeamGold, "structure");
        }

        private static void DotaStructureDestroyed(SimulationContext<MatchState> context, MatchUnit structure, StructureBounty bounty)
        {
            MatchUnit? killer = context.State.Find(structure.LastAttackerId);
            List<MatchUnit> heroes = context.State.Heroes.ToList();
            if (killer != null && killer.Team == structure.Team)
            {
                foreach (MatchUnit hero in heroes)
                {
                    int percent = hero.Team == structure.Team ? bounty.DeniedOwnGoldPercent : bounty.DeniedEnemyGoldPercent;
                    if (percent > 0)
                        HeroProgression.GrantGold(context, hero, bounty.TeamGold * percent / 100, "structure_deny");
                }

                return;
            }

            MatchUnit? lastHitter = killer?.Hero != null ? killer : null;
            foreach (MatchUnit hero in heroes.Where(h => h.Team != structure.Team))
            {
                if (hero != lastHitter || bounty.LastHitterGetsTeamGold)
                    HeroProgression.GrantGold(context, hero, bounty.TeamGold, "structure");
            }

            if (lastHitter != null)
                HeroProgression.GrantGold(context, lastHitter, context.Random("economy").NextInt(bounty.LastHitGoldMin, bounty.LastHitGoldMax + 1), "structure_last_hit");
        }

        /// <summary>
        /// Living heroes of the receiving team within the share radius split the XP equally (remainder dropped);
        /// a hero killer always shares, wherever it stands.
        /// </summary>
        internal static void ShareXp(SimulationContext<MatchState> context, MatchUnit dead, Team? receivingTeam, int xp, MatchUnit? killer = null)
        {
            int radius = context.State.Ruleset.HeroRules!.XpShareRadius;
            // A null team: heroes of both teams (Dota's neutral creeps).
            List<MatchUnit> receivers = context.State.Heroes
                .Where(h => (receivingTeam == null ? h.Team != Team.Neutral : h.Team == receivingTeam) && h.IsAlive &&
                            (h == killer || Geometry.WithinRange(h.Position, dead.Position, radius)))
                .ToList();
            if (receivers.Count == 0)
                return;

            int share = xp / receivers.Count;
            foreach (MatchUnit receiver in receivers)
                HeroProgression.GrantXp(context, receiver, share);
        }
    }
}
