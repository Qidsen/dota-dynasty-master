using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Dota's Twin Gates (game file <c>twin_gate_portal_warp</c>; Fandom «Twin Gate»; Valve 7.35, 7.40, 7.41e): a hero within
    /// <see cref="Radius"/> of one of the two gates pays <see cref="ManaCost"/> and channels <see cref="ChannelMs"/>, then stands by
    /// the other; a stun, a root or another order breaks the channel and gives the mana back; a rooted hero cannot start it.
    /// </summary>
    public sealed class TwinGateRules
    {
        public TwinGateRules(IReadOnlyList<FixedVector2> positions, int radius, int channelMs, int manaCost)
        {
            Positions = positions.ToArray();
            Radius = radius;
            ChannelMs = channelMs;
            ManaCost = manaCost;
        }

        /// <summary>The two gates.</summary>
        public IReadOnlyList<FixedVector2> Positions { get; }

        public int Radius { get; }
        public int ChannelMs { get; }
        public int ManaCost { get; }

        /// <summary>The gate at a point (the nearest), and the other one.</summary>
        public (FixedVector2 From, FixedVector2 To) Route(FixedVector2 gate) =>
            FixedVector2.DistanceSquared(gate, Positions[0]) <= FixedVector2.DistanceSquared(gate, Positions[1])
                ? (Positions[0], Positions[1])
                : (Positions[1], Positions[0]);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Positions.Count);
            foreach (FixedVector2 position in Positions)
                w.WriteVector(position);
            w.WriteInt64(Radius);
            w.WriteInt64(ChannelMs);
            w.WriteInt64(ManaCost);
        }
    }
}
