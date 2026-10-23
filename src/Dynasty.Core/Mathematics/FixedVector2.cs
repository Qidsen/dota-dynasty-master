using System;

namespace Dynasty.Core.Mathematics
{
    /// <summary>2D map-plane vector in fixed-point units.</summary>
    public readonly struct FixedVector2 : IEquatable<FixedVector2>
    {
        public FixedVector2(Fixed x, Fixed y)
        {
            X = x;
            Y = y;
        }

        public Fixed X { get; }
        public Fixed Y { get; }

        public static FixedVector2 Zero => default;

        public static FixedVector2 FromInts(int x, int y) => new FixedVector2(Fixed.FromInt(x), Fixed.FromInt(y));

        public Fixed LengthSquared => X * X + Y * Y;

        public Fixed Length => Fixed.Sqrt(LengthSquared);

        public static Fixed DistanceSquared(FixedVector2 a, FixedVector2 b) => (b - a).LengthSquared;

        public static Fixed Distance(FixedVector2 a, FixedVector2 b) => (b - a).Length;

        /// <summary>Moves at most <paramref name="maxStep"/> along the straight line toward the target.</summary>
        public static FixedVector2 MoveTowards(FixedVector2 from, FixedVector2 to, Fixed maxStep)
        {
            FixedVector2 delta = to - from;
            Fixed distance = delta.Length;
            if (distance <= maxStep)
                return to;

            // Multiply before dividing to keep exact results for exact inputs.
            return new FixedVector2(
                from.X + delta.X * maxStep / distance,
                from.Y + delta.Y * maxStep / distance);
        }

        /// <summary>Point reflection; used to build mirror-symmetric maps.</summary>
        public FixedVector2 ReflectThrough(FixedVector2 center) =>
            new FixedVector2(center.X + center.X - X, center.Y + center.Y - Y);

        public bool Equals(FixedVector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is FixedVector2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => "(" + X + ", " + Y + ")";

        public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.X + b.X, a.Y + b.Y);
        public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.X - b.X, a.Y - b.Y);
        public static FixedVector2 operator *(FixedVector2 v, Fixed s) => new FixedVector2(v.X * s, v.Y * s);
        public static bool operator ==(FixedVector2 a, FixedVector2 b) => a.Equals(b);
        public static bool operator !=(FixedVector2 a, FixedVector2 b) => !a.Equals(b);
    }
}
