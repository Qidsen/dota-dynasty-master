using System.Text.Json.Nodes;

/// <summary>
/// Dota's camp types of 7.41f (Liquipedia «Neutral Creeps», the tables of each tier: the units of each camp; the unit names are
/// the game's, by the units' Fandom pages and, for the frogs new in 7.40, by Liquipedia's evolution chains). Each spawn a dry
/// camp takes one of its tier's types at random, never the same twice in a row (Fandom «Neutral creeps» «Spawning»).
/// Not carried over: the split of mud golems into shard golems, the Dark Troll Summoner's skeletons, the neutrals' abilities (B-158).
/// </summary>
static class Jungle
{
    private static readonly (string Unit, int Count)[][][] Tiers =
    {
        new[]   // small
        {
            new[] { ("neutral_kobold", 3), ("neutral_kobold_tunneler", 1), ("neutral_kobold_taskmaster", 1) },     // Kobold Camp
            new[] { ("neutral_forest_troll_berserker", 2), ("neutral_forest_troll_high_priest", 1) },                 // Hill Troll Camp
            new[] { ("neutral_forest_troll_berserker", 2), ("neutral_kobold_taskmaster", 1) },                        // Hill Troll and Kobold Camp
            new[] { ("neutral_gnoll_assassin", 3) },                                                                  // Vhoul Assassin Camp
            new[] { ("neutral_fel_beast", 2), ("neutral_ghost", 1) },                                                 // Ghost Camp
            new[] { ("neutral_harpy_scout", 2), ("neutral_harpy_storm", 1) },                                         // Harpy Camp
        },
        new[]   // medium
        {
            new[] { ("neutral_centaur_outrunner", 1), ("neutral_centaur_khan", 1) },                                  // Centaur Camp
            new[] { ("neutral_giant_wolf", 2), ("neutral_alpha_wolf", 1) },                                           // Wolf Camp
            new[] { ("neutral_satyr_trickster", 2), ("neutral_satyr_soulstealer", 2) },                               // Satyr Camp
            new[] { ("neutral_ogre_mauler", 2), ("neutral_ogre_magi", 1) },                                           // Ogre Camp
            new[] { ("neutral_mud_golem", 2) },                                                                       // Golem Camp
        },
        new[]   // large
        {
            new[] { ("neutral_centaur_outrunner", 2), ("neutral_centaur_khan", 1) },                                  // Large Centaur Camp
            new[] { ("neutral_satyr_trickster", 1), ("neutral_satyr_soulstealer", 1), ("neutral_satyr_hellcaller", 1) },   // Large Satyr Camp
            new[] { ("neutral_polar_furbolg_champion", 1), ("neutral_polar_furbolg_ursa_warrior", 1) },              // Hellbear Camp
            new[] { ("neutral_wildkin", 2), ("neutral_enraged_wildkin", 1) },                                         // Wildwing Camp
            new[] { ("neutral_dark_troll", 2), ("neutral_dark_troll_warlord", 1) },                                   // Troll Camp
            new[] { ("neutral_warpine_raider", 2) },                                                                  // Warpine Camp
        },
        new[]   // ancient
        {
            new[] { ("neutral_black_drake", 2), ("neutral_black_dragon", 1) },                                        // Dragon Camp
            new[] { ("neutral_rock_golem", 2), ("neutral_granite_golem", 1) },                                        // Large Golem Camp
            new[] { ("neutral_small_thunder_lizard", 2), ("neutral_big_thunder_lizard", 1) },                         // Thunderhide Camp
            new[] { ("neutral_frostbitten_golem", 2), ("neutral_ice_shaman", 1) },                                    // Frostbitten Camp
            new[] { ("neutral_prowler_acolyte", 2), ("neutral_prowler_shaman", 1) },                                  // Prowler Camp
        },
    };

    /// <summary>
    /// The frogs of the flooded camps by tier (Liquipedia «Neutral Creeps» «Evolving camps», 7.41f): Pollywog → Boglet → Croaker →
    /// Ancient Croaker, and Pollywog → Marshmage Apprentice → Marshmage → Ancient Marshmage; a camp has three: two of the first
    /// line, one of the second (Pollywog Camp 3 Pollywogs, Froglet Camp 2 Boglets and an Apprentice, and so on).
    /// </summary>
    private static readonly string[][] Frogs =
    {
        new[] { "neutral_tadpole", "neutral_froglet", "neutral_grown_frog", "neutral_ancient_frog" },
        new[] { "neutral_tadpole", "neutral_froglet", "neutral_grown_frog", "neutral_ancient_frog" },
        new[] { "neutral_tadpole", "neutral_froglet_mage", "neutral_grown_frog_mage", "neutral_ancient_frog_mage" },
    };

    /// <summary>Every neutral unit a camp may spawn (game unit names without "npc_dota_").</summary>
    public static IEnumerable<string> Units => Tiers.SelectMany(t => t).SelectMany(c => c).Select(u => u.Unit)
        .Concat(Frogs.SelectMany(f => f)).Distinct().OrderBy(u => u, StringComparer.Ordinal);

    public static JsonArray Variants(int tier) => new(Tiers[tier].Select(c => (JsonNode)Composition(c)).ToArray());

    /// <summary>
    /// A flooded camp from a tier (0 small: Pollywog Camp, 1 medium: Froglet Camp): every 5 minutes one frog evolves, the next
    /// only after all have (Liquipedia: «Pollywog Camp with 1 evolution contains 2 Pollywogs and 1 Boglet»), up to the given
    /// number of evolutions — 6 by 30:00 (two tiers), or 3 for the medium camp by the safe lane's tier 2 that «can only evolve once»
    /// (7.41). Which frog evolves second and third is not in the sources: the first line before the second (B-158).
    /// </summary>
    public static JsonObject Evolution(int startTier, int evolutions)
    {
        var stages = new JsonArray();
        for (int k = 0; k <= evolutions; k++)
        {
            var levels = new int[3];
            for (int slot = 0; slot < 3; slot++)
                levels[slot] = startTier + (k + 2 - slot) / 3;
            stages.Add(Composition(Enumerable.Range(0, 3).Select(slot => (Frogs[slot][levels[slot]], 1)).ToArray()));
        }

        return new JsonObject { ["interval"] = 300, ["stages"] = stages };
    }

    private static JsonArray Composition((string Unit, int Count)[] units) => new(units
        .GroupBy(u => u.Unit).Select(g => (JsonNode)new JsonObject { ["creep"] = g.Key, ["count"] = g.Sum(u => u.Count) }).ToArray());
}
