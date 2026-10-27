using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Randomness;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>Spawns runes on empty spots and hands each one to the closest hero standing on it.</summary>
    internal sealed class RuneSystem : ISimulationSystem<MatchState>
    {
        public string Name => "runes";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            RuneRules? rules = state.Ruleset.Runes;
            if (rules == null)
                return;

            long now = context.Tick.Value;
            DeterministicRandom random = context.Random("runes");
            if (rules.Schedules.Count > 0)
                SpawnScheduled(context, state, rules, random, now);
            else if (now >= state.NextRuneTick)
            {
                for (int spot = 0; spot < rules.Spots.Count; spot++)
                {
                    if (state.Units.Any(u => u.Kind == UnitKind.Rune && u.RuneSpot == spot))
                        continue;
                    RuneType type = rules.Types[random.NextInt(0, rules.Types.Count)];
                    MatchUnit rune = state.SpawnRune(context.Ids.Allocate(), type, spot, rules.Spots[spot]);
                    context.Emit(new RuneSpawned(rune.Id, type.Id, rune.Position));
                }

                state.NextRuneTick += context.TickRate.TicksFor(rules.IntervalMs);
            }

            foreach (MatchUnit rune in state.Units.Where(u => u.Kind == UnitKind.Rune).ToList())
            {
                MatchUnit? taker = ClosestTaker(state, rune, rules.PickupRadius, random);
                if (taker == null)
                    continue;

                RuneType type = rune.Rune!;
                if (type.Effect != null)
                    Modifiers.Apply(context, taker, type.Effect, rune.Id);
                if (type.Effects.Count > 0)
                    Effects.Run(context, taker, type.Effects, 1, taker, taker.Position);
                if (type.Gold > 0)
                    HeroProgression.GrantGold(context, taker, type.Gold, "rune");
                int teamGold = type.TeamGoldAt(rune.RuneSpawnTick * 1000 / context.TickRate.TicksPerSecond);
                if (teamGold > 0)
                {
                    foreach (MatchUnit hero in state.Heroes.Where(h => h.Team == taker.Team).ToList())
                        HeroProgression.GrantGold(context, hero, teamGold, "bounty_rune");
                }
                state.Remove(rune);
                context.Emit(new RunePickedUp(taker.Id, type.Id, rune.Position));
            }
        }

        /// <summary>
        /// Dota's rune timings: each schedule due now puts its runes down (all spots, or one random spot) after the untaken runes
        /// at its spots are gone (unless they stay); types drawn without a repeat in a cycle and never twice in a row if asked.
        /// </summary>
        private static void SpawnScheduled(SimulationContext<MatchState> context, MatchState state, RuneRules rules, DeterministicRandom random, long now)
        {
            int count = rules.Schedules.Count;
            state.RuneScheduleSpawns ??= new int[count];
            state.RuneBags ??= Enumerable.Range(0, count).Select(_ => new List<RuneType>()).ToArray();
            state.RuneLastTypes ??= new RuneType?[count];
            long nowMs = now * 1000 / context.TickRate.TicksPerSecond;
            for (int s = 0; s < count; s++)
            {
                RuneSchedule schedule = rules.Schedules[s];
                int n = state.RuneScheduleSpawns[s];
                long dueMs = schedule.FirstSpawnMs + (long)schedule.IntervalMs * n;
                bool due = nowMs >= dueMs && (n == 0 || schedule.IntervalMs > 0) && (schedule.UntilMs == 0 || dueMs <= schedule.UntilMs);
                if (!due)
                    continue;

                state.RuneScheduleSpawns[s] = n + 1;
                if (!schedule.Stay)
                {
                    // The runes at these spots that do not stay are gone (Dota: unused water runes vanish when the power runes come).
                    foreach (MatchUnit old in state.Units.Where(u => u.Kind == UnitKind.Rune && u.RuneSchedule >= 0 && !rules.Schedules[u.RuneSchedule].Stay &&
                                                                     schedule.Spots.Contains(u.Position)).ToList())
                    {
                        state.Remove(old);
                        context.Emit(new RuneExpired(old.Id, old.TypeId));
                    }
                }

                IEnumerable<int> spots = schedule.Placement == RunePlacement.OneRandomSpot
                    ? new[] { random.NextInt(0, schedule.Spots.Count) }
                    : Enumerable.Range(0, schedule.Spots.Count);
                foreach (int spot in spots.ToList())
                {
                    RuneType type = Draw(state, schedule, s, random);
                    MatchUnit rune = state.SpawnRune(context.Ids.Allocate(), type, spot, schedule.Spots[spot]);
                    rune.RuneSchedule = s;
                    rune.RuneSpawnTick = now;
                    context.Emit(new RuneSpawned(rune.Id, type.Id, rune.Position));
                }
            }
        }

        private static RuneType Draw(MatchState state, RuneSchedule schedule, int s, DeterministicRandom random)
        {
            if (!schedule.NoRepeat)
                return schedule.Types[random.NextInt(0, schedule.Types.Count)];
            List<RuneType> bag = state.RuneBags![s];
            if (bag.Count == 0)
                bag.AddRange(schedule.Types);
            RuneType? last = state.RuneLastTypes![s];
            List<RuneType> choices = bag.Count > 1 && last != null && bag.Contains(last) ? bag.Where(t => t != last).ToList() : bag;
            RuneType type = choices[random.NextInt(0, choices.Count)];
            bag.Remove(type);
            state.RuneLastTypes[s] = type;
            return type;
        }

        /// <summary>Nearest living hero within the radius; exact ties are broken by the rune stream, not by unit order.</summary>
        private static MatchUnit? ClosestTaker(MatchState state, MatchUnit rune, int radius, DeterministicRandom random)
        {
            Fixed reach = Fixed.FromInt(radius);
            Fixed limit = reach * reach;
            var closest = new List<MatchUnit>();
            Fixed best = Fixed.Zero;
            foreach (MatchUnit hero in state.Heroes)
            {
                if (!hero.IsAlive)
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(hero.Position, rune.Position);
                if (distance > limit)
                    continue;
                if (closest.Count == 0 || distance < best)
                {
                    closest.Clear();
                    closest.Add(hero);
                    best = distance;
                }
                else if (distance == best)
                {
                    closest.Add(hero);
                }
            }

            return closest.Count switch
            {
                0 => null,
                1 => closest[0],
                _ => closest[random.NextInt(0, closest.Count)],
            };
        }
    }
}
