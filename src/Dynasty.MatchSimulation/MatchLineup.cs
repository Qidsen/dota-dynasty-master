using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynasty.MatchSimulation
{
    /// <summary>One hero chosen for a match and the lane it starts in.</summary>
    public sealed class HeroPick
    {
        public HeroPick(string heroId, string laneId)
        {
            HeroId = heroId;
            LaneId = laneId;
        }

        public string HeroId { get; }
        public string LaneId { get; }
    }

    /// <summary>Heroes of both teams for one match (the outcome of a draft).</summary>
    public sealed class MatchLineup
    {
        public const int MaxHeroesPerTeam = 5;

        public MatchLineup(IReadOnlyList<HeroPick> dawn, IReadOnlyList<HeroPick> dusk)
        {
            Dawn = dawn.ToArray();
            Dusk = dusk.ToArray();
        }

        public static MatchLineup Empty { get; } = new MatchLineup(Array.Empty<HeroPick>(), Array.Empty<HeroPick>());

        public IReadOnlyList<HeroPick> Dawn { get; }
        public IReadOnlyList<HeroPick> Dusk { get; }
    }
}
