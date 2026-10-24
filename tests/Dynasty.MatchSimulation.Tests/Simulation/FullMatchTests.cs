using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Simulation;

/// <summary>Whole matches on the built-in map: the phase 01 acceptance criteria.</summary>
public class FullMatchTests
{
    private const int TicksPerMinute = 30 * 60;

    [Fact]
    public void LabMatch_EndsByDestroyingTheEnemyAncient()
    {
        var kernel = Match.Create(BaseRuleset.CreateDawnSiegeLab(), seed: 1);

        kernel.RunUntilFinished(60 * TicksPerMinute);

        Assert.True(kernel.IsFinished, "Lab match did not finish within 60 game minutes.");
        Assert.Equal(Team.Dawn, kernel.State.Winner);
        Assert.Contains("dusk.ancient", kernel.State.DestroyedStructures);
        MatchTestKit.AssertProtectionOrderRespected(kernel);
    }

    [Fact]
    public void BaseMatch_IsReproducibleFromSeed()
    {
        var a = Match.Create(BaseRuleset.Create(), seed: 42);
        var b = Match.Create(BaseRuleset.Create(), seed: 42);

        a.RunTicks(3 * TicksPerMinute);
        b.RunTicks(3 * TicksPerMinute);

        Assert.Equal(a.Events.Checksum, b.Events.Checksum);
        Assert.True(a.Events.Records.Count > 100);
    }

    [Fact]
    public void BaseMatch_DifferentSeedsDiverge()
    {
        var a = Match.Create(BaseRuleset.Create(), seed: 1);
        var b = Match.Create(BaseRuleset.Create(), seed: 2);

        a.RunTicks(3 * TicksPerMinute);
        b.RunTicks(3 * TicksPerMinute);

        Assert.NotEqual(a.Events.Checksum, b.Events.Checksum);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void BaseMatch_BatchInvariantsHoldForTenMinutes(ulong seed)
    {
        var kernel = Match.Create(BaseRuleset.Create(), seed);
        Fixed mapSize = Fixed.FromInt(10000);
        long stuckLimit = 10 * 30;   // 10 seconds without moving or attacking

        for (int i = 0; i < 10 * 60; i++)
        {
            kernel.RunTicks(30);
            long now = kernel.CurrentTick.Value;

            foreach (MatchUnit unit in kernel.State.Units)
            {
                Assert.InRange(unit.Position.X.Raw, 0, mapSize.Raw);
                Assert.InRange(unit.Position.Y.Raw, 0, mapSize.Raw);
                Assert.True(unit.Hp > Fixed.Zero && unit.Hp <= Fixed.FromInt(unit.MaxHp), unit.TypeId + " has invalid HP " + unit.Hp);
                // Lane creeps must keep moving or fighting; neutral camp creeps idle in their camp by design.
                if (unit.Kind == UnitKind.Creep && unit.Team != Team.Neutral)
                    Assert.True(now - unit.LastActiveTick <= stuckLimit, unit.TypeId + " " + unit.Id + " is stuck at " + unit.Position);
            }
        }

        Assert.True(kernel.EventsOf<UnitKilled>().Count() > 50, "Creeps should be fighting and dying.");
        MatchTestKit.AssertProtectionOrderRespected(kernel);
    }
}
