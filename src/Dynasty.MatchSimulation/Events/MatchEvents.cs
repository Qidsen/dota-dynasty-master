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
        public DamageDealt(EntityId attacker, EntityId target, Fixed amount, Fixed targetHpAfter, DamageType damageType, bool isCritical = false)
        {
            Attacker = attacker;
            Target = target;
            Amount = amount;
            TargetHpAfter = targetHpAfter;
            DamageType = damageType;
            IsCritical = isCritical;
        }

        public EntityId Attacker { get; }
        public EntityId Target { get; }

        /// <summary>Damage after armor.</summary>
        public Fixed Amount { get; }

        public Fixed TargetHpAfter { get; }

        public DamageType DamageType { get; }

        public bool IsCritical { get; }

        public string Type => "match.damage_dealt";
        public int SchemaVersion => 3;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Attacker.Value);
            hash.AddInt64(Target.Value);
            hash.AddInt64(Amount.Raw);
            hash.AddInt64(TargetHpAfter.Raw);
            hash.AddInt64((int)DamageType);
            hash.AddInt64(IsCritical ? 1 : 0);
        }
    }

    /// <summary>HP restored by an ability, item, modifier or lifesteal (passive regeneration is not reported).</summary>
    public sealed class HealApplied : IDomainEvent
    {
        public HealApplied(EntityId source, EntityId target, Fixed amount)
        {
            Source = source;
            Target = target;
            Amount = amount;
        }

        public EntityId Source { get; }
        public EntityId Target { get; }

        /// <summary>HP actually restored (after the max-HP cap).</summary>
        public Fixed Amount { get; }

        public string Type => "match.heal_applied";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Source.Value);
            hash.AddInt64(Target.Value);
            hash.AddInt64(Amount.Raw);
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

    /// <summary>A rune nobody took was gone when the next ones came (Dota's power and water runes).</summary>
    public sealed class RuneExpired : IDomainEvent
    {
        public RuneExpired(EntityId rune, string runeTypeId)
        {
            Rune = rune;
            RuneTypeId = runeTypeId;
        }

        public EntityId Rune { get; }
        public string RuneTypeId { get; }

        public string Type => "match.rune_expired";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Rune.Value);
            hash.AddString(RuneTypeId);
        }
    }

    /// <summary>A hero got an item without buying it (Dota's Roshan drops).</summary>
    public sealed class ItemReceived : IDomainEvent
    {
        public ItemReceived(EntityId hero, string itemId)
        {
            Hero = hero;
            ItemId = itemId;
        }

        public EntityId Hero { get; }
        public string ItemId { get; }

        public string Type => "match.item_received";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(ItemId);
        }
    }

    /// <summary>The boss cast one of its own spells (Dota's Roshan: Slam, Roar of Retribution).</summary>
    public sealed class BossAbilityCast : IDomainEvent
    {
        public BossAbilityCast(EntityId boss, string abilityId)
        {
            Boss = boss;
            AbilityId = abilityId;
        }

        public EntityId Boss { get; }
        public string AbilityId { get; }

        public string Type => "match.boss_ability_cast";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Boss.Value);
            hash.AddString(AbilityId);
        }
    }

    /// <summary>A spell aimed at the unit was blocked (Dota's Linken's Sphere, Roshan's Spell Block).</summary>
    public sealed class SpellBlocked : IDomainEvent
    {
        public SpellBlocked(EntityId unit, EntityId caster, string abilityId)
        {
            Unit = unit;
            Caster = caster;
            AbilityId = abilityId;
        }

        public EntityId Unit { get; }
        public EntityId Caster { get; }
        public string AbilityId { get; }

        public string Type => "match.spell_blocked";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Unit.Value);
            hash.AddInt64(Caster.Value);
            hash.AddString(AbilityId);
        }
    }

    /// <summary>A team used its Glyph of Fortification.</summary>
    public sealed class GlyphUsed : IDomainEvent
    {
        public GlyphUsed(Team team)
        {
            Team = team;
        }

        public Team Team { get; }

        public string Type => "match.glyph_used";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64((int)Team);
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

    /// <summary>Bounty gold (passive income is a rule, not an event). Reasons: last_hit, hero_kill, structure.</summary>
    public sealed class GoldEarned : IDomainEvent
    {
        public GoldEarned(EntityId hero, int amount, string reason)
        {
            Hero = hero;
            Amount = amount;
            Reason = reason;
        }

        public EntityId Hero { get; }
        public int Amount { get; }
        public string Reason { get; }

        public string Type => "match.gold_earned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddInt64(Amount);
            hash.AddString(Reason);
        }
    }

    public sealed class HeroLeveledUp : IDomainEvent
    {
        public HeroLeveledUp(EntityId hero, int level)
        {
            Hero = hero;
            Level = level;
        }

        public EntityId Hero { get; }
        public int Level { get; }

        public string Type => "match.hero_leveled_up";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddInt64(Level);
        }
    }

    public sealed class HeroKilled : IDomainEvent
    {
        public HeroKilled(EntityId victim, EntityId killer, System.Collections.Generic.IReadOnlyList<EntityId> assists, int bountyGold, long respawnTick)
        {
            Victim = victim;
            Killer = killer;
            Assists = assists;
            BountyGold = bountyGold;
            RespawnTick = respawnTick;
        }

        public EntityId Victim { get; }

        /// <summary>Last unit to damage the victim (may be a creep or tower).</summary>
        public EntityId Killer { get; }

        /// <summary>Enemy heroes (other than the killer) who damaged the victim within the assist window, by ID.</summary>
        public System.Collections.Generic.IReadOnlyList<EntityId> Assists { get; }

        /// <summary>Gold paid to the killer; 0 when the killer was not an enemy hero.</summary>
        public int BountyGold { get; }

        public long RespawnTick { get; }

        public string Type => "match.hero_killed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Victim.Value);
            hash.AddInt64(Killer.Value);
            hash.AddInt64(Assists.Count);
            foreach (EntityId assist in Assists)
                hash.AddInt64(assist.Value);
            hash.AddInt64(BountyGold);
            hash.AddInt64(RespawnTick);
        }
    }

    public sealed class HeroRespawned : IDomainEvent
    {
        public HeroRespawned(EntityId hero)
        {
            Hero = hero;
        }

        public EntityId Hero { get; }

        public string Type => "match.hero_respawned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash) => hash.AddInt64(Hero.Value);
    }

    public sealed class AbilityCast : IDomainEvent
    {
        public AbilityCast(EntityId caster, string abilityId, int level, EntityId target, FixedVector2 point)
        {
            Caster = caster;
            AbilityId = abilityId;
            Level = level;
            Target = target;
            Point = point;
        }

        public EntityId Caster { get; }
        public string AbilityId { get; }
        public int Level { get; }

        /// <summary>Unit target; None for point and no-target abilities.</summary>
        public EntityId Target { get; }

        public FixedVector2 Point { get; }

        public string Type => "match.ability_cast";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Caster.Value);
            hash.AddString(AbilityId);
            hash.AddInt64(Level);
            hash.AddInt64(Target.Value);
            hash.AddInt64(Point.X.Raw);
            hash.AddInt64(Point.Y.Raw);
        }
    }

    /// <summary>An ordered cast that could not happen. Reasons: not_learned, cooldown, mana, silenced, invalid_target, interrupted.</summary>
    public sealed class AbilityCastFailed : IDomainEvent
    {
        public AbilityCastFailed(EntityId caster, string abilityId, string reason)
        {
            Caster = caster;
            AbilityId = abilityId;
            Reason = reason;
        }

        public EntityId Caster { get; }
        public string AbilityId { get; }
        public string Reason { get; }

        public string Type => "match.ability_cast_failed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Caster.Value);
            hash.AddString(AbilityId);
            hash.AddString(Reason);
        }
    }

    public sealed class AbilityLearned : IDomainEvent
    {
        public AbilityLearned(EntityId hero, string abilityId, int level)
        {
            Hero = hero;
            AbilityId = abilityId;
            Level = level;
        }

        public EntityId Hero { get; }
        public string AbilityId { get; }
        public int Level { get; }

        public string Type => "match.ability_learned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(AbilityId);
            hash.AddInt64(Level);
        }
    }

    public sealed class TalentLearned : IDomainEvent
    {
        public TalentLearned(EntityId hero, string talentId)
        {
            Hero = hero;
            TalentId = talentId;
        }

        public EntityId Hero { get; }
        public string TalentId { get; }

        public string Type => "match.talent_learned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(TalentId);
        }
    }

    /// <summary>A unit that would have died came back instead (Reincarnation).</summary>
    public sealed class UnitRevived : IDomainEvent
    {
        public UnitRevived(EntityId unit, string modifierKey)
        {
            Unit = unit;
            ModifierKey = modifierKey;
        }

        public EntityId Unit { get; }
        public string ModifierKey { get; }

        public string Type => "match.unit_revived";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Unit.Value);
            hash.AddString(ModifierKey);
        }
    }

    public sealed class UnitSummoned : IDomainEvent
    {
        public UnitSummoned(EntityId unit, EntityId owner, string creepTypeId, long expiresTick)
        {
            Unit = unit;
            Owner = owner;
            CreepTypeId = creepTypeId;
            ExpiresTick = expiresTick;
        }

        public EntityId Unit { get; }
        public EntityId Owner { get; }
        public string CreepTypeId { get; }
        public long ExpiresTick { get; }

        public string Type => "match.unit_summoned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Unit.Value);
            hash.AddInt64(Owner.Value);
            hash.AddString(CreepTypeId);
            hash.AddInt64(ExpiresTick);
        }
    }

    /// <summary>A temporary unit reached the end of its lifetime (no bounty).</summary>
    public sealed class UnitExpired : IDomainEvent
    {
        public UnitExpired(EntityId unit)
        {
            Unit = unit;
        }

        public EntityId Unit { get; }

        public string Type => "match.unit_expired";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash) => hash.AddInt64(Unit.Value);
    }

    public sealed class ItemPurchased : IDomainEvent
    {
        public ItemPurchased(EntityId hero, string itemId, int cost, System.Collections.Generic.IReadOnlyList<string> consumedComponents)
        {
            Hero = hero;
            ItemId = itemId;
            Cost = cost;
            ConsumedComponents = consumedComponents;
        }

        public EntityId Hero { get; }
        public string ItemId { get; }

        /// <summary>Gold actually paid (full price minus consumed components).</summary>
        public int Cost { get; }

        public System.Collections.Generic.IReadOnlyList<string> ConsumedComponents { get; }

        public string Type => "match.item_purchased";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(ItemId);
            hash.AddInt64(Cost);
            hash.AddInt64(ConsumedComponents.Count);
            foreach (string component in ConsumedComponents)
                hash.AddString(component);
        }
    }

    /// <summary>Reasons: unknown_item, not_in_shop, gold, inventory_full, not_owned.</summary>
    public sealed class ItemPurchaseFailed : IDomainEvent
    {
        public ItemPurchaseFailed(EntityId hero, string itemId, string reason)
        {
            Hero = hero;
            ItemId = itemId;
            Reason = reason;
        }

        public EntityId Hero { get; }
        public string ItemId { get; }
        public string Reason { get; }

        public string Type => "match.item_purchase_failed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(ItemId);
            hash.AddString(Reason);
        }
    }

    public sealed class ItemSold : IDomainEvent
    {
        public ItemSold(EntityId hero, string itemId, int refund)
        {
            Hero = hero;
            ItemId = itemId;
            Refund = refund;
        }

        public EntityId Hero { get; }
        public string ItemId { get; }
        public int Refund { get; }

        public string Type => "match.item_sold";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(ItemId);
            hash.AddInt64(Refund);
        }
    }

    public sealed class BossSpawned : IDomainEvent
    {
        public BossSpawned(EntityId boss, string bossId)
        {
            Boss = boss;
            BossId = bossId;
        }

        public EntityId Boss { get; }
        public string BossId { get; }

        public string Type => "match.boss_spawned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Boss.Value);
            hash.AddString(BossId);
        }
    }

    public sealed class WatcherTaken : IDomainEvent
    {
        public WatcherTaken(int watcher, Team team)
        {
            Watcher = watcher;
            Team = team;
        }

        /// <summary>Index of the Watcher in the rules.</summary>
        public int Watcher { get; }

        public Team Team { get; }

        public string Type => "match.watcher_taken";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Watcher);
            hash.AddInt64((int)Team);
        }
    }

    public sealed class WatcherTurnedOff : IDomainEvent
    {
        public WatcherTurnedOff(int watcher, Team team)
        {
            Watcher = watcher;
            Team = team;
        }

        /// <summary>Index of the Watcher in the rules.</summary>
        public int Watcher { get; }

        /// <summary>The team that turned it off.</summary>
        public Team Team { get; }

        public string Type => "match.watcher_turned_off";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Watcher);
            hash.AddInt64((int)Team);
        }
    }

    public sealed class LotusPicked : IDomainEvent
    {
        public LotusPicked(int pool, EntityId hero)
        {
            Pool = pool;
            Hero = hero;
        }

        /// <summary>Index of the pool in the rules.</summary>
        public int Pool { get; }

        public EntityId Hero { get; }

        public string Type => "match.lotus_picked";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Pool);
            hash.AddInt64(Hero.Value);
        }
    }

    public sealed class ShrineTaken : IDomainEvent
    {
        public ShrineTaken(int shrine, Team team, EntityId taker, int xp)
        {
            Shrine = shrine;
            Team = team;
            Taker = taker;
            Xp = xp;
        }

        /// <summary>Index of the shrine in the rules.</summary>
        public int Shrine { get; }

        public Team Team { get; }
        public EntityId Taker { get; }

        /// <summary>Experience given to the taker and to the teammate with the least.</summary>
        public int Xp { get; }

        public string Type => "match.shrine_taken";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Shrine);
            hash.AddInt64((int)Team);
            hash.AddInt64(Taker.Value);
            hash.AddInt64(Xp);
        }
    }

    public sealed class MinibossSpawned : IDomainEvent
    {
        public MinibossSpawned(EntityId miniboss, string minibossId)
        {
            Miniboss = miniboss;
            MinibossId = minibossId;
        }

        public EntityId Miniboss { get; }
        public string MinibossId { get; }

        public string Type => "match.miniboss_spawned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Miniboss.Value);
            hash.AddString(MinibossId);
        }
    }

    public sealed class MinibossKilled : IDomainEvent
    {
        public MinibossKilled(EntityId miniboss, Team team, EntityId killer, long respawnTick)
        {
            Miniboss = miniboss;
            Team = team;
            Killer = killer;
            RespawnTick = respawnTick;
        }

        public EntityId Miniboss { get; }

        /// <summary>Team credited with the kill (the last attacker's team); Neutral when nobody earned it.</summary>
        public Team Team { get; }

        public EntityId Killer { get; }
        public long RespawnTick { get; }

        public string Type => "match.miniboss_killed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Miniboss.Value);
            hash.AddInt64((int)Team);
            hash.AddInt64(Killer.Value);
            hash.AddInt64(RespawnTick);
        }
    }

    public sealed class BossKilled : IDomainEvent
    {
        public BossKilled(EntityId boss, Team team, EntityId killer, long respawnTick)
        {
            Boss = boss;
            Team = team;
            Killer = killer;
            RespawnTick = respawnTick;
        }

        public EntityId Boss { get; }

        /// <summary>Team credited with the kill (the last attacker's team); Neutral when nobody earned it.</summary>
        public Team Team { get; }

        public EntityId Killer { get; }
        public long RespawnTick { get; }

        public string Type => "match.boss_killed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Boss.Value);
            hash.AddInt64((int)Team);
            hash.AddInt64(Killer.Value);
            hash.AddInt64(RespawnTick);
        }
    }

    public sealed class HeroBoughtBack : IDomainEvent
    {
        public HeroBoughtBack(EntityId hero, int cost)
        {
            Hero = hero;
            Cost = cost;
        }

        public EntityId Hero { get; }
        public int Cost { get; }

        public string Type => "match.hero_bought_back";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddInt64(Cost);
        }
    }

    /// <summary>Reasons: disabled, not_dead, cooldown, gold.</summary>
    public sealed class BuybackFailed : IDomainEvent
    {
        public BuybackFailed(EntityId hero, string reason)
        {
            Hero = hero;
            Reason = reason;
        }

        public EntityId Hero { get; }
        public string Reason { get; }

        public string Type => "match.buyback_failed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(Reason);
        }
    }

    public sealed class CampSpawned : IDomainEvent
    {
        public CampSpawned(string campId)
        {
            CampId = campId;
        }

        public string CampId { get; }

        public string Type => "match.camp_spawned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash) => hash.AddString(CampId);
    }

    public sealed class RuneSpawned : IDomainEvent
    {
        public RuneSpawned(EntityId rune, string runeTypeId, FixedVector2 position)
        {
            Rune = rune;
            RuneTypeId = runeTypeId;
            Position = position;
        }

        public EntityId Rune { get; }
        public string RuneTypeId { get; }
        public FixedVector2 Position { get; }

        public string Type => "match.rune_spawned";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Rune.Value);
            hash.AddString(RuneTypeId);
            hash.AddInt64(Position.X.Raw);
            hash.AddInt64(Position.Y.Raw);
        }
    }

    public sealed class RunePickedUp : IDomainEvent
    {
        public RunePickedUp(EntityId hero, string runeTypeId, FixedVector2 position)
        {
            Hero = hero;
            RuneTypeId = runeTypeId;
            Position = position;
        }

        public EntityId Hero { get; }
        public string RuneTypeId { get; }
        public FixedVector2 Position { get; }

        public string Type => "match.rune_picked_up";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Hero.Value);
            hash.AddString(RuneTypeId);
            hash.AddInt64(Position.X.Raw);
            hash.AddInt64(Position.Y.Raw);
        }
    }

    public sealed class ProjectileLaunched : IDomainEvent
    {
        public ProjectileLaunched(long projectile, EntityId source, EntityId target, bool isAbility)
        {
            Projectile = projectile;
            Source = source;
            Target = target;
            IsAbility = isAbility;
        }

        public long Projectile { get; }
        public EntityId Source { get; }
        public EntityId Target { get; }
        public bool IsAbility { get; }

        public string Type => "match.projectile_launched";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Projectile);
            hash.AddInt64(Source.Value);
            hash.AddInt64(Target.Value);
            hash.AddInt64(IsAbility ? 1 : 0);
        }
    }

    /// <summary>A projectile lost its target to a disjoint (blink, teleport).</summary>
    public sealed class ProjectileDodged : IDomainEvent
    {
        public ProjectileDodged(long projectile, EntityId source, EntityId target)
        {
            Projectile = projectile;
            Source = source;
            Target = target;
        }

        public long Projectile { get; }
        public EntityId Source { get; }
        public EntityId Target { get; }

        public string Type => "match.projectile_dodged";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(Projectile);
            hash.AddInt64(Source.Value);
            hash.AddInt64(Target.Value);
        }
    }

    public sealed class DayNightChanged : IDomainEvent
    {
        public DayNightChanged(bool isNight)
        {
            IsNight = isNight;
        }

        public bool IsNight { get; }

        public string Type => "match.day_night_changed";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash) => hash.AddInt64(IsNight ? 1 : 0);
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
