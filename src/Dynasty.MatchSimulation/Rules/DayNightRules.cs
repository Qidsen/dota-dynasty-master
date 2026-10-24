using Dynasty.Core.Hashing;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>A repeating day/night cycle starting with day; at night units use their night vision range.</summary>
    public sealed class DayNightRules
    {
        public DayNightRules(int dayMs, int nightMs)
        {
            DayMs = dayMs;
            NightMs = nightMs;
        }

        public int DayMs { get; }
        public int NightMs { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(DayMs);
            w.WriteInt64(NightMs);
        }
    }
}
