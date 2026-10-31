using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Trample's stomps by the distance travelled, and Stampede's trample after every move of the tick: an enemy hero, creep or summon within the trample radius of a bearer, not yet
    /// marked by the same caster, gets the trample effects from that caster (who also marks it: once a cast).
    /// </summary>
    internal sealed class TrampleSystem : ISimulationSystem<MatchState>
    {
        public string Name => "trample";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            // Heroes, a flying orb, a wall; units these effects add (a Wall of Replica's replicas) wait for the next tick.
            for (int i = 0, count = state.Units.Count; i < count; i++)
            {
                MatchUnit bearer = state.Units[i];
                if (!bearer.IsAlive || bearer.Modifiers.Count == 0)
                    continue;
                foreach (ActiveModifier stepping in bearer.Modifiers.Where(m => m.Spec.EveryDistance > 0).ToList())
                {
                    // Primal Beast's Trample: a stomp for every so many units travelled this tick and before.
                    stepping.Travelled += FixedVector2.Distance(bearer.StartPosition, bearer.Position);
                    Fixed step = Fixed.FromInt(stepping.Spec.EveryDistance);
                    while (stepping.Travelled >= step && bearer.IsAlive)
                    {
                        stepping.Travelled -= step;
                        Effects.Run(context, bearer, stepping.Spec.OnDistance, 1, bearer, bearer.Position);
                    }
                }

                foreach (ActiveModifier stampede in bearer.Modifiers.Where(m => m.Spec.TrampleRadius > 0 || m.Spec.WallHalfLength > 0).ToList())
                {
                    MatchUnit caster = state.Find(stampede.Source) ?? bearer;
                    // Wall of Replica: a line across the cast's way through the bearer instead of a circle round it.
                    FixedVector2 along = stampede.Spec.WallHalfLength > 0 && stampede.Aim != FixedVector2.Zero
                        ? stampede.Aim * (Fixed.FromInt(stampede.Spec.WallHalfLength) / stampede.Aim.Length) : FixedVector2.Zero;
                    foreach (MatchUnit enemy in state.Units.Where(u => u.IsAlive && u.Team != bearer.Team &&
                                                                       (u.Kind == UnitKind.Hero || u.Kind == UnitKind.Creep || u.Kind == UnitKind.Summon) &&
                                                                       (u.Flags & ModifierFlags.Invulnerable) == 0 &&
                                                                       (stampede.Spec.WallHalfLength > 0
                                                                           ? Effects.NearSegment(u.Position, bearer.Position - along, bearer.Position + along, stampede.Spec.TrampleRadius + u.BoundingRadius)
                                                                           : Geometry.WithinRange(u.Position, bearer.Position, stampede.Spec.TrampleRadius + u.BoundingRadius))).ToList())
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
