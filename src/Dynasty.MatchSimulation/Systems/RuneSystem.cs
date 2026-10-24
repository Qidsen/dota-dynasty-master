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
            if (now >= state.NextRuneTick)
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
                if (type.Gold > 0)
                    HeroProgression.GrantGold(context, taker, type.Gold, "rune");
                state.Remove(rune);
                context.Emit(new RunePickedUp(taker.Id, type.Id, rune.Position));
            }
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
