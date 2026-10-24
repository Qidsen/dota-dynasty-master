using System.Collections.Generic;
using System.Linq;
using Dynasty.Players;

namespace Dynasty.HeroAI
{
    /// <summary>
    /// How quickly a team acts on a call (spec §17: chemistry shows as delayed calls, not a bonus).
    /// Teamwork across the team and its best leader shorten the delay; without a language every
    /// player speaks, calls take longer still.
    /// </summary>
    public static class TeamCommunication
    {
        /// <summary>Ticks between a team plan being called and the heroes acting on it.</summary>
        public static int CallDelayTicks(IReadOnlyList<PlayerProfile> players)
        {
            if (players.Count == 0)
                return 0;
            int teamwork = players.Sum(p => p.Attributes[Attribute.Teamwork]) / players.Count;
            int leadership = players.Max(p => p.Attributes[Attribute.Leadership]);
            int delay = (Attributes.Max - (teamwork + leadership) / 2) * 6;
            bool commonLanguage = players[0].Languages.Any(language => players.All(p => p.Languages.Contains(language)));
            return commonLanguage ? delay : delay + 45;
        }
    }
}
