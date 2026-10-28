using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class MapLayoutTests
{
    [Fact]
    public void Layout_CopiesTheStaticMapAsPlainNumbers()
    {
        RulesetContent ruleset = BaseRuleset.Create();
        MapDefinition map = ruleset.Map;

        MapLayout layout = MapLayout.From(ruleset);

        Assert.Equal(map.Width, layout.Width);
        Assert.Equal(map.Height, layout.Height);
        Assert.Equal(map.Obstacles.Count, layout.Obstacles.Count);
        ObstacleDefinition circle = map.Obstacles.First(o => o.Shape == ObstacleShape.Circle);
        MapObstacle copied = layout.Obstacles[map.Obstacles.ToList().IndexOf(circle)];
        Assert.True(copied.IsCircle);
        Assert.Equal(circle.Center.X.ToDouble(), copied.CenterX, 3);
        Assert.Equal(circle.Radius, copied.Radius, 3);
        Assert.Equal(map.Lanes.Select(l => l.Waypoints.Count), layout.Lanes.Select(l => l.Points.Count));
        Assert.Equal(map.Elevation.Count, layout.Plateaus.Count);
        Assert.Equal(map.Fountains.Count, layout.Fountains.Count);
        Assert.Equal(map.Camps.Count, layout.Camps.Count);
        Assert.Equal(ruleset.Boss!.Position.Y.ToDouble(), layout.BossPit!.Value.Y, 3);
        Assert.Contains(layout.Markers, m => m.Kind == MapMarkerKind.BossPit);
    }

    private const string Hero = @"{
        ""id"": ""antimage"", ""name"": ""Anti-Mage"", ""roles"": [""carry""], ""attackType"": ""melee"",
        ""primaryAttribute"": ""agility"", ""strength"": 21, ""agility"": 25, ""intelligence"": 12,
        ""strengthGain"": 1.6, ""agilityGain"": 2.8, ""intelligenceGain"": 1.8,
        ""baseHealth"": 120, ""baseHealthRegen"": 1.5, ""baseMana"": 75, ""baseManaRegen"": 0, ""baseArmor"": 2, ""magicResist"": 25,
        ""attackDamageMin"": 29, ""attackDamageMax"": 33, ""attackRange"": 150, ""projectileSpeed"": 0,
        ""baseAttackTime"": 1.4, ""baseAttackSpeed"": 100, ""attackPoint"": 0.3, ""moveSpeed"": 315, ""dayVision"": 1800, ""nightVision"": 800 }";

    /// <summary>
    /// The Dota map objects that are places rather than units (shrines, lotus pools, watchers, Twin Gates) are on the layout, so the
    /// viewer draws them; so is the boss pit.
    /// </summary>
    [Fact]
    public void Layout_HasTheMapObjects()
    {
        string json = @"{ ""format"": ""dynasty.ruleset-pack"", ""formatVersion"": 1, ""id"": ""dota"", ""version"": 7410,
            ""shrines"": { ""positions"": [1000, 2000, 9000, 8000], ""first"": 420, ""interval"": 420, ""radius"": 300, ""capture"": 3, ""xp"": 200, ""xpPerInterval"": 300 },
            ""lotusPools"": { ""positions"": [1500, 2500], ""first"": 180, ""interval"": 180, ""max"": 6, ""radius"": 350, ""firstPick"": 1.5, ""nextPickPercent"": 70, ""minPick"": 0.3, ""item"": ""cheese"" },
            ""items"": [ { ""id"": ""cheese"", ""name"": ""Cheese"", ""cost"": 0 } ],
            ""watchers"": { ""positions"": [3000, 4000], ""radius"": 200, ""channel"": 1, ""on"": 420, ""off"": 120, ""dayVision"": 800, ""nightVision"": 450 },
            ""twinGates"": { ""positions"": [1100, 2100, 8900, 7900], ""radius"": 200, ""channel"": 4, ""manaCost"": 30 },
            ""heroes"": [" + string.Join(",", Enumerable.Range(0, 20).Select(i => Hero.Replace(@"""antimage""", @"""hero" + i + @""""))) + "] }";
        string path = Path.Combine(Path.GetTempPath(), "dynasty-pack-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            MapLayout layout = MapLayout.From(RulesetSource.Load(path));

            Assert.Equal(2, layout.Markers.Count(m => m.Kind == MapMarkerKind.Shrine));
            Assert.Contains(layout.Markers, m => m.Kind == MapMarkerKind.LotusPool && m.X == 1500 && m.Y == 2500);
            Assert.Contains(layout.Markers, m => m.Kind == MapMarkerKind.Watcher && m.X == 3000 && m.Y == 4000);
            Assert.Equal(2, layout.Markers.Count(m => m.Kind == MapMarkerKind.TwinGate));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
