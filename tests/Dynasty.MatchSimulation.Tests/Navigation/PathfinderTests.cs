using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Navigation;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Navigation;

public class PathfinderTests
{
    private static NavGrid Grid(params ObstacleDefinition[] obstacles) =>
        NavGrid.Build(width: 2000, height: 2000, cellSize: 100, obstacles, clearance: 0);

    private static ObstacleDefinition Rect(string id, int x0, int y0, int x1, int y1) =>
        ObstacleDefinition.Rectangle(id, P(x0, y0), P(x1, y1));

    [Fact]
    public void Grid_MarksCellsInsideObstaclesAsBlocked()
    {
        var grid = Grid(Rect("wall", 900, 0, 1100, 1500));

        Assert.False(grid.IsWalkable(P(1000, 500)));
        Assert.True(grid.IsWalkable(P(500, 500)));
        Assert.True(grid.IsWalkable(P(1000, 1700)));
    }

    [Fact]
    public void Grid_CircleObstaclesBlockTheirArea()
    {
        var grid = Grid(ObstacleDefinition.Circle("rock", P(1000, 1000), 250));

        Assert.False(grid.IsWalkable(P(1000, 1000)));
        Assert.False(grid.IsWalkable(P(1150, 1000)));
        Assert.True(grid.IsWalkable(P(1400, 1000)));
    }

    [Fact]
    public void Clearance_InflatesObstacles()
    {
        var tight = NavGrid.Build(2000, 2000, 100, new[] { Rect("wall", 900, 0, 1100, 1500) }, clearance: 0);
        // Walkability is sampled at cell centers: the cell centered at x=1250 is blocked once the
        // wall (right edge 1100) grows by at least 150.
        var padded = NavGrid.Build(2000, 2000, 100, new[] { Rect("wall", 900, 0, 1100, 1500) }, clearance: 200);

        Assert.True(tight.IsWalkable(P(1250, 500)));
        Assert.False(padded.IsWalkable(P(1250, 500)));
    }

    [Fact]
    public void OpenGround_GivesStraightPathToGoal()
    {
        var path = new Pathfinder(Grid()).FindPath(P(150, 150), P(1850, 1250));

        Assert.NotNull(path);
        Assert.Equal(new[] { P(1850, 1250) }, path);
    }

    [Fact]
    public void Wall_IsWalkedAroundWithEverySegmentClear()
    {
        var grid = Grid(Rect("wall", 900, 0, 1100, 1500));

        var path = new Pathfinder(grid).FindPath(P(300, 300), P(1700, 300));

        Assert.NotNull(path);
        Assert.Equal(P(1700, 300), path![^1]);
        Assert.True(path.Count >= 2);
        FixedVector2 from = P(300, 300);
        foreach (FixedVector2 point in path)
        {
            Assert.True(grid.HasLineOfSight(from, point), $"segment {from} -> {point} crosses an obstacle");
            from = point;
        }

        Assert.Contains(path, p => p.Y > Fixed.FromInt(1500));
    }

    [Fact]
    public void UnreachableGoal_ReturnsNull()
    {
        var grid = Grid(
            Rect("n", 1300, 1300, 2000, 1400),
            Rect("w", 1300, 1300, 1400, 2000));

        Assert.Null(new Pathfinder(grid).FindPath(P(300, 300), P(1700, 1700)));
    }

    [Fact]
    public void GoalInsideObstacle_EndsAtNearestWalkablePoint()
    {
        var grid = Grid(ObstacleDefinition.Circle("rock", P(1000, 1000), 250));

        var path = new Pathfinder(grid).FindPath(P(200, 1000), P(1000, 1000));

        Assert.NotNull(path);
        Assert.True(grid.IsWalkable(path![^1]));
        Assert.InRange(FixedVector2.Distance(path[^1], P(1000, 1000)).ToDouble(), 250, 450);
    }

    [Fact]
    public void Pathfinding_IsDeterministic()
    {
        var grid = Grid(Rect("a", 500, 200, 600, 1800), Rect("b", 1200, 0, 1300, 1600));

        var first = new Pathfinder(grid).FindPath(P(100, 100), P(1900, 1900));
        var second = new Pathfinder(grid).FindPath(P(100, 100), P(1900, 1900));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Validate_RejectsLaneThroughObstacle()
    {
        var draft = new RulesetDraft();
        draft.Obstacles.Add(ObstacleDefinition.Circle("blocker", P(5000, 5000), 200));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("blocker") || e.Contains("mid"));
    }

    [Fact]
    public void BaseMap_LanesAreConnectedAndObstaclesMirrored()
    {
        RulesetContent ruleset = BaseRuleset.Create();
        var grid = NavGrid.FromMap(ruleset.Map);
        var center = FixedVector2.FromInts(5000, 5000);

        Assert.NotEmpty(ruleset.Map.Obstacles);
        foreach (ObstacleDefinition obstacle in ruleset.Map.Obstacles)
            Assert.Contains(ruleset.Map.Obstacles, o => o.Center == obstacle.Center.ReflectThrough(center) && o.Radius == obstacle.Radius);

        // Base to base is reachable.
        Assert.NotNull(new Pathfinder(grid).FindPath(P(1500, 1500), P(8500, 8500)));
    }
}
