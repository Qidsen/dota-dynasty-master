using Dynasty.Core.Events;
using Dynasty.Core.Hashing;
using Dynasty.Core.Ids;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Events
{
    public sealed class CreepSpawned : IDomainEvent
    {
        public CreepSpawned(EntityId unit, Team team, string creepTypeId, string laneId)
        {
            Unit = unit;
            Team = team;
            CreepTypeId = creepTypeId;
            LaneId = laneId;
        }

        public EntityId Unit { get; }
        public Team Team { get; }
        public string CreepTypeId { get; }
        public string LaneId { get; }

        public string Type => "match.creep_spawned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Unit.Value);
            hash.AddInt64((int)Team);
            hash.AddString(CreepTypeId);
            hash.AddString(LaneId);
        }
    }

    public sealed class AttackStarted : IDomainEvent
    {
        public AttackStarted(EntityId attacker, EntityId target)
        {
            Attacker = attacker;
            Target = target;
        }

        public EntityId Attacker { get; }
        public EntityId Target { get; }

        public string Type => "match.attack_started";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Attacker.Value);
            hash.AddInt64(Target.Value);
        }
    }

    public sealed class DamageDealt : IDomainEvent
    {
        public DamageDealt(EntityId attacker, EntityId target, Fixed amount, Fixed targetHpAfter, DamageType damageType)
        {
            Attacker = attacker;
            Target = target;
            Amount = amount;
            TargetHpAfter = targetHpAfter;
            DamageType = damageType;
        }

        public EntityId Attacker { get; }
        public EntityId Target { get; }

        /// <summary>Damage after armor.</summary>
        public Fixed Amount { get; }

        public Fixed TargetHpAfter { get; }

        public DamageType DamageType { get; }

        public string Type => "match.damage_dealt";
        public int SchemaVersion => 2;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Attacker.Value);
            hash.AddInt64(Target.Value);
            hash.AddInt64(Amount.Raw);
            hash.AddInt64(TargetHpAfter.Raw);
            hash.AddInt64((int)DamageType);
        }
    }

    public sealed class UnitKilled : IDomainEvent
    {
        public UnitKilled(EntityId victim, Team victimTeam, string victimTypeId, EntityId killer)
        {
            Victim = victim;
            VictimTeam = victimTeam;
            VictimTypeId = victimTypeId;
            Killer = killer;
        }

        public EntityId Victim { get; }
        public Team VictimTeam { get; }
        public string VictimTypeId { get; }
        public EntityId Killer { get; }

        public string Type => "match.unit_killed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Victim.Value);
            hash.AddInt64((int)VictimTeam);
            hash.AddString(VictimTypeId);
            hash.AddInt64(Killer.Value);
        }
    }

    public sealed class StructureDestroyed : IDomainEvent
    {
        public StructureDestroyed(EntityId structure, string placementId, Team team, EntityId killer)
        {
            Structure = structure;
            PlacementId = placementId;
            Team = team;
            Killer = killer;
        }

        public EntityId Structure { get; }
        public string PlacementId { get; }
        public Team Team { get; }
        public EntityId Killer { get; }

        public string Type => "match.structure_destroyed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Structure.Value);
            hash.AddString(PlacementId);
            hash.AddInt64((int)Team);
            hash.AddInt64(Killer.Value);
        }
    }

    public sealed class ModifierApplied : IDomainEvent
    {
        public ModifierApplied(EntityId target, EntityId source, string key, long expiresTick)
        {
            Target = target;
            Source = source;
            Key = key;
            ExpiresTick = expiresTick;
        }

        public EntityId Target { get; }
        public EntityId Source { get; }
        public string Key { get; }

        /// <summary>-1 for permanent modifiers.</summary>
        public long ExpiresTick { get; }

        public string Type => "match.modifier_applied";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Target.Value);
            hash.AddInt64(Source.Value);
            hash.AddString(Key);
            hash.AddInt64(ExpiresTick);
        }
    }

    public sealed class ModifierRemoved : IDomainEvent
    {
        public ModifierRemoved(EntityId target, string key, string reason)
        {
            Target = target;
            Key = key;
            Reason = reason;
        }

        public EntityId Target { get; }
        public string Key { get; }

        /// <summary>"expired", "dispelled", ...</summary>
        public string Reason { get; }

        public string Type => "match.modifier_removed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Target.Value);
            hash.AddString(Key);
            hash.AddString(Reason);
        }
    }

    public sealed class MatchFinished : IDomainEvent
    {
        public MatchFinished(Team winner, string destroyedAncient)
        {
            Winner = winner;
            DestroyedAncient = destroyedAncient;
        }

        public Team Winner { get; }
        public string DestroyedAncient { get; }

        public string Type => "match.finished";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64((int)Winner);
            hash.AddString(DestroyedAncient);
        }
    }
}
