using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

public class MovementTests
{
    [Fact]
    public void DawnCreep_WalksAlongTheLaneAtItsMoveSpeed()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        var kernel = draft.Start();

        kernel.RunTicks(30);

        Assert.Equal(P(1300, 5000), Assert.Single(kernel.State.Creeps(Team.Dawn)).Position);
    }

    [Fact]
    public void DuskCreep_WalksTheLaneInReverse()
    {
        var draft = new RulesetDraft();
        draft.DawnWave.Clear();
        var kernel = draft.Start();

        kernel.RunTicks(30);

        Assert.Equal(P(8700, 5000), Assert.Single(kernel.State.Creeps(Team.Dusk)).Position);
    }

    [Fact]
    public void Creep_TurnsAtWaypoints()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Clear();
        draft.Lanes[0] = new LaneDefinition("mid", new[] { P(1000, 5000), P(2000, 5000), P(2000, 8000), P(9000, 8000) });
        var kernel = draft.Start();

        kernel.RunTicks(150);

        Assert.Equal(P(2000, 5500), Assert.Single(kernel.State.Creeps(Team.Dawn)).Position);
    }

    [Fact]
    public void Creep_StopsWhenItsTargetIsInAttackRange()
    {
        var kernel = new RulesetDraft().Start();

        kernel.StepUntil(() => kernel.State.Units.Any(u => u.Kind == UnitKind.Creep && u.TargetId.IsValid), 1000);
        kernel.RunTicks(30);

        var creeps = kernel.State.Units.Where(u => u.Kind == UnitKind.Creep).ToList();
        var gap = Dynasty.Core.Mathematics.FixedVector2.Distance(creeps[0].Position, creeps[1].Position);
        // Melee reach is 100 edge-to-edge plus two 16-unit radii.
        Assert.InRange(gap.ToDouble(), 120, 132.01);
    }
}
