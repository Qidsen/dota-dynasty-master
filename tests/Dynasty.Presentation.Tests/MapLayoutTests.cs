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
    }
}
