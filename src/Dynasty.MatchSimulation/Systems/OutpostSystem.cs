using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Dota's Outposts (<see cref="OutpostRules"/>): puts them on the map at the start, then runs each enemy team's capture — a tick
    /// per hero of the team within range, reset when none is — handing the Outpost over when it completes.
    /// </summary>
    internal sealed class OutpostSystem : ISimulationSystem<MatchState>
    {
        private static readonly UnitStats Stats = new UnitStats(1, Fixed.Zero, 0, 0, 0, 0, 1000, 0, 0, 96, visionRange: 0, nightVisionRange: 0);

        public string Name => "outposts";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            OutpostRules? rules = state.Ruleset.Outposts;
            if (rules == null)
                return;
            if (state.Outposts.Count == 0)
            {
                for (int i = 0; i < rules.Positions.Count; i++)
                    state.SpawnOutpost(context.Ids.Allocate(), rules.Owners[i], rules.Positions[i], Stats);
            }

            long capture = context.TickRate.TicksFor(rules.CaptureMs);
            for (int i = 0; i < state.Outposts.Count; i++)
            {
                OutpostState outpost = state.Outposts[i];
                foreach (Team team in new[] { Team.Dawn, Team.Dusk })
                {
                    int slot = team == Team.Dawn ? 0 : 1;
                    int heroes = outpost.Unit.Team == team ? 0 : state.Heroes.Count(h => h.Team == team && h.IsAlive && !h.IllusionOf.IsValid &&
                        (h.Flags & ModifierFlags.Stunned) == 0 && Geometry.WithinRange(h.Position, outpost.Unit.Position, rules.Radius));
                    outpost.Progress[slot] = heroes == 0 ? 0 : outpost.Progress[slot] + heroes;
                }

                bool dawn = outpost.Progress[0] >= capture;
                bool dusk = outpost.Progress[1] >= capture;
                if (!dawn && !dusk)
                    continue;
                // Both captures ending on the same tick: drawn, not decided by the order of the teams (D-014).
                Team taker = dawn && dusk ? (context.Random("objectives").NextInt(0, 2) == 0 ? Team.Dawn : Team.Dusk) : dawn ? Team.Dawn : Team.Dusk;
                outpost.Unit.Team = taker;
                outpost.Progress[0] = outpost.Progress[1] = 0;
                context.Emit(new OutpostCaptured(i, taker));
            }
        }
    }
}
