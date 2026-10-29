using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Keeps blocked units on their side of each standing ring wall: a unit that ended the tick across the wall (walking, pushed or
    /// separated) is put back just on its side. Each unit's side is remembered from the tick the wall formed (or the unit first came
    /// under it), so the result does not depend on the order units moved in. Debuff-immune units pass and their side follows them.
    /// Expired walls are removed.
    /// </summary>
    internal sealed class WallSystem : ISimulationSystem<MatchState>
    {
        public string Name => "walls";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            if (state.Walls.Count == 0)
                return;
            long now = context.Tick.Value;
            foreach (GroundWall wall in state.Walls.ToList())
            {
                if (now >= wall.ExpiresTick)
                {
                    state.RemoveWall(wall);
                    continue;
                }

                if (now < wall.FormsTick)
                    continue;
                Fixed radius = Fixed.FromInt(wall.Radius);
                foreach (MatchUnit unit in state.Units)
                {
                    if (!unit.IsAlive || (unit.Kind != UnitKind.Hero && unit.Kind != UnitKind.Creep && unit.Kind != UnitKind.Summon))
                        continue;
                    FixedVector2 offset = unit.Position - wall.Position;
                    bool inside = offset.LengthSquared < radius * radius;
                    if (!wall.Known.Contains(unit.Id) || !Blocks(wall, unit))
                    {
                        wall.Known.Add(unit.Id);
                        if (inside)
                            wall.Inside.Add(unit.Id);
                        else
                            wall.Inside.Remove(unit.Id);
                        continue;
                    }

                    bool wasInside = wall.Inside.Contains(unit.Id);
                    if (wasInside == inside)
                        continue;
                    Fixed length = offset.Length;
                    if (length == Fixed.Zero)
                        offset = FixedVector2.FromInts(1, 0);   // only when a unit outside landed on the center
                    Fixed at = wasInside ? radius - Fixed.One : radius + Fixed.One;
                    unit.Position = wall.Position + offset * (at / (length == Fixed.Zero ? Fixed.One : length));
                }
            }
        }

        private static bool Blocks(GroundWall wall, MatchUnit unit)
        {
            if ((unit.Flags & ModifierFlags.DebuffImmune) != 0)
                return false;
            switch (wall.Blocks)
            {
                case TargetTeam.Enemy:
                    return unit.Team != wall.Team;
                case TargetTeam.Ally:
                    return unit.Team == wall.Team;
                default:
                    return true;
            }
        }
    }
}
