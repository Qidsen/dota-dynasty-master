using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Heroes;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

public class CollisionTests
{
    private static MatchUnit H(MatchState state, string heroId) =>
        state.Units.Single(u => u.Hero != null && u.Hero.Definition.Id == heroId);

    private static SimulationKernel<MatchState> Pair(FixedVector2 alpha, FixedVector2 beta, RulesetDraft? draft = null)
    {
        draft ??= new RulesetDraft().WithoutWaves();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, new[] { ("beta", "mid") });
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = alpha;
            H(ctx.State, "beta").Position = beta;
        }));
        return kernel;
    }

    private static double Gap(SimulationKernel<MatchState> kernel) =>
        FixedVector2.Distance(H(kernel.State, "alpha").Position, H(kernel.State, "beta").Position).ToDouble();

    [Fact]
    public void OverlappingUnits_ArePushedApart()
    {
        var kernel = Pair(P(5000, 3000), P(5010, 3000));   // radii 24 + 24, only 10 apart

        kernel.RunTicks(10);

        Assert.True(Gap(kernel) >= 47.9, "gap " + Gap(kernel));
    }

    [Fact]
    public void Separation_IsSymmetric()
    {
        var kernel = Pair(P(5000, 3000), P(5010, 3000));

        kernel.RunTicks(10);

        Fixed midX = (H(kernel.State, "alpha").Position.X + H(kernel.State, "beta").Position.X) / Fixed.FromInt(2);
        Assert.InRange(midX.ToDouble(), 5004.9, 5005.1);
        Assert.Equal(H(kernel.State, "alpha").Position.Y, H(kernel.State, "beta").Position.Y);
    }

    [Fact]
    public void UnitsOnTheSameSpot_StillSeparate()
    {
        var kernel = Pair(P(5000, 3000), P(5000, 3000));

        kernel.RunTicks(10);

        Assert.True(Gap(kernel) >= 47.9);
    }

    [Fact]
    public void Separation_NeverPushesIntoObstacles()
    {
        var draft = new RulesetDraft().WithoutWaves();
        // Beta's cell (centre x 4950) is walkable; the next cell east (centre 5050) is inside the grown wall.
        draft.Obstacles.Add(ObstacleDefinition.Rectangle("wall", P(5040, 2500), P(5300, 3500)));
        var kernel = Pair(P(4980, 3000), P(4990, 3000), draft);   // beta would be pushed east into the wall

        kernel.RunTicks(20);

        Assert.True(kernel.State.Nav.IsWalkable(H(kernel.State, "beta").Position));
        Assert.True(Gap(kernel) >= 47.9, "alpha should yield instead");
    }

    [Fact]
    public void AHero_PassesAnAlliedCreepMarchingHeadOn()
    {
        // Regression: with a push as strong as a step, a retreating hero walking into an allied
        // creep marching on the same line was pushed back as far as it walked, forever.
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid") }, Array.Empty<(string, string)>());
        FixedVector2 before = FixedVector2.Zero;
        FixedVector2 destination = FixedVector2.Zero;
        kernel.AddSystem(new LambdaSystem(30, ctx => before = ctx.State.Creeps(Team.Dawn).First().Position));
        kernel.AddSystem(new LambdaSystem(31, ctx =>
        {
            // Put the hero head-on in the marching creep's path, walking the other way.
            FixedVector2 creep = ctx.State.Creeps(Team.Dawn).First().Position;
            FixedVector2 step = creep - before;
            Fixed length = FixedVector2.Distance(creep, before);
            var direction = new FixedVector2(step.X / length, step.Y / length);
            H(ctx.State, "alpha").Position = creep + direction * Fixed.FromInt(40);
            destination = creep - direction * Fixed.FromInt(800);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Move(destination));
        }));

        kernel.RunTicks(31 + 150);

        double left = FixedVector2.Distance(H(kernel.State, "alpha").Position, destination).ToDouble();
        Assert.True(left < 100, "hero still " + left + " from its destination");
    }

    [Fact]
    public void HeroesStillReachTheirDestinationThroughACrowd()
    {
        var draft = new RulesetDraft().WithoutWaves();
        var kernel = draft.StartWithHeroes(new[] { ("alpha", "mid"), ("beta", "mid"), ("gamma", "mid") }, Array.Empty<(string, string)>());
        kernel.AddSystem(new LambdaSystem(0, ctx =>
        {
            H(ctx.State, "alpha").Position = P(4000, 3000);
            H(ctx.State, "beta").Position = P(5000, 3000);    // standing on alpha's destination
            H(ctx.State, "gamma").Position = P(5000, 3040);
            HeroOrders.Issue(H(ctx.State, "alpha"), HeroOrder.Move(P(5000, 3000)));
        }));

        kernel.RunTicks(200);

        Assert.Equal(HeroOrderKind.Idle, H(kernel.State, "alpha").Hero!.Order.Kind);
        Assert.True(FixedVector2.Distance(H(kernel.State, "alpha").Position, P(5000, 3000)).ToDouble() < 80);
    }
}
