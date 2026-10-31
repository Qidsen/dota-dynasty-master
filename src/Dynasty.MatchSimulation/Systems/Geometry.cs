using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>Range checks between units. Ranges are edge-to-edge: center distance minus both radii.</summary>
    internal static class Geometry
    {
        public static bool WithinEdgeRange(MatchUnit a, MatchUnit b, int range)
        {
            Fixed reach = Fixed.FromInt(range + a.BoundingRadius + b.BoundingRadius);
            return FixedVector2.DistanceSquared(a.Position, b.Position) <= reach * reach;
        }

        /// <summary>Like <see cref="WithinEdgeRange(MatchUnit, MatchUnit, int)"/>, against the target's tick-start position.</summary>
        public static bool WithinEdgeRangeOfStart(MatchUnit unit, MatchUnit target, int range)
        {
            Fixed reach = Fixed.FromInt(range + unit.BoundingRadius + target.BoundingRadius);
            return FixedVector2.DistanceSquared(unit.Position, target.StartPosition) <= reach * reach;
        }

        public static bool WithinRange(FixedVector2 a, FixedVector2 b, int range)
        {
            Fixed reach = Fixed.FromInt(range);
            return FixedVector2.DistanceSquared(a, b) <= reach * reach;
        }

        public static Fixed EdgeDistance(MatchUnit a, MatchUnit b) =>
            FixedVector2.Distance(a.Position, b.Position) - Fixed.FromInt(a.BoundingRadius + b.BoundingRadius);

        /// <summary>The unit vector at a whole number of degrees from the x axis, from the cosine table (no floating point).</summary>
        public static FixedVector2 Direction(int degrees)
        {
            int angle = ((degrees % 360) + 360) % 360;
            int sine = ((90 - angle) % 360 + 360) % 360;
            return new FixedVector2(Cos(angle), Cos(sine));
        }

        private static Fixed Cos(int angle) =>
            Fixed.FromInt(Combat.Damage.CosTenThousandths[angle <= 180 ? angle : 360 - angle]) / Fixed.FromInt(10000);
    }
}
