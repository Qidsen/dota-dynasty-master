using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// Dota's Tormentor (game file <c>npc_dota_miniboss</c>; Valve 7.38–7.41f): a neutral that cannot move or attack, guarded by a
    /// regenerating barrier, reflecting part of the damage it takes to the heroes around and whoever dealt it, burning everything
    /// around for a while after a hit; it stands on the side away from the boss (by day <see cref="DayPosition"/>, by night
    /// <see cref="NightPosition"/>, moving with the time of day), and killed it pays the killing team and gives one hero an item.
    /// Values growing with time grow every <see cref="GrowthIntervalMs"/> of match time (Dota: a minute).
    /// </summary>
    public sealed class MinibossDefinition
    {
        public MinibossDefinition(string id, string creepTypeId, FixedVector2 dayPosition, FixedVector2 nightPosition, int firstSpawnMs, int respawnMs,
            int teamGold, string? rewardItemId, int teamGoldWithoutItem, int growthIntervalMs,
            int barrier, int barrierPerInterval, Fixed barrierRegen, Fixed barrierRegenPerInterval,
            Fixed reflectPercent, Fixed reflectPercentPerInterval, int reflectRadius, int reflectIntervalMs,
            int shiningRadius, Fixed shiningDamage, Fixed shiningDamagePerInterval, int shiningIntervalMs, int shiningAfterHitMs,
            ModifierSpec? deathModifier = null, int deathModifierRadius = 0)
        {
            Id = id;
            CreepTypeId = creepTypeId;
            DayPosition = dayPosition;
            NightPosition = nightPosition;
            FirstSpawnMs = firstSpawnMs;
            RespawnMs = respawnMs;
            TeamGold = teamGold;
            RewardItemId = rewardItemId;
            TeamGoldWithoutItem = teamGoldWithoutItem;
            GrowthIntervalMs = growthIntervalMs;
            Barrier = barrier;
            BarrierPerInterval = barrierPerInterval;
            BarrierRegen = barrierRegen;
            BarrierRegenPerInterval = barrierRegenPerInterval;
            ReflectPercent = reflectPercent;
            ReflectPercentPerInterval = reflectPercentPerInterval;
            ReflectRadius = reflectRadius;
            ReflectIntervalMs = reflectIntervalMs;
            ShiningRadius = shiningRadius;
            ShiningDamage = shiningDamage;
            ShiningDamagePerInterval = shiningDamagePerInterval;
            ShiningIntervalMs = shiningIntervalMs;
            ShiningAfterHitMs = shiningAfterHitMs;
            DeathModifier = deathModifier;
            DeathModifierRadius = deathModifierRadius;
        }

        public string Id { get; }

        /// <summary>Stats come from this creep type.</summary>
        public string CreepTypeId { get; }

        public FixedVector2 DayPosition { get; }
        public FixedVector2 NightPosition { get; }
        public FixedVector2 PositionAt(bool night) => night ? NightPosition : DayPosition;

        public int FirstSpawnMs { get; }

        /// <summary>From its death to its next spawn.</summary>
        public int RespawnMs { get; }

        /// <summary>To every hero of the killing team.</summary>
        public int TeamGold { get; }

        /// <summary>Given to the killing team's hero with the lowest net worth that has no item of its kind (Dota: Aghanim's Shard).</summary>
        public string? RewardItemId { get; }

        /// <summary>More gold to every hero of the killing team when they all have the item already.</summary>
        public int TeamGoldWithoutItem { get; }

        public int GrowthIntervalMs { get; }

        public int Barrier { get; }
        public int BarrierPerInterval { get; }

        /// <summary>Barrier regenerated a second.</summary>
        public Fixed BarrierRegen { get; }

        public Fixed BarrierRegenPerInterval { get; }

        /// <summary>Percent of the damage taken (before any reduction) sent back, split among the heroes around and the dealers.</summary>
        public Fixed ReflectPercent { get; }

        public Fixed ReflectPercentPerInterval { get; }
        public int ReflectRadius { get; }

        /// <summary>The reflected damage goes out in a wave this often.</summary>
        public int ReflectIntervalMs { get; }

        public int ShiningRadius { get; }

        /// <summary>Magical damage a second to every unit around while it was hit in the last <see cref="ShiningAfterHitMs"/>.</summary>
        public Fixed ShiningDamage { get; }

        public Fixed ShiningDamagePerInterval { get; }
        public int ShiningIntervalMs { get; }
        public int ShiningAfterHitMs { get; }

        /// <summary>Given at its death to every hero within <see cref="DeathModifierRadius"/> (Dota: Alleviation); null for none.</summary>
        public ModifierSpec? DeathModifier { get; }

        public int DeathModifierRadius { get; }

        /// <summary>Whole growth intervals passed at a match time.</summary>
        public int IntervalsAt(long timeMs) => GrowthIntervalMs <= 0 ? 0 : (int)(timeMs / GrowthIntervalMs);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteString(CreepTypeId);
            w.WriteVector(DayPosition);
            w.WriteVector(NightPosition);
            foreach (int value in new[]
                     {
                         FirstSpawnMs, RespawnMs, TeamGold, TeamGoldWithoutItem, GrowthIntervalMs, Barrier, BarrierPerInterval, ReflectRadius,
                         ReflectIntervalMs, ShiningRadius, ShiningIntervalMs, ShiningAfterHitMs, DeathModifierRadius,
                     })
                w.WriteInt64(value);
            w.WriteString(RewardItemId ?? "");
            foreach (Fixed value in new[] { BarrierRegen, BarrierRegenPerInterval, ReflectPercent, ReflectPercentPerInterval, ShiningDamage, ShiningDamagePerInterval })
                w.WriteFixed(value);
            w.WriteBool(DeathModifier != null);
            DeathModifier?.WriteCanonical(w);
        }
    }
}
