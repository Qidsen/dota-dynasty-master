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
    /// <summary>
    /// Spawns and drives neutral units (jungle camps and the boss). Neutrals fight whoever hurt
    /// them (a whole camp answers a hit on any member; the boss also answers heroes standing next
    /// to it), never chase beyond their leash circle, forget the attacker once it leaves, and walk
    /// home when idle. Creeps, towers and summons ignore neutrals (see the targeting system).
    /// </summary>
    internal sealed class NeutralSystem : ISimulationSystem<MatchState>
    {
        /// <summary>Idle neutrals stop this close to home (camp members push each other apart there).</summary>
        private const int HomeTolerance = 50;

        public string Name => "neutrals";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            SpawnBoss(context, state, now);
            Grow(context, state);
            CastBossAbilities(context, state, now);
            foreach (CampState camp in state.Camps)
                UpdateCamp(context, state, camp, now);
            MovePit(state, now);
            PickUpAegis(context, state);

            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Team != Team.Neutral || !unit.IsAlive || unit.Kind == UnitKind.Rune || unit.Kind == UnitKind.Miniboss || unit.Kind == UnitKind.GroundItem)
                    continue;

                MatchUnit? target = ChooseTarget(state, unit);
                unit.TargetId = target?.Id ?? EntityId.None;
                if (!unit.CanMove)
                    continue;

                if (target != null)
                    MovementSystem.Approach(unit, target, unit.AttackRange);
                else if (unit.BossPath != null)
                    WalkPath(unit);
                else if (!Geometry.WithinRange(unit.Position, unit.NeutralHome, HomeTolerance))
                    unit.Position = FixedVector2.MoveTowards(unit.Position, unit.NeutralHome, unit.StepPerTick);
            }
        }

        /// <summary>
        /// A boss's own spells: lands the one being cast once its cast point has passed, else starts the first ready one whose
        /// trigger holds (disabled bosses cast only spells that ignore disables).
        /// </summary>
        private static void CastBossAbilities(SimulationContext<MatchState> context, MatchState state, long now)
        {
            BossDefinition? definition = state.Ruleset.Boss;
            MatchUnit? boss = state.Boss;
            if (definition == null || boss == null || !boss.IsAlive || definition.Abilities.Count == 0)
                return;
            boss.BossAbilityReadyTicks ??= new long[definition.Abilities.Count];

            if (boss.BossCastingAbility >= 0)
            {
                if (now < boss.BossCastLandsTick)
                    return;
                Land(context, state, boss, definition, definition.Abilities[boss.BossCastingAbility], now);
                boss.BossCastingAbility = -1;
                return;
            }

            bool disabled = (boss.Flags & (ModifierFlags.Stunned | ModifierFlags.Silenced)) != 0;
            for (int i = 0; i < definition.Abilities.Count; i++)
            {
                BossAbility ability = definition.Abilities[i];
                if (now < boss.BossAbilityReadyTicks[i] || (disabled && !ability.IgnoresDisables) || !Triggered(state, boss, ability))
                    continue;
                boss.BossAbilityReadyTicks[i] = now + context.TickRate.TicksFor(ability.CooldownMs);
                if (ability.DispelsSelf)
                    Effects.Dispel(context, boss, boss, strong: true);
                boss.BossCastingAbility = i;
                boss.BossCastLandsTick = now + context.TickRate.TicksFor(ability.CastPointMs);
                return;
            }
        }

        private static bool Triggered(MatchState state, MatchUnit boss, BossAbility ability)
        {
            switch (ability.Trigger)
            {
                case BossAbilityTrigger.Engaged:
                    return boss.TargetId.IsValid && state.Units.Any(u => u.Team != Team.Neutral && u.IsAlive && u.Kind != UnitKind.Structure && u.Kind != UnitKind.Rune &&
                                                                         Geometry.WithinRange(u.Position, boss.Position, ability.Radius));
                case BossAbilityTrigger.HitByLastKillersBelowHealth:
                    return state.BossLastKillerTeam != Team.Neutral && state.Find(boss.LastAttackerId)?.Team == state.BossLastKillerTeam &&
                           boss.Hp * Fixed.FromInt(100) < Fixed.FromInt(boss.MaxHp * ability.BelowHealthPercent);
                default:
                    return false;
            }
        }

        private static void Land(SimulationContext<MatchState> context, MatchState state, MatchUnit boss, BossDefinition definition, BossAbility ability, long now)
        {
            int intervals = definition.Growth?.IntervalsAt(now * 1000 / context.TickRate.TicksPerSecond) ?? 0;
            Fixed damage = Fixed.FromInt(ability.Damage + ability.DamagePerInterval * intervals);
            foreach (MatchUnit unit in state.Units.Where(u => u.Team != Team.Neutral && u.IsAlive && u.Kind != UnitKind.Structure && u.Kind != UnitKind.Rune &&
                                                              Geometry.WithinRange(u.Position, boss.Position, ability.Radius)).ToList())
            {
                Fixed amount = unit.Kind == UnitKind.Creep ? damage * Fixed.FromInt(ability.CreepDamagePercent) / Fixed.FromInt(100) : damage;
                Damage.Deal(context, boss.Id, unit, amount, ability.DamageType);
                foreach (ModifierSpec modifier in ability.Modifiers)
                    Modifiers.Apply(context, unit, modifier, boss.Id);
            }

            context.Emit(new BossAbilityCast(boss.Id, ability.Id));
        }

        private static readonly System.Collections.Generic.Dictionary<(BossGrowth, int), ModifierSpec> GrowthSpecs =
            new System.Collections.Generic.Dictionary<(BossGrowth, int), ModifierSpec>();

        /// <summary>Dota's Roshan: the growth of the intervals passed so far; the health gained heals too.</summary>
        private static void Grow(SimulationContext<MatchState> context, MatchState state)
        {
            BossGrowth? growth = state.Ruleset.Boss?.Growth;
            MatchUnit? boss = state.Boss;
            if (growth == null || boss == null || !boss.IsAlive)
                return;
            int intervals = growth.IntervalsAt(context.Tick.Value * 1000 / context.TickRate.TicksPerSecond);
            if (intervals == boss.BossGrowthIntervals)
                return;

            ModifierSpec spec;
            lock (GrowthSpecs)
            {
                if (!GrowthSpecs.TryGetValue((growth, intervals), out spec!))
                    GrowthSpecs[(growth, intervals)] = spec = new ModifierSpec("boss_growth", -1, dispellable: false, maxHpBonus: growth.Health * intervals,
                        damageBonus: growth.Damage * intervals, armorBonus: growth.Armor * Fixed.FromInt(intervals + (growth.ArmorFromStart ? 1 : 0)));
            }

            int oldMax = boss.MaxHp;
            Fixed hp = boss.Hp;   // read before the old growth comes off (that clamps the health)
            ActiveModifier? old = boss.FindModifier("boss_growth", boss.Id);
            if (old != null)
                boss.RemoveModifier(old);
            boss.AddModifier(new ActiveModifier(spec, boss.Id, 0, -1, 0));
            boss.Hp = Fixed.Min(Fixed.FromInt(boss.MaxHp), hp + Fixed.FromInt(boss.MaxHp - oldMax));
            boss.BossGrowthIntervals = intervals;
        }

        private static void SpawnBoss(SimulationContext<MatchState> context, MatchState state, long now)
        {
            BossDefinition? definition = state.Ruleset.Boss;
            if (definition != null && state.Boss == null && state.NextBossSpawnTick >= 0 && now >= state.NextBossSpawnTick)
            {
                // Dota: Roshan takes back an unused Aegis when he respawns; its carrier gets the regeneration instead.
                if (state.GroundAegis != null)
                    state.Remove(state.GroundAegis);
                if (definition.Aegis != null)
                {
                    foreach (MatchUnit hero in state.Heroes.ToList())
                    {
                        ActiveModifier? aegis = hero.FindModifier(definition.Aegis.Key, hero.Modifiers.FirstOrDefault(m => m.Spec.Key == definition.Aegis.Key)?.Source ?? default);
                        if (aegis == null)
                            continue;
                        Modifiers.Remove(context, hero, aegis, "reclaimed");
                        if (definition.Aegis.ExpireModifier != null && hero.IsAlive)
                            Modifiers.Apply(context, hero, definition.Aegis.ExpireModifier, aegis.Source);
                    }
                }

                MatchUnit spawned = state.SpawnBoss(context.Ids.Allocate(), definition, definition.PitAt(state.IsNight(now)));
                context.Emit(new BossSpawned(spawned.Id, definition.Id));
            }
        }

        private const int AegisPickupRadius = 150;

        /// <summary>A hero (of either team) on the dropped Aegis takes it; several at once draw (D-014).</summary>
        private static void PickUpAegis(SimulationContext<MatchState> context, MatchState state)
        {
            MatchUnit? aegis = state.GroundAegis;
            ModifierSpec? spec = state.Ruleset.Boss?.Aegis;
            if (aegis == null || spec == null || !aegis.IsAlive)
                return;
            var takers = state.Heroes.Where(h => h.IsAlive && !h.IllusionOf.IsValid && Geometry.WithinRange(h.Position, aegis.Position, AegisPickupRadius))
                .OrderBy(h => h.Id.Value).ToList();
            if (takers.Count == 0)
                return;
            MatchUnit taker = takers.Count == 1 ? takers[0] : takers[context.Random("objectives").NextInt(0, takers.Count)];
            state.Remove(aegis);
            Modifiers.Apply(context, taker, spec, aegis.Id);
            context.Emit(new AegisPicked(taker.Id));
        }

        /// <summary>Dota's Roshan: at every change of day and night his home becomes the other pit, reached along a path.</summary>
        private static void MovePit(MatchState state, long now)
        {
            BossDefinition? definition = state.Ruleset.Boss;
            MatchUnit? boss = state.Boss;
            if (definition?.NightPosition == null || boss == null || !boss.IsAlive)
                return;
            FixedVector2 pit = definition.PitAt(state.IsNight(now));
            if (pit == boss.NeutralHome)
                return;
            boss.NeutralHome = pit;
            boss.BossPath = state.Pathfinder.FindPath(boss.Position, pit);
            boss.BossPathIndex = 0;
        }

        private static void WalkPath(MatchUnit boss)
        {
            System.Collections.Generic.IReadOnlyList<FixedVector2> path = boss.BossPath!;
            while (boss.BossPathIndex < path.Count && boss.Position == path[boss.BossPathIndex])
                boss.BossPathIndex++;
            if (boss.BossPathIndex >= path.Count)
            {
                boss.BossPath = null;
                return;
            }

            boss.Position = FixedVector2.MoveTowards(boss.Position, path[boss.BossPathIndex], boss.StepPerTick);
        }

        private static void UpdateCamp(SimulationContext<MatchState> context, MatchState state, CampState camp, long now)
        {
            if (camp.NextSpawnTick < 0)
            {
                if (camp.Members.All(m => !m.IsAlive))
                    camp.NextSpawnTick = NextSpawnAfterClear(context, camp.Definition, now);
                else if (Evolves(context, camp, now) && !Busy(state, camp))
                {
                    // Dota's flooded camp evolves: the idle camp is replaced by its new stage.
                    foreach (MatchUnit member in camp.Members.Where(m => m.IsAlive).ToList())
                    {
                        state.Remove(member);
                        context.Emit(new UnitExpired(member.Id));
                    }

                    Spawn(context, state, camp, now);
                }

                return;
            }

            if (now < camp.NextSpawnTick)
                return;
            Spawn(context, state, camp, now);
        }

        private static bool Evolves(SimulationContext<MatchState> context, CampState camp, long now) =>
            camp.Definition.Evolution is CampEvolution evolution && evolution.StageAt(now * 1000 / context.TickRate.TicksPerSecond) != camp.Stage;

        /// <summary>Fighting, or blocked by a living unit of the teams within its leash.</summary>
        private static bool Busy(MatchState state, CampState camp) =>
            camp.Members.Any(m => m.IsAlive && m.TargetId.IsValid) ||
            state.Units.Any(u => u.Team != Team.Neutral && u.IsAlive && (u.Kind == UnitKind.Hero || u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) &&
                                 Geometry.WithinRange(u.Position, camp.Definition.Position, camp.Definition.LeashRadius));

        private static void Spawn(SimulationContext<MatchState> context, MatchState state, CampState camp, long now)
        {
            NeutralCampDefinition definition = camp.Definition;
            System.Collections.Generic.IReadOnlyList<WaveEntry> creeps;
            if (definition.Evolution is CampEvolution evolution)
            {
                camp.Stage = evolution.StageAt(now * 1000 / context.TickRate.TicksPerSecond);
                creeps = evolution.Stages[camp.Stage];
            }
            else
            {
                int variant = 0;
                if (definition.Variants.Count > 1)
                {
                    // Never the last one again: draw among the others.
                    int count = camp.LastVariant < 0 ? definition.Variants.Count : definition.Variants.Count - 1;
                    variant = context.Random("neutrals").NextInt(0, count);
                    if (camp.LastVariant >= 0 && variant >= camp.LastVariant)
                        variant++;
                }

                camp.LastVariant = variant;
                creeps = definition.Variants[variant];
            }

            camp.Members.Clear();
            foreach (WaveEntry entry in creeps)
            {
                CreepType type = state.Ruleset.FindCreepType(entry.CreepTypeId)!;
                for (int i = 0; i < entry.Count; i++)
                    state.SpawnCampCreep(context.Ids.Allocate(), camp, type);
            }

            camp.NextSpawnTick = -1;
            context.Emit(new CampSpawned(definition.Id));
        }

        private static long NextSpawnAfterClear(SimulationContext<MatchState> context, NeutralCampDefinition camp, long now)
        {
            long interval = context.TickRate.TicksFor(camp.RespawnMs);
            if (!camp.AlignedToClock)
                return now + interval;
            long first = context.TickRate.TicksFor(camp.FirstSpawnMs);
            return first + ((now - first) / interval + 1) * interval;
        }

        private static MatchUnit? ChooseTarget(MatchState state, MatchUnit unit)
        {
            // A camp shares its aggression: any member's attacker is everyone's target.
            var aggressors = unit.Camp != null ? unit.Camp.Members.Where(m => m.IsAlive).Select(m => m.LastAttackerId) : new[] { unit.LastAttackerId };
            foreach (EntityId id in aggressors)
            {
                MatchUnit? attacker = state.Find(id);
                if (attacker != null && state.CanAttack(unit, attacker) && InLeash(unit, attacker))
                    return attacker;
            }

            // The attacker left the leash (or died): forget it.
            if (unit.Camp != null)
            {
                foreach (MatchUnit member in unit.Camp.Members)
                    member.LastAttackerId = EntityId.None;
            }
            else
            {
                unit.LastAttackerId = EntityId.None;
            }

            int provoke = state.Ruleset.Boss?.ProvokeRadius ?? 0;
            if (unit.Kind != UnitKind.Boss || provoke <= 0)
                return null;
            return state.Units
                .Where(u => u.Kind == UnitKind.Hero && state.CanAttack(unit, u) && InLeash(unit, u) && Geometry.WithinRange(u.Position, unit.Position, provoke))
                .OrderBy(u => FixedVector2.DistanceSquared(u.Position, unit.Position))
                .FirstOrDefault();
        }

        private static bool InLeash(MatchUnit neutral, MatchUnit other) =>
            Geometry.WithinRange(other.Position, neutral.NeutralHome, neutral.LeashRadius);
    }

    internal static class BossRewards
    {
        public static void BossDied(SimulationContext<MatchState> context, MatchUnit boss)
        {
            MatchState state = context.State;
            BossDefinition definition = state.Ruleset.Boss!;
            long now = context.Tick.Value;
            long respawn = now + context.Random("objectives").NextInt(
                (int)context.TickRate.TicksFor(definition.RespawnMinMs),
                (int)context.TickRate.TicksFor(definition.RespawnMaxMs) + 1);
            state.NextBossSpawnTick = respawn;

            MatchUnit? killer = state.Find(boss.LastAttackerId);
            Team team = killer != null && killer.Team != Team.Neutral ? killer.Team : Team.Neutral;
            state.BossDeaths++;
            if (team != Team.Neutral)
            {
                state.BossLastKillerTeam = team;
                foreach (MatchUnit hero in state.Heroes.Where(h => h.Team == team).ToList())
                {
                    if (hero.IsAlive && definition.Reward != null)
                        Modifiers.Apply(context, hero, definition.Reward, boss.Id);
                    if (definition.TeamGold > 0)
                        HeroProgression.GrantGold(context, hero, definition.TeamGold, "boss");
                }

                foreach (BossDrop drop in definition.Drops.Where(d => state.BossDeaths >= d.FromDeath))
                {
                    MatchUnit? receiver = state.Heroes.Where(h => h.Team == team && h.IsAlive && h.Hero!.Inventory.Count < HeroShop.InventorySize)
                        .OrderByDescending(h => h.Hero!.NetWorth).ThenBy(h => h.Id.Value).FirstOrDefault();
                    if (receiver != null)
                        HeroShop.Give(context, receiver, state.Ruleset.FindItem(drop.ItemId)!);
                }

                if (definition.Aegis != null && definition.AegisDrops)
                    state.SpawnGroundAegis(context.Ids.Allocate(), boss.Position);
                else if (definition.Aegis != null)
                {
                    MatchUnit? holder = state.Heroes.Where(h => h.Team == team && h.IsAlive)
                        .OrderByDescending(h => h.Hero!.NetWorth).ThenBy(h => h.Id.Value).FirstOrDefault();
                    if (holder != null)
                        Modifiers.Apply(context, holder, definition.Aegis, boss.Id);
                }

                // Dota: more gold for the hero that landed the last hit; XP for the killing team's heroes around.
                if (killer!.Hero != null && definition.KillerGoldMax > 0)
                    HeroProgression.GrantGold(context, killer, context.Random("economy").NextInt(definition.KillerGoldMin, definition.KillerGoldMax + 1), "boss_last_hit");
                int xp = definition.Xp + (definition.Growth?.Xp ?? 0) * (definition.Growth?.IntervalsAt(now * 1000 / context.TickRate.TicksPerSecond) ?? 0);
                if (xp > 0 && state.Ruleset.HeroRules != null)
                    Rewards.ShareXp(context, boss, team, xp, killer.Hero != null ? killer : null);
            }

            context.Emit(new BossKilled(boss.Id, team, boss.LastAttackerId, respawn));
        }
    }
}
