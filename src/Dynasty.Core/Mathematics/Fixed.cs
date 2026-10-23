using System;

namespace Dynasty.Core.Mathematics
{
    /// <summary>
    /// Signed Q47.16 fixed-point number (64-bit raw, 16 fractional bits). All authoritative
    /// simulation quantities use it instead of float/double so results are bit-identical on
    /// .NET, Mono and IL2CPP (see DECISION_LOG D-003).
    /// Precision is 1/65536. Multiplication is checked: operands up to roughly ±46 000 are
    /// always safe; larger products throw <see cref="OverflowException"/> instead of wrapping.
    /// Shifts floor toward negative infinity; division truncates toward zero.
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
    {
        public const int FractionalBits = 16;
        private const long OneRaw = 1L << FractionalBits;

        private Fixed(long raw)
        {
            Raw = raw;
        }

        public long Raw { get; }

        public static Fixed Zero => default;
        public static Fixed One => new Fixed(OneRaw);

        public static Fixed FromRaw(long raw) => new Fixed(raw);

        public static Fixed FromInt(int value) => new Fixed((long)value << FractionalBits);

        public static Fixed FromRatio(long numerator, long denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException();
            return new Fixed(checked(numerator << FractionalBits) / denominator);
        }

        public int ToIntFloor() => (int)(Raw >> FractionalBits);

        /// <summary>Display/diagnostics only. Never feed the result back into the simulation.</summary>
        public double ToDouble() => (double)Raw / OneRaw;

        public static Fixed Sqrt(Fixed value)
        {
            if (value.Raw < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Square root of a negative number.");
            if (value.Raw > (long.MaxValue >> FractionalBits))
                throw new OverflowException("Value too large for fixed-point square root.");

            // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16)
            return new Fixed((long)IntegerSqrt((ulong)value.Raw << FractionalBits));
        }

        public static Fixed Abs(Fixed value) => value.Raw < 0 ? new Fixed(-value.Raw) : value;
        public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
        public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

        public override string ToString() => ToDouble().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        public static Fixed operator +(Fixed a, Fixed b) => new Fixed(checked(a.Raw + b.Raw));
        public static Fixed operator -(Fixed a, Fixed b) => new Fixed(checked(a.Raw - b.Raw));
        public static Fixed operator -(Fixed a) => new Fixed(checked(-a.Raw));
        public static Fixed operator *(Fixed a, Fixed b) => new Fixed(checked(a.Raw * b.Raw) >> FractionalBits);

        public static Fixed operator /(Fixed a, Fixed b)
        {
            if (b.Raw == 0)
                throw new DivideByZeroException();
            return new Fixed(checked(a.Raw << FractionalBits) / b.Raw);
        }

        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

        /// <summary>Floor of the square root, bit-by-bit; exact and branch-deterministic.</summary>
        private static ulong IntegerSqrt(ulong n)
        {
            ulong result = 0;
            ulong bit = 1UL << 62;
            while (bit > n)
                bit >>= 2;

            while (bit != 0)
            {
                if (n >= result + bit)
                {
                    n -= result + bit;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }

                bit >>= 2;
            }

            return result;
        }
    }
}
