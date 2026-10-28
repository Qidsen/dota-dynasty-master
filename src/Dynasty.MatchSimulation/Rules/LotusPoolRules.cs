using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Dota's Lotus Pools (Fandom «Lotus Pool», «Healing Lotus»; Valve 7.38, 7.41; game file <c>ability_lotus_pool</c>): from
    /// <see cref="FirstMs"/> and every <see cref="IntervalMs"/> each pool grows a lotus, up to <see cref="Max"/>. A hero within
    /// <see cref="Radius"/> picks them one after another: the first takes <see cref="FirstPickMs"/>, each next one in a row
    /// <see cref="NextPickPercent"/>% of the one before, at least <see cref="MinPickMs"/>; each is an item (<see cref="ItemId"/>) for a
    /// random hero of the team in range. Heroes of both teams in range run the countdown back; nobody in range resets it and the run.
    /// </summary>
    public sealed class LotusPoolRules
    {
        public LotusPoolRules(IReadOnlyList<FixedVector2> positions, int firstMs, int intervalMs, int max, int radius, int firstPickMs, int nextPickPercent,
            int minPickMs, string itemId)
        {
            Positions = positions.ToArray();
            FirstMs = firstMs;
            IntervalMs = intervalMs;
            Max = max;
            Radius = radius;
            FirstPickMs = firstPickMs;
            NextPickPercent = nextPickPercent;
            MinPickMs = minPickMs;
            ItemId = itemId;
        }

        public IReadOnlyList<FixedVector2> Positions { get; }
        public int FirstMs { get; }
        public int IntervalMs { get; }
        public int Max { get; }
        public int Radius { get; }
        public int FirstPickMs { get; }
        public int NextPickPercent { get; }
        public int MinPickMs { get; }
        public string ItemId { get; }

        /// <summary>Lotuses grown so far at a match time (0 before the first).</summary>
        public int GrownAt(long timeMs) => timeMs < FirstMs || IntervalMs <= 0 ? 0 : (int)((timeMs - FirstMs) / IntervalMs) + 1;

        /// <summary>Time to pick the lotus after <paramref name="picked"/> already picked in a row.</summary>
        public int PickMs(int picked)
        {
            long ms = FirstPickMs;
            for (int i = 0; i < picked && ms > MinPickMs; i++)
                ms = ms * NextPickPercent / 100;
            return (int)System.Math.Max(MinPickMs, ms);
        }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Positions.Count);
            foreach (FixedVector2 position in Positions)
                w.WriteVector(position);
            foreach (int value in new[] { FirstMs, IntervalMs, Max, Radius, FirstPickMs, NextPickPercent, MinPickMs })
                w.WriteInt64(value);
            w.WriteString(ItemId);
        }
    }
}
