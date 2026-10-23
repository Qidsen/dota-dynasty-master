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
            int respawnMinMs, int respawnMaxMs, int teamGold, ModifierSpec reward)
        {
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

        /// <summary>Applied to every living hero of the killing team.</summary>
        public ModifierSpec Reward { get; }

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
            Reward.WriteCanonical(w);
        }
    }
}
