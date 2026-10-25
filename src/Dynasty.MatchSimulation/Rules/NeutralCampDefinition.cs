using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// A neutral jungle camp: its creeps ignore passers-by, fight back together when one of them is
    /// hit, never leave the leash circle, and the camp respawns a fixed time after being cleared — or,
    /// aligned to the clock (Dota), at the next spawn time: first spawn plus whole respawn intervals.
    /// </summary>
    public sealed class NeutralCampDefinition
    {
        public NeutralCampDefinition(string id, FixedVector2 position, IReadOnlyList<WaveEntry> creeps, int firstSpawnMs, int respawnMs, int leashRadius, bool alignedToClock = false)
        {
            AlignedToClock = alignedToClock;
            Id = id;
            Position = position;
            Creeps = creeps.ToArray();
            FirstSpawnMs = firstSpawnMs;
            RespawnMs = respawnMs;
            LeashRadius = leashRadius;
        }

        public string Id { get; }
        public FixedVector2 Position { get; }
        public IReadOnlyList<WaveEntry> Creeps { get; }
        public int FirstSpawnMs { get; }

        /// <summary>Delay between the camp being cleared and its next spawn.</summary>
        public int RespawnMs { get; }

        public int LeashRadius { get; }

        /// <summary>A cleared camp spawns at the next first-spawn-plus-intervals mark instead of an interval after clearing.</summary>
        public bool AlignedToClock { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteVector(Position);
            w.WriteInt64(Creeps.Count);
            foreach (WaveEntry entry in Creeps)
            {
                w.WriteString(entry.CreepTypeId);
                w.WriteInt64(entry.Count);
            }

            w.WriteInt64(FirstSpawnMs);
            w.WriteInt64(RespawnMs);
            w.WriteInt64(LeashRadius);
            w.WriteBool(AlignedToClock);
        }
    }
}
