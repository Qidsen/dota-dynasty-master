using System.Collections.Generic;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Dota's lane aggro (<see cref="DotaAggroRules"/>; Liquipedia «Lane Creeps» and «Buildings», 7.41), for lane creeps and
    /// buildings; summons keep the plain rule. Everything that draws aggro is read from what the previous tick left (orders
    /// issued, attacks started), so the order units are processed in does not matter (D-014).
    /// </summary>
    internal static class DotaAggro
    {
        public static void Tick(SimulationContext<MatchState> context, DotaAggroRules rules)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            var draws = new List<MatchUnit>();       // attack orders on enemy heroes (off cooldown)
            var attacks = new List<MatchUnit>();     // attacks on heroes started last tick (off the draw cooldown)
            var heroAttacks = new List<MatchUnit>(); // the same, whatever the cooldown (towers protect heroes from these)
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Kind != UnitKind.Hero || !unit.IsAlive)
                    continue;
                Heroes.HeroState? hero = unit.Hero;
                if (hero != null && hero.AttackOrders != hero.AttackOrdersSeen)
                {
                    hero.AttackOrdersSeen = hero.AttackOrders;
                    MatchUnit? target = hero.Order.Kind == Heroes.HeroOrderKind.Attack ? state.Find(hero.Order.Target) : null;
                    if (target != null && target.Kind == UnitKind.Hero && target.Team != unit.Team && now >= hero.AggroCooldownUntilTick)
                    {
                        hero.AggroCooldownUntilTick = now + context.TickRate.TicksFor(rules.CooldownMs);
                        draws.Add(unit);
                        continue;
                    }
                }

                if (unit.AttackStartTick == now - 1 && state.Find(unit.AttackStartTarget)?.Kind == UnitKind.Hero)
                {
                    heroAttacks.Add(unit);
                    if (hero == null || now >= hero.AggroCooldownUntilTick)
                        attacks.Add(unit);
                }
            }

            foreach (MatchUnit unit in state.Units)
            {
                if (!unit.CanAttackAtAll || !unit.IsAlive || unit.Team == Team.Neutral)
                    continue;
                if (unit.Kind == UnitKind.Creep && unit.Lane != null)
                    Creep(context, rules, unit, draws, attacks);
                else if (unit.Kind == UnitKind.Structure)
                    Building(state, rules, unit, heroAttacks);
            }
        }

        private static void Creep(SimulationContext<MatchState> context, DotaAggroRules rules, MatchUnit creep, List<MatchUnit> draws, List<MatchUnit> attacks)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            if (!creep.AggroUnlocked)
                creep.AggroUnlocked = now * 1000 / context.TickRate.TicksPerSecond >= rules.LockedUntilMs || NearOwnTier1(state, rules, creep);

            if (creep.ForcedUntilTick > now)
            {
                MatchUnit? forced = state.Find(creep.ForcedTargetId);
                if (forced != null && state.CanAttack(creep, forced))
                {
                    creep.TargetId = forced.Id;
                    return;
                }

                creep.ForcedUntilTick = 0;
            }

            if (creep.AggroUnlocked)
            {
                MatchUnit? drawer = NearestWithin(state, creep, draws, rules.DrawRadius);
                if (drawer != null)
                {
                    creep.TargetId = drawer.Id;
                    creep.ForcedTargetId = drawer.Id;
                    creep.ForcedUntilTick = now + context.TickRate.TicksFor(rules.ForcedMs);
                    return;
                }

                MatchUnit? attacker = NearestWithin(state, creep, attacks, rules.DrawRadius);
                if (attacker != null)
                {
                    creep.TargetId = attacker.Id;
                    return;
                }
            }

            creep.TargetId = Choose(state, creep, creep.AcquisitionRange, threatRadius: int.MaxValue);
        }

        private static void Building(MatchState state, DotaAggroRules rules, MatchUnit building, List<MatchUnit> heroAttacks)
        {
            // A tower protects heroes: it turns at once on a hero near it that attacks a hero (not the fountain).
            if (building.TypeId != StructureType.FountainId)
            {
                MatchUnit? attacker = NearestWithin(state, building, heroAttacks, rules.TowerProtectRadius, building.AttackRange);
                if (attacker != null)
                {
                    building.TargetId = attacker.Id;
                    return;
                }
            }

            building.TargetId = Choose(state, building, building.AttackRange, rules.TowerThreatRadius, chasesOutOfRange: false);
        }

        /// <summary>The enemy among <paramref name="candidates"/> nearest the unit within the radius of it, that it can attack.</summary>
        private static MatchUnit? NearestWithin(MatchState state, MatchUnit unit, List<MatchUnit> candidates, int radius, int attackRange = int.MaxValue)
        {
            MatchUnit? best = null;
            Fixed bestDistance = Fixed.Zero;
            foreach (MatchUnit candidate in candidates)
            {
                if (candidate.Team == unit.Team || !Geometry.WithinRange(candidate.Position, unit.Position, radius) || !state.CanAttack(unit, candidate) ||
                    !state.IsVisibleTo(unit.Team, candidate) || (attackRange != int.MaxValue && !Geometry.WithinEdgeRange(unit, candidate, attackRange)))
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(candidate.Position, unit.Position);
                if (best == null || distance < bestDistance || (distance == bestDistance && candidate.Id.Value < best.Id.Value))
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Keeps the current target unless a unit of a higher type priority is in attack range, or (for creeps) the target has
        /// left the attack range and another is in it; a new target is the best by type, threat (within the threat radius),
        /// then distance (ties by ID). A creep keeps chasing a target out of its range while nothing else is in range.
        /// </summary>
        private static EntityId Choose(MatchState state, MatchUnit unit, int searchRange, int threatRadius, bool chasesOutOfRange = true)
        {
            MatchUnit? current = state.Find(unit.TargetId);
            bool valid = current != null && current.Team != Team.Neutral && state.CanAttack(unit, current) && state.IsVisibleTo(unit.Team, current) &&
                         (chasesOutOfRange || Geometry.WithinEdgeRange(unit, current, unit.AttackRange));
            if (!valid)
                return Best(state, unit, searchRange, threatRadius, int.MaxValue)?.Id ?? EntityId.None;

            MatchUnit? higher = Best(state, unit, unit.AttackRange, threatRadius, TypePriority(unit, current!));
            if (higher != null)
                return higher.Id;
            if (!Geometry.WithinEdgeRange(unit, current!, unit.AttackRange))
                return Best(state, unit, unit.AttackRange, threatRadius, int.MaxValue)?.Id ?? current!.Id;
            return current!.Id;
        }

        /// <summary>The best target within range whose type priority is below <paramref name="belowType"/>.</summary>
        private static MatchUnit? Best(MatchState state, MatchUnit unit, int range, int threatRadius, int belowType)
        {
            MatchUnit? best = null;
            (int Type, int Threat, Fixed Distance) bestKey = default;
            foreach (MatchUnit candidate in state.Units)
            {
                if (candidate.Team == Team.Neutral || !Geometry.WithinEdgeRange(unit, candidate, range) || !state.CanAttack(unit, candidate) ||
                    !state.IsVisibleTo(unit.Team, candidate))
                    continue;
                int type = TypePriority(unit, candidate);
                if (type >= belowType)
                    continue;
                (int, int, Fixed) key = (type, Threat(state, unit, candidate, threatRadius), Geometry.EdgeDistance(unit, candidate));
                if (best == null || Less(key, bestKey) || (!Less(bestKey, key) && candidate.Id.Value < best.Id.Value))
                {
                    best = candidate;
                    bestKey = key;
                }
            }

            return best;
        }

        private static bool Less((int Type, int Threat, Fixed Distance) a, (int Type, int Threat, Fixed Distance) b) =>
            a.Type != b.Type ? a.Type < b.Type : a.Threat != b.Threat ? a.Threat < b.Threat : a.Distance < b.Distance;

        /// <summary>
        /// Lower first. Lane creeps and towers: heroes and other units alike, then siege creeps, then buildings. Siege creeps:
        /// buildings, then other units, then siege creeps, then heroes (illusions too).
        /// </summary>
        private static int TypePriority(MatchUnit unit, MatchUnit candidate)
        {
            bool siegeCandidate = IsSiege(candidate);
            if (IsSiege(unit))
                return candidate.Kind == UnitKind.Structure ? 0 : candidate.Kind == UnitKind.Hero ? 3 : siegeCandidate ? 2 : 1;
            return candidate.Kind == UnitKind.Structure ? 2 : siegeCandidate ? 1 : 0;
        }

        private static bool IsSiege(MatchUnit unit) => unit.Kind == UnitKind.Creep && unit.Stats.AttackClass == AttackClass.Reinforced;

        /// <summary>0: attacking the unit; 1: attacking one of its allies; 2: anything else (or farther than the threat radius).</summary>
        private static int Threat(MatchState state, MatchUnit unit, MatchUnit candidate, int threatRadius)
        {
            if (threatRadius != int.MaxValue && !Geometry.WithinRange(candidate.Position, unit.Position, threatRadius))
                return 2;
            MatchUnit? target = state.Find(candidate.TargetId);
            return target == unit ? 0 : target != null && target.Team == unit.Team ? 1 : 2;
        }

        /// <summary>Whether the creep is within the unlock radius of its team's tier 1 tower on its lane (none: unlocked).</summary>
        private static bool NearOwnTier1(MatchState state, DotaAggroRules rules, MatchUnit creep)
        {
            string laneTag = "." + creep.Lane!.Id + ".";
            bool any = false;
            foreach (StructurePlacement placement in state.Ruleset.Map.Structures)
            {
                if (placement.Team != creep.Team || placement.StructureTypeId != rules.UnlockStructureTypeId || !placement.Id.Contains(laneTag))
                    continue;
                any = true;
                if (Geometry.WithinRange(creep.Position, placement.Position, rules.UnlockRadius))
                    return true;
            }

            return !any;
        }
    }
}
