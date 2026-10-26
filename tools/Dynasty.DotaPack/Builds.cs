using System.Text.Json.Nodes;

/// <summary>
/// Hero item builds from OpenDota's item popularity (recent public matches): the most bought finished items
/// of the early, mid and late game, in that order. Popularity counts component purchases too, so an item is
/// left out when an item built from it is also popular for the hero (bought at least a quarter as often);
/// consumables are left to the AI's own rules (teleport, wards, healing).
/// </summary>
static class Builds
{
    private static readonly (string Phase, int MinCost, int Take)[] Phases =
    {
        ("early_game_items", 500, 2),
        ("mid_game_items", 1000, 3),
        ("late_game_items", 2000, 3),
    };

    public static JsonArray For(DotaData data, int heroId, JsonObject itemIds, JsonObject shopItems, HashSet<string> packItems)
    {
        JsonObject popularity = data.OpenDota("heroes/" + heroId + "/itemPopularity").AsObject();
        var counts = new Dictionary<string, int>();
        var byPhase = new List<(string Phase, string Item, int Count)>();
        foreach (var (phase, _, _) in Phases)
        {
            if (popularity[phase] is not JsonObject phaseCounts)
                continue;
            foreach (var (id, count) in phaseCounts)
            {
                string? item = itemIds[id]?.GetValue<string>();
                if (item == null || !packItems.Contains(item))
                    continue;
                byPhase.Add((phase, item, count!.GetValue<int>()));
                counts[item] = counts.GetValueOrDefault(item) + count!.GetValue<int>();
            }
        }

        bool Upgraded(string item) => counts.Any(other => other.Key != item && other.Value * 4 >= counts[item] && BuiltFrom(shopItems, other.Key, item));

        var build = new List<string>();
        foreach (var (phase, minCost, take) in Phases)
        {
            build.AddRange(byPhase
                .Where(x => x.Phase == phase && !build.Contains(x.Item) && !Upgraded(x.Item))
                .Where(x => shopItems[x.Item]!["qual"]?.GetValue<string>() != "consumable" && (int)DotaData.Decimal(shopItems[x.Item]!["cost"]) >= minCost)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Item, StringComparer.Ordinal)
                .Take(take)
                .Select(x => x.Item));
        }

        return new JsonArray(build.Select(i => (JsonNode)i!).ToArray());
    }

    /// <summary>Whether the item's recipe uses the component, directly or through its own components.</summary>
    private static bool BuiltFrom(JsonObject shopItems, string item, string component, int depth = 0)
    {
        if (depth > 6 || shopItems[item]?["components"] is not JsonArray components)
            return false;
        return components.Any(c => c!.GetValue<string>() == component || BuiltFrom(shopItems, c.GetValue<string>(), component, depth + 1));
    }
}
