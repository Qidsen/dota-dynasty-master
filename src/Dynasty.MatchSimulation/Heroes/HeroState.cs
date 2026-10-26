using System.Collections.Generic;
using System.Linq;
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

        /// <summary>What attributes give (from the ruleset's hero rules); null in rulesets without hero rules.</summary>
        internal AttributeRules? AttributeRules { get; set; }

        /// <summary>The ruleset's hero rules (skill point levels, talents); null in rulesets without hero rules.</summary>
        internal HeroRules? Rules { get; set; }

        private readonly List<string> _talents = new List<string>();

        /// <summary>Talents learned, in the order learned.</summary>
        public IReadOnlyList<string> Talents => _talents;

        internal void AddTalent(string talentId) => _talents.Add(talentId);

        /// <summary>Talent points not used yet (separate from skill points).</summary>
        public int UnspentTalentPoints => (Rules?.TalentPointsAt(Level) ?? 0) - _talents.Count;

        /// <summary>Levels of the attribute bonus taken (skill points no ability could use).</summary>
        public int AttributeBonusLevel { get; internal set; }

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
                int spent = AttributeBonusLevel;
                foreach (AbilityState ability in _abilities)
                {
                    if (!ability.Definition.Innate)
                        spent += ability.Level;
                }

                return (Rules?.SkillPointsAt(Level) ?? Level) - spent;
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
            Base = definition;
            Level = level;
        }

        /// <summary>The definition in use: the ability itself or its variant for the hero's upgrades (talents, scepter, shard).</summary>
        public AbilityDefinition Definition { get; internal set; }

        /// <summary>The ability without upgrades (the one listed by the hero).</summary>
        public AbilityDefinition Base { get; }
        public int Level { get; internal set; }

        /// <summary>First tick the ability can be cast again.</summary>
        public long CooldownReadyTick { get; internal set; }

        public bool IsReady(long now) => Level > 0 && now >= CooldownReadyTick && (Definition.MaxChargesAt(Level) == 0 || Charges > 0);

        /// <summary>Charges left (abilities with charges).</summary>
        public int Charges { get; internal set; }

        /// <summary>Tick the next spent charge comes back; -1 while all charges are there.</summary>
        internal long NextChargeTick { get; set; } = -1;

        /// <summary>Learning or levelling grants the charges the new level adds (all of them when first learned).</summary>
        internal void GainLevelCharges(int previousLevel) =>
            Charges += System.Math.Max(0, Definition.MaxChargesAt(Level) - Definition.MaxChargesAt(previousLevel));

        /// <summary>Spends a charge; the restore timer runs while any charge is missing.</summary>
        internal void SpendCharge(long now, Dynasty.Core.Time.TickRate rate)
        {
            if (Definition.MaxChargesAt(Level) == 0)
                return;
            Charges--;
            if (NextChargeTick < 0)
                NextChargeTick = now + rate.TicksFor(Definition.ChargeRestoreAt(Level));
        }

        /// <summary>Brings back a charge whose restore time is over.</summary>
        internal void RestoreCharges(long now, Dynasty.Core.Time.TickRate rate)
        {
            if (NextChargeTick < 0 || now < NextChargeTick)
                return;
            Charges++;
            NextChargeTick = Charges < Definition.MaxChargesAt(Level) ? NextChargeTick + rate.TicksFor(Definition.ChargeRestoreAt(Level)) : -1;
        }
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
        /// that can be levelled now, but an unlearned ability comes before levelling a learned one,
        /// so the whole kit is available early. Passive abilities apply their level's permanent modifier.
        /// </summary>
        private static AbilityState? NextToLearn(HeroState state, bool unlearnedOnly)
        {
            foreach (string abilityId in state.Definition.SkillPriority)
            {
                AbilityState? candidate = state.Ability(abilityId);
                if (candidate != null && !candidate.Definition.Innate && (!unlearnedOnly || candidate.Level == 0) && candidate.Level < candidate.Definition.MaxLevel &&
                    candidate.Definition.RequiredHeroLevel[candidate.Level] <= state.Level)
                    return candidate;
            }

            return null;
        }

        public static void LearnSkills(SimulationContext<MatchState> context, MatchUnit hero)
        {
            HeroState state = hero.Hero!;
            foreach (AbilityState innate in state.Abilities.Where(a => a.Definition.Innate && a.Level == 0).ToList())
            {
                innate.Level = 1;   // free, at level one, never levelled further
                innate.GainLevelCharges(0);
                if (innate.Definition.Targeting == AbilityTargeting.Passive)
                    Combat.Modifiers.Apply(context, hero, innate.Definition.PassiveModifiers[0], hero.Id);
                context.Emit(new AbilityLearned(hero.Id, innate.Definition.Id, 1));
            }

            // Talent points: the first talent of the lowest tier reached without one, then (Dota's 27–30) the ones a tier still lacks.
            IReadOnlyList<TalentDefinition> talents = state.Definition.Talents;
            while (state.UnspentTalentPoints > 0)
            {
                TalentDefinition? talent = talents.FirstOrDefault(t => t.Level <= state.Level && !talents.Any(o => o.Level == t.Level && state.Talents.Contains(o.Id)))
                                           ?? talents.FirstOrDefault(t => t.Level <= state.Level && !state.Talents.Contains(t.Id));
                if (talent == null)
                    break;
                LearnTalent(context, hero, talent);
            }

            while (state.UnspentSkillPoints > 0)
            {
                AbilityState? next = NextToLearn(state, unlearnedOnly: true) ?? NextToLearn(state, unlearnedOnly: false);
                if (next == null)
                {
                    // Nothing to level: the attribute bonus, while it has levels left.
                    HeroRules? rules = state.Rules;
                    if (rules?.AttributeBonus == null || state.AttributeBonusLevel >= rules.AttributeBonusLevels)
                        return;
                    state.AttributeBonusLevel++;
                    Combat.Modifiers.Apply(context, hero, rules.AttributeBonus, hero.Id);
                    context.Emit(new AbilityLearned(hero.Id, AttributeBonusId, state.AttributeBonusLevel));
                    continue;
                }

                next.Level++;
                next.GainLevelCharges(next.Level - 1);
                if (next.Definition.Targeting == AbilityTargeting.Passive)
                    Combat.Modifiers.Apply(context, hero, next.Definition.PassiveModifiers[next.Level - 1], hero.Id);
                context.Emit(new AbilityLearned(hero.Id, next.Definition.Id, next.Level));
            }
        }

        /// <summary>The id the attribute bonus is learned under (events).</summary>
        public const string AttributeBonusId = "attribute_bonus";

        private static void LearnTalent(SimulationContext<MatchState> context, MatchUnit hero, TalentDefinition talent)
        {
            hero.Hero!.AddTalent(talent.Id);
            if (talent.Bonus != null)
                Combat.Modifiers.Apply(context, hero, talent.Bonus, hero.Id);
            context.Emit(new TalentLearned(hero.Id, talent.Id));
            RefreshUpgrades(context, hero);
        }

        /// <summary>
        /// Switches the hero's abilities to their variants for its upgrades: the talents learned and the upgrades
        /// of the items carried (Aghanim's Scepter, Shard). A passive's modifier follows its new variant.
        /// </summary>
        public static void RefreshUpgrades(SimulationContext<MatchState> context, MatchUnit hero)
        {
            HeroState state = hero.Hero!;
            var upgrades = new HashSet<string>(state.Talents);
            foreach (ItemSlot slot in state.InventorySlots)
            {
                if (slot.Definition.Upgrade != null)
                    upgrades.Add(slot.Definition.Upgrade);
            }

            foreach (AbilityState ability in state.Abilities)
            {
                AbilityDefinition variant = ability.Base.VariantFor(upgrades);
                if (ReferenceEquals(variant, ability.Definition))
                    continue;
                ability.Definition = variant;
                if (variant.Targeting == AbilityTargeting.Passive && ability.Level > 0)
                    Combat.Modifiers.Apply(context, hero, variant.PassiveModifiers[System.Math.Min(ability.Level, variant.PassiveModifiers.Count) - 1], hero.Id);
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
