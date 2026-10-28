using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Dota's Watchers (<see cref="WatcherRules"/>): each team's heroes standing at a Watcher run that team's channel; a completed one
    /// turns a ready Watcher on for the team or another team's Watcher off. Its vision is added by the vision system.
    /// </summary>
    internal sealed class WatcherSystem : ISimulationSystem<MatchState>
    {
        public string Name => "watchers";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            WatcherRules? rules = state.Ruleset.Watchers;
            if (rules == null)
                return;
            long now = context.Tick.Value;
            long channel = context.TickRate.TicksFor(rules.ChannelMs);

            for (int i = 0; i < state.Watchers.Count; i++)
            {
                WatcherState watcher = state.Watchers[i];
                if (watcher.UntilTick >= 0 && now >= watcher.UntilTick)
                {
                    watcher.Owner = Team.Neutral;   // its time is up: ready again
                    watcher.Off = false;
                    watcher.UntilTick = -1;
                }

                foreach (Team team in new[] { Team.Dawn, Team.Dusk })
                {
                    int slot = team == Team.Dawn ? 0 : 1;
                    bool channeling = !watcher.Off && watcher.Owner != team && state.Heroes.Any(h => h.Team == team && h.IsAlive && !h.IllusionOf.IsValid &&
                        (h.Flags & ModifierFlags.Stunned) == 0 && Geometry.WithinRange(h.Position, rules.Positions[i], rules.Radius));
                    watcher.Progress[slot] = channeling ? watcher.Progress[slot] + 1 : 0;
                }

                bool dawnDone = !watcher.Off && watcher.Progress[0] >= channel;
                bool duskDone = !watcher.Off && watcher.Progress[1] >= channel;
                if (dawnDone || duskDone)
                {
                    // Both channels ending on the same tick: drawn, not decided by the order of the teams (D-014).
                    Team team = dawnDone && duskDone ? (context.Random("objectives").NextInt(0, 2) == 0 ? Team.Dawn : Team.Dusk) : dawnDone ? Team.Dawn : Team.Dusk;
                    watcher.Progress[0] = watcher.Progress[1] = 0;
                    if (watcher.Owner == Team.Neutral)
                    {
                        watcher.Owner = team;
                        watcher.UntilTick = now + context.TickRate.TicksFor(rules.OnMs);
                        context.Emit(new WatcherTaken(i, team));
                    }
                    else
                    {
                        watcher.Owner = Team.Neutral;
                        watcher.Off = true;
                        watcher.UntilTick = now + context.TickRate.TicksFor(rules.OffMs);
                        context.Emit(new WatcherTurnedOff(i, team));
                    }
                }
            }
        }
    }
}
