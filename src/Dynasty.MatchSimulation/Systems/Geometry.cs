using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>Range checks between units. Ranges are edge-to-edge: center distance minus both radii.</summary>
    internal static class Geometry
    {
        public static bool WithinEdgeRange(MatchUnit a, MatchUnit b, int range)
        {
            Fixed reach = Fixed.FromInt(range + a.Stats.BoundingRadius + b.Stats.BoundingRadius);
            return FixedVector2.DistanceSquared(a.Position, b.Position) <= reach * reach;
        }

        public static Fixed EdgeDistance(MatchUnit a, MatchUnit b) =>
            FixedVector2.Distance(a.Position, b.Position) - Fixed.FromInt(a.Stats.BoundingRadius + b.Stats.BoundingRadius);
    }
}
