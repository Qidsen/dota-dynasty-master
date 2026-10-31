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
            int wave = state.WavesSpawned + 1;
            long timeMs = now * 1000 / state.Rate.TicksPerSecond;
            foreach (LaneDefinition lane in state.Ruleset.Map.Lanes)
            {
                foreach (Team team in new[] { Team.Dawn, Team.Dusk })
                {
                    bool mega = waves.MegaUpgrades.Count > 0 && AllEnemyBarracksDestroyed(state, team);
                    foreach ((string creepTypeId, int count) in Composition(waves.CompositionFor(team), wave, timeMs))
                    {
                        string typeId = creepTypeId;
                        string? megaId = mega ? waves.MegaUpgrades.FirstOrDefault(u => u.FromCreepTypeId == typeId)?.ToCreepTypeId : null;
                        if (megaId != null)
                            typeId = megaId;
                        else if (EnemyBarracksDestroyed(state, team, lane.Id, typeId))
                            typeId = waves.SuperUpgrades.FirstOrDefault(u => u.FromCreepTypeId == typeId)?.ToCreepTypeId ?? typeId;
                        CreepType type = state.Ruleset.FindCreepType(typeId)!;
                        for (int i = 0; i < count; i++)
                        {
                            MatchUnit creep = state.SpawnCreep(context.Ids.Allocate(), team, type, lane, now);
                            Upgrade(creep, waves.Upgrades, timeMs);
                            context.Emit(new CreepSpawned(creep.Id, team, type.Id, lane.Id));
                        }
                    }
                }
            }

            state.WavesSpawned = wave;
            state.NextWaveTick += state.Rate.TicksFor(waves.IntervalMs);
        }

        private static readonly Dictionary<(string, int), ModifierSpec> UpgradeSpecs = new Dictionary<(string, int), ModifierSpec>();

        /// <summary>Dota's creep upgrades: the steps of the intervals passed so far (health and damage up to the limit).</summary>
        internal static void Upgrade(MatchUnit creep, CreepUpgradeRules? upgrades, long timeMs)
        {
            CreepUpgradeStep? step = upgrades?.StepFor(creep.TypeId);
            if (step == null)
                return;
            int cycles = (int)(timeMs / upgrades!.IntervalMs);
            if (cycles <= 0)
                return;
            int statCycles = System.Math.Min(cycles, upgrades.MaxStatCycles);
            if (statCycles > 0 && (step.Health != 0 || step.Damage != 0))
            {
                ModifierSpec spec;
                lock (UpgradeSpecs)
                {
                    if (!UpgradeSpecs.TryGetValue((creep.TypeId + "/" + step.Health + "/" + step.Damage, statCycles), out spec!))
                        UpgradeSpecs[(creep.TypeId + "/" + step.Health + "/" + step.Damage, statCycles)] = spec = new ModifierSpec("creep_upgrade", -1,
                            maxHpBonus: step.Health * statCycles, damageBonus: step.Damage * statCycles, dispellable: false);
                }

                creep.AddModifier(new ActiveModifier(spec, creep.Id, 0, -1, 0));
                creep.Hp = Fixed.FromInt(creep.MaxHp);
            }

            creep.BountyGoldBonus = Fixed.FromRatio((long)step.GoldHundredths * cycles, 100);
            creep.BountyXpBonus = step.Xp * cycles;

            // Dota's flagbearer: its aura's magic resistance grows with the cycles.
            int resist = step.AuraMagicResistPerCycle * System.Math.Min(cycles, step.AuraMaxCycles);
            if (resist > 0)
            {
                ModifierSpec aura;
                lock (UpgradeSpecs)
                {
                    if (!UpgradeSpecs.TryGetValue((creep.TypeId + "/aura/" + step.AuraRadius, resist), out aura!))
                        UpgradeSpecs[(creep.TypeId + "/aura/" + step.AuraRadius, resist)] = aura = new ModifierSpec(creep.TypeId + "_aura_magic_resist", -1,
                            dispellable: false, aura: new AuraSpec(step.AuraRadius, TargetTeam.Ally, heroesOnly: false,
                                new ModifierSpec(creep.TypeId + "_aura_magic_resist_bonus", 500, magicResistBonus: resist, unique: true), creepsOnly: true));
                }

                creep.AddModifier(new ActiveModifier(aura, creep.Id, 0, -1, 0));
            }
        }

        /// <summary>
        /// The creeps of this wave by type, in the order the entries first name them: the entries the wave has, summed per type,
        /// less the creeps replaced by others (a flagbearer instead of a melee creep).
        /// </summary>
        private static List<(string CreepTypeId, int Count)> Composition(IReadOnlyList<WaveEntry> entries, int wave, long timeMs)
        {
            var counts = new List<(string CreepTypeId, int Count)>();
            void Add(string typeId, int count)
            {
                int index = counts.FindIndex(c => c.CreepTypeId == typeId);
                if (index < 0)
                    counts.Add((typeId, count));
                else
                    counts[index] = (typeId, counts[index].Count + count);
            }

            foreach (WaveEntry entry in entries)
            {
                if (!entry.In(wave, timeMs))
                    continue;
                Add(entry.CreepTypeId, entry.Count);
                if (entry.ReplacesCreepTypeId != null)
                    Add(entry.ReplacesCreepTypeId, -entry.Count);
            }

            return counts.Where(c => c.Count > 0).ToList();
        }

        /// <summary>
        /// Whether an opponent's barracks for this lane that upgrades this creep type has fallen (the team's creeps of the type
        /// there become super creeps).
        /// </summary>
        private static bool EnemyBarracksDestroyed(MatchState state, Team team, string laneId, string creepTypeId) =>
            state.Ruleset.Map.Structures.Any(s => s.Team != team && s.BarracksLane == laneId && state.DestroyedStructures.Contains(s.Id) &&
                state.Ruleset.FindStructureType(s.StructureTypeId)?.UpgradesCreepTypeIds is var upgrades && (upgrades == null || upgrades.Contains(creepTypeId)));

        /// <summary>Whether every barracks of the opponent has fallen (Dota's mega creeps).</summary>
        private static bool AllEnemyBarracksDestroyed(MatchState state, Team team) =>
            state.Ruleset.Map.Structures.Where(s => s.Team != team && s.BarracksLane != null).All(s => state.DestroyedStructures.Contains(s.Id)) &&
            state.Ruleset.Map.Structures.Any(s => s.Team != team && s.BarracksLane != null);
    }

    /// <summary>
    /// Periodic damage/heal of modifiers, then expiry. Runs before targeting so expired
    /// stuns and slows no longer affect this tick's actions.
    /// </summary>
    /// <summary>
    /// Auras: every few ticks each unit's aura-carrying modifiers give their aura modifier to the matching units
    /// in range (refreshing it; it fades after its own duration once out of range). Depends only on positions,
    /// so the order units are processed in does not matter.
    /// </summary>
    internal sealed class AuraSystem : ISimulationSystem<MatchState>
    {
        public const int IntervalTicks = 10;

        public string Name => "auras";

        public void Tick(SimulationContext<MatchState> context)
        {
            if (context.Tick.Value % IntervalTicks != 0)
                return;
            MatchState state = context.State;
            IReadOnlyList<MatchUnit> units = state.Units;
            for (int i = 0, count = units.Count; i < count; i++)
            {
                MatchUnit bearer = units[i];
                if (!bearer.IsAlive || bearer.Modifiers.Count == 0)
                    continue;
                foreach (ActiveModifier modifier in bearer.Modifiers.Where(m => m.Spec.Aura != null).ToList())
                {
                    AuraSpec aura = modifier.Spec.Aura!;
                    for (int j = 0; j < count; j++)
                    {
                        MatchUnit unit = units[j];
                        if ((aura.HeroesOnly && unit.Kind != UnitKind.Hero) || (aura.CreepsOnly && unit.Kind != UnitKind.Creep) || !Effects.Matches(state, bearer, unit, aura.Team) ||
                            !Geometry.WithinRange(unit.Position, bearer.Position, aura.Radius))
                            continue;
                        Modifiers.Apply(context, unit, aura.Modifier, bearer.Id);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Dota's Backdoor Protection: an enemy lane creep within a building's radius turns its protection off until the
    /// reactivation time after the last one leaves; while on, it heals the enemy damage taken. Depends only on positions.
    /// </summary>
    internal sealed class BackdoorSystem : ISimulationSystem<MatchState>
    {
        public string Name => "backdoor";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            IReadOnlyList<MatchUnit> units = state.Units;
            long now = context.Tick.Value;
            for (int i = 0, count = units.Count; i < count; i++)
            {
                MatchUnit building = units[i];
                BackdoorProtection? backdoor = building.Backdoor;
                if (backdoor == null || !building.IsAlive)
                    continue;
                FixedVector2 center = building.Position;
                if (backdoor.AroundAncient)
                {
                    MatchUnit? ancient = units.FirstOrDefault(u => u.Kind == UnitKind.Structure && u.Team == building.Team && u.Placement?.IsAncient == true);
                    if (ancient != null)
                        center = ancient.Position;
                }

                Team enemy = building.Team.Opponent();
                for (int j = 0; j < count; j++)
                {
                    MatchUnit creep = units[j];
                    if (creep.Kind == UnitKind.Creep && creep.Team == enemy && creep.IsAlive && Geometry.WithinRange(creep.Position, center, backdoor.Radius))
                    {
                        building.BackdoorOffUntilTick = now + context.TickRate.TicksFor(backdoor.ReactivationMs);
                        break;
                    }
                }

                building.BackdoorActive = now >= building.BackdoorOffUntilTick;
                if (!building.BackdoorActive)
                {
                    building.BackdoorHealLeft = Fixed.Zero;
                    continue;
                }

                if (building.BackdoorHealLeft <= Fixed.Zero)
                    continue;
                Fixed heal = Fixed.Min(building.BackdoorHealLeft, Fixed.FromInt(backdoor.HealPerSecond) / Fixed.FromInt(context.TickRate.TicksPerSecond));
                building.BackdoorHealLeft -= heal;
                Damage.Heal(context, building.Id, building, heal);
            }
        }
    }

    /// <summary>
    /// Dota's Glyph of Fortification: a team's request, once off cooldown, gives its buildings and lane creeps a modifier that
    /// takes away all damage (towers with <see cref="StructureType.GlyphExtraTargets"/> also strike more enemies per attack).
    /// </summary>
    internal sealed class GlyphSystem : ISimulationSystem<MatchState>
    {
        private readonly Dictionary<int, ModifierSpec> _buildingSpecs = new Dictionary<int, ModifierSpec>();
        private ModifierSpec? _creepSpec;

        public string Name => "glyph";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            GlyphRules? glyph = state.Ruleset.Combat.Glyph;
            if (glyph == null)
                return;
            long now = context.Tick.Value;
            if (now == 0)
            {
                foreach (Team team in new[] { Team.Dawn, Team.Dusk })
                    state.SetGlyphReadyTick(team, context.TickRate.TicksFor(glyph.InitialCooldownMs));
            }

            foreach (Team team in new[] { Team.Dawn, Team.Dusk })
            {
                if (!state.TakeGlyphRequest(team) || now < state.GlyphReadyTick(team))
                    continue;
                state.SetGlyphReadyTick(team, now + context.TickRate.TicksFor(glyph.CooldownMs));
                _creepSpec ??= new ModifierSpec("glyph", glyph.CreepDurationMs, incomingDamagePercent: -100);
                foreach (MatchUnit unit in state.Units.Where(u => u.Team == team && u.IsAlive).ToList())
                {
                    if (unit.Kind == UnitKind.Structure)
                        Modifiers.Apply(context, unit, BuildingSpec(glyph, state.Ruleset.FindStructureType(unit.TypeId)?.GlyphExtraTargets ?? 0), unit.Id);
                    else if (unit.Kind == UnitKind.Creep && unit.LaneId != null)
                        Modifiers.Apply(context, unit, _creepSpec, unit.Id);
                }

                context.Emit(new GlyphUsed(team));
            }
        }

        private ModifierSpec BuildingSpec(GlyphRules glyph, int extraTargets)
        {
            if (!_buildingSpecs.TryGetValue(extraTargets, out ModifierSpec? spec))
                _buildingSpecs[extraTargets] = spec = new ModifierSpec("glyph", glyph.BuildingDurationMs, incomingDamagePercent: -100,
                    extraTargets: extraTargets, extraTargetRadiusFromAttackRange: extraTargets > 0, extraTargetDamagePercent: 100);
            return spec;
        }

        /// <summary>Losing the team's first building of a resetting type sets its glyph's cooldown to the reset delay.</summary>
        internal static void StructureLost(SimulationContext<MatchState> context, MatchUnit structure)
        {
            GlyphRules? glyph = context.State.Ruleset.Combat.Glyph;
            if (glyph == null || context.State.Ruleset.FindStructureType(structure.TypeId)?.ResetsGlyphOnFirstLoss != true ||
                !context.State.FirstLossOf(structure.Team, structure.TypeId))
                return;
            long reset = context.Tick.Value + context.TickRate.TicksFor(glyph.ResetDelayMs);
            if (context.State.GlyphReadyTick(structure.Team) > reset)
                context.State.SetGlyphReadyTick(structure.Team, reset);
        }
    }

    /// <summary>Pulses ground zones in creation order and removes expired ones; a zone whose source is gone ends.</summary>
    internal sealed class ZoneSystem : ISimulationSystem<MatchState>
    {
        public string Name => "zones";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            if (state.Zones.Count == 0)
                return;
            long now = context.Tick.Value;
            foreach (GroundZone zone in state.Zones.ToList())
            {
                MatchUnit? source = state.Find(zone.Source);
                if (source != null && now >= zone.NextPulseTick && zone.NextPulseTick <= zone.ExpiresTick)
                {
                    Effects.Pulse(context, source, zone);
                    zone.NextPulseTick += zone.Definition.PeriodMs > 0 ? context.TickRate.TicksFor(zone.Definition.PeriodMs) : AuraSystem.IntervalTicks;
                }

                if (source == null || now >= zone.ExpiresTick)
                    state.RemoveZone(zone);
            }
        }
    }

    internal sealed class ModifierSystem : ISimulationSystem<MatchState>
    {
        private readonly List<ActiveModifier> _scratch = new List<ActiveModifier>();

        public string Name => "modifiers";

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            // By index over the units there at the start: periodic effects may summon (Tombstone's zombies).
            UnitView units = context.State.Units;
            int count = units.Count;
            for (int i = 0; i < count; i++)
            {
                MatchUnit unit = units[i];
                if (unit.Modifiers.Count == 0)
                    continue;

                if (unit.HasHealthScaledBonuses)
                    unit.Recalculate();   // Berserker's Blood follows the health
                ModifierFading.FadeWhenIdle(context, unit, now);
                ModifierFading.BreakLinks(context, unit);
                _scratch.Clear();
                _scratch.AddRange(unit.Modifiers);
                foreach (ActiveModifier modifier in _scratch)
                {
                    ModifierSpec spec = modifier.Spec;
                    if (spec.IsPeriodic && now >= modifier.NextPeriodTick)
                    {
                        Fixed periodFraction = Fixed.FromRatio(spec.PeriodMs, 1000) * Fixed.FromInt(modifier.Stacks);
                        if (spec.ManaCostPerSecond > Fixed.Zero && unit.Hero != null)
                        {
                            // A toggle's upkeep: paid every period; off when the bearer cannot pay.
                            Fixed upkeep = spec.ManaCostPerSecond * periodFraction;
                            if (unit.Hero.Mana < upkeep)
                            {
                                Modifiers.Remove(context, unit, modifier, "out_of_mana");
                                continue;
                            }

                            unit.Hero.Mana -= upkeep;
                        }

                        if (spec.PeriodicEffects.Count > 0 && spec.PeriodicFromBearer)
                            Effects.RunSpell(context, unit, spec.PeriodicEffects, 1, unit, unit.Position);
                        else if (spec.PeriodicEffects.Count > 0 && context.State.Find(modifier.Source) is MatchUnit linker && linker.IsAlive)
                            Effects.RunSpell(context, linker, spec.PeriodicEffects, 1, unit, unit.Position);

                        if (spec.AttributeShift != 0 && unit.Hero != null && unit.Hero.CanShiftAttribute(spec.AttributeShift))
                        {
                            unit.Hero.AttributeShift += spec.AttributeShift;
                            unit.Recalculate();
                        }
                        if (spec.DamagePerSecond > Fixed.Zero)
                            Damage.Deal(context, modifier.Source, unit, spec.DamagePerSecond * periodFraction, spec.DamageType);
                        if (spec.DamagePerSecondMaxHealthPercent > Fixed.Zero)
                            Damage.Deal(context, modifier.Source, unit, Fixed.FromInt(unit.MaxHp) * spec.DamagePerSecondMaxHealthPercent / Fixed.FromInt(100) * periodFraction, spec.DamageType);
                        if (spec.HealPerSecond > Fixed.Zero)
                            Damage.Heal(context, modifier.Source, unit, spec.HealPerSecond * periodFraction);
                        // Dota's Regeneration rune: lower for a while after damage from any player's unit or the boss.
                        bool damaged = spec.DamagedRegenMs > 0 && unit.LastPlayerDamageTick >= 0 &&
                                       now - unit.LastPlayerDamageTick < context.TickRate.TicksFor(spec.DamagedRegenMs);
                        Fixed healPercent = damaged ? spec.DamagedRegenMaxPercent : spec.HealPerSecondMaxHealthPercent;
                        Fixed manaPercent = damaged ? spec.DamagedRegenMaxPercent : spec.ManaPerSecondMaxManaPercent;
                        if (healPercent > Fixed.Zero)
                            Damage.Heal(context, modifier.Source, unit, Fixed.FromInt(unit.MaxHp) * healPercent / Fixed.FromInt(100) * periodFraction);
                        if (manaPercent > Fixed.Zero && unit.Hero != null)
                            unit.Hero.Mana = Fixed.Min(Fixed.FromInt(unit.Hero.MaxMana),
                                unit.Hero.Mana + Fixed.FromInt(unit.Hero.MaxMana) * manaPercent / Fixed.FromInt(100) * periodFraction);
                        modifier.NextPeriodTick += context.TickRate.TicksFor(spec.PeriodMs);
                    }

                    if (spec.EndsWhenFull && unit.Hp >= Fixed.FromInt(unit.MaxHp) && (unit.Hero == null || unit.Hero.Mana >= Fixed.FromInt(unit.Hero.MaxMana)))
                    {
                        Modifiers.Remove(context, unit, modifier, "full");
                        continue;
                    }

                    if (modifier.ExpiresTick >= 0 && now >= modifier.ExpiresTick)
                    {
                        Modifiers.Remove(context, unit, modifier, "expired");
                        if (spec.ExpireModifier != null && unit.IsAlive)
                            Modifiers.Apply(context, unit, spec.ExpireModifier, modifier.Source);
                    }
                    else if (spec.BreakRadius > 0 && EnemyBreakerNear(context.State, unit, spec.BreakRadius))
                        Modifiers.Remove(context, unit, modifier, "broken");
                }
            }
        }

        /// <summary>An enemy hero or attacking structure within the radius (positions from the end of the previous tick).</summary>
        private static bool EnemyBreakerNear(MatchState state, MatchUnit unit, int radius)
        {
            foreach (MatchUnit other in state.Units)
            {
                if (other.Team == unit.Team || other.Team == Team.Neutral || !other.IsAlive)
                    continue;
                bool breaker = other.Hero != null || (other.Kind == UnitKind.Structure && other.CanAttackAtAll);
                if (breaker && Geometry.WithinRange(other.Position, unit.Position, radius))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Records every unit's position and status flags. Runs after the tick-start updates
    /// (modifier expiry, respawns, shopping) and before anything acts.
    /// </summary>
    internal sealed class TickSnapshotSystem : ISimulationSystem<MatchState>
    {
        public string Name => "tick-snapshot";

        /// <summary>How far back heroes' positions are kept: Time Lapse's five seconds and a margin.</summary>
        private const int HistoryMs = 6000;

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            long kept = context.TickRate.TicksFor(HistoryMs);
            foreach (MatchUnit unit in context.State.Units)
            {
                unit.StartPosition = unit.Position;
                unit.StartFlags = unit.Flags;
                if (unit.Hero == null || unit.IllusionOf.IsValid)
                    continue;
                // Where heroes were over the last seconds (Time Lapse, Glimpse).
                unit.History.Enqueue(new PastState(now, unit.Position, unit.Hp, unit.Hero.Mana));
                while (unit.History.Count > 0 && unit.History.Peek().Tick < now - kept)
                    unit.History.Dequeue();
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
            DotaAggroRules? dota = state.Ruleset.Combat.DotaAggro;
            if (dota != null)
                DotaAggro.Tick(context, dota);
            foreach (MatchUnit unit in state.Units)
            {
                // Heroes get targets from orders, neutrals from the neutral system; with Dota's aggro, lane creeps and buildings too.
                if (!unit.CanAttackAtAll || !unit.IsAlive || unit.Kind == UnitKind.Hero || unit.Team == Team.Neutral)
                    continue;
                if (dota != null && ((unit.Kind == UnitKind.Creep && unit.Lane != null) || unit.Kind == UnitKind.Structure))
                    continue;

                if (unit.LockedTarget.IsValid)
                {
                    // Tombstone's zombies: only their target; it dead or out of sight, they die (the official note).
                    MatchUnit? locked = state.Find(unit.LockedTarget);
                    if (locked == null || !locked.IsAlive || !state.IsVisibleTo(unit.Team, locked))
                    {
                        unit.TargetId = EntityId.None;
                        unit.ExpiresTick = context.Tick.Value;
                    }
                    else
                    {
                        unit.TargetId = locked.Id;
                    }

                    continue;
                }

                int searchRange = unit.Kind == UnitKind.Structure ? unit.AttackRange : unit.AcquisitionRange;
                MatchUnit? current = state.Find(unit.TargetId);
                bool keep = current != null && current.Team != Team.Neutral && state.CanAttack(unit, current) && state.IsVisibleTo(unit.Team, current) &&
                            Geometry.WithinEdgeRange(unit, current, searchRange);
                if (state.Ruleset.Combat.HeroAggro)
                {
                    unit.TargetId = PickByAggro(state, unit, searchRange, keep ? current : null);
                    continue;
                }

                if (keep)
                    continue;

                unit.TargetId = FindNearest(state, unit, searchRange);
            }
        }

        /// <summary>
        /// Lane aggro (<see cref="CombatRules.HeroAggro"/>): an enemy hero attacking one of the unit's heroes
        /// first, then other enemy units, enemy heroes last; nearest within a class (ties by lower ID).
        /// The current target is kept unless a better class is in range. Reads only what the previous
        /// tick left (hero attack targets), so the order units are processed in does not matter.
        /// </summary>
        private static EntityId PickByAggro(MatchState state, MatchUnit unit, int range, MatchUnit? current)
        {
            int keepClass = current != null ? AggroClass(state, unit, current) : int.MaxValue;
            EntityId best = EntityId.None;
            int bestClass = keepClass;
            Fixed bestDistance = Fixed.Zero;
            foreach (MatchUnit candidate in state.Units)
            {
                if (!Geometry.WithinEdgeRange(unit, candidate, range) || candidate.Team == Team.Neutral || !state.CanAttack(unit, candidate) ||
                    !state.IsVisibleTo(unit.Team, candidate))
                    continue;

                int candidateClass = AggroClass(state, unit, candidate);
                if (candidateClass > bestClass || (candidateClass == bestClass && !best.IsValid))
                    continue;   // worse, or no better than the kept target
                Fixed distance = Geometry.EdgeDistance(unit, candidate);
                if (candidateClass == bestClass && distance >= bestDistance)
                    continue;
                best = candidate.Id;
                bestClass = candidateClass;
                bestDistance = distance;
            }

            return best.IsValid ? best : current?.Id ?? EntityId.None;
        }

        /// <summary>0: an enemy hero attacking one of the unit's heroes; 1: any other non-hero; 2: an enemy hero.</summary>
        private static int AggroClass(MatchState state, MatchUnit unit, MatchUnit candidate)
        {
            if (candidate.Hero == null)
                return 1;
            MatchUnit? victim = state.Find(candidate.TargetId);
            return victim != null && victim.Hero != null && victim.Team == unit.Team ? 0 : 2;
        }

        internal static EntityId FindNearest(MatchState state, MatchUnit unit, int range)
        {
            EntityId best = EntityId.None;
            Fixed bestDistance = Fixed.Zero;
            foreach (MatchUnit candidate in state.Units)
            {
                // Creeps, towers and summons leave neutral objectives alone. Cheapest test first.
                if (!Geometry.WithinEdgeRange(unit, candidate, range) || candidate.Team == Team.Neutral || !state.CanAttack(unit, candidate) ||
                    !state.IsVisibleTo(unit.Team, candidate))
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
                if ((unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon) || unit.Team == Team.Neutral || !unit.CanMove)
                    continue;

                MatchUnit? target = state.Find(unit.TargetId);
                if (target == null || !state.CanAttack(unit, target))
                    target = unit.Lane != null && unit.NextWaypoint >= unit.WaypointCount ? NearestVulnerableStructure(state, unit) : null;

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
            int count = unit.WaypointCount;
            while (unit.NextWaypoint < count && unit.Position == unit.WaypointAt(unit.NextWaypoint))
                unit.NextWaypoint++;
            if (unit.NextWaypoint >= count)
                return false;

            unit.Position = FixedVector2.MoveTowards(unit.Position, unit.WaypointAt(unit.NextWaypoint), unit.StepPerTick);
            // Count the waypoint as reached now, before separation may nudge the unit off it.
            if (unit.Position == unit.WaypointAt(unit.NextWaypoint))
                unit.NextWaypoint++;
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

    /// <summary>Riki's Backstab (7.41): invisible after a while without starting an attack.</summary>
    internal static class ModifierFading
    {
        /// <summary>Links (Static Link, Tether) end when the bearer and the caster are too far apart, or the caster is gone.</summary>
        internal static void BreakLinks(SimulationContext<MatchState> context, MatchUnit unit)
        {
            foreach (ActiveModifier link in unit.Modifiers.Where(m => m.Spec.BreakDistance > 0).ToList())
            {
                MatchUnit? caster = context.State.Find(link.Source);
                if (caster == null || !caster.IsAlive || !Geometry.WithinRange(unit.Position, caster.Position, link.Spec.BreakDistance))
                    Modifiers.Remove(context, unit, link, "link_broken");
            }
        }

        internal static void FadeWhenIdle(SimulationContext<MatchState> context, MatchUnit unit, long now)
        {
            foreach (ActiveModifier fade in unit.Modifiers.Where(m => m.Spec.InvisibleAfterIdleMs > 0).ToList())
            {
                string key = fade.Spec.Key + "_invisible";
                if (unit.Modifiers.Any(m => m.Spec.Key == key))
                    continue;
                long idleSince = System.Math.Max(fade.AppliedTick, unit.AttackStartTick);
                if (now - idleSince >= context.TickRate.TicksFor(fade.Spec.InvisibleAfterIdleMs))
                    Modifiers.Apply(context, unit, new ModifierSpec(key, -1, ModifierFlags.Invisible, breakOnAction: true, dispellable: false), unit.Id);
            }
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
            // On-hit effects may create units (Juxtapose's illusions); they are appended and attack from the next tick.
            IReadOnlyList<MatchUnit> units = state.Units;
            for (int i = 0, count = units.Count; i < count; i++)
            {
                MatchUnit unit = units[i];
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
            if (target == null || !context.State.CanAttack(unit, target) || !context.State.IsVisibleTo(unit.Team, target) ||
                !Geometry.WithinEdgeRange(unit, target, unit.AttackRange))
                return;

            unit.PendingTargetId = target.Id;
            if (target.Position != unit.Position)
                unit.Facing = target.Position - unit.Position;
            unit.AttackStartTick = now;
            unit.AttackStartTarget = target.Id;
            unit.PendingAttackTick = now + unit.AttackPointTicks;
            unit.NextAttackTick = now + unit.AttackIntervalTicks;
            unit.LastActiveTick = now;
            Modifiers.BreakOnAction(context, unit);
            context.Emit(new AttackStarted(unit.Id, target.Id));
        }

        private static void Land(SimulationContext<MatchState> context, MatchUnit unit)
        {
            unit.PendingAttackTick = -1;
            MatchUnit? target = context.State.Find(unit.PendingTargetId);
            if (target == null || !context.State.CanAttack(unit, target))
                return;

            Fixed damage = RollDamage(context, unit, target, out bool critical, out int critLifesteal);
            HitExtraTargets(context, unit, target);
            if (unit.Stats.ProjectileSpeed > 0)
            {
                // Ranged: the rolled hit travels; it lands (or is dodged) in the projectile system.
                Projectile projectile = context.State.LaunchProjectile(unit, target, unit.Stats.ProjectileSpeed);
                projectile.Damage = damage;
                projectile.IsCritical = critical;
                projectile.CritLifestealPercent = critLifesteal;
                context.Emit(new ProjectileLaunched(projectile.Id, unit.Id, target.Id, isAbility: false));
                return;
            }

            ApplyHit(context, unit, target, damage, critical, critLifestealPercent: critLifesteal);
        }

        /// <summary>Split attacks: the other enemies nearest to the attacker (then by id) take an instant share of the attack.</summary>
        private static void HitExtraTargets(SimulationContext<MatchState> context, MatchUnit unit, MatchUnit target)
        {
            if (unit.Modifiers.Count == 0)
                return;
            MatchState state = context.State;
            foreach (ActiveModifier split in unit.Modifiers.Where(m => m.Spec.ExtraTargets > 0).ToList())
            {
                ModifierSpec spec = split.Spec;
                if (!Rolls(context, spec.ExtraTargetChancePercent * 100))
                    continue;
                int radius = spec.ExtraTargetRadius + (spec.ExtraTargetRadiusFromAttackRange ? unit.AttackRange : 0);
                List<MatchUnit> others = state.Units
                    .Where(u => u != target && Effects.Matches(state, unit, u, TargetTeam.Enemy) && state.CanAttack(unit, u) && state.IsVisibleTo(unit.Team, u) &&
                                Geometry.WithinRange(unit.Position, u.Position, radius + u.BoundingRadius))
                    .OrderBy(u => FixedVector2.Distance(unit.Position, u.Position)).ThenBy(u => u.Id.Value)
                    .Take(spec.ExtraTargets).ToList();
                foreach (MatchUnit other in others)
                    Strike(context, unit, other, spec.ExtraTargetDamagePercent, spendsCharges: false);
            }
        }

        /// <summary>An instant attack made by an ability, with a percent of the attack's damage; it lands at once, even for ranged units.</summary>
        internal static void Strike(SimulationContext<MatchState> context, MatchUnit unit, MatchUnit target, int damagePercent, bool spendsCharges = true)
        {
            Fixed damage = RollDamage(context, unit, target, out bool critical, out int critLifesteal);
            ApplyHit(context, unit, target, damage * Fixed.FromInt(damagePercent) / Fixed.FromInt(100), critical, spendsCharges, critLifesteal);
        }

        /// <summary>One attack's damage roll: critical strike, attack damage percent, attack and armor classes, structures.</summary>
        private static Fixed RollDamage(SimulationContext<MatchState> context, MatchUnit unit, MatchUnit target, out bool critical, out int critLifesteal)
        {
            critLifesteal = 0;
            int roll = context.Random("combat").NextInt(unit.AttackDamageMin, unit.AttackDamageMax + 1);
            Fixed damage = Fixed.FromInt(roll);

            critical = false;
            if (unit.Combat?.DotaStacking == true)
            {
                // Dota: from the strongest multiplier down, each pseudo-random; the first that procs counts. Not against buildings
                // (and such attacks do not count as tries).
                if (target.Kind != UnitKind.Structure)
                {
                    long now = context.Tick.Value;
                    foreach (ActiveModifier crit in unit.CritModifiers)
                    {
                        ModifierSpec spec = crit.Spec;
                        if (spec.ProcCooldownMs > 0 && now < crit.NextProcTick)
                            continue;   // Mortal Strike: on its cooldown
                        if (!PseudoRandom.Roll(context, spec.CritChanceHundredths, ref crit.PrdTries))
                            continue;
                        critical = true;
                        // Chaos Strike: a multiplier drawn between its bounds, and lifesteal on the attack.
                        int multiplier = spec.CritMultiplierMaxPercent > spec.CritMultiplierPercent
                            ? context.Random("combat").NextInt(spec.CritMultiplierPercent, spec.CritMultiplierMaxPercent + 1)
                            : spec.CritMultiplierPercent;
                        damage = damage * Fixed.FromInt(multiplier) / Fixed.FromInt(100);
                        critLifesteal = spec.CritLifestealPercent;
                        if (spec.ProcCooldownMs > 0)
                            crit.NextProcTick = now + context.TickRate.TicksFor(spec.ProcCooldownMs);
                        break;
                    }
                }
            }
            else
            {
                critical = unit.CritChanceHundredths > 0 && Draw(context, unit.CritChanceHundredths);
                if (critical)
                    damage = damage * Fixed.FromInt(unit.CritMultiplierPercent) / Fixed.FromInt(100);
            }
            if (unit.AttackDamageHundredths != 0)
                damage = damage * Fixed.FromInt(10000 + unit.AttackDamageHundredths) / Fixed.FromInt(10000);
            CombatRules combat = context.State.Ruleset.Combat;
            int classPercent = target.Kind == UnitKind.Structure && unit.IllusionOf.IsValid && combat.IllusionBuildingPercent > 0 ? combat.IllusionBuildingPercent
                : target.Kind == UnitKind.Structure && unit.Kind == UnitKind.Summon && !unit.IllusionOf.IsValid && combat.SummonBuildingPercent > 0 ? combat.SummonBuildingPercent
                : combat.AttackClasses.Percent(AttackClassOf(unit), DefenseClassOf(target));
            if (classPercent != 100)
                damage = damage * Fixed.FromInt(classPercent) / Fixed.FromInt(100);
            if (target.Kind == UnitKind.Structure && unit.StructureDamagePercent != 0)
                damage = damage * Fixed.FromInt(100 + unit.StructureDamagePercent) / Fixed.FromInt(100);
            return damage;
        }

        private static AttackClass AttackClassOf(MatchUnit unit) =>
            unit.Kind == UnitKind.Hero ? AttackClass.Hero : unit.Kind == UnitKind.Structure ? AttackClass.Reinforced : unit.Stats.AttackClass;

        private static DefenseClass DefenseClassOf(MatchUnit unit) =>
            unit.Kind == UnitKind.Hero || unit.IllusionOf.IsValid ? DefenseClass.Hero
            : unit.Kind == UnitKind.Structure || unit.Stats.AttackClass == AttackClass.Reinforced ? DefenseClass.Reinforced
            : DefenseClass.Unit;

        internal static void ApplyHit(SimulationContext<MatchState> context, MatchUnit attacker, MatchUnit target, Fixed damage, bool critical, bool spendsCharges = true,
            int critLifestealPercent = 0)
        {
            // Charged buffs count the attacks that land while they are on: not the hit that grants one, not the extra targets' hits.
            List<ActiveModifier>? charged = spendsCharges ? attacker.Modifiers.Where(m => m.Spec.AttackCharges > 0).ToList() : null;
            // Draw only when there is a chance to roll, so matches without evasion or chance procs keep their random streams.
            if (target.EvasionHundredths > 0 &&
                (context.State.Ruleset.Combat.DotaStacking ? PseudoRandom.Roll(context, target.EvasionHundredths, ref target.EvasionTries) : Draw(context, target.EvasionHundredths)))
                return;
            int block = 0;
            foreach (ActiveModifier blocking in target.Modifiers)
            {
                // Dota's damage block (Kraken Shell): before armor, the strongest one; less against creeps.
                int amount = attacker.Kind == UnitKind.Creep ? blocking.Spec.DamageBlock * blocking.Spec.DamageBlockCreepPercent / 100 : blocking.Spec.DamageBlock;
                block = System.Math.Max(block, amount);
            }
            if (block > 0)
                damage = Fixed.Max(Fixed.Zero, damage - Fixed.FromInt(block));
            Fixed dealt = Damage.Deal(context, attacker.Id, target, damage, DamageType.Physical, critical);
            if (attacker.LifestealPercent + critLifestealPercent > 0 && attacker.IsAlive)
                Damage.Heal(context, attacker.Id, attacker, dealt * Fixed.FromInt(attacker.LifestealPercent + critLifestealPercent) / Fixed.FromInt(100));

            // Cleave: melee attacks splash a percent of their damage onto the other enemies near the target.
            if (attacker.Stats.ProjectileSpeed == 0)
            {
                foreach (ActiveModifier cleave in attacker.Modifiers.Where(m => m.Spec.CleavePercent > 0).ToList())
                {
                    Fixed splash = damage * Fixed.FromInt(cleave.Spec.CleavePercent) / Fixed.FromInt(100);
                    foreach (MatchUnit other in context.State.Units.Where(u => u != target && Effects.Matches(context.State, attacker, u, TargetTeam.Enemy) &&
                                                                                Geometry.WithinRange(u.Position, target.Position, cleave.Spec.CleaveRadius)).ToList())
                        Damage.Deal(context, attacker.Id, other, splash, DamageType.Physical);
                }
            }

            // Splash: any attack, ranged too (Dota's fountain), hits the other enemies near the target for a percent of its damage.
            foreach (ActiveModifier splash in attacker.Modifiers.Where(m => m.Spec.SplashPercent > 0).ToList())
            {
                Fixed share = damage * Fixed.FromInt(splash.Spec.SplashPercent) / Fixed.FromInt(100);
                foreach (MatchUnit other in context.State.Units.Where(u => u != target && Effects.Matches(context.State, attacker, u, TargetTeam.Enemy) &&
                                                                            Geometry.WithinRange(u.Position, target.Position, splash.Spec.SplashRadius)).ToList())
                    Damage.Deal(context, attacker.Id, other, share, DamageType.Physical);
            }

            // Fervor: stacks gathered on one target are dropped when the attacks turn to another.
            foreach (ActiveModifier tracker in attacker.Modifiers.Where(m => m.Spec.ResetStacksKey != null).ToList())
            {
                if (tracker.LastTarget.IsValid && tracker.LastTarget != target.Id)
                {
                    foreach (ActiveModifier stacks in attacker.Modifiers.Where(m => m.Spec.Key == tracker.Spec.ResetStacksKey).ToList())
                        Modifiers.Remove(context, attacker, stacks, "new_target");
                }

                tracker.LastTarget = target.Id;
            }

            // On-hit effects (bash, bonus magic damage, slows) of the attacker's modifiers, in their order on the unit.
            foreach (ActiveModifier modifier in attacker.Modifiers.Where(m => m.Spec.OnHit.Count > 0).ToList())
            {
                if ((modifier.Spec.OnHitHeroesOnly && target.Hero == null) || modifier.Procing)
                    continue;
                if (context.Tick.Value < modifier.NextProcTick)
                    continue;
                if (modifier.Spec.OnHitAfterAttacks > 0 && modifier.AttackCount++ < modifier.Spec.OnHitAfterAttacks)
                    continue;
                modifier.AttackCount = 0;
                // Impetus and Arcane Orb: the proc costs mana (a share of the current mana for the orb); without it there is none.
                Fixed manaCost = Fixed.FromInt(modifier.Spec.OnHitManaCost);
                if (modifier.Spec.OnHitManaCostPercent > 0 && attacker.Hero != null)
                    manaCost += attacker.Hero.Mana * Fixed.FromInt(modifier.Spec.OnHitManaCostPercent) / Fixed.FromInt(100);
                if (manaCost > Fixed.Zero && (attacker.Hero == null || attacker.Hero.Mana < manaCost))
                    continue;
                if (modifier.Spec.PseudoRandom ? !PseudoRandom.Roll(context, modifier.Spec.OnHitChanceHundredths, ref modifier.PrdTries)
                                                : !Rolls(context, modifier.Spec.OnHitChanceHundredths))
                    continue;
                if (manaCost > Fixed.Zero)
                    attacker.Hero!.Mana -= manaCost;
                StartProcCooldown(context, modifier);
                modifier.Procing = true;
                Effects.Run(context, attacker, modifier.Spec.OnHit, 1, target, target.Position);
                modifier.Procing = false;
            }

            // Effects of the target's modifiers on being hit (Counter Helix), every Nth landing attack.
            foreach (ActiveModifier modifier in target.Modifiers.Where(m => m.Spec.WhenAttacked.Count > 0).ToList())
            {
                if (context.Tick.Value < modifier.NextProcTick || ++modifier.AttackCount < modifier.Spec.WhenAttackedEvery || !target.IsAlive)
                    continue;
                modifier.AttackCount = 0;
                if (modifier.Spec.PseudoRandom ? !PseudoRandom.Roll(context, modifier.Spec.WhenAttackedChanceHundredths, ref modifier.PrdTries)
                                                : !Rolls(context, modifier.Spec.WhenAttackedChanceHundredths))
                    continue;
                StartProcCooldown(context, modifier);
                Effects.Run(context, target, modifier.Spec.WhenAttacked, 1, attacker, attacker.Position);
            }

            foreach (ActiveModifier used in attacker.Modifiers.Where(m => m.Spec.ConsumedOnHit).ToList())
                Modifiers.Remove(context, attacker, used, "consumed");
            foreach (ActiveModifier buff in charged ?? Enumerable.Empty<ActiveModifier>())
            {
                if (attacker.Modifiers.Contains(buff) && ++buff.AttacksLanded >= buff.Spec.AttackCharges)
                    Modifiers.Remove(context, attacker, buff, "consumed");
            }
        }

        /// <summary>
        /// A chance in hundredths of a percent; draws from the combat stream only when it is neither 0 nor 100%.
        /// A whole percent is rolled out of 100 (the draw it always had), a fractional one out of 10000.
        /// </summary>
        private static bool Rolls(SimulationContext<MatchState> context, int chanceHundredths)
        {
            if (chanceHundredths >= 10000)
                return true;
            return chanceHundredths > 0 && Draw(context, chanceHundredths);
        }

        /// <summary>One draw for a chance in hundredths (critical strikes and evasion always draw, even at 100%).</summary>
        private static bool Draw(SimulationContext<MatchState> context, int chanceHundredths) =>
            chanceHundredths % 100 == 0
                ? context.Random("combat").NextInt(0, 100) < chanceHundredths / 100
                : context.Random("combat").NextInt(0, 10000) < chanceHundredths;

        private static void StartProcCooldown(SimulationContext<MatchState> context, ActiveModifier modifier)
        {
            if (modifier.Spec.ProcCooldownMs > 0)
                modifier.NextProcTick = context.Tick.Value + context.TickRate.TicksFor(modifier.Spec.ProcCooldownMs);
        }
    }

    /// <summary>
    /// Moves homing projectiles toward their targets and resolves arrivals. Runs after every unit
    /// has moved, so it does not matter which side's units were processed first.
    /// </summary>
    internal sealed class ProjectileSystem : ISimulationSystem<MatchState>
    {
        public string Name => "projectiles";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            if (state.Projectiles.Count == 0)
                return;

            foreach (Projectile projectile in state.Projectiles.ToList())
            {
                MatchUnit? target = state.Find(projectile.Target);
                if (target == null || !target.IsAlive)
                {
                    state.RemoveProjectile(projectile);   // fizzles
                    continue;
                }

                Fixed distance = FixedVector2.Distance(projectile.Position, target.Position);
                if (distance > projectile.StepPerTick + Fixed.FromInt(target.BoundingRadius))
                {
                    projectile.Position = FixedVector2.MoveTowards(projectile.Position, target.Position, projectile.StepPerTick);
                    continue;
                }

                state.RemoveProjectile(projectile);
                MatchUnit? source = state.Find(projectile.Source);
                if (projectile.IsAbility)
                {
                    if (source != null)
                        Effects.RunSpell(context, source, projectile.Effects!, projectile.Level, target, target.Position);
                }
                else if (source != null)
                {
                    AttackSystem.ApplyHit(context, source, target, projectile.Damage, projectile.IsCritical, critLifestealPercent: projectile.CritLifestealPercent);
                }
                else
                {
                    Damage.Deal(context, projectile.Source, target, projectile.Damage, DamageType.Physical, projectile.IsCritical);
                }
            }
        }
    }

    /// <summary>Removes dead units, records destroyed structures, and ends the match when an ancient falls.</summary>
    internal sealed class CleanupSystem : ISimulationSystem<MatchState>
    {
        public string Name => "cleanup";

        /// <summary>A ready reviving modifier (Reincarnation) brings the unit back with full health and mana and runs its on-death effects.</summary>
        private static bool Revive(SimulationContext<MatchState> context, MatchUnit unit)
        {
            long now = context.Tick.Value;
            ActiveModifier? revive = unit.Modifiers.FirstOrDefault(m => m.Spec.Revives && now >= m.NextProcTick);
            if (revive == null)
                return false;
            if (revive.Spec.ConsumedOnRevive)
                unit.RemoveModifier(revive);
            if (revive.Spec.ReviveDelayMs > 0 && unit.Hero != null)
            {
                // Dota's Aegis: down where it died for the delay, then back there (the hero economy system brings it back).
                unit.Hero.RespawnTick = now + context.TickRate.TicksFor(revive.Spec.ReviveDelayMs);
                unit.Hero.Reincarnating = true;
                Heroes.HeroOrders.Issue(unit, Heroes.HeroOrder.Idle);
                unit.PendingAttackTick = -1;
                Effects.Run(context, unit, revive.Spec.OnDeath, 1, context.State.Find(unit.LastAttackerId) ?? unit, unit.Position);
                return true;
            }

            unit.Hp = Fixed.FromInt(unit.MaxHp);
            if (unit.Hero != null)
                unit.Hero.Mana = Fixed.FromInt(unit.Hero.MaxMana);
            if (revive.Spec.ProcCooldownMs > 0)
                revive.NextProcTick = now + context.TickRate.TicksFor(revive.Spec.ProcCooldownMs);
            context.Emit(new UnitRevived(unit.Id, revive.Spec.Key));
            Effects.Run(context, unit, revive.Spec.OnDeath, 1, context.State.Find(unit.LastAttackerId) ?? unit, unit.Position);
            return true;
        }

        /// <summary>
        /// The dead unit's on-death effects (with its killer as the target) and its killer's on-kill effects
        /// (with the victim as the target). Units these effects kill are handled on the next tick.
        /// </summary>
        private static void RunDeathTriggers(SimulationContext<MatchState> context, MatchUnit unit)
        {
            MatchUnit? killer = context.State.Find(unit.LastAttackerId);
            foreach (ActiveModifier modifier in unit.Modifiers.Where(m => m.Spec.OnDeath.Count > 0 && !m.Spec.Revives).ToList())   // a reviving modifier's run at the revive
                Effects.Run(context, unit, modifier.Spec.OnDeath, 1, killer ?? unit, unit.Position);
            if (killer == null || killer == unit)
                return;
            foreach (ActiveModifier modifier in killer.Modifiers.Where(m => m.Spec.OnKill.Count > 0 && (!m.Spec.OnKillHeroesOnly || unit.Hero != null)).ToList())
                Effects.Run(context, killer, modifier.Spec.OnKill, 1, unit, unit.Position);
        }

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
                if (Revive(context, unit))
                    continue;
                RunDeathTriggers(context, unit);
                if (unit.Hero != null)
                {
                    // Heroes stay in the match and respawn.
                    Rewards.HeroDied(context, unit);
                    continue;
                }

                state.Remove(unit);
                if (unit.Kind == UnitKind.Boss)
                {
                    context.Emit(new UnitKilled(unit.Id, unit.Team, unit.TypeId, unit.LastAttackerId));
                    BossRewards.BossDied(context, unit);
                    continue;
                }

                if (unit.Kind == UnitKind.GroundItem)
                {
                    context.Emit(new AegisDenied(unit.LastAttackerId));
                    continue;
                }

                if (unit.Kind == UnitKind.Miniboss)
                {
                    context.Emit(new UnitKilled(unit.Id, unit.Team, unit.TypeId, unit.LastAttackerId));
                    MinibossSystem.Died(context, unit);
                    continue;
                }
                if (unit.Placement == null)
                {
                    context.Emit(new UnitKilled(unit.Id, unit.Team, unit.TypeId, unit.LastAttackerId));
                    if (unit.Kind == UnitKind.Creep || unit.Kind == UnitKind.Summon)
                        Rewards.CreepDied(context, unit, state.Ruleset.FindCreepType(unit.TypeId));
                    continue;
                }

                context.Emit(new StructureDestroyed(unit.Id, unit.Placement.Id, unit.Team, unit.LastAttackerId));
                Rewards.StructureDestroyed(context, unit);
                GlyphSystem.StructureLost(context, unit);
                if (unit.Placement.BarracksLane != null)
                    state.UpdateBarracksReinforcement();
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
