using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Dota's Shrines of Wisdom (<see cref="ShrineRules"/>): fills them on the clock and runs each one's countdown — up for the one
    /// team with heroes in range, back when both are, reset when none is — giving the experience when it completes.
    /// </summary>
    internal sealed class ShrineSystem : ISimulationSystem<MatchState>
    {
        public string Name => "shrines";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            ShrineRules? rules = state.Ruleset.Shrines;
            if (rules == null)
                return;
            long timeMs = context.Tick.Value * 1000 / context.TickRate.TicksPerSecond;
            int activations = rules.ActivationsAt(timeMs);
            long captureTicks = context.TickRate.TicksFor(rules.CaptureMs);

            for (int i = 0; i < state.Shrines.Count; i++)
            {
                ShrineState shrine = state.Shrines[i];
                if (activations > shrine.Activations)
                {
                    shrine.Charges += activations - shrine.Activations;
                    shrine.Activations = activations;
                }

                if (shrine.Charges == 0)
                    continue;
                bool dawn = HeroInRange(state, rules, i, Team.Dawn);
                bool dusk = HeroInRange(state, rules, i, Team.Dusk);
                if (dawn && dusk)
                {
                    shrine.Progress = System.Math.Max(0, shrine.Progress - 1);   // contested: the countdown runs back
                    continue;
                }

                if (!dawn && !dusk)
                {
                    shrine.Progress = 0;
                    continue;
                }

                Team team = dawn ? Team.Dawn : Team.Dusk;
                if (shrine.Progress > 0 && shrine.ProgressTeam != team)
                {
                    shrine.Progress--;   // the other team's countdown first runs back
                    continue;
                }

                shrine.ProgressTeam = team;
                if (++shrine.Progress < captureTicks)
                    continue;
                Take(context, state, rules, i, team, timeMs);
            }
        }

        private static bool HeroInRange(MatchState state, ShrineRules rules, int shrine, Team team) =>
            state.Heroes.Any(h => h.Team == team && h.IsAlive && !h.IllusionOf.IsValid && Geometry.WithinRange(h.Position, rules.Positions[shrine], rules.Radius));

        private static void Take(SimulationContext<MatchState> context, MatchState state, ShrineRules rules, int index, Team team, long timeMs)
        {
            ShrineState shrine = state.Shrines[index];
            int xp = shrine.Charges * rules.XpAt(timeMs);
            var inRange = state.Heroes.Where(h => h.Team == team && h.IsAlive && !h.IllusionOf.IsValid &&
                                                  Geometry.WithinRange(h.Position, rules.Positions[index], rules.Radius)).OrderBy(h => h.Id.Value).ToList();
            MatchUnit taker = inRange[context.Random("objectives").NextInt(0, inRange.Count)];
            MatchUnit? least = state.Heroes.Where(h => h.Team == team && h != taker && !h.IllusionOf.IsValid)
                .OrderBy(h => h.Hero!.Xp).ThenBy(h => h.Id.Value).FirstOrDefault();
            HeroProgression.GrantXp(context, taker, xp);
            if (least != null)
                HeroProgression.GrantXp(context, least, xp);
            shrine.Charges = 0;
            shrine.Progress = 0;
            context.Emit(new ShrineTaken(index, team, taker.Id, xp));
        }
    }
}
