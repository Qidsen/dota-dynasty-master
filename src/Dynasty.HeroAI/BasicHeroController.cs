using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.HeroAI
{
    public sealed class HeroAiSettings
    {
        /// <summary>Below this HP percent a hero walks home; it comes back at <see cref="ReturnHpPercent"/>.</summary>
        public int RetreatHpPercent { get; set; } = 30;
        public int ReturnHpPercent { get; set; } = 90;

        /// <summary>Heroes re-think every this many ticks (staggered), a stand-in for reaction time.</summary>
        public int DecisionIntervalTicks { get; set; } = 3;

        /// <summary>Enemy heroes closer than this are engaged with attacks.</summary>
        public int EngageRange { get; set; } = 700;
    }

    /// <summary>
    /// The AI's window on the match: exactly the team's fog-of-war knowledge from the Vision
    /// module (spec §9). The AI must not look at units through any other path.
    /// </summary>
    public sealed class TeamPerception
    {
        private readonly MatchState _state;
        private readonly Team _team;

        private TeamPerception(MatchState state, Team team)
        {
            _state = state;
            _team = team;
        }

        public static TeamPerception Build(MatchState state, Team team, HeroAiSettings settings) => new TeamPerception(state, team);

        public bool CanSee(MatchUnit unit) => _state.IsVisibleTo(_team, unit);
    }

    public static class HeroAi
    {
        /// <summary>Adds the basic controller to a match; it then decides for every hero in it.</summary>
        public static void Attach(SimulationKernel<MatchState> kernel, HeroAiSettings? settings = null) =>
            kernel.AddSystem(new BasicHeroController(settings ?? new HeroAiSettings()));
    }

    /// <summary>
    /// Minimal execution-level hero behaviour for phase 02: lane at the creep front, last-hit and
    /// deny, use abilities on visible enemy heroes, fight when healthy, retreat when hurt, buy the
    /// item build. It only reads public state, sees only what <see cref="TeamPerception"/> reveals,
    /// and acts exclusively by issuing hero orders and shop requests.
    /// Phase 03 replaces this with strategic/tactical layers.
    /// </summary>
    public sealed class BasicHeroController : ISimulationSystem<MatchState>
    {
        private readonly HeroAiSettings _settings;
        private readonly Dictionary<long, bool> _retreating = new Dictionary<long, bool>();

        public BasicHeroController(HeroAiSettings settings)
        {
            _settings = settings;
        }

        public string Name => "hero-ai-basic";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            TeamPerception? dawn = null;
            TeamPerception? dusk = null;

            foreach (MatchUnit hero in state.Units)
            {
                if (hero.Hero == null || (now + hero.Id.Value) % _settings.DecisionIntervalTicks != 0)
                    continue;

                TeamPerception perception = hero.Team == Team.Dawn
                    ? dawn ??= TeamPerception.Build(state, Team.Dawn, _settings)
                    : dusk ??= TeamPerception.Build(state, Team.Dusk, _settings);
                Decide(state, hero, perception, now);
            }
        }

        private void Decide(MatchState state, MatchUnit hero, TeamPerception perception, long now)
        {
            HeroState h = hero.Hero!;
            TryShop(state, hero);
            if (h.IsDead || h.IsCasting || !hero.IsAlive)
                return;

            int hpPercent = (int)(hero.Hp * Fixed.FromInt(100) / Fixed.FromInt(hero.MaxHp)).ToIntFloor();
            _retreating.TryGetValue(hero.Id.Value, out bool retreating);
            if (retreating && hpPercent >= _settings.ReturnHpPercent)
                retreating = false;
            else if (!retreating && hpPercent < _settings.RetreatHpPercent)
                retreating = true;
            _retreating[hero.Id.Value] = retreating;

            if (retreating)
            {
                Issue(hero, HeroOrder.Move(state.Ruleset.Map.FountainOf(hero.Team)!.Position));
                return;
            }

            if (TryCast(state, hero, perception, now))
                return;
            if (TryEngageHero(state, hero, perception, hpPercent))
                return;
            Lane(state, hero, perception);
        }

        private static void TryShop(MatchState state, MatchUnit hero)
        {
            HeroState h = hero.Hero!;
            FountainPlacement? fountain = state.Ruleset.Map.FountainOf(hero.Team);
            bool inShop = h.IsDead || (fountain != null && Within(hero.Position, fountain.Position, fountain.Radius));
            if (!inShop)
                return;

            string? target = NextBuildItem(h);
            ItemDefinition? item = target != null ? NextPurchaseToward(state, h, state.Ruleset.FindItem(target)) : null;
            if (item == null)
                return;

            int ownedComponentCost = item.Components
                .Where(c => h.Inventory.Any(slot => slot.Definition.Id == c))
                .Sum(c => state.Ruleset.FindItem(c)!.Cost);
            if (h.Gold >= Fixed.FromInt(item.Cost - ownedComponentCost))
                HeroShop.QueuePurchase(hero, item.Id);
        }

        /// <summary>First build entry not yet owned, counting duplicates and items already built from it.</summary>
        private static string? NextBuildItem(HeroState hero)
        {
            var owned = hero.Inventory.Select(s => s.Definition.Id).ToList();
            foreach (string itemId in hero.Definition.ItemBuild)
            {
                if (owned.Remove(itemId))
                    continue;
                return itemId;
            }

            return null;
        }

        /// <summary>The first missing component of the target (recursively), or the target itself once all are owned.</summary>
        private static ItemDefinition? NextPurchaseToward(MatchState state, HeroState hero, ItemDefinition? target)
        {
            if (target == null)
                return null;

            var owned = hero.Inventory.Select(s => s.Definition.Id).ToList();
            foreach (string componentId in target.Components)
            {
                if (owned.Remove(componentId))
                    continue;
                return NextPurchaseToward(state, hero, state.Ruleset.FindItem(componentId));
            }

            return target;
        }

        private bool TryCast(MatchState state, MatchUnit hero, TeamPerception perception, long now)
        {
            HeroState h = hero.Hero!;
            if ((hero.Flags & MatchSimulation.Combat.ModifierFlags.Silenced) != 0)
                return false;

            foreach (AbilityState ability in h.Abilities.Concat(h.Inventory.Where(i => i.Active != null).Select(i => i.Active!)))
            {
                AbilityDefinition def = ability.Definition;
                if (def.Targeting == AbilityTargeting.Passive || !ability.IsReady(now) || h.Mana < Fixed.FromInt(def.ManaCostAt(ability.Level)))
                    continue;

                HeroOrder? order = ChooseCast(state, hero, perception, def);
                if (order != null)
                {
                    Issue(hero, order);
                    return true;
                }
            }

            return false;
        }

        private HeroOrder? ChooseCast(MatchState state, MatchUnit hero, TeamPerception perception, AbilityDefinition def)
        {
            int reach = def.CastRange + 200;
            switch (def.Targeting)
            {
                case AbilityTargeting.Unit when def.TargetTeam == TargetTeam.Ally:
                    MatchUnit? hurt = state.Units
                        .Where(u => u.Hero != null && u.Team == hero.Team && u.IsAlive && u.Hp * Fixed.FromInt(100) < Fixed.FromInt(u.MaxHp * 60) && Within(hero.Position, u.Position, reach))
                        .OrderBy(u => u.Hp)
                        .FirstOrDefault();
                    return hurt != null ? HeroOrder.Cast(def.Id, hurt.Id) : null;

                case AbilityTargeting.Unit:
                    MatchUnit? enemy = NearestVisibleEnemyHero(state, hero, perception, reach);
                    return enemy != null ? HeroOrder.Cast(def.Id, enemy.Id) : null;

                case AbilityTargeting.Point:
                    MatchUnit? pointTarget = NearestVisibleEnemyHero(state, hero, perception, reach);
                    return pointTarget != null ? HeroOrder.CastAtPoint(def.Id, pointTarget.Position) : null;

                default:
                    if (def.Effects.Any(e => e.Kind == EffectKind.Summon))
                        return AnyVisibleEnemy(state, hero, perception, 800) ? HeroOrder.CastNoTarget(def.Id) : null;
                    EffectDefinition? area = def.Effects.FirstOrDefault(e => e.Kind == EffectKind.Area);
                    int radius = area?.Radius ?? 600;
                    if (area != null && area.Team == TargetTeam.Ally)
                    {
                        // Group heal/buff: when an allied hero inside the area is hurt.
                        bool allyHurt = state.Units.Any(u => u.Hero != null && u.Team == hero.Team && u.IsAlive &&
                                                             u.Hp * Fixed.FromInt(100) < Fixed.FromInt(u.MaxHp * 60) && Within(hero.Position, u.Position, radius));
                        return allyHurt ? HeroOrder.CastNoTarget(def.Id) : null;
                    }

                    return NearestVisibleEnemyHero(state, hero, perception, radius) != null ? HeroOrder.CastNoTarget(def.Id) : null;
            }
        }

        private bool TryEngageHero(MatchState state, MatchUnit hero, TeamPerception perception, int hpPercent)
        {
            if (hpPercent < 50)
                return false;

            MatchUnit? enemy = NearestVisibleEnemyHero(state, hero, perception, _settings.EngageRange);
            if (enemy == null || UnderEnemyTower(state, hero.Team, enemy.Position))
                return false;

            Issue(hero, HeroOrder.Attack(enemy.Id));
            return true;
        }

        private static void Lane(MatchState state, MatchUnit hero, TeamPerception perception)
        {
            // Keep hitting a creep we already committed to.
            if (hero.Hero!.Order.Kind == HeroOrderKind.Attack)
            {
                MatchUnit? current = state.Find(hero.Hero.Order.Target);
                if (current != null && current.IsAlive && current.Kind != UnitKind.Hero)
                    return;
            }

            MatchUnit? lastHit = FindLastHit(state, hero, perception);
            if (lastHit != null)
            {
                Issue(hero, HeroOrder.Attack(lastHit.Id));
                return;
            }

            MatchUnit? structure = FindPushTarget(state, hero, perception);
            if (structure != null)
            {
                Issue(hero, HeroOrder.Attack(structure.Id));
                return;
            }

            FixedVector2 front = LaneFront(state, hero);
            if (UnderEnemyTower(state, hero.Team, front))
                front = LaneRally(state, hero);
            Issue(hero, HeroOrder.Move(front));
        }

        /// <summary>A visible enemy creep this hero would kill with one hit, or an allied creep to deny.</summary>
        private static MatchUnit? FindLastHit(MatchState state, MatchUnit hero, TeamPerception perception)
        {
            int reach = hero.AttackRange + 400;
            Fixed factor = state.Ruleset.Combat.ArmorFactor;
            MatchUnit? best = null;
            foreach (MatchUnit unit in state.Units)
            {
                if ((unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon) || !unit.IsAlive || !perception.CanSee(unit))
                    continue;
                if (!Within(hero.Position, unit.Position, reach) || UnderEnemyTower(state, hero.Team, unit.Position))
                    continue;

                Fixed hit = Fixed.FromInt(hero.AttackDamageMin) * CombatMath.ArmorMultiplier(unit.Armor, factor);
                bool deny = unit.Team == hero.Team && unit.Hp * Fixed.FromInt(2) < Fixed.FromInt(unit.MaxHp);
                if (unit.Hp <= hit && (unit.Team != hero.Team || deny) && (best == null || unit.Hp < best.Hp))
                    best = unit;
            }

            return best;
        }

        /// <summary>A vulnerable enemy structure our creeps are already attacking (no enemy creeps visible nearby).</summary>
        private static MatchUnit? FindPushTarget(MatchState state, MatchUnit hero, TeamPerception perception)
        {
            bool enemyCreepsNear = state.Units.Any(u => u.Team != hero.Team && u.IsAlive && (u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) &&
                                                        perception.CanSee(u) && Within(hero.Position, u.Position, 900));
            if (enemyCreepsNear)
                return null;

            foreach (MatchUnit structure in state.Units)
            {
                if (structure.Kind != UnitKind.Structure || structure.Team == hero.Team || !structure.IsAlive || !state.IsVulnerable(structure))
                    continue;
                if (!Within(hero.Position, structure.Position, 1000))
                    continue;
                bool tanked = state.Units.Any(u => u.Team == hero.Team && u.Kind == UnitKind.Creep && u.IsAlive && Within(u.Position, structure.Position, TowerReach(structure)));
                if (tanked || !structure.CanAttackAtAll)
                    return structure;
            }

            return null;
        }

        /// <summary>Just behind the most advanced allied creep of the hero's lane, or the lane rally point.</summary>
        private static FixedVector2 LaneFront(MatchState state, MatchUnit hero)
        {
            FixedVector2 enemyFountain = state.Ruleset.Map.FountainOf(hero.Team.Opponent())!.Position;
            FixedVector2 ownFountain = state.Ruleset.Map.FountainOf(hero.Team)!.Position;
            MatchUnit? front = null;
            Fixed best = Fixed.Zero;
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Team != hero.Team || unit.Kind != UnitKind.Creep || !unit.IsAlive || unit.LaneId != hero.Hero!.AssignedLane)
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(unit.Position, enemyFountain);
                if (front == null || distance < best)
                {
                    front = unit;
                    best = distance;
                }
            }

            return front != null ? FixedVector2.MoveTowards(front.Position, ownFountain, Fixed.FromInt(150)) : LaneRally(state, hero);
        }

        /// <summary>The hero's outermost living tower on its lane, or its fountain.</summary>
        private static FixedVector2 LaneRally(MatchState state, MatchUnit hero)
        {
            FixedVector2 enemyFountain = state.Ruleset.Map.FountainOf(hero.Team.Opponent())!.Position;
            string laneTag = "." + hero.Hero!.AssignedLane + ".";
            MatchUnit? outer = null;
            Fixed best = Fixed.Zero;
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Team != hero.Team || unit.Placement == null || !unit.Placement.Id.Contains(laneTag))
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(unit.Position, enemyFountain);
                if (outer == null || distance < best)
                {
                    outer = unit;
                    best = distance;
                }
            }

            return outer?.Position ?? state.Ruleset.Map.FountainOf(hero.Team)!.Position;
        }

        /// <summary>Whether standing at the point would draw fire from an enemy tower no allied creep is tanking.</summary>
        private static bool UnderEnemyTower(MatchState state, Team team, FixedVector2 point)
        {
            foreach (MatchUnit tower in state.Units)
            {
                if (tower.Kind != UnitKind.Structure || tower.Team == team || !tower.IsAlive || !tower.CanAttackAtAll)
                    continue;
                int reach = TowerReach(tower);
                if (!Within(point, tower.Position, reach + 100))
                    continue;
                bool tanked = state.Units.Any(u => u.Team == team && (u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) && u.IsAlive && Within(u.Position, tower.Position, reach));
                if (!tanked)
                    return true;
            }

            return false;
        }

        private static int TowerReach(MatchUnit tower) => tower.AttackRange + tower.BoundingRadius + 24;

        private static MatchUnit? NearestVisibleEnemyHero(MatchState state, MatchUnit hero, TeamPerception perception, int range)
        {
            MatchUnit? best = null;
            Fixed bestDistance = Fixed.Zero;
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Hero == null || unit.Team == hero.Team || !unit.IsAlive || !perception.CanSee(unit) || !state.IsVulnerable(unit))
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(hero.Position, unit.Position);
                Fixed reach = Fixed.FromInt(range);
                if (distance <= reach * reach && (best == null || distance < bestDistance))
                {
                    best = unit;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool AnyVisibleEnemy(MatchState state, MatchUnit hero, TeamPerception perception, int range) =>
            state.Units.Any(u => u.Team != hero.Team && u.IsAlive && u.Kind != UnitKind.Structure && perception.CanSee(u) && Within(hero.Position, u.Position, range));

        private static bool Within(FixedVector2 a, FixedVector2 b, int range)
        {
            Fixed r = Fixed.FromInt(range);
            return FixedVector2.DistanceSquared(a, b) <= r * r;
        }

        /// <summary>Issues the order unless the hero is already doing the same thing (avoids re-pathing every decision).</summary>
        private static void Issue(MatchUnit hero, HeroOrder order)
        {
            HeroOrder current = hero.Hero!.Order;
            if (current.Kind == order.Kind && current.Target == order.Target && current.AbilityId == order.AbilityId &&
                (order.Kind != HeroOrderKind.Move || Within(current.Point, order.Point, 100)))
                return;

            HeroOrders.Issue(hero, order);
        }
    }
}
