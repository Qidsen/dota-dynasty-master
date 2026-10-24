using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynasty.Players
{
    /// <summary>Qualities a player has but nobody sees directly (spec §14); 1–20.</summary>
    public sealed class HiddenQualities
    {
        public HiddenQualities(int potential, int professionalism, int loyalty, int burnoutProneness, int learningSpeed)
        {
            Potential = potential;
            Professionalism = professionalism;
            Loyalty = loyalty;
            BurnoutProneness = burnoutProneness;
            LearningSpeed = learningSpeed;
        }

        /// <summary>The level the player's skills can grow to.</summary>
        public int Potential { get; }

        public int Professionalism { get; }
        public int Loyalty { get; }
        public int BurnoutProneness { get; }
        public int LearningSpeed { get; }
    }

    /// <summary>
    /// What a player knows of one hero (spec §15), 0–100 each. A nerf does not erase mastery:
    /// mechanical skill stays, only the hero's strength in the new balance changes.
    /// </summary>
    public sealed class HeroPoolRecord
    {
        public HeroPoolRecord(string heroId, int mastery, int recentPractice, int confidence, int patchFamiliarity, int matchupKnowledge, int roleFamiliarity,
            int gamesPlayed = 0, int wins = 0)
        {
            foreach (int value in new[] { mastery, recentPractice, confidence, patchFamiliarity, matchupKnowledge, roleFamiliarity })
            {
                if (value < 0 || value > 100)
                    throw new ArgumentOutOfRangeException(nameof(mastery), "Hero pool values are 0..100.");
            }

            HeroId = heroId;
            Mastery = mastery;
            RecentPractice = recentPractice;
            Confidence = confidence;
            PatchFamiliarity = patchFamiliarity;
            MatchupKnowledge = matchupKnowledge;
            RoleFamiliarity = roleFamiliarity;
            GamesPlayed = gamesPlayed;
            Wins = wins;
        }

        public string HeroId { get; }

        /// <summary>Long-term mastery.</summary>
        public int Mastery { get; }

        public int RecentPractice { get; }
        public int Confidence { get; }
        public int PatchFamiliarity { get; }
        public int MatchupKnowledge { get; }
        public int RoleFamiliarity { get; }
        public int GamesPlayed { get; }
        public int Wins { get; }

        /// <summary>How well the player can play the hero right now, 0–100: mastery, sharpened by practice and familiarity.</summary>
        public int Readiness => (Mastery * 6 + RecentPractice + Confidence + PatchFamiliarity + RoleFamiliarity) / 10;
    }

    /// <summary>A professional player (spec §14): identity, attributes, positions, hero pool and hidden qualities.</summary>
    public sealed class PlayerProfile
    {
        private readonly Dictionary<string, HeroPoolRecord> _pool;

        public PlayerProfile(string id, string handle, string name, string nationality, int birthYear, IReadOnlyList<string> languages, string region,
            IReadOnlyList<int> positionProficiency, AttributeSet attributes, HiddenQualities hidden, IReadOnlyList<HeroPoolRecord> heroPool)
        {
            if (positionProficiency.Count != 5 || positionProficiency.Any(p => p < 0 || p > 100))
                throw new ArgumentException("Position proficiency needs five values 0..100.", nameof(positionProficiency));
            Id = id;
            Handle = handle;
            Name = name;
            Nationality = nationality;
            BirthYear = birthYear;
            Languages = languages.ToArray();
            Region = region;
            PositionProficiency = positionProficiency.ToArray();
            Attributes = attributes;
            Hidden = hidden;
            _pool = heroPool.ToDictionary(h => h.HeroId);
            HeroPool = heroPool.OrderBy(h => h.HeroId, StringComparer.Ordinal).ToArray();
        }

        public string Id { get; }

        /// <summary>The in-game nickname.</summary>
        public string Handle { get; }

        public string Name { get; }
        public string Nationality { get; }
        public int BirthYear { get; }
        public IReadOnlyList<string> Languages { get; }
        public string Region { get; }

        /// <summary>Comfort on positions 1–5 (index 0 = position 1), 0–100.</summary>
        public IReadOnlyList<int> PositionProficiency { get; }

        public AttributeSet Attributes { get; }
        public HiddenQualities Hidden { get; }

        /// <summary>Heroes the player knows, ordered by hero ID.</summary>
        public IReadOnlyList<HeroPoolRecord> HeroPool { get; }

        public int Proficiency(int position) => PositionProficiency[position - 1];

        public HeroPoolRecord? Hero(string heroId) => _pool.TryGetValue(heroId, out HeroPoolRecord? record) ? record : null;
    }
}
