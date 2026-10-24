using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Navigation;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Vision;

public class HighGroundTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    /// <summary>A plateau (level 1) of radius 600 around (5000, 2000); no creeps.</summary>
    private static RulesetDraft WithPlateau()
    {
        var draft = new RulesetDraft().WithoutWaves();
        draft.Elevation.Add(new ElevationZone("plateau", P(5000, 2000), 600, 1));
        return draft;
    }

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
    public void ElevationAt_ReportsTheHighestZoneCoveringThePoint()
    {
        var kernel = Place(WithPlateau(), P(1000, 1000), P(9000, 1000));

        Assert.Equal(1, kernel.State.ElevationAt(P(5000, 2000)));
        Assert.Equal(0, kernel.State.ElevationAt(P(5000, 3000)));
    }

    [Fact]
    public void LowGround_CannotSeeUpOntoTheHighGround()
    {
        var kernel = Place(WithPlateau(), P(5000, 3000), P(5000, 2200));   // alpha below, beta 800 away on top

        Assert.False(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
    }

    [Fact]
    public void HighGround_SeesDownOntoTheLowGround()
    {
        var kernel = Place(WithPlateau(), P(5000, 2200), P(5000, 3000));   // alpha on top, beta below

        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
    }

    [Fact]
    public void SameLevel_SeesEachOtherNormally()
    {
        var kernel = Place(WithPlateau(), P(4800, 2000), P(5300, 2000));

        Assert.True(kernel.State.IsVisibleTo(Team.Dawn, H(kernel.State, "beta")));
        Assert.True(kernel.State.IsVisibleTo(Team.Dusk, H(kernel.State, "alpha")));
    }

    [Fact]
    public void Elevation_DoesNotBlockMovement()
    {
        RulesetContent ruleset = WithPlateau().Build();
        var path = new Pathfinder(NavGrid.FromMap(ruleset.Map)).FindPath(P(5000, 3500), P(5000, 2000));

        Assert.Equal(new[] { P(5000, 2000) }, path);
    }

    [Fact]
    public void Validation_RejectsNegativeLevelsAndZonesOutsideTheMap()
    {
        var draft = WithPlateau();
        draft.Elevation.Add(new ElevationZone("pit", P(3000, 3000), 300, -1));
        draft.Elevation.Add(new ElevationZone("cloud", P(30000, 3000), 300, 1));

        IReadOnlyList<string> errors = draft.Build().Validate();

        Assert.Contains(errors, e => e.Contains("pit"));
        Assert.Contains(errors, e => e.Contains("cloud"));
    }

    [Fact]
    public void BaseMap_BasesAreMirroredHighGround()
    {
        RulesetContent ruleset = BaseRuleset.Create();
        var center = FixedVector2.FromInts(5000, 5000);

        Assert.NotEmpty(ruleset.Map.Elevation);
        foreach (ElevationZone zone in ruleset.Map.Elevation)
            Assert.Single(ruleset.Map.Elevation, z => z.Center == zone.Center.ReflectThrough(center) && z.Radius == zone.Radius && z.Level == zone.Level);

        var kernel = Match.Create(ruleset, 1);
        foreach (StructurePlacement t3 in ruleset.Map.Structures.Where(s => s.Id.EndsWith(".t3")))
            Assert.Equal(1, kernel.State.ElevationAt(t3.Position));
        Assert.Equal(0, kernel.State.ElevationAt(center));
    }
}
