using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynasty.Players
{
    /// <summary>The player attributes of spec §14, each on a 1–20 scale.</summary>
    public enum Attribute
    {
        // Mechanics
        LastHit,
        Deny,
        Reaction,
        SpellAccuracy,
        Micro,
        MultiUnitControl,
        ItemUsage,
        Movement,
        ResourceEfficiency,

        // Macro
        MapAwareness,
        FarmingPatterns,
        Rotations,
        ObjectiveControl,
        Vision,
        Initiation,
        TargetPriority,
        LateGameDecisions,
        Adaptability,

        // Psychology
        Confidence,
        Consistency,
        PressureHandling,
        TiltResistance,
        Motivation,
        Discipline,
        Leadership,
        Teamwork,
        WorkEthic,
        Ambition,
    }

    public enum AttributeGroup
    {
        Mechanics,
        Macro,
        Psychology,
    }

    public static class Attributes
    {
        public const int Min = 1;
        public const int Max = 20;

        public static IReadOnlyList<Attribute> All { get; } = ((Attribute[])Enum.GetValues(typeof(Attribute))).OrderBy(a => (int)a).ToArray();

        public static AttributeGroup GroupOf(Attribute attribute) =>
            attribute <= Attribute.ResourceEfficiency ? AttributeGroup.Mechanics
            : attribute <= Attribute.Adaptability ? AttributeGroup.Macro
            : AttributeGroup.Psychology;
    }

    /// <summary>An immutable set of every attribute's value.</summary>
    public sealed class AttributeSet
    {
        private readonly int[] _values;

        private AttributeSet(int[] values)
        {
            _values = values;
        }

        public int this[Attribute attribute] => _values[(int)attribute];

        public static AttributeSet Uniform(int value)
        {
            Check(value);
            return new AttributeSet(Enumerable.Repeat(value, Attributes.All.Count).ToArray());
        }

        public static AttributeSet From(IReadOnlyDictionary<Attribute, int> values)
        {
            var array = new int[Attributes.All.Count];
            foreach (Attribute attribute in Attributes.All)
            {
                if (!values.TryGetValue(attribute, out int value))
                    throw new ArgumentException("Missing attribute " + attribute + ".", nameof(values));
                Check(value);
                array[(int)attribute] = value;
            }

            return new AttributeSet(array);
        }

        public AttributeSet With(Attribute attribute, int value)
        {
            Check(value);
            var copy = (int[])_values.Clone();
            copy[(int)attribute] = value;
            return new AttributeSet(copy);
        }

        /// <summary>Average of a group (rounded down).</summary>
        public int Average(AttributeGroup group)
        {
            var values = Attributes.All.Where(a => Attributes.GroupOf(a) == group).Select(a => this[a]).ToList();
            return values.Sum() / values.Count;
        }

        private static void Check(int value)
        {
            if (value < Attributes.Min || value > Attributes.Max)
                throw new ArgumentOutOfRangeException(nameof(value), "Attributes are " + Attributes.Min + ".." + Attributes.Max + ".");
        }
    }
}
