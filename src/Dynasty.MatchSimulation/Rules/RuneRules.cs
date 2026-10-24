using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>What a rune gives the hero who picks it up: a modifier, gold, or both.</summary>
    public sealed class RuneType
    {
        public RuneType(string id, ModifierSpec? effect = null, int gold = 0)
        {
            Id = id;
            Effect = effect;
            Gold = gold;
        }

        public string Id { get; }
        public ModifierSpec? Effect { get; }
        public int Gold { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteBool(Effect != null);
            Effect?.WriteCanonical(w);
            w.WriteInt64(Gold);
        }
    }

    /// <summary>
    /// Power-up runes: on every interval each empty spot gets a rune of a random type (stream
    /// "runes"); a hero standing within <see cref="PickupRadius"/> picks it up — the closest one,
    /// exact ties decided by the same stream, never by processing order.
    /// </summary>
    public sealed class RuneRules
    {
        public RuneRules(IReadOnlyList<FixedVector2> spots, int firstSpawnMs, int intervalMs, IReadOnlyList<RuneType> types, int pickupRadius = 100)
        {
            Spots = spots.ToArray();
            FirstSpawnMs = firstSpawnMs;
            IntervalMs = intervalMs;
            Types = types.ToArray();
            PickupRadius = pickupRadius;
        }

        public IReadOnlyList<FixedVector2> Spots { get; }
        public int FirstSpawnMs { get; }
        public int IntervalMs { get; }
        public IReadOnlyList<RuneType> Types { get; }
        public int PickupRadius { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Spots.Count);
            foreach (FixedVector2 spot in Spots)
                w.WriteVector(spot);
            w.WriteInt64(FirstSpawnMs);
            w.WriteInt64(IntervalMs);
            w.WriteInt64(Types.Count);
            foreach (RuneType type in Types)
                type.WriteCanonical(w);
            w.WriteInt64(PickupRadius);
        }
    }
}
