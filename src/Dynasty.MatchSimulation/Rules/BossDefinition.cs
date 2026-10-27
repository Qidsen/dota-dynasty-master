using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// A neutral objective that lives in a pit, ignores creeps, defends itself, and rewards the
    /// team that kills it with gold and a timed team-wide modifier.
    /// </summary>
    public sealed class BossDefinition
    {
        public BossDefinition(string id, string creepTypeId, FixedVector2 position, int leashRadius, int firstSpawnMs,
            int respawnMinMs, int respawnMaxMs, int teamGold, ModifierSpec? reward, int killerGoldMin = 0, int killerGoldMax = 0, int xp = 0,
            BossGrowth? growth = null)
        {
            KillerGoldMin = killerGoldMin;
            KillerGoldMax = killerGoldMax;
            Xp = xp;
            Growth = growth;
            Id = id;
            CreepTypeId = creepTypeId;
            Position = position;
            LeashRadius = leashRadius;
            FirstSpawnMs = firstSpawnMs;
            RespawnMinMs = respawnMinMs;
            RespawnMaxMs = respawnMaxMs;
            TeamGold = teamGold;
            Reward = reward;
        }

        public string Id { get; }

        /// <summary>Stats come from this creep type.</summary>
        public string CreepTypeId { get; }

        /// <summary>Center of the pit; the boss spawns and returns here.</summary>
        public FixedVector2 Position { get; }

        /// <summary>The boss never follows a target beyond this distance from its pit.</summary>
        public int LeashRadius { get; }

        public int FirstSpawnMs { get; }
        public int RespawnMinMs { get; }
        public int RespawnMaxMs { get; }

        /// <summary>Gold for every hero of the killing team.</summary>
        public int TeamGold { get; }

        /// <summary>Applied to every living hero of the killing team; null = none (Dota's Roshan).</summary>
        public ModifierSpec? Reward { get; }

        /// <summary>Gold on top for the hero that landed the last hit (Dota: 200–290).</summary>
        public int KillerGoldMin { get; }
        public int KillerGoldMax { get; }

        /// <summary>XP the killing team's heroes around share (with <see cref="BossGrowth.Xp"/> per interval).</summary>
        public int Xp { get; }

        /// <summary>Dota's Roshan grows every interval from the horn, alive or dead; null = never.</summary>
        public BossGrowth? Growth { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteString(CreepTypeId);
            w.WriteVector(Position);
            w.WriteInt64(LeashRadius);
            w.WriteInt64(FirstSpawnMs);
            w.WriteInt64(RespawnMinMs);
            w.WriteInt64(RespawnMaxMs);
            w.WriteInt64(TeamGold);
            w.WriteBool(Reward != null);
            Reward?.WriteCanonical(w);
            w.WriteInt64(KillerGoldMin);
            w.WriteInt64(KillerGoldMax);
            w.WriteInt64(Xp);
            w.WriteBool(Growth != null);
            if (Growth != null)
            {
                w.WriteInt64(Growth.IntervalMs);
                w.WriteInt64(Growth.Health);
                w.WriteFixed(Growth.Armor);
                w.WriteInt64(Growth.Damage);
                w.WriteInt64(Growth.Xp);
                w.WriteBool(Growth.ArmorFromStart);
            }
        }
    }

    /// <summary>
    /// Dota's Roshan growth (Liquipedia «Roshan»; D-051): every interval from the horn, alive or dead, more health, armor,
    /// attack damage and XP bounty; the armor's first step already at the horn.
    /// </summary>
    public sealed class BossGrowth
    {
        public BossGrowth(int intervalMs, int health, Fixed armor, int damage, int xp, bool armorFromStart)
        {
            IntervalMs = intervalMs;
            Health = health;
            Armor = armor;
            Damage = damage;
            Xp = xp;
            ArmorFromStart = armorFromStart;
        }

        public int IntervalMs { get; }
        public int Health { get; }
        public Fixed Armor { get; }
        public int Damage { get; }
        public int Xp { get; }
        public bool ArmorFromStart { get; }

        /// <summary>Whole intervals passed at this match time.</summary>
        public int IntervalsAt(long timeMs) => (int)(timeMs / IntervalMs);
    }
}
