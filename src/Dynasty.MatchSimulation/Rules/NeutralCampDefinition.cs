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
        public NeutralCampDefinition(string id, FixedVector2 position, IReadOnlyList<WaveEntry> creeps, int firstSpawnMs, int respawnMs, int leashRadius, bool alignedToClock = false,
            IReadOnlyList<IReadOnlyList<WaveEntry>>? variants = null, CampEvolution? evolution = null)
        {
            Variants = (variants ?? new[] { creeps }).Select(v => (IReadOnlyList<WaveEntry>)v.ToArray()).ToArray();
            Evolution = evolution;
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

        /// <summary>
        /// The camp types it may spawn (Dota: those of its tier), one at random each spawn and never the same twice in a row;
        /// just <see cref="Creeps"/> when not given.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<WaveEntry>> Variants { get; }

        /// <summary>Dota's flooded camps: stages by match time instead of the variants; null for other camps.</summary>
        public CampEvolution? Evolution { get; }

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
            WriteCompositions(w, Variants);
            w.WriteBool(Evolution != null);
            if (Evolution != null)
            {
                w.WriteInt64(Evolution.IntervalMs);
                WriteCompositions(w, Evolution.Stages);
            }
        }

        private static void WriteCompositions(CanonicalWriter w, IReadOnlyList<IReadOnlyList<WaveEntry>> compositions)
        {
            w.WriteInt64(compositions.Count);
            foreach (IReadOnlyList<WaveEntry> composition in compositions)
            {
                w.WriteInt64(composition.Count);
                foreach (WaveEntry entry in composition)
                {
                    w.WriteString(entry.CreepTypeId);
                    w.WriteInt64(entry.Count);
                }
            }
        }
    }

    /// <summary>
    /// A flooded camp's evolution (Liquipedia «Neutral Creeps», 7.41f): at every whole <see cref="IntervalMs"/> of match time the
    /// camp moves to its next stage, up to the last; an idle, unblocked camp is replaced by the new stage at once, otherwise the
    /// stage comes with its next spawn.
    /// </summary>
    public sealed class CampEvolution
    {
        public CampEvolution(int intervalMs, IReadOnlyList<IReadOnlyList<WaveEntry>> stages)
        {
            IntervalMs = intervalMs;
            Stages = stages.Select(s => (IReadOnlyList<WaveEntry>)s.ToArray()).ToArray();
        }

        public int IntervalMs { get; }
        public IReadOnlyList<IReadOnlyList<WaveEntry>> Stages { get; }

        /// <summary>The stage at a match time.</summary>
        public int StageAt(long timeMs) => IntervalMs <= 0 ? 0 : (int)System.Math.Min(timeMs / IntervalMs, Stages.Count - 1);
    }
}
