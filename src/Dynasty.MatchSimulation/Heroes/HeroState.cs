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
        internal HeroState(HeroDefinition definition, string assignedLane, int startingGold)
        {
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

        public HeroOrder Order { get; internal set; }

        internal IReadOnlyList<FixedVector2>? Path { get; set; }
        internal int PathIndex { get; set; }
        internal FixedVector2 PathGoal { get; set; }

        /// <summary>Enemy heroes that damaged this hero, with the tick of their latest hit.</summary>
        internal SortedDictionary<long, long> RecentHeroAttackers { get; } = new SortedDictionary<long, long>();
    }

    public enum HeroOrderKind
    {
        Idle = 0,
        Move = 1,
        Attack = 2,
    }

    /// <summary>What a hero is currently trying to do. Set by AI/controllers, carried out by match systems.</summary>
    public sealed class HeroOrder
    {
        private HeroOrder(HeroOrderKind kind, FixedVector2 point, EntityId target)
        {
            Kind = kind;
            Point = point;
            Target = target;
        }

        public static HeroOrder Idle { get; } = new HeroOrder(HeroOrderKind.Idle, FixedVector2.Zero, EntityId.None);

        public HeroOrderKind Kind { get; }
        public FixedVector2 Point { get; }
        public EntityId Target { get; }

        public static HeroOrder Move(FixedVector2 point) => new HeroOrder(HeroOrderKind.Move, point, EntityId.None);

        public static HeroOrder Attack(EntityId target) => new HeroOrder(HeroOrderKind.Attack, FixedVector2.Zero, target);
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
            state.MaxMana = state.Definition.MaxManaAtLevel(state.Level);

            if (hero.IsAlive)
            {
                hero.Hp += Fixed.FromInt(hero.MaxHp - oldMaxHp);
                state.Mana += Fixed.FromInt(state.MaxMana - oldMaxMana);
            }

            context.Emit(new HeroLeveledUp(hero.Id, state.Level));
        }
    }
}
