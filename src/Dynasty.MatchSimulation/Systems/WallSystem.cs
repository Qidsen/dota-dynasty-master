using System.Linq;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Keeps blocked units on their side of each standing ring wall: a unit that ended the tick across the wall (walking, pushed or
    /// separated) is put back just on its side; a blink or a teleport goes over it. Each unit's side is remembered from the tick the wall formed (or the unit first came
    /// under it), so the result does not depend on the order units moved in. Debuff-immune and teleported units pass and their side follows them.
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
                    if (wall.Shock.Count > 0 && !inside && unit.Team != wall.Team && unit.Id != wall.CasterPasses && !wall.Shocked.Contains(unit.Id) &&
                        offset.LengthSquared <= (radius + Fixed.FromInt(wall.ShockReach)) * (radius + Fixed.FromInt(wall.ShockReach)) &&
                        state.Find(wall.CasterPasses) is MatchUnit cogs)
                    {
                        // Power Cogs: a cog shocks an enemy outside coming near, once.
                        wall.Shocked.Add(unit.Id);
                        Effects.Run(context, cogs, wall.Shock, wall.Level, unit, unit.Position);
                        offset = unit.Position - wall.Position;
                        inside = offset.LengthSquared < radius * radius;
                    }

                    if (!wall.Known.Contains(unit.Id) || !Blocks(wall, unit) || unit.TeleportedTick == now)
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
                    if (ThroughOpening(wall, offset))
                    {
                        // An arc's way out: the unit crossed where there is no wall.
                        if (inside)
                            wall.Inside.Add(unit.Id);
                        else
                            wall.Inside.Remove(unit.Id);
                        continue;
                    }

                    Fixed length = offset.Length;
                    if (length == Fixed.Zero)
                        offset = FixedVector2.FromInts(1, 0);   // only when a unit outside landed on the center
                    Fixed at = wasInside ? radius - Fixed.One : radius + Fixed.One;
                    unit.Position = wall.Position + offset * (at / (length == Fixed.Zero ? Fixed.One : length));
                }
            }
        }

        /// <summary>Whether a unit at <paramref name="offset"/> from an arc's centre is in its opening (within half the gap of the way out).</summary>
        private static bool ThroughOpening(GroundWall wall, FixedVector2 offset)
        {
            if (wall.ArcDegrees <= 0 || wall.ArcDegrees >= 360 || wall.Opening == FixedVector2.Zero || offset == FixedVector2.Zero)
                return false;
            int halfGap = (360 - wall.ArcDegrees) / 2;
            // The offset's length along the way out, against cos(half the gap) times its length (no overflow: lengths of a few hundred).
            Fixed along = (offset.X * wall.Opening.X + offset.Y * wall.Opening.Y) / wall.Opening.Length;
            return along * Fixed.FromInt(10000) >= Fixed.FromInt(Damage.CosTenThousandths[halfGap]) * offset.Length;
        }

        private static bool Blocks(GroundWall wall, MatchUnit unit)
        {
            if (unit.Id == wall.CasterPasses)
                return false;   // Clockwerk walks through his cogs
            if ((unit.Flags & ModifierFlags.DebuffImmune) != 0 && !wall.BlocksImmune)
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
