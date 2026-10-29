using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Slashes in progress (Dota's Omnislash, the project owner on 2026-10-29): each is the hero's normal attack on a random visible
    /// enemy within the radius of the hero, the hero moved next to it; one every attack interval divided by the rate; they end
    /// with the slashing modifier, or at once when no enemy is near.
    /// </summary>
    internal sealed class SlashSystem : ISimulationSystem<MatchState>
    {
        public string Name => "slashes";

        public void Tick(SimulationContext<MatchState> context)
        {
            long now = context.Tick.Value;
            foreach (MatchUnit unit in context.State.Units.Where(u => u.Slashes != null).ToList())
            {
                SlashState slashes = unit.Slashes!;
                if (!unit.IsAlive || unit.Modifiers.All(m => m.Spec.Key != slashes.ModifierKey))
                {
                    unit.Slashes = null;
                    continue;
                }

                if (now < slashes.NextTick)
                    continue;
                List<MatchUnit> near = context.State.Units
                    .Where(u => Effects.Matches(context.State, unit, u, TargetTeam.Enemy) && context.State.CanAttack(unit, u) &&
                                context.State.IsVisibleTo(unit.Team, u) && Geometry.WithinRange(unit.Position, u.Position, slashes.Radius + u.BoundingRadius))
                    .OrderBy(u => u.Id.Value).ToList();
                if (near.Count == 0)
                {
                    End(context, unit);
                    continue;
                }

                Slash(context, unit, near.Count == 1 ? near[0] : near[context.Random("combat").NextInt(0, near.Count)]);
            }
        }

        /// <summary>One slash: the hero lands next to the target and attacks it; the next comes an attack interval divided by the rate later.</summary>
        internal static void Slash(SimulationContext<MatchState> context, MatchUnit unit, MatchUnit target)
        {
            SlashState slashes = unit.Slashes!;
            Fixed reach = Fixed.FromInt(unit.BoundingRadius + target.BoundingRadius);
            unit.Position = FixedVector2.MoveTowards(target.Position, unit.Position, reach);   // on the side it came from
            unit.StartPosition = unit.Position;
            unit.TeleportedTick = context.Tick.Value;
            AttackSystem.Strike(context, unit, target, 100);
            slashes.NextTick = context.Tick.Value + System.Math.Max(1, unit.AttackIntervalTicks * 100 / System.Math.Max(1, slashes.RatePercent));
        }

        private static void End(SimulationContext<MatchState> context, MatchUnit unit)
        {
            string key = unit.Slashes!.ModifierKey;
            unit.Slashes = null;
            foreach (ActiveModifier modifier in unit.Modifiers.Where(m => m.Spec.Key == key).ToList())
                Modifiers.Remove(context, unit, modifier, "slashes_ended");
        }
    }
}
