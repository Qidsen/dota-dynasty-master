using System.Collections.Generic;
using Dynasty.Core.Hashing;

namespace Dynasty.Core.Randomness
{
    /// <summary>
    /// Independent named random streams derived from one master seed. A stream's sequence
    /// depends only on (masterSeed, name), so adding a new consumer never shifts existing ones.
    /// </summary>
    public sealed class RandomStreams
    {
        private readonly ulong _masterSeed;
        private readonly Dictionary<string, DeterministicRandom> _streams = new Dictionary<string, DeterministicRandom>();

        public RandomStreams(ulong masterSeed)
        {
            _masterSeed = masterSeed;
        }

        public DeterministicRandom Get(string name)
        {
            if (!_streams.TryGetValue(name, out DeterministicRandom? stream))
            {
                ulong mixed = _masterSeed ^ Fnv1a64.HashString(name);
                stream = new DeterministicRandom(SplitMix64.Next(ref mixed));
                _streams.Add(name, stream);
            }

            return stream;
        }
    }
}
