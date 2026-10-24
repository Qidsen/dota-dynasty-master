using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Randomness;

namespace Dynasty.Players
{
    /// <summary>
    /// Creates plausible original players for demos and new worlds: attributes around a quality
    /// level with role-typical strengths, comfort on the player's position, a hero pool over the
    /// given heroes and hidden qualities. Deterministic for a given RNG.
    /// </summary>
    public static class PlayerGenerator
    {
        private static readonly Dictionary<int, Attribute[]> RoleStrengths = new Dictionary<int, Attribute[]>
        {
            [1] = new[] { Attribute.LastHit, Attribute.FarmingPatterns, Attribute.LateGameDecisions, Attribute.ResourceEfficiency, Attribute.Movement },
            [2] = new[] { Attribute.LastHit, Attribute.Deny, Attribute.Reaction, Attribute.SpellAccuracy, Attribute.Rotations },
            [3] = new[] { Attribute.Initiation, Attribute.Movement, Attribute.Adaptability, Attribute.ObjectiveControl, Attribute.PressureHandling },
            [4] = new[] { Attribute.Rotations, Attribute.Vision, Attribute.MapAwareness, Attribute.SpellAccuracy, Attribute.Initiation },
            [5] = new[] { Attribute.Vision, Attribute.MapAwareness, Attribute.Teamwork, Attribute.Leadership, Attribute.ItemUsage },
        };

        public static PlayerProfile Generate(DeterministicRandom random, string id, int position, int quality, IReadOnlyList<string> heroes,
            string? handle = null)
        {
            if (position < 1 || position > 5)
                throw new ArgumentOutOfRangeException(nameof(position));
            if (quality < Attributes.Min || quality > Attributes.Max)
                throw new ArgumentOutOfRangeException(nameof(quality));

            var values = new Dictionary<Attribute, int>();
            foreach (Attribute attribute in Attributes.All)
            {
                int value = quality + random.NextInt(-3, 4) + (RoleStrengths[position].Contains(attribute) ? 2 : 0);
                values[attribute] = Clamp(value, Attributes.Min, Attributes.Max);
            }

            var proficiency = new int[5];
            for (int p = 1; p <= 5; p++)
                proficiency[p - 1] = p == position ? random.NextInt(80, 101) : Clamp(random.NextInt(10, 60) + (Math.Abs(p - position) == 1 ? 15 : 0), 0, 100);

            var pool = new List<HeroPoolRecord>();
            foreach (string hero in heroes)
            {
                int mastery = Clamp(quality * 4 + random.NextInt(-20, 21), 0, 100);
                pool.Add(new HeroPoolRecord(hero, mastery, random.NextInt(20, 81), random.NextInt(30, 91), random.NextInt(40, 101),
                    Clamp(mastery + random.NextInt(-15, 16), 0, 100), Clamp(mastery + random.NextInt(-10, 11), 0, 100)));
            }

            int mechanics = Attributes.All.Where(a => Attributes.GroupOf(a) == AttributeGroup.Mechanics).Sum(a => values[a]) / 9;
            var hidden = new HiddenQualities(
                Clamp(mechanics + random.NextInt(0, 5), Attributes.Min, Attributes.Max),
                random.NextInt(5, 21), random.NextInt(5, 21), random.NextInt(1, 16), random.NextInt(5, 21));

            return new PlayerProfile(id, handle ?? id, handle ?? id, "", 2000 + random.NextInt(0, 9), new[] { "en" }, "",
                proficiency, AttributeSet.From(values), hidden, pool);
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
