using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynasty.Drafting
{
    /// <summary>
    /// What a player knows of one hero (spec §15). Phase 05 uses long-term mastery and current
    /// confidence; practice, patch familiarity, matchup knowledge and history come with the player model (phase 06).
    /// </summary>
    public sealed class HeroPoolEntry
    {
        public HeroPoolEntry(string heroId, int mastery, int confidence)
        {
            if (mastery < 0 || mastery > 100 || confidence < 0 || confidence > 100)
                throw new ArgumentOutOfRangeException(nameof(mastery), "Mastery and confidence are 0..100.");
            HeroId = heroId;
            Mastery = mastery;
            Confidence = confidence;
        }

        public string HeroId { get; }

        /// <summary>Long-term mastery, 0..100.</summary>
        public int Mastery { get; }

        /// <summary>Current confidence on the hero, 0..100.</summary>
        public int Confidence { get; }
    }

    /// <summary>A player as the draft sees them: the position they play and their hero pool.</summary>
    public sealed class RosterPlayer
    {
        private readonly Dictionary<string, HeroPoolEntry> _pool;

        public RosterPlayer(string id, string name, int position, IReadOnlyList<HeroPoolEntry> pool)
        {
            if (position < 1 || position > 5)
                throw new ArgumentOutOfRangeException(nameof(position), "Positions are 1..5.");
            Id = id;
            Name = name;
            Position = position;
            _pool = pool.ToDictionary(e => e.HeroId);
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Position 1–5 the player plays in this team.</summary>
        public int Position { get; }

        public IEnumerable<HeroPoolEntry> Pool => _pool.Values.OrderBy(e => e.HeroId, StringComparer.Ordinal);

        /// <summary>The player's entry for a hero, or null if the hero is outside their pool.</summary>
        public HeroPoolEntry? Entry(string heroId) => _pool.TryGetValue(heroId, out HeroPoolEntry? entry) ? entry : null;
    }

    /// <summary>Five players, one per position.</summary>
    public sealed class TeamRoster
    {
        public TeamRoster(string name, IReadOnlyList<RosterPlayer> players)
        {
            if (players.Count != 5 || players.Select(p => p.Position).Distinct().Count() != 5)
                throw new ArgumentException("A roster needs five players on positions 1..5.", nameof(players));
            Name = name;
            Players = players.OrderBy(p => p.Position).ToArray();
        }

        public string Name { get; }

        /// <summary>Ordered by position (index 0 = position 1).</summary>
        public IReadOnlyList<RosterPlayer> Players { get; }

        public RosterPlayer At(int position) => Players[position - 1];
    }
}
