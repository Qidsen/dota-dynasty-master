using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Navigation;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Navigation;

/// <summary>
/// A map given cell by cell, as Dota's is (gridnav, height levels and vision blockers of the map file, trees as
/// 128×128 blocks). The draft map is 10000×10000 with 100-unit cells: 100×100 cells.
/// </summary>
public class MapTerrainTests
{
    private const int Cells = 100;

    private static MapTerrain Terrain(
        Func<int, int, bool>? blocked = null,
        Func<int, int, bool>? blocksSight = null,
        Func<int, int, int>? level = null,
        IReadOnlyList<FixedVector2>? trees = null,
        int treeSize = 200)
    {
        var walkable = new bool[Cells * Cells];
        var sight = new bool[Cells * Cells];
        var levels = new int[Cells * Cells];
        for (int row = 0; row < Cells; row++)
        for (int column = 0; column < Cells; column++)
        {
            int i = row * Cells + column;
            walkable[i] = blocked == null || !blocked(column, row);
            sight[i] = blocksSight != null && blocksSight(column, row);
            levels[i] = level == null ? 0 : level(column, row);
        }

        return new MapTerrain(Cells, Cells, walkable, sight, levels, trees, treeSize);
    }

    private static RulesetDraft Draft(MapTerrain terrain)
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Terrain = terrain;
        return draft;
    }

    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static SimulationKernel<MatchState> Place(RulesetDraft draft, FixedVector2 alpha, FixedVector2 beta)
    {
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = alpha;
            H(ctx.State, "beta").Position = beta;
        }));
        kernel.RunTicks(4);
        return kernel;
    }

    [Fact]
    public void Cells_MarkedUnwalkable_AreBlockedAndWalkedAround()
    {
        // A wall at column 50 from row 0 up to row 70.
        RulesetContent ruleset = Draft(Terrain(blocked: (c, r) => c == 50 && r < 70)).Build();
        NavGrid grid = NavGrid.FromMap(ruleset.Map);

        Assert.False(grid.IsWalkable(P(5050, 3000)));
        Assert.True(grid.IsWalkable(P(4950, 3000)));
        IReadOnlyList<FixedVector2>? path = new Pathfinder(grid).FindPath(P(3000, 3000), P(7000, 3000));
        Assert.NotNull(path);
        Assert.Contains(path!, p => p.Y >= Fixed.FromInt(7000));
    }

    [Fact]
    public void Tree_BlocksTheSquareAroundItForWalkingAndSight()
    {
        RulesetContent ruleset = Draft(Terrain(trees: new[] { P(3000, 3000) })).Build();
        NavGrid grid = NavGrid.FromMap(ruleset.Map);

        // 200×200 around (3000, 3000): the four cells whose centers are 50 away.
        Assert.False(grid.IsWalkable(P(2950, 2950)));
        Assert.False(grid.IsWalkable(P(3050, 3050)));
        Assert.True(grid.IsWalkable(P(3150, 3050)));
        Assert.True(grid.IsWalkable(P(2850, 2950)));

        var kernel = Match.Create(ruleset, 1);
        Assert.False(kernel.State.SightGrid.HasClearLine(P(2500, 3000), P(3500, 3000)));
        Assert.True(kernel.State.SightGrid.HasClearLine(P(2500, 3500), P(3500, 3500)));
    }

    [Fact]
    public void VisionBlocker_BlocksSightButNotWalking()
    {
        // A vision blocker cell at (50, 20): the heroes are 600 apart on row 20 with it between them.
        var draft = Draft(Terrain(blocksSight: (c, r) => c == 50 && r == 20));
        var kernel = Place(draft, P(4750, 2050), P(5350, 2050));

        Assert.True(NavGrid.FromMap(draft.Build().Map).IsWalkable(P(5050, 2050)));
        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));

        var open = Place(Draft(Terrain()), P(4750, 2050), P(5350, 2050));
        Assert.True(open.State.IsVisibleTo(Team.Dawn, H(open.State, "beta")));
    }

    [Fact]
    public void Levels_GiveTheGroundHeightOfEachCell()
    {
        var kernel = Place(Draft(Terrain(level: (c, r) => c >= 50 ? 2 : 1)), P(1000, 1000), P(9000, 1000));

        Assert.Equal(1, kernel.State.ElevationAt(P(4950, 3000)));
        Assert.Equal(2, kernel.State.ElevationAt(P(5050, 3000)));
    }

    [Fact]
    public void LowGround_CannotSeeUpOntoHigherCells()
    {
        var draft = Draft(Terrain(level: (c, r) => c >= 50 ? 1 : 0));

        var below = Place(draft, P(4700, 2050), P(5300, 2050));
        Assert.False(below.State.IsVisibleTo(Team.Dawn, H(below.State, "beta")));
        Assert.True(below.State.IsVisibleTo(Team.Dusk, H(below.State, "alpha")));
    }

    [Fact]
    public void HigherGroundInBetween_BlocksSightAcrossIt()
    {
        // A ridge of level 1 at columns 49–51; both heroes on level 0 on either side.
        var draft = Draft(Terrain(level: (c, r) => c >= 49 && c <= 51 ? 1 : 0));
        var kernel = Place(draft, P(4600, 2050), P(5400, 2050));

        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
        Assert.False(kernel.State.IsVisibleTo(Team.Dusk, H(kernel.State, "alpha")));
    }

    [Fact]
    public void Terrain_IsPartOfTheRulesetIdentity()
    {
        string open = Draft(Terrain()).Build().ToDescriptor().Fingerprint.ToString();
        string walled = Draft(Terrain(blocked: (c, r) => c == 10 && r == 10)).Build().ToDescriptor().Fingerprint.ToString();
        string blind = Draft(Terrain(blocksSight: (c, r) => c == 10 && r == 10)).Build().ToDescriptor().Fingerprint.ToString();
        string raised = Draft(Terrain(level: (c, r) => c == 10 && r == 10 ? 1 : 0)).Build().ToDescriptor().Fingerprint.ToString();
        string wooded = Draft(Terrain(trees: new[] { P(1000, 1000) })).Build().ToDescriptor().Fingerprint.ToString();

        Assert.Equal(5, new[] { open, walled, blind, raised, wooded }.Distinct().Count());
    }

    [Fact]
    public void Validation_RequiresTheTerrainToCoverTheMapCellByCell()
    {
        var tooSmall = Draft(new MapTerrain(10, 10, new bool[100]));
        var cellMissing = Draft(new MapTerrain(Cells, Cells, new bool[Cells * Cells - 1]));
        var levelMissing = Draft(new MapTerrain(Cells, Cells, new bool[Cells * Cells], levels: new int[5]));

        Assert.Contains(tooSmall.Build().Validate(), e => e.Contains("terrain"));
        Assert.Contains(cellMissing.Build().Validate(), e => e.Contains("terrain"));
        Assert.Contains(levelMissing.Build().Validate(), e => e.Contains("terrain"));
        Assert.DoesNotContain(Draft(Terrain()).Build().Validate(), e => e.Contains("terrain"));
    }
}

public class LanePathTests
{
    private static RulesetDraft TwoPaths()
    {
        var draft = new RulesetDraft();
        draft.Lanes.Clear();
        // Dota's creeps of each side walk their own line of the lane (path_corner chains of the map file).
        draft.Lanes.Add(new LaneDefinition("mid", new[] { P(1000, 5000), P(5000, 5000), P(9000, 5000) }, new[] { P(9000, 5400), P(8800, 5400) }));
        return draft;
    }

    [Fact]
    public void EachSide_WalksItsOwnPathOfTheLane()
    {
        var kernel = TwoPaths().Start();
        kernel.RunTicks(60);   // Dusk's short path ends after 200 units

        MatchUnit dawn = kernel.State.Units.Single(u => u.Kind == UnitKind.Creep && u.Team == Team.Dawn);
        MatchUnit dusk = kernel.State.Units.Single(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk);
        Assert.Equal(Fixed.FromInt(5000), dawn.Position.Y);
        Assert.True(dusk.Position.Y > Fixed.FromInt(5300), "then heads for the nearest building: " + dusk.Position);
        Assert.True(dawn.Position.X > Fixed.FromInt(1000));
        Assert.True(dusk.Position.X < Fixed.FromInt(9000));
    }

    [Fact]
    public void WithoutItsOwnPath_DuskWalksTheLaneBackwards()
    {
        var kernel = new RulesetDraft().Start();
        kernel.RunTicks(30);

        MatchUnit dusk = kernel.State.Units.Single(u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk);
        Assert.Equal(Fixed.FromInt(5000), dusk.Position.Y);
        Assert.True(dusk.Position.X < Fixed.FromInt(9000));
    }

    [Fact]
    public void Validation_RequiresADuskPathOfTwoPointsInsideTheMap()
    {
        var draft = new RulesetDraft();
        draft.Lanes.Add(new LaneDefinition("short", new[] { P(1000, 1000), P(9000, 1000) }, new[] { P(9000, 1000) }));
        draft.Lanes.Add(new LaneDefinition("away", new[] { P(1000, 2000), P(9000, 2000) }, new[] { P(9000, 2000), P(-50, 2000) }));

        IReadOnlyList<string> errors = draft.Build().Validate();
        Assert.Contains(errors, e => e.Contains("'short'"));
        Assert.Contains(errors, e => e.Contains("'away'"));
        Assert.Empty(TwoPaths().Build().Validate());
    }

    [Fact]
    public void DuskPath_IsPartOfTheRulesetIdentity()
    {
        var shared = new RulesetDraft();
        shared.Lanes.Clear();
        shared.Lanes.Add(new LaneDefinition("mid", new[] { P(1000, 5000), P(5000, 5000), P(9000, 5000) }));

        Assert.NotEqual(shared.Build().ToDescriptor().Fingerprint.ToString(), TwoPaths().Build().ToDescriptor().Fingerprint.ToString());
    }
}
