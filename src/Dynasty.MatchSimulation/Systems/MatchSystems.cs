using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>Spawns a creep wave on every lane for both teams at the ruleset's interval.</summary>
    internal sealed class WaveSpawnSystem : ISimulationSystem<MatchState>
    {
        public string Name => "wave-spawn";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            if (now < state.NextWaveTick)
                return;

            WaveRules waves = state.Ruleset.Waves;
            foreach (LaneDefinition lane in state.Ruleset.Map.Lanes)
            {
                foreach (Team team in new[] { Team.Dawn, Team.Dusk })
                {
                    foreach (WaveEntry entry in waves.CompositionFor(team))
                    {
                        CreepType type = state.Ruleset.FindCreepType(entry.CreepTypeId)!;
                        for (int i = 0; i < entry.Count; i++)
                        {
                            MatchUnit creep = state.SpawnCreep(context.Ids.Allocate(), team, type, lane, now);
                            context.Emit(new CreepSpawned(creep.Id, team, type.Id, lane.Id));
                        }
                    }
                }
            }

            state.NextWaveTick += state.Rate.TicksFor(waves.IntervalMs);
        }
    }

    /// <summary>
    /// Keeps a valid target or acquires the nearest attackable enemy (edge distance, ties by lower ID).
    /// Creeps search within acquisition range; towers within attack range and only hit units.
    /// </summary>
    internal sealed class TargetingSystem : ISimulationSystem<MatchState>
    {
        public string Name => "targeting";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            foreach (MatchUnit unit in state.Units)
            {
                if (!unit.Stats.CanAttack || !unit.IsAlive)
                    continue;

                int searchRange = unit.Kind == UnitKind.Structure ? unit.Stats.AttackRange : unit.Stats.AcquisitionRange;
                MatchUnit? current = state.Find(unit.TargetId);
                if (current != null && state.CanAttack(unit, current) && Geometry.WithinEdgeRange(unit, current, searchRange))
                    continue;

                unit.TargetId = FindNearest(state, unit, searchRange);
            }
        }

        private static EntityId FindNearest(MatchState state, MatchUnit unit, int range)
        {
            EntityId best = EntityId.None;
            Fixed bestDistance = Fixed.Zero;
            foreach (MatchUnit candidate in state.Units)
            {
                if (!state.CanAttack(unit, candidate) || !Geometry.WithinEdgeRange(unit, candidate, range))
                    continue;

                Fixed distance = Geometry.EdgeDistance(unit, candidate);
                if (!best.IsValid || distance < bestDistance)
                {
                    best = candidate.Id;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }

    /// <summary>
    /// Creeps chase their target until it is within attack range; without a target they walk
    /// their lane, and past its end head for the nearest vulnerable enemy structure.
    /// </summary>
    internal sealed class MovementSystem : ISimulationSystem<MatchState>
    {
        public string Name => "movement";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Kind != UnitKind.Creep || unit.StepPerTick <= Fixed.Zero)
                    continue;

                MatchUnit? target = state.Find(unit.TargetId);
                if (target == null || !state.CanAttack(unit, target))
                    target = unit.NextWaypoint >= unit.Lane!.Waypoints.Count ? NearestVulnerableStructure(state, unit) : null;

                if (target != null)
                {
                    if (Approach(unit, target))
                        unit.LastActiveTick = now;
                    continue;
                }

                if (FollowLane(unit))
                    unit.LastActiveTick = now;
            }
        }

        /// <summary>Moves toward the target until it is within attack range. Returns true if it moved.</summary>
        private static bool Approach(MatchUnit unit, MatchUnit target)
        {
            // Same predicate as the attack check; a separate sqrt-based test could disagree at the
            // boundary (floored sqrt) and leave the unit neither moving nor attacking.
            if (Geometry.WithinEdgeRange(unit, target, unit.Stats.AttackRange))
                return false;

            Fixed reach = Fixed.FromInt(unit.Stats.AttackRange + unit.Stats.BoundingRadius + target.Stats.BoundingRadius);
            Fixed distance = FixedVector2.Distance(unit.Position, target.Position);
            // +1 unit guarantees progress into range even when the floored distance equals reach.
            Fixed step = Fixed.Min(unit.StepPerTick, distance - reach + Fixed.One);
            unit.Position = FixedVector2.MoveTowards(unit.Position, target.Position, step);
            return true;
        }

        private static bool FollowLane(MatchUnit unit)
        {
            int count = unit.Lane!.Waypoints.Count;
            while (unit.NextWaypoint < count && unit.Position == unit.WaypointAt(unit.NextWaypoint))
                unit.NextWaypoint++;
            if (unit.NextWaypoint >= count)
                return false;

            unit.Position = FixedVector2.MoveTowards(unit.Position, unit.WaypointAt(unit.NextWaypoint), unit.StepPerTick);
            return true;
        }

        private static MatchUnit? NearestVulnerableStructure(MatchState state, MatchUnit unit)
        {
            MatchUnit? best = null;
            Fixed bestDistance = Fixed.Zero;
            foreach (MatchUnit candidate in state.Units)
            {
                if (candidate.Kind != UnitKind.Structure || !state.CanAttack(unit, candidate))
                    continue;

                Fixed distance = FixedVector2.DistanceSquared(unit.Position, candidate.Position);
                if (best == null || distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }

    /// <summary>
    /// Starts attacks (wind-up = attack point) and lands them. Attacks due this tick land even if
    /// the attacker itself died earlier in the same tick, so simultaneous hits do not favour
    /// whichever unit happens to be processed first.
    /// </summary>
    internal sealed class AttackSystem : ISimulationSystem<MatchState>
    {
        public string Name => "attack";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            foreach (MatchUnit unit in state.Units)
            {
                if (!unit.Stats.CanAttack)
                    continue;

                if (unit.PendingAttackTick < 0 && unit.IsAlive && now >= unit.NextAttackTick)
                    TryStartAttack(context, unit, now);

                if (unit.PendingAttackTick >= 0 && now >= unit.PendingAttackTick)
                    Land(context, unit);
            }
        }

        private static void TryStartAttack(SimulationContext<MatchState> context, MatchUnit unit, long now)
        {
            MatchUnit? target = context.State.Find(unit.TargetId);
            if (target == null || !context.State.CanAttack(unit, target) || !Geometry.WithinEdgeRange(unit, target, unit.Stats.AttackRange))
                return;

            unit.PendingTargetId = target.Id;
            unit.PendingAttackTick = now + unit.AttackPointTicks;
            unit.NextAttackTick = now + unit.AttackIntervalTicks;
            unit.LastActiveTick = now;
            context.Emit(new AttackStarted(unit.Id, target.Id));
        }

        private static void Land(SimulationContext<MatchState> context, MatchUnit unit)
        {
            unit.PendingAttackTick = -1;
            MatchUnit? target = context.State.Find(unit.PendingTargetId);
            if (target == null || !target.IsAlive || target.Team == unit.Team || !context.State.IsVulnerable(target))
                return;

            int roll = context.Random("combat").NextInt(unit.Stats.AttackDamageMin, unit.Stats.AttackDamageMax + 1);
            Fixed damage = Fixed.FromInt(roll) * CombatMath.ArmorMultiplier(target.Stats.Armor, context.State.Ruleset.Combat.ArmorFactor);

            target.Hp = Fixed.Max(Fixed.Zero, target.Hp - damage);
            target.LastAttackerId = unit.Id;
            context.Emit(new DamageDealt(unit.Id, target.Id, damage, target.Hp));
        }
    }

    /// <summary>Removes dead units, records destroyed structures, and ends the match when an ancient falls.</summary>
    internal sealed class CleanupSystem : ISimulationSystem<MatchState>
    {
        public string Name => "cleanup";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            List<MatchUnit> dead = state.Units.Where(u => !u.IsAlive).ToList();
            StructurePlacement? fallenAncient = null;

            foreach (MatchUnit unit in dead)
            {
                state.Remove(unit);
                if (unit.Placement == null)
                {
                    context.Emit(new UnitKilled(unit.Id, unit.Team, unit.TypeId, unit.LastAttackerId));
                    continue;
                }

                context.Emit(new StructureDestroyed(unit.Id, unit.Placement.Id, unit.Team, unit.LastAttackerId));
                if (unit.Placement.IsAncient && fallenAncient == null)
                    fallenAncient = unit.Placement;
            }

            if (fallenAncient != null && state.Winner == null)
            {
                state.Winner = fallenAncient.Team.Opponent();
                context.Emit(new MatchFinished(state.Winner.Value, fallenAncient.Id));
                context.EndSimulation();
            }
        }
    }
}
