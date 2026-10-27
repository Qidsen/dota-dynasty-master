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
        /// <summary>Heroes this close to the boss provoke it even without attacking.</summary>
        private const int BossProvokeRadius = 300;

        /// <summary>Idle neutrals stop this close to home (camp members push each other apart there).</summary>
        private const int HomeTolerance = 50;

        public string Name => "neutrals";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            SpawnBoss(context, state, now);
            Grow(context, state);
            foreach (CampState camp in state.Camps)
                UpdateCamp(context, state, camp, now);

            foreach (MatchUnit unit in state.Units)
            {
                if (unit.Team != Team.Neutral || !unit.IsAlive || unit.Kind == UnitKind.Rune)
                    continue;

                MatchUnit? target = ChooseTarget(state, unit);
                unit.TargetId = target?.Id ?? EntityId.None;
                if (!unit.CanMove)
                    continue;

                if (target != null)
                    MovementSystem.Approach(unit, target, unit.AttackRange);
                else if (!Geometry.WithinRange(unit.Position, unit.NeutralHome, HomeTolerance))
                    unit.Position = FixedVector2.MoveTowards(unit.Position, unit.NeutralHome, unit.StepPerTick);
            }
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

                MatchUnit spawned = state.SpawnBoss(context.Ids.Allocate(), definition);
                context.Emit(new BossSpawned(spawned.Id, definition.Id));
            }
        }

        private static void UpdateCamp(SimulationContext<MatchState> context, MatchState state, CampState camp, long now)
        {
            if (camp.NextSpawnTick < 0)
            {
                if (camp.Members.All(m => !m.IsAlive))
                    camp.NextSpawnTick = NextSpawnAfterClear(context, camp.Definition, now);
                return;
            }

            if (now < camp.NextSpawnTick)
                return;

            camp.Members.Clear();
            foreach (WaveEntry entry in camp.Definition.Creeps)
            {
                CreepType type = state.Ruleset.FindCreepType(entry.CreepTypeId)!;
                for (int i = 0; i < entry.Count; i++)
                    state.SpawnCampCreep(context.Ids.Allocate(), camp, type);
            }

            camp.NextSpawnTick = -1;
            context.Emit(new CampSpawned(camp.Definition.Id));
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

            if (unit.Kind != UnitKind.Boss)
                return null;
            return state.Units
                .Where(u => u.Kind == UnitKind.Hero && state.CanAttack(unit, u) && InLeash(unit, u) && Geometry.WithinRange(u.Position, unit.Position, BossProvokeRadius))
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
            if (team != Team.Neutral)
            {
                foreach (MatchUnit hero in state.Heroes.Where(h => h.Team == team).ToList())
                {
                    if (hero.IsAlive && definition.Reward != null)
                        Modifiers.Apply(context, hero, definition.Reward, boss.Id);
                    if (definition.TeamGold > 0)
                        HeroProgression.GrantGold(context, hero, definition.TeamGold, "boss");
                }

                if (definition.Aegis != null)
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
