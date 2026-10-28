using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Dota's Watchers (game file <c>npc_dota_lantern</c>, <c>ability_lamp_use</c>; Fandom «Watcher»; Valve 7.39c, 7.40): a hero of a
    /// team within <see cref="Radius"/> for <see cref="ChannelMs"/> turns a ready Watcher on for the team for <see cref="OnMs"/> — the
    /// team sees around it, <see cref="DayVision"/> by day and <see cref="NightVision"/> by night — or, when it is on for the other
    /// team, turns it off for <see cref="OffMs"/>, when nobody can take it.
    /// </summary>
    public sealed class WatcherRules
    {
        public WatcherRules(IReadOnlyList<FixedVector2> positions, int radius, int channelMs, int onMs, int offMs, int dayVision, int nightVision)
        {
            Positions = positions.ToArray();
            Radius = radius;
            ChannelMs = channelMs;
            OnMs = onMs;
            OffMs = offMs;
            DayVision = dayVision;
            NightVision = nightVision;
        }

        public IReadOnlyList<FixedVector2> Positions { get; }
        public int Radius { get; }
        public int ChannelMs { get; }
        public int OnMs { get; }
        public int OffMs { get; }
        public int DayVision { get; }
        public int NightVision { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Positions.Count);
            foreach (FixedVector2 position in Positions)
                w.WriteVector(position);
            foreach (int value in new[] { Radius, ChannelMs, OnMs, OffMs, DayVision, NightVision })
                w.WriteInt64(value);
        }
    }
}
