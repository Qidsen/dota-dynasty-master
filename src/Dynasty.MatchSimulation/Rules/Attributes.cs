using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>The attribute that adds to a hero's attack damage (Dota: strength, agility, intelligence or universal).</summary>
    public enum PrimaryAttribute
    {
        Strength = 0,
        Agility = 1,
        Intelligence = 2,

        /// <summary>Every attribute adds to the attack damage, at a lower rate.</summary>
        Universal = 3,
    }

    /// <summary>
    /// A hero's attributes at level 1 and their gain per level. With attributes the hero's health, mana,
    /// regeneration, armor, attack speed, magic resistance and attack damage come from them
    /// (<see cref="AttributeRules"/>) on top of its base stats.
    /// </summary>
    public sealed class HeroAttributes
    {
        public HeroAttributes(PrimaryAttribute primary, int strength, int agility, int intelligence,
            Fixed strengthGain, Fixed agilityGain, Fixed intelligenceGain, int baseAttackSpeed = 100)
        {
            Primary = primary;
            Strength = strength;
            Agility = agility;
            Intelligence = intelligence;
            StrengthGain = strengthGain;
            AgilityGain = agilityGain;
            IntelligenceGain = intelligenceGain;
            BaseAttackSpeed = baseAttackSpeed;
        }

        public PrimaryAttribute Primary { get; }
        public int Strength { get; }
        public int Agility { get; }
        public int Intelligence { get; }
        public Fixed StrengthGain { get; }
        public Fixed AgilityGain { get; }
        public Fixed IntelligenceGain { get; }

        /// <summary>Attack speed before agility and bonuses (Dota: 100 for most heroes); the base attack time is the stats' attack interval.</summary>
        public int BaseAttackSpeed { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64((int)Primary);
            w.WriteInt64(Strength);
            w.WriteInt64(Agility);
            w.WriteInt64(Intelligence);
            w.WriteFixed(StrengthGain);
            w.WriteFixed(AgilityGain);
            w.WriteFixed(IntelligenceGain);
            w.WriteInt64(BaseAttackSpeed);
        }
    }

    /// <summary>What one point of each attribute is worth. <see cref="Dota"/> is Dota's (Liquipedia «Attributes»).</summary>
    public sealed class AttributeRules
    {
        public AttributeRules(int healthPerStrength, Fixed healthRegenPerStrength, Fixed armorPerAgility, int attackSpeedPerAgility,
            int manaPerIntelligence, Fixed manaRegenPerIntelligence, Fixed magicResistPercentPerIntelligence,
            Fixed damagePerPrimaryPoint, Fixed damagePerUniversalPoint)
        {
            HealthPerStrength = healthPerStrength;
            HealthRegenPerStrength = healthRegenPerStrength;
            ArmorPerAgility = armorPerAgility;
            AttackSpeedPerAgility = attackSpeedPerAgility;
            ManaPerIntelligence = manaPerIntelligence;
            ManaRegenPerIntelligence = manaRegenPerIntelligence;
            MagicResistPercentPerIntelligence = magicResistPercentPerIntelligence;
            DamagePerPrimaryPoint = damagePerPrimaryPoint;
            DamagePerUniversalPoint = damagePerUniversalPoint;
        }

        /// <summary>22 health and 0.1 regeneration per strength; 1/6 armor and 1 attack speed per agility; 12 mana, 0.05 regeneration and 0.1% magic resistance per intelligence; 1 damage per primary point, 0.45 per point of every attribute for universal heroes.</summary>
        public static AttributeRules Dota { get; } = new AttributeRules(22, Fixed.FromRatio(1, 10), Fixed.FromRatio(1, 6), 1,
            12, Fixed.FromRatio(5, 100), Fixed.FromRatio(1, 10), Fixed.One, Fixed.FromRatio(45, 100));

        public int HealthPerStrength { get; }
        public Fixed HealthRegenPerStrength { get; }
        public Fixed ArmorPerAgility { get; }
        public int AttackSpeedPerAgility { get; }
        public int ManaPerIntelligence { get; }
        public Fixed ManaRegenPerIntelligence { get; }
        public Fixed MagicResistPercentPerIntelligence { get; }
        public Fixed DamagePerPrimaryPoint { get; }
        public Fixed DamagePerUniversalPoint { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(HealthPerStrength);
            w.WriteFixed(HealthRegenPerStrength);
            w.WriteFixed(ArmorPerAgility);
            w.WriteInt64(AttackSpeedPerAgility);
            w.WriteInt64(ManaPerIntelligence);
            w.WriteFixed(ManaRegenPerIntelligence);
            w.WriteFixed(MagicResistPercentPerIntelligence);
            w.WriteFixed(DamagePerPrimaryPoint);
            w.WriteFixed(DamagePerUniversalPoint);
        }
    }
}
