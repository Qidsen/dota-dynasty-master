using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
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
    /// Periodic damage/heal of modifiers, then expiry. Runs before targeting so expired
    /// stuns and slows no longer affect this tick's actions.
    /// </summary>
    internal sealed class ModifierSystem : ISimulationSystem<MatchState>
    {
        private readonly List<ActiveModifier> _scratch = new List<ActiveModifier>();

        public string Name => "modifiers";

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            foreach (MatchUnit unit in context.State.Units)
            {
                if (unit.Modifiers.Count == 0)
                    continue;

                _scratch.Clear();
                _scratch.AddRange(unit.Modifiers);
                foreach (ActiveModifier modifier in _scratch)
                {
                    ModifierSpec spec = modifier.Spec;
                    if (spec.IsPeriodic && now >= modifier.NextPeriodTick)
                    {
                        Fixed periodFraction = Fixed.FromRatio(spec.PeriodMs, 1000);
                        if (spec.DamagePerSecond > Fixed.Zero)
                            Damage.Deal(context, modifier.Source, unit, spec.DamagePerSecond * periodFraction, spec.DamageType);
                        if (spec.HealPerSecond > Fixed.Zero)
                            Damage.Heal(context, modifier.Source, unit, spec.HealPerSecond * periodFraction);
                        modifier.NextPeriodTick += context.TickRate.TicksFor(spec.PeriodMs);
                    }

                    if (modifier.ExpiresTick >= 0 && now >= modifier.ExpiresTick)
                        Modifiers.Remove(context, unit, modifier, "expired");
                }
            }
        }
    }

    /// <summary>
    /// Records every unit's position and status flags. Runs after the tick-start updates
    /// (modifier expiry, respawns, shopping) and before anything acts.
    /// </summary>
    internal sealed class TickSnapshotSystem : ISimulationSystem<MatchState>
    {
        public string Name => "tick-snapshot";

        public void Tick(SimulationContext<MatchState> context)
        {
            foreach (MatchUnit unit in context.State.Units)
            {
                unit.StartPosition = unit.Position;
                unit.StartFlags = unit.Flags;
            }
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
                if (!unit.CanAttackAtAll || !unit.IsAlive || unit.Kind == UnitKind.Hero)
                    continue;

                int searchRange = unit.Kind == UnitKind.Structure ? unit.AttackRange : unit.AcquisitionRange;
                MatchUnit? current = state.Find(unit.TargetId);
                if (current != null && state.CanAttack(unit, current) && Geometry.WithinEdgeRange(unit, current, searchRange))
                    continue;

                unit.TargetId = FindNearest(state, unit, searchRange);
            }
        }

        internal static EntityId FindNearest(MatchState state, MatchUnit unit, int range)
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
                if ((unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon) || !unit.CanMove)
                    continue;

                MatchUnit? target = state.Find(unit.TargetId);
                if (target == null || !state.CanAttack(unit, target))
                    target = unit.Lane != null && unit.NextWaypoint >= unit.Lane.Waypoints.Count ? NearestVulnerableStructure(state, unit) : null;

                if (target != null)
                {
                    if (Approach(unit, target, unit.AttackRange))
                        unit.LastActiveTick = now;
                    continue;
                }

                if (unit.Lane != null)
                {
                    if (FollowLane(unit))
                        unit.LastActiveTick = now;
                }
                else if (unit.Kind == UnitKind.Summon && FollowOwner(state, unit))
                {
                    unit.LastActiveTick = now;
                }
            }
        }

        /// <summary>Summons without a target stay near their owner.</summary>
        private static bool FollowOwner(MatchState state, MatchUnit unit)
        {
            MatchUnit? owner = state.Find(unit.Owner);
            if (owner == null || !owner.IsAlive || Geometry.WithinRange(unit.Position, owner.Position, 300))
                return false;

            unit.Position = FixedVector2.MoveTowards(unit.Position, owner.Position, unit.StepPerTick);
            return true;
        }

        /// <summary>
        /// Moves toward the target until it is within <paramref name="range"/> (edge to edge). Returns
        /// true if it moved. Aims at the target's tick-start position, so pursuit does not depend on
        /// whether the target was processed earlier or later in this tick.
        /// </summary>
        internal static bool Approach(MatchUnit unit, MatchUnit target, int range)
        {
            // Squared-distance predicate, consistent with the attack/cast checks; a separate sqrt-based
            // test could disagree at the boundary (floored sqrt) and leave the unit neither moving nor acting.
            if (Geometry.WithinEdgeRangeOfStart(unit, target, range))
                return false;

            Fixed reach = Fixed.FromInt(range + unit.BoundingRadius + target.BoundingRadius);
            Fixed distance = FixedVector2.Distance(unit.Position, target.StartPosition);
            // +1 unit guarantees progress into range even when the floored distance equals reach.
            Fixed step = Fixed.Min(unit.StepPerTick, distance - reach + Fixed.One);
            unit.Position = FixedVector2.MoveTowards(unit.Position, target.StartPosition, step);
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
    /// whichever unit happens to be processed first. Stun and disarm interrupt the wind-up.
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
                if (!unit.CanAttackAtAll)
                    continue;

                if (unit.PendingAttackTick >= 0 && !unit.CanStartAttack && unit.IsAlive)
                    unit.PendingAttackTick = -1;

                if (unit.PendingAttackTick < 0 && unit.IsAlive && unit.CanStartAttack && now >= unit.NextAttackTick)
                    TryStartAttack(context, unit, now);

                if (unit.PendingAttackTick >= 0 && now >= unit.PendingAttackTick)
                    Land(context, unit);
            }
        }

        private static void TryStartAttack(SimulationContext<MatchState> context, MatchUnit unit, long now)
        {
            MatchUnit? target = context.State.Find(unit.TargetId);
            if (target == null || !context.State.CanAttack(unit, target) || !Geometry.WithinEdgeRange(unit, target, unit.AttackRange))
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
            if (target == null || !context.State.CanAttack(unit, target))
                return;

            int roll = context.Random("combat").NextInt(unit.AttackDamageMin, unit.AttackDamageMax + 1);
            Fixed damage = Fixed.FromInt(roll);

            bool critical = unit.CritChancePercent > 0 && context.Random("combat").NextInt(0, 100) < unit.CritChancePercent;
            if (critical)
                damage = damage * Fixed.FromInt(unit.CritMultiplierPercent) / Fixed.FromInt(100);
            if (target.Kind == UnitKind.Structure && unit.StructureDamagePercent != 0)
                damage = damage * Fixed.FromInt(100 + unit.StructureDamagePercent) / Fixed.FromInt(100);

            Fixed dealt = Damage.Deal(context, unit.Id, target, damage, DamageType.Physical, critical);
            if (unit.LifestealPercent > 0 && unit.IsAlive)
                Damage.Heal(context, unit.Id, unit, dealt * Fixed.FromInt(unit.LifestealPercent) / Fixed.FromInt(100));
        }
    }

    /// <summary>Removes dead units, records destroyed structures, and ends the match when an ancient falls.</summary>
    internal sealed class CleanupSystem : ISimulationSystem<MatchState>
    {
        public string Name => "cleanup";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            foreach (MatchUnit expired in state.Units.Where(u => u.ExpiresTick >= 0 && now >= u.ExpiresTick && u.IsAlive).ToList())
            {
                state.Remove(expired);
                context.Emit(new UnitExpired(expired.Id));
            }

            List<MatchUnit> dead = state.Units.Where(u => !u.IsAlive && !(u.Hero?.IsDead ?? false)).ToList();
            StructurePlacement? fallenAncient = null;

            foreach (MatchUnit unit in dead)
            {
                if (unit.Hero != null)
                {
                    // Heroes stay in the match and respawn.
                    Rewards.HeroDied(context, unit);
                    continue;
                }

                state.Remove(unit);
                if (unit.Placement == null)
                {
                    context.Emit(new UnitKilled(unit.Id, unit.Team, unit.TypeId, unit.LastAttackerId));
                    if (unit.Kind == UnitKind.Creep || unit.Kind == UnitKind.Summon)
                        Rewards.CreepDied(context, unit, state.Ruleset.FindCreepType(unit.TypeId));
                    continue;
                }

                context.Emit(new StructureDestroyed(unit.Id, unit.Placement.Id, unit.Team, unit.LastAttackerId));
                Rewards.StructureDestroyed(context, unit);
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
