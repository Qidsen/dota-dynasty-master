using System;
using System.Collections.Generic;
using Dynasty.Core.Randomness;

namespace Dynasty.Players
{
    public readonly struct AttributeRange
    {
        public AttributeRange(int min, int max)
        {
            Min = min;
            Max = max;
        }

        public int Min { get; }
        public int Max { get; }
        public int Width => Max - Min;
    }

    /// <summary>
    /// What a scout sees of a player (spec §14): an interval per attribute and a confidence, never
    /// the exact value. The truth is always inside; better scouts give narrower intervals. Where the
    /// truth sits inside the interval is random, so the middle is not the answer.
    /// </summary>
    public sealed class ScoutReport
    {
        private readonly Dictionary<Attribute, AttributeRange> _ranges;

        private ScoutReport(string playerId, Dictionary<Attribute, AttributeRange> ranges, int confidencePercent)
        {
            PlayerId = playerId;
            _ranges = ranges;
            ConfidencePercent = confidencePercent;
        }

        public string PlayerId { get; }

        /// <summary>How sure the scout is about the report, 0–100.</summary>
        public int ConfidencePercent { get; }

        public AttributeRange Range(Attribute attribute) => _ranges[attribute];

        public static ScoutReport Create(PlayerProfile player, int scoutAccuracy, DeterministicRandom random)
        {
            if (scoutAccuracy < Attributes.Min || scoutAccuracy > Attributes.Max)
                throw new ArgumentOutOfRangeException(nameof(scoutAccuracy));
            int width = Math.Max(1, (Attributes.Max - scoutAccuracy) / 2 + 1);   // accuracy 20 → ±~1, accuracy 1 → ±~10
            var ranges = new Dictionary<Attribute, AttributeRange>();
            foreach (Attribute attribute in Attributes.All)
            {
                int truth = player.Attributes[attribute];
                int below = random.NextInt(0, width + 1);
                int min = Math.Max(Attributes.Min, truth - below);
                int max = Math.Min(Attributes.Max, truth + (width - below));
                ranges[attribute] = new AttributeRange(min, max);
            }

            return new ScoutReport(player.Id, ranges, 30 + scoutAccuracy * 3);
        }
    }
}
