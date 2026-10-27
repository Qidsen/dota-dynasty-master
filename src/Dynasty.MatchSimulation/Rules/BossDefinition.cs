using System.Collections.Generic;
using System.Linq;
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
            BossGrowth? growth = null, ModifierSpec? aegis = null, IReadOnlyList<BossAbility>? abilities = null, int provokeRadius = 300, IReadOnlyList<BossDrop>? drops = null)
        {
            Drops = (drops ?? System.Array.Empty<BossDrop>()).ToArray();
            ProvokeRadius = provokeRadius;
            Abilities = (abilities ?? System.Array.Empty<BossAbility>()).ToArray();
            Aegis = aegis;
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

        /// <summary>
        /// Dota's Aegis of the Immortal: given at every death to the killing team's richest living hero (in Dota it drops to be
        /// picked up); the boss's respawn takes an unused one back, as if it ran out. Null = none.
        /// </summary>
        public ModifierSpec? Aegis { get; }

        /// <summary>Heroes this close to the boss provoke it without attacking; 0 = only attackers (Dota's Roshan: «only attacking when aggroed»).</summary>
        public int ProvokeRadius { get; }

        /// <summary>Items its deaths give (Dota's Roshan: Cheese and a Refresher Shard from the third).</summary>
        public IReadOnlyList<BossDrop> Drops { get; }

        /// <summary>Spells the boss casts by itself (Dota's Roshan: Slam, Roar of Retribution).</summary>
        public IReadOnlyList<BossAbility> Abilities { get; }

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

            w.WriteBool(Aegis != null);
            Aegis?.WriteCanonical(w);
            w.WriteInt64(ProvokeRadius);
            w.WriteInt64(Drops.Count);
            foreach (BossDrop drop in Drops)
            {
                w.WriteString(drop.ItemId);
                w.WriteInt64(drop.FromDeath);
            }
            w.WriteInt64(Abilities.Count);
            foreach (BossAbility ability in Abilities)
                ability.WriteCanonical(w);
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

    /// <summary>
    /// An item a boss's death gives from its <see cref="FromDeath"/>-th death on, to the killing team's richest living hero with room
    /// in its inventory (in Dota it drops to the ground).
    /// </summary>
    public sealed class BossDrop
    {
        public BossDrop(string itemId, int fromDeath)
        {
            ItemId = itemId;
            FromDeath = fromDeath;
        }

        public string ItemId { get; }
        public int FromDeath { get; }
    }

    /// <summary>When a boss casts a spell of its own.</summary>
    public enum BossAbilityTrigger
    {
        /// <summary>While it fights and an enemy is within the spell's radius (Dota's Slam).</summary>
        Engaged = 0,

        /// <summary>When the team that last killed it hits it below a health percent (Dota's Roar of Retribution).</summary>
        HitByLastKillersBelowHealth = 1,
    }

    /// <summary>
    /// A boss's own area spell (game file `roshan_slam`, `roshan_revengeroar`): after its cast point it damages every
    /// non-neutral unit within the radius (creeps by a percent of it) and applies its modifiers to them; the damage grows by
    /// <see cref="DamagePerInterval"/> every growth interval of the boss.
    /// </summary>
    public sealed class BossAbility
    {
        public BossAbility(string id, BossAbilityTrigger trigger, int cooldownMs, int castPointMs, int radius, Combat.DamageType damageType, int damage,
            int damagePerInterval = 0, int creepDamagePercent = 100, IReadOnlyList<ModifierSpec>? modifiers = null, int belowHealthPercent = 100,
            bool ignoresDisables = false, bool dispelsSelf = false)
        {
            Id = id;
            Trigger = trigger;
            CooldownMs = cooldownMs;
            CastPointMs = castPointMs;
            Radius = radius;
            DamageType = damageType;
            Damage = damage;
            DamagePerInterval = damagePerInterval;
            CreepDamagePercent = creepDamagePercent;
            Modifiers = (modifiers ?? System.Array.Empty<ModifierSpec>()).ToArray();
            BelowHealthPercent = belowHealthPercent;
            IgnoresDisables = ignoresDisables;
            DispelsSelf = dispelsSelf;
        }

        public string Id { get; }
        public BossAbilityTrigger Trigger { get; }
        public int CooldownMs { get; }
        public int CastPointMs { get; }
        public int Radius { get; }
        public Combat.DamageType DamageType { get; }
        public int Damage { get; }
        public int DamagePerInterval { get; }

        /// <summary>Creeps take this percent of the damage (Dota's Slam: 200).</summary>
        public int CreepDamagePercent { get; }

        public IReadOnlyList<ModifierSpec> Modifiers { get; }

        /// <summary>For <see cref="BossAbilityTrigger.HitByLastKillersBelowHealth"/>: below this percent of its health.</summary>
        public int BelowHealthPercent { get; }

        /// <summary>Cast even when stunned or silenced (Dota 7.39: the Roar).</summary>
        public bool IgnoresDisables { get; }

        /// <summary>The cast strongly dispels the boss (Dota 7.39: the Roar).</summary>
        public bool DispelsSelf { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            foreach (int value in new[] { (int)Trigger, CooldownMs, CastPointMs, Radius, (int)DamageType, Damage, DamagePerInterval, CreepDamagePercent, BelowHealthPercent })
                w.WriteInt64(value);
            w.WriteBool(IgnoresDisables);
            w.WriteBool(DispelsSelf);
            w.WriteInt64(Modifiers.Count);
            foreach (ModifierSpec modifier in Modifiers)
                modifier.WriteCanonical(w);
        }
    }
}
