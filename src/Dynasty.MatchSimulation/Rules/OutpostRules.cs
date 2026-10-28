using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Dota's Outposts (Fandom «Outpost»; Valve 7.24, 7.33, 7.36): buildings that cannot be attacked, start owned as given
    /// (<see cref="Owners"/>, Dota: the side they stand on), and are captured by enemy heroes within <see cref="Radius"/> in
    /// <see cref="CaptureMs"/> divided by how many of them are there; the owner's heroes can teleport to them. No vision, no experience.
    /// </summary>
    public sealed class OutpostRules
    {
        public OutpostRules(IReadOnlyList<FixedVector2> positions, IReadOnlyList<Team> owners, int radius, int captureMs)
        {
            Positions = positions.ToArray();
            Owners = owners.ToArray();
            Radius = radius;
            CaptureMs = captureMs;
        }

        public IReadOnlyList<FixedVector2> Positions { get; }
        public IReadOnlyList<Team> Owners { get; }
        public int Radius { get; }
        public int CaptureMs { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Positions.Count);
            foreach (FixedVector2 position in Positions)
                w.WriteVector(position);
            w.WriteInt64(Owners.Count);
            foreach (Team owner in Owners)
                w.WriteInt64((int)owner);
            w.WriteInt64(Radius);
            w.WriteInt64(CaptureMs);
        }
    }
}
