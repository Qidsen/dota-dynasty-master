using System;
using Dynasty.Core.Hashing;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// How an attack scales against the kind of unit it hits (Dota's attack classes). Heroes always
    /// attack as <see cref="Hero"/> and structures as <see cref="Reinforced"/>; other units take the
    /// class from their stats.
    /// </summary>
    public enum AttackClass
    {
        /// <summary>Non-hero units with no special class (neutrals, summons).</summary>
        Basic = 0,

        /// <summary>Melee lane creeps.</summary>
        Runty = 1,

        /// <summary>Ranged lane creeps.</summary>
        Piercing = 2,

        /// <summary>Siege creeps, towers and other buildings.</summary>
        Reinforced = 3,

        Hero = 4,
    }

    /// <summary>What an attack hits: a hero, a reinforced unit (buildings, siege creeps) or any other unit.</summary>
    public enum DefenseClass
    {
        Hero = 0,
        Unit = 1,
        Reinforced = 2,
    }

    /// <summary>
    /// Attack damage percent per attack class and defense class. <see cref="Uniform"/> (every entry
    /// 100) leaves attacks as they are; <see cref="Dota"/> is the table of Dota 7.31+.
    /// </summary>
    public sealed class AttackClassTable
    {
        private const int Classes = 5;
        private const int Defenses = 3;
        private readonly int[] _percent;

        private AttackClassTable(int[] percent)
        {
            _percent = percent;
        }

        public static AttackClassTable Uniform { get; } = new AttackClassTable(Filled(100));

        /// <summary>Dota 7.31+: heroes hit buildings for 50%, melee creeps heroes 75% and buildings 70%,
        /// ranged creeps creeps 150%, heroes 50% and buildings 35%, siege and buildings hit buildings 250%,
        /// other units hit buildings for 70%.</summary>
        public static AttackClassTable Dota { get; } = Uniform
            .With(AttackClass.Hero, 100, 100, 50)
            .With(AttackClass.Basic, 100, 100, 70)
            .With(AttackClass.Runty, 75, 100, 70)
            .With(AttackClass.Piercing, 50, 150, 35)
            .With(AttackClass.Reinforced, 100, 100, 250);

        public int Percent(AttackClass attack, DefenseClass defense) => _percent[(int)attack * Defenses + (int)defense];

        /// <summary>A copy with one attack class's row replaced.</summary>
        public AttackClassTable With(AttackClass attack, int vsHero, int vsUnit, int vsReinforced)
        {
            if (vsHero < 0 || vsUnit < 0 || vsReinforced < 0)
                throw new ArgumentOutOfRangeException(nameof(vsHero), "Attack class percents cannot be negative.");
            var copy = (int[])_percent.Clone();
            int row = (int)attack * Defenses;
            copy[row] = vsHero;
            copy[row + 1] = vsUnit;
            copy[row + 2] = vsReinforced;
            return new AttackClassTable(copy);
        }

        internal void WriteCanonical(CanonicalWriter w)
        {
            foreach (int percent in _percent)
                w.WriteInt64(percent);
        }

        private static int[] Filled(int value)
        {
            var percent = new int[Classes * Defenses];
            for (int i = 0; i < percent.Length; i++)
                percent[i] = value;
            return percent;
        }
    }
}
