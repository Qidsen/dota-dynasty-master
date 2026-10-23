using System;

namespace Dynasty.Core.Randomness
{
    /// <summary>Serializable xoshiro256** state.</summary>
    public readonly struct RandomState
    {
        public RandomState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public ulong S0 { get; }
        public ulong S1 { get; }
        public ulong S2 { get; }
        public ulong S3 { get; }
    }

    /// <summary>
    /// xoshiro256** generator. Integer-only, so its output is identical on every
    /// platform and runtime (.NET, Mono, IL2CPP). Never use System.Random in simulation code.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public DeterministicRandom(ulong seed)
        {
            ulong sm = seed;
            _s0 = SplitMix64.Next(ref sm);
            _s1 = SplitMix64.Next(ref sm);
            _s2 = SplitMix64.Next(ref sm);
            _s3 = SplitMix64.Next(ref sm);
        }

        private DeterministicRandom(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            if ((s0 | s1 | s2 | s3) == 0)
                throw new ArgumentException("xoshiro256** state must not be all zeros.");
            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;
        }

        public RandomState State => new RandomState(_s0, _s1, _s2, _s3);

        public static DeterministicRandom FromState(RandomState state) =>
            new DeterministicRandom(state.S0, state.S1, state.S2, state.S3);

        public static DeterministicRandom FromState(ulong s0, ulong s1, ulong s2, ulong s3) =>
            new DeterministicRandom(s0, s1, s2, s3);

        public ulong NextUInt64()
        {
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);

            return result;
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive), without modulo bias.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range must not be empty.");

            ulong range = (ulong)((long)maxExclusive - minInclusive);
            ulong limit = ulong.MaxValue - ulong.MaxValue % range;
            ulong value;
            do
            {
                value = NextUInt64();
            } while (value >= limit);

            return (int)(minInclusive + (long)(value % range));
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
    }
}
