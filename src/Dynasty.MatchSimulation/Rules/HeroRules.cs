using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// A talent (Dota): one of the two choices of a tier at <see cref="Level"/>. It gives a permanent
    /// <see cref="Bonus"/> (generic stat talents) and/or upgrades abilities that have a variant for its id.
    /// </summary>
    public sealed class TalentDefinition
    {
        public TalentDefinition(string id, string name, int level, Combat.ModifierSpec? bonus = null)
        {
            Id = id;
            Name = name;
            Level = level;
            Bonus = bonus;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Hero level of the talent's tier.</summary>
        public int Level { get; }

        public Combat.ModifierSpec? Bonus { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteString(Name);
            w.WriteInt64(Level);
            w.WriteBool(Bonus != null);
            Bonus?.WriteCanonical(w);
        }
    }

    /// <summary>
    /// A hero: level-1 combat stats plus per-level growth, mana and regeneration.
    /// Abilities and item builds reference this by <see cref="Id"/>.
    /// </summary>
    public sealed class HeroDefinition
    {
        public HeroDefinition(
            string id,
            string name,
            IReadOnlyList<string> roles,
            UnitStats baseStats,
            int hpPerLevel,
            Fixed armorPerLevel,
            int damagePerLevel,
            int baseMana,
            int manaPerLevel,
            Fixed hpRegenPerSecond,
            Fixed manaRegenPerSecond,
            IReadOnlyList<string>? abilityIds = null,
            IReadOnlyList<string>? skillPriority = null,
            IReadOnlyList<string>? itemBuild = null,
            int attackSpeedPerLevel = 0,
            HeroAttributes? attributes = null,
            IReadOnlyList<TalentDefinition>? talents = null)
        {
            Talents = (talents ?? Array.Empty<TalentDefinition>()).ToArray();
            Attributes = attributes;
            AttackSpeedPerLevel = attackSpeedPerLevel;
            ItemBuild = (itemBuild ?? Array.Empty<string>()).ToArray();
            Id = id;
            Name = name;
            Roles = roles.ToArray();
            BaseStats = baseStats;
            HpPerLevel = hpPerLevel;
            ArmorPerLevel = armorPerLevel;
            DamagePerLevel = damagePerLevel;
            BaseMana = baseMana;
            ManaPerLevel = manaPerLevel;
            HpRegenPerSecond = hpRegenPerSecond;
            ManaRegenPerSecond = manaRegenPerSecond;
            AbilityIds = (abilityIds ?? Array.Empty<string>()).ToArray();

            // Default: the last ability (the ultimate) first, then the rest in order, so the
            // ultimate is taken as soon as its level requirement allows.
            SkillPriority = (skillPriority ?? AbilityIds.Skip(AbilityIds.Count - 1).Concat(AbilityIds.Take(AbilityIds.Count - 1))).ToArray();
        }

        /// <summary>
        /// Order in which skill points are spent: each point goes to the first ability in this list
        /// that can be levelled at the current hero level.
        /// </summary>
        public IReadOnlyList<string> SkillPriority { get; }

        /// <summary>Talents by tier (<see cref="TalentDefinition.Level"/>); the first of a tier is the one taken first.</summary>
        public IReadOnlyList<TalentDefinition> Talents { get; }

        /// <summary>Attributes; with them health, mana, regeneration, armor, attack speed, magic resistance and damage come from attributes (Dota).</summary>
        public HeroAttributes? Attributes { get; }

        /// <summary>Attack speed gained per level after the first (Dota's agility), in attack speed points.</summary>
        public int AttackSpeedPerLevel { get; }

        /// <summary>Default purchase order used by AI controllers (item IDs).</summary>
        public IReadOnlyList<string> ItemBuild { get; }

        /// <summary>Stable ID, independent of the display name.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>Role tags, e.g. "carry", "mid", "offlane", "support".</summary>
        public IReadOnlyList<string> Roles { get; }

        public UnitStats BaseStats { get; }
        public int HpPerLevel { get; }
        public Fixed ArmorPerLevel { get; }
        public int DamagePerLevel { get; }
        public int BaseMana { get; }
        public int ManaPerLevel { get; }
        public Fixed HpRegenPerSecond { get; }
        public Fixed ManaRegenPerSecond { get; }
        public IReadOnlyList<string> AbilityIds { get; }

        public UnitStats StatsAtLevel(int level)
        {
            int extra = level - 1;
            UnitStats b = BaseStats;
            return new UnitStats(
                b.MaxHp + HpPerLevel * extra,
                b.Armor + ArmorPerLevel * Fixed.FromInt(extra),
                b.MoveSpeed,
                b.AttackDamageMin + DamagePerLevel * extra,
                b.AttackDamageMax + DamagePerLevel * extra,
                b.AttackRange,
                b.AttackIntervalMs,
                b.AttackPointMs,
                b.AcquisitionRange,
                b.BoundingRadius,
                b.MagicResistPercent,
                b.VisionRange,
                b.TrueSightRange,
                b.InnateFlags,
                b.ProjectileSpeed,
                b.NightVisionRange);
        }

        public int MaxManaAtLevel(int level) => BaseMana + ManaPerLevel * (level - 1);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteString(Name);
            w.WriteInt64(Roles.Count);
            foreach (string role in Roles)
                w.WriteString(role);
            BaseStats.WriteCanonical(w);
            w.WriteInt64(HpPerLevel);
            w.WriteFixed(ArmorPerLevel);
            w.WriteInt64(DamagePerLevel);
            w.WriteInt64(AttackSpeedPerLevel);
            w.WriteBool(Attributes != null);
            Attributes?.WriteCanonical(w);
            w.WriteInt64(BaseMana);
            w.WriteInt64(ManaPerLevel);
            w.WriteFixed(HpRegenPerSecond);
            w.WriteFixed(ManaRegenPerSecond);
            w.WriteInt64(AbilityIds.Count);
            foreach (string ability in AbilityIds)
                w.WriteString(ability);
            w.WriteInt64(SkillPriority.Count);
            foreach (string ability in SkillPriority)
                w.WriteString(ability);
            w.WriteInt64(ItemBuild.Count);
            foreach (string item in ItemBuild)
                w.WriteString(item);
            w.WriteInt64(Talents.Count);
            foreach (TalentDefinition talent in Talents)
                talent.WriteCanonical(w);
        }
    }

    /// <summary>Hero progression and match economy. All values are ruleset data, never hard-coded.</summary>
    public sealed class HeroRules
    {
        public HeroRules(
            IReadOnlyList<int> xpForLevel,
            int xpShareRadius,
            int heroKillXpBase,
            int heroKillXpPerLevel,
            int heroKillGoldBase,
            int heroKillGoldPerLevel,
            int assistWindowMs,
            int goldPerMinute,
            int startingGold,
            int respawnBaseMs,
            int respawnPerLevelMs,
            int goldLossPerLevel,
            int structureTeamGold,
            int fountainHealPercentPerSecond,
            int buybackBaseGold = 0,
            int buybackGoldPerLevel = 0,
            int buybackCooldownMs = 0,
            int heroKillXpPercentOfVictim = 0,
            AttributeRules? attributes = null,
            IReadOnlyList<int>? skillPointLevels = null,
            IReadOnlyList<int>? talentPointLevels = null,
            int attributeBonusLevels = 0,
            int attributeBonusPerLevel = 0)
        {
            SkillPointLevels = skillPointLevels?.ToArray();
            TalentPointLevels = (talentPointLevels ?? Array.Empty<int>()).ToArray();
            AttributeBonusLevels = attributeBonusLevels;
            AttributeBonusPerLevel = attributeBonusPerLevel;
            AttributeBonus = attributeBonusLevels > 0
                ? new Combat.ModifierSpec("attribute_bonus", -1, strengthBonus: attributeBonusPerLevel, agilityBonus: attributeBonusPerLevel,
                    intelligenceBonus: attributeBonusPerLevel, dispellable: false, maxStacks: attributeBonusLevels)
                : null;
            Attributes = attributes ?? AttributeRules.Dota;
            HeroKillXpPercentOfVictim = heroKillXpPercentOfVictim;
            BuybackBaseGold = buybackBaseGold;
            BuybackGoldPerLevel = buybackGoldPerLevel;
            BuybackCooldownMs = buybackCooldownMs;
            XpForLevel = xpForLevel.ToArray();
            XpShareRadius = xpShareRadius;
            HeroKillXpBase = heroKillXpBase;
            HeroKillXpPerLevel = heroKillXpPerLevel;
            HeroKillGoldBase = heroKillGoldBase;
            HeroKillGoldPerLevel = heroKillGoldPerLevel;
            AssistWindowMs = assistWindowMs;
            GoldPerMinute = goldPerMinute;
            StartingGold = startingGold;
            RespawnBaseMs = respawnBaseMs;
            RespawnPerLevelMs = respawnPerLevelMs;
            GoldLossPerLevel = goldLossPerLevel;
            StructureTeamGold = structureTeamGold;
            FountainHealPercentPerSecond = fountainHealPercentPerSecond;
        }

        /// <summary>Total XP required to reach level i+1 (index 0 is level 1 and must be 0).</summary>
        public IReadOnlyList<int> XpForLevel { get; }

        public int MaxLevel => XpForLevel.Count;

        /// <summary>Enemy heroes within this distance of a dying unit share its XP.</summary>
        public int XpShareRadius { get; }

        public int HeroKillXpBase { get; }
        public int HeroKillXpPerLevel { get; }

        /// <summary>What a point of each attribute gives heroes that have attributes.</summary>
        public AttributeRules Attributes { get; }

        /// <summary>
        /// Hero kill XP also includes this percent of the victim's total XP (Dota: 100 + 13%, the bonus stops growing
        /// after the victim's level 25 — not modelled yet, docs/DOTA_RULES_AUDIT.md).
        /// </summary>
        public int HeroKillXpPercentOfVictim { get; }
        public int HeroKillGoldBase { get; }
        public int HeroKillGoldPerLevel { get; }

        /// <summary>Enemy heroes who damaged the victim within this window get an assist.</summary>
        public int AssistWindowMs { get; }

        public int GoldPerMinute { get; }
        public int StartingGold { get; }
        public int RespawnBaseMs { get; }
        public int RespawnPerLevelMs { get; }
        public int GoldLossPerLevel { get; }

        /// <summary>Gold given to every hero of the team that destroys an enemy structure.</summary>
        public int StructureTeamGold { get; }

        public int FountainHealPercentPerSecond { get; }

        /// <summary>Buyback price = base + per level × hero level; buyback is disabled when both are 0.</summary>
        public int BuybackBaseGold { get; }
        public int BuybackGoldPerLevel { get; }
        public int BuybackCooldownMs { get; }

        public bool BuybackEnabled => BuybackBaseGold > 0 || BuybackGoldPerLevel > 0;

        public int BuybackCost(int level) => BuybackBaseGold + BuybackGoldPerLevel * level;

        /// <summary>Levels that give a skill point (for abilities and the attribute bonus); null = every level.</summary>
        public IReadOnlyList<int>? SkillPointLevels { get; }

        public int SkillPointsAt(int level) => SkillPointLevels == null ? level : SkillPointLevels.Count(l => l <= level);

        /// <summary>
        /// Levels that give a talent point, separate from skill points (Dota 7.40+: 10, 15, 20, 25, 27, 28, 29, 30). A point
        /// takes a talent of the lowest tier reached that has none yet, otherwise the talent a tier still lacks.
        /// </summary>
        public IReadOnlyList<int> TalentPointLevels { get; }

        public int TalentPointsAt(int level) => TalentPointLevels.Count(l => l <= level);

        /// <summary>The attribute bonus (Dota: +2 to all attributes, 7 times) takes a skill point no ability can use.</summary>
        public int AttributeBonusLevels { get; }
        public int AttributeBonusPerLevel { get; }

        /// <summary>The attribute bonus modifier (each level adds a stack); null without an attribute bonus.</summary>
        public Combat.ModifierSpec? AttributeBonus { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(BuybackBaseGold);
            w.WriteInt64(BuybackGoldPerLevel);
            w.WriteInt64(BuybackCooldownMs);
            w.WriteInt64(XpForLevel.Count);
            foreach (int xp in XpForLevel)
                w.WriteInt64(xp);
            w.WriteInt64(XpShareRadius);
            w.WriteInt64(HeroKillXpBase);
            w.WriteInt64(HeroKillXpPerLevel);
            w.WriteInt64(HeroKillXpPercentOfVictim);
            Attributes.WriteCanonical(w);
            w.WriteInt64(HeroKillGoldBase);
            w.WriteInt64(HeroKillGoldPerLevel);
            w.WriteInt64(AssistWindowMs);
            w.WriteInt64(GoldPerMinute);
            w.WriteInt64(StartingGold);
            w.WriteInt64(RespawnBaseMs);
            w.WriteInt64(RespawnPerLevelMs);
            w.WriteInt64(GoldLossPerLevel);
            w.WriteInt64(StructureTeamGold);
            w.WriteInt64(FountainHealPercentPerSecond);
            w.WriteBool(SkillPointLevels != null);
            w.WriteInt64(SkillPointLevels?.Count ?? 0);
            foreach (int level in SkillPointLevels ?? Array.Empty<int>())
                w.WriteInt64(level);
            w.WriteInt64(TalentPointLevels.Count);
            foreach (int level in TalentPointLevels)
                w.WriteInt64(level);
            w.WriteInt64(AttributeBonusLevels);
            w.WriteInt64(AttributeBonusPerLevel);
        }
    }

    /// <summary>Team spawn/heal area for heroes.</summary>
    public sealed class FountainPlacement
    {
        public FountainPlacement(Team team, FixedVector2 position, int radius)
        {
            Team = team;
            Position = position;
            Radius = radius;
        }

        public Team Team { get; }
        public FixedVector2 Position { get; }
        public int Radius { get; }
    }

    /// <summary>Gold and XP a creep is worth.</summary>
    public sealed class Bounty
    {
        public Bounty(int goldMin, int goldMax, int xp)
        {
            GoldMin = goldMin;
            GoldMax = goldMax;
            Xp = xp;
        }

        public static Bounty None { get; } = new Bounty(0, 0, 0);

        public int GoldMin { get; }
        public int GoldMax { get; }
        public int Xp { get; }
    }
}
