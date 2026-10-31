using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Stampede's trample after every move of the tick: an enemy hero, creep or summon within the trample radius of a bearer, not yet
    /// marked by the same caster, gets the trample effects from that caster (who also marks it: once a cast).
    /// </summary>
    internal sealed class TrampleSystem : ISimulationSystem<MatchState>
    {
        public string Name => "trample";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            foreach (MatchUnit bearer in state.Heroes)
            {
                if (!bearer.IsAlive || bearer.Modifiers.Count == 0)
                    continue;
                foreach (ActiveModifier stampede in bearer.Modifiers.Where(m => m.Spec.TrampleRadius > 0).ToList())
                {
                    MatchUnit caster = state.Find(stampede.Source) ?? bearer;
                    foreach (MatchUnit enemy in state.Units.Where(u => u.IsAlive && u.Team != bearer.Team &&
                                                                       (u.Kind == UnitKind.Hero || u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) &&
                                                                       (u.Flags & ModifierFlags.Invulnerable) == 0 &&
                                                                       Geometry.WithinRange(u.Position, bearer.Position, stampede.Spec.TrampleRadius + u.BoundingRadius)).ToList())
                    {
                        if (enemy.Modifiers.Any(m => m.Spec.Key == stampede.Spec.TrampleMarker && m.Source == caster.Id))
                            continue;
                        Effects.Run(context, caster, stampede.Spec.OnTrample, 1, enemy, enemy.Position);
                    }
                }
            }
        }
    }
}
