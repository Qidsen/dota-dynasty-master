using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Sets off proximity modifiers (Proximity Mines) after every move of the tick: an enemy hero, creep or summon within the radius
    /// starts the count, none near stops it; once it has run for the delay the effects run with the bearer as the caster and the
    /// bearer is gone (no bounty: it was not killed).
    /// </summary>
    internal sealed class ProximitySystem : ISimulationSystem<MatchState>
    {
        public string Name => "proximity";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            long now = context.Tick.Value;
            foreach (MatchUnit bearer in state.Units.Where(u => u.IsAlive && u.Modifiers.Any(m => m.Spec.ProximityRadius > 0)).ToList())
            {
                foreach (ActiveModifier modifier in bearer.Modifiers.Where(m => m.Spec.ProximityRadius > 0).ToList())
                {
                    if (!bearer.IsAlive)
                        break;
                    if (!EnemyNear(state, bearer, modifier.Spec.ProximityRadius))
                    {
                        modifier.ProximitySinceTick = -1;
                        continue;
                    }

                    if (modifier.ProximitySinceTick < 0)
                        modifier.ProximitySinceTick = now;
                    if (now - modifier.ProximitySinceTick < context.TickRate.TicksFor(modifier.Spec.ProximityDelayMs))
                        continue;
                    Effects.Run(context, bearer, modifier.Spec.OnProximity, 1, bearer, bearer.Position);
                    bearer.ExpiresTick = now;
                }
            }
        }

        private static bool EnemyNear(MatchState state, MatchUnit bearer, int radius)
        {
            foreach (MatchUnit unit in state.Units)
            {
                if (unit.IsAlive && unit.Team != bearer.Team && (unit.Kind == UnitKind.Hero || unit.Kind == UnitKind.Creep || unit.Kind == UnitKind.Summon) &&
                    Geometry.WithinRange(unit.Position, bearer.Position, radius))
                    return true;
            }

            return false;
        }
    }
}
