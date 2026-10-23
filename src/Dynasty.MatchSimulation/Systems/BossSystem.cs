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
    /// Spawns the neutral boss and drives it: it fights whoever last hurt it or stands next to it,
    /// never leaves its leash circle, and walks back to its pit when it has nobody to fight.
    /// Creeps, towers and summons ignore it (see the targeting system).
    /// </summary>
    internal sealed class BossSystem : ISimulationSystem<MatchState>
    {
        /// <summary>Heroes this close to the boss provoke it even without attacking.</summary>
        private const int ProvokeRadius = 300;

        public string Name => "boss";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            BossDefinition? definition = state.Ruleset.Boss;
            if (definition == null)
                return;

            long now = context.Tick.Value;
            if (state.Boss == null && state.NextBossSpawnTick >= 0 && now >= state.NextBossSpawnTick)
            {
                MatchUnit spawned = state.SpawnBoss(context.Ids.Allocate(), definition);
                context.Emit(new BossSpawned(spawned.Id, definition.Id));
            }

            MatchUnit? boss = state.Boss;
            if (boss == null || !boss.IsAlive)
                return;

            MatchUnit? target = ChooseTarget(state, boss, definition);
            boss.TargetId = target?.Id ?? EntityId.None;
            if (!boss.CanMove)
                return;

            if (target != null)
                MovementSystem.Approach(boss, target, boss.AttackRange);
            else if (boss.Position != definition.Position)
                boss.Position = FixedVector2.MoveTowards(boss.Position, definition.Position, boss.StepPerTick);
        }

        private static MatchUnit? ChooseTarget(MatchState state, MatchUnit boss, BossDefinition definition)
        {
            MatchUnit? attacker = state.Find(boss.LastAttackerId);
            if (attacker != null && InLeash(attacker, definition) && state.CanAttack(boss, attacker))
                return attacker;

            return state.Units
                .Where(u => u.Kind == UnitKind.Hero && state.CanAttack(boss, u) && InLeash(u, definition) && Geometry.WithinRange(u.Position, boss.Position, ProvokeRadius))
                .OrderBy(u => FixedVector2.DistanceSquared(u.Position, boss.Position))
                .FirstOrDefault();
        }

        private static bool InLeash(MatchUnit unit, BossDefinition definition) =>
            Geometry.WithinRange(unit.Position, definition.Position, definition.LeashRadius);
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
                    if (hero.IsAlive)
                        Modifiers.Apply(context, hero, definition.Reward, boss.Id);
                    if (definition.TeamGold > 0)
                        HeroProgression.GrantGold(context, hero, definition.TeamGold, "boss");
                }
            }

            context.Emit(new BossKilled(boss.Id, team, boss.LastAttackerId, respawn));
        }
    }
}
