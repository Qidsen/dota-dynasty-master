using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// A circular area of raised ground. Movement is unaffected (slopes are implicit); vision is:
    /// units only see targets standing on their own level or below.
    /// </summary>
    public sealed class ElevationZone
    {
        public ElevationZone(string id, FixedVector2 center, int radius, int level)
        {
            Id = id;
            Center = center;
            Radius = radius;
            Level = level;
        }

        public string Id { get; }
        public FixedVector2 Center { get; }
        public int Radius { get; }

        /// <summary>Ground level inside the zone; 0 is low ground.</summary>
        public int Level { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteVector(Center);
            w.WriteInt64(Radius);
            w.WriteInt64(Level);
        }
    }
}
