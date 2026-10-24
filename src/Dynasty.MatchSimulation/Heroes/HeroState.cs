using System.Collections.Generic;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Heroes
{
    /// <summary>Hero-only state of a <see cref="MatchUnit"/>: progression, economy, scoreboard and current order.</summary>
    public sealed class HeroState
    {
        private readonly List<AbilityState> _abilities;

        internal HeroState(HeroDefinition definition, string assignedLane, int startingGold, IReadOnlyList<AbilityDefinition> abilities)
        {
            _abilities = new List<AbilityState>();
            foreach (AbilityDefinition ability in abilities)
                _abilities.Add(new AbilityState(ability));
            CastEndTick = -1;
            Definition = definition;
            AssignedLane = assignedLane;
            Level = 1;
            Gold = Fixed.FromInt(startingGold);
            MaxMana = definition.MaxManaAtLevel(1);
            Mana = Fixed.FromInt(MaxMana);
            Order = HeroOrder.Idle;
            RespawnTick = -1;
        }

        public HeroDefinition Definition { get; }
        public string AssignedLane { get; }
        public int Level { get; internal set; }
        public int Xp { get; internal set; }
        public Fixed Gold { get; internal set; }
        public Fixed Mana { get; internal set; }
        public int MaxMana { get; internal set; }
        public int Kills { get; internal set; }
        public int Deaths { get; internal set; }
        public int Assists { get; internal set; }
        public int LastHits { get; internal set; }
        public int Denies { get; internal set; }

        /// <summary>Tick the hero comes back; -1 while alive.</summary>
        public long RespawnTick { get; internal set; }

        public bool IsDead => RespawnTick >= 0;

        /// <summary>First tick a buyback is allowed again.</summary>
        public long BuybackReadyTick { get; internal set; }

        internal bool BuybackRequested { get; set; }

        public HeroOrder Order { get; internal set; }

        public IReadOnlyList<ItemSlot> Inventory => InventorySlots;

        internal List<ItemSlot> InventorySlots { get; } = new List<ItemSlot>();

        internal List<ShopRequest> ShopRequests { get; } = new List<ShopRequest>();

        /// <summary>The hero's abilities in definition order.</summary>
        public IReadOnlyList<AbilityState> Abilities => _abilities;

        /// <summary>A hero ability or the active of a carried item.</summary>
        public AbilityState? Ability(string abilityId)
        {
            foreach (AbilityState ability in _abilities)
            {
                if (ability.Definition.Id == abilityId)
                    return ability;
            }

            foreach (ItemSlot slot in InventorySlots)
            {
                if (slot.Active != null && slot.Active.Definition.Id == abilityId)
                    return slot.Active;
            }

            return null;
        }

        public int AbilityLevel(string abilityId) => Ability(abilityId)?.Level ?? 0;

        /// <summary>Hero level minus hero-ability levels already learned (item actives do not count).</summary>
        public int UnspentSkillPoints
        {
            get
            {
                int spent = 0;
                foreach (AbilityState ability in _abilities)
                    spent += ability.Level;
                return Level - spent;
            }
        }

        /// <summary>In the wind-up of an ability (between cast start and effect).</summary>
        public bool IsCasting => CastEndTick >= 0;

        internal long CastEndTick { get; set; }

        internal IReadOnlyList<FixedVector2>? Path { get; set; }
        internal int PathIndex { get; set; }
        internal FixedVector2 PathGoal { get; set; }

        /// <summary>Enemy heroes that damaged this hero, with the tick of their latest hit.</summary>
        internal SortedDictionary<long, long> RecentHeroAttackers { get; } = new SortedDictionary<long, long>();
    }

    /// <summary>Learned level and cooldown of one hero ability.</summary>
    public sealed class AbilityState
    {
        internal AbilityState(AbilityDefinition definition, int level = 0)
        {
            Definition = definition;
            Level = level;
        }

        public AbilityDefinition Definition { get; }
        public int Level { get; internal set; }

        /// <summary>First tick the ability can be cast again.</summary>
        public long CooldownReadyTick { get; internal set; }

        public bool IsReady(long now) => Level > 0 && now >= CooldownReadyTick;
    }

    public enum HeroOrderKind
    {
        Idle = 0,
        Move = 1,
        Attack = 2,
        Cast = 3,
    }

    /// <summary>What a hero is currently trying to do. Set by AI/controllers, carried out by match systems.</summary>
    public sealed class HeroOrder
    {
        private HeroOrder(HeroOrderKind kind, FixedVector2 point, EntityId target, string? abilityId = null)
        {
            Kind = kind;
            Point = point;
            Target = target;
            AbilityId = abilityId;
        }

        public static HeroOrder Idle { get; } = new HeroOrder(HeroOrderKind.Idle, FixedVector2.Zero, EntityId.None);

        public HeroOrderKind Kind { get; }
        public FixedVector2 Point { get; }
        public EntityId Target { get; }

        /// <summary>Ability to cast for <see cref="HeroOrderKind.Cast"/> orders.</summary>
        public string? AbilityId { get; }

        public static HeroOrder Move(FixedVector2 point) => new HeroOrder(HeroOrderKind.Move, point, EntityId.None);

        public static HeroOrder Attack(EntityId target) => new HeroOrder(HeroOrderKind.Attack, FixedVector2.Zero, target);

        public static HeroOrder Cast(string abilityId, EntityId target) =>
            new HeroOrder(HeroOrderKind.Cast, FixedVector2.Zero, target, abilityId);

        public static HeroOrder CastAtPoint(string abilityId, FixedVector2 point) =>
            new HeroOrder(HeroOrderKind.Cast, point, EntityId.None, abilityId);

        public static HeroOrder CastNoTarget(string abilityId) =>
            new HeroOrder(HeroOrderKind.Cast, FixedVector2.Zero, EntityId.None, abilityId);
    }

    public static class HeroOrders
    {
        /// <summary>
        /// Replaces the hero's current order. Feasibility (target alive, attackable, reachable) is
        /// checked every tick by the order system, not here.
        /// </summary>
        public static void Issue(MatchUnit hero, HeroOrder order)
        {
            HeroState state = hero.Hero!;
            state.Order = order;
            state.Path = null;
            state.CastEndTick = -1;
            hero.TargetId = order.Kind == HeroOrderKind.Attack ? order.Target : EntityId.None;
        }
    }

    public static class HeroProgression
    {
        public static void GrantXp(SimulationContext<MatchState> context, MatchUnit hero, int amount)
        {
            HeroState state = hero.Hero!;
            HeroRules rules = context.State.Ruleset.HeroRules!;
            state.Xp += amount;
            while (state.Level < rules.MaxLevel && state.Xp >= rules.XpForLevel[state.Level])
                LevelUp(context, hero, state);
        }

        /// <summary>
        /// Spends unspent skill points: each goes to the first ability in the hero's skill priority
        /// that can be levelled now. Passive abilities apply their level's permanent modifier.
        /// </summary>
        public static void LearnSkills(SimulationContext<MatchState> context, MatchUnit hero)
        {
            HeroState state = hero.Hero!;
            while (state.UnspentSkillPoints > 0)
            {
                AbilityState? next = null;
                foreach (string abilityId in state.Definition.SkillPriority)
                {
                    AbilityState? candidate = state.Ability(abilityId);
                    if (candidate != null && candidate.Level < candidate.Definition.MaxLevel &&
                        candidate.Definition.RequiredHeroLevel[candidate.Level] <= state.Level)
                    {
                        next = candidate;
                        break;
                    }
                }

                if (next == null)
                    return;

                next.Level++;
                if (next.Definition.Targeting == AbilityTargeting.Passive)
                    Combat.Modifiers.Apply(context, hero, next.Definition.PassiveModifiers[next.Level - 1], hero.Id);
                context.Emit(new AbilityLearned(hero.Id, next.Definition.Id, next.Level));
            }
        }

        public static void GrantGold(SimulationContext<MatchState> context, MatchUnit hero, int amount, string reason)
        {
            hero.Hero!.Gold += Fixed.FromInt(amount);
            context.Emit(new GoldEarned(hero.Id, amount, reason));
        }

        private static void LevelUp(SimulationContext<MatchState> context, MatchUnit hero, HeroState state)
        {
            int oldMaxHp = hero.MaxHp;
            int oldMaxMana = state.MaxMana;

            state.Level++;
            hero.Stats = state.Definition.StatsAtLevel(state.Level);
            hero.Recalculate();

            if (hero.IsAlive)
            {
                hero.Hp += Fixed.FromInt(hero.MaxHp - oldMaxHp);
                state.Mana += Fixed.FromInt(state.MaxMana - oldMaxMana);
            }

            context.Emit(new HeroLeveledUp(hero.Id, state.Level));
        }
    }
}
