using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Dota's Shrines of Wisdom (Valve 7.38, 7.41; game file <c>ability_xp_fountain</c>): from <see cref="FirstMs"/> and every
    /// <see cref="IntervalMs"/> each shrine fills with experience; what is not taken stays and adds up (as Wisdom Runes). A hero within
    /// <see cref="Radius"/> for <see cref="CaptureMs"/> takes it all: every filling is worth <see cref="Xp"/> plus
    /// <see cref="XpPerInterval"/> for each activation since the first, by the clock at the taking; a random hero of the team in range
    /// and their teammate with the least experience each get it. Heroes of both teams in range run the countdown back.
    /// </summary>
    public sealed class ShrineRules
    {
        public ShrineRules(IReadOnlyList<FixedVector2> positions, int firstMs, int intervalMs, int radius, int captureMs, int xp, int xpPerInterval)
        {
            Positions = positions.ToArray();
            FirstMs = firstMs;
            IntervalMs = intervalMs;
            Radius = radius;
            CaptureMs = captureMs;
            Xp = xp;
            XpPerInterval = xpPerInterval;
        }

        public IReadOnlyList<FixedVector2> Positions { get; }
        public int FirstMs { get; }
        public int IntervalMs { get; }
        public int Radius { get; }
        public int CaptureMs { get; }
        public int Xp { get; }
        public int XpPerInterval { get; }

        /// <summary>Activations so far at a match time (0 before the first).</summary>
        public int ActivationsAt(long timeMs) => timeMs < FirstMs || IntervalMs <= 0 ? 0 : (int)((timeMs - FirstMs) / IntervalMs) + 1;

        /// <summary>What one filling is worth at a match time.</summary>
        public int XpAt(long timeMs) => Xp + XpPerInterval * System.Math.Max(0, ActivationsAt(timeMs) - 1);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Positions.Count);
            foreach (FixedVector2 position in Positions)
                w.WriteVector(position);
            foreach (int value in new[] { FirstMs, IntervalMs, Radius, CaptureMs, Xp, XpPerInterval })
                w.WriteInt64(value);
        }
    }
}
