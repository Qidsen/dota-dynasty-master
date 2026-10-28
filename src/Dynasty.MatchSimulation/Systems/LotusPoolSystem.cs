using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Dota's Lotus Pools (<see cref="LotusPoolRules"/>): grows the lotuses on the clock and runs each pool's countdown — up for the
    /// one team with heroes in range, back when both are, reset with the run when none is — giving a lotus each time it completes.
    /// </summary>
    internal sealed class LotusPoolSystem : ISimulationSystem<MatchState>
    {
        public string Name => "lotus-pools";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            LotusPoolRules? rules = state.Ruleset.LotusPools;
            if (rules == null)
                return;
            long timeMs = context.Tick.Value * 1000 / context.TickRate.TicksPerSecond;
            int grown = rules.GrownAt(timeMs);

            for (int i = 0; i < state.LotusPools.Count; i++)
            {
                LotusPoolState pool = state.LotusPools[i];
                if (grown > pool.Grown)
                {
                    pool.Lotuses = System.Math.Min(rules.Max, pool.Lotuses + grown - pool.Grown);
                    pool.Grown = grown;
                }

                if (pool.Lotuses == 0)
                    continue;
                bool dawn = HeroInRange(state, rules, i, Team.Dawn);
                bool dusk = HeroInRange(state, rules, i, Team.Dusk);
                if (dawn && dusk)
                {
                    pool.Progress = System.Math.Max(0, pool.Progress - 1);   // contested: the countdown runs back
                    continue;
                }

                if (!dawn && !dusk)
                {
                    pool.Progress = 0;
                    pool.Picked = 0;
                    continue;
                }

                Team team = dawn ? Team.Dawn : Team.Dusk;
                if (pool.ProgressTeam != team && (pool.Progress > 0 || pool.Picked > 0))
                {
                    pool.Picked = 0;
                    if (pool.Progress > 0)
                    {
                        pool.Progress--;   // the other team's countdown first runs back
                        continue;
                    }
                }

                pool.ProgressTeam = team;
                if (++pool.Progress < context.TickRate.TicksFor(rules.PickMs(pool.Picked)))
                    continue;
                Pick(context, state, rules, i, team);
            }
        }

        private static bool HeroInRange(MatchState state, LotusPoolRules rules, int pool, Team team) =>
            state.Heroes.Any(h => h.Team == team && h.IsAlive && !h.IllusionOf.IsValid && Geometry.WithinRange(h.Position, rules.Positions[pool], rules.Radius));

        private static void Pick(SimulationContext<MatchState> context, MatchState state, LotusPoolRules rules, int index, Team team)
        {
            LotusPoolState pool = state.LotusPools[index];
            var inRange = state.Heroes.Where(h => h.Team == team && h.IsAlive && !h.IllusionOf.IsValid &&
                                                  Geometry.WithinRange(h.Position, rules.Positions[index], rules.Radius)).OrderBy(h => h.Id.Value).ToList();
            MatchUnit picker = inRange[context.Random("objectives").NextInt(0, inRange.Count)];
            pool.Lotuses--;
            pool.Picked++;
            pool.Progress = 0;
            HeroShop.Give(context, picker, state.Ruleset.FindItem(rules.ItemId)!);
            context.Emit(new LotusPicked(index, picker.Id));
        }
    }
}
