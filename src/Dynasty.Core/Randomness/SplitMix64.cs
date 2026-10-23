namespace Dynasty.Core.Randomness
{
    /// <summary>SplitMix64 generator, used to expand seeds into generator state.</summary>
    public static class SplitMix64
    {
        public static ulong Next(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
