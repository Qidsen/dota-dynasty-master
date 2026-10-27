using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// A shop item. <see cref="Cost"/> is the full price; owned components are consumed and their
    /// cost is deducted when the item is bought.
    /// </summary>
    public sealed class ItemDefinition
    {
        public ItemDefinition(
            string id,
            string name,
            int cost,
            ModifierSpec? bonuses = null,
            IReadOnlyList<string>? components = null,
            string? activeAbilityId = null,
            int charges = 0,
            string? upgrade = null,
            bool inShop = true)
        {
            InShop = inShop;
            Upgrade = upgrade;
            Charges = charges;
            Id = id;
            Name = name;
            Cost = cost;
            Bonuses = bonuses;
            Components = (components ?? Array.Empty<string>()).ToArray();
            ActiveAbilityId = activeAbilityId;
        }

        public string Id { get; }
        public string Name { get; }
        public int Cost { get; }

        /// <summary>Permanent stat bonus while carried (spec duration is ignored).</summary>
        public ModifierSpec? Bonuses { get; }

        public IReadOnlyList<string> Components { get; }

        /// <summary>Ability cast when the item is used; null for purely passive items.</summary>
        public string? ActiveAbilityId { get; }

        /// <summary>Uses before the item is consumed; 0 = not consumable.</summary>
        public int Charges { get; }

        /// <summary>While carried, the hero's abilities use their variants for this upgrade ("scepter", "shard").</summary>
        public string? Upgrade { get; }

        /// <summary>Sold in the shop; false for items only given (Dota's Roshan drops: Cheese, Refresher Shard).</summary>
        public bool InShop { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteString(Name);
            w.WriteInt64(Cost);
            w.WriteBool(Bonuses != null);
            Bonuses?.WriteCanonical(w);
            w.WriteInt64(Components.Count);
            foreach (string component in Components)
                w.WriteString(component);
            w.WriteString(ActiveAbilityId ?? "");
            w.WriteInt64(Charges);
            w.WriteString(Upgrade ?? "");
            w.WriteBool(InShop);
        }
    }
}
