using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

public class VictoryTests
{
    /// <summary>One unkillable, hard-hitting Dawn creep per wave; Dusk sends nothing.</summary>
    private static RulesetDraft Siege()
    {
        var draft = new RulesetDraft();
        draft.Creeps.Add(new CreepType("juggernaut", MeleeStats(hp: 1_000_000, armor: 0, dmgMin: 300, dmgMax: 300)));
        draft.DawnWave[0] = new WaveEntry("juggernaut", 1);
        draft.DuskWave.Clear();
        return draft;
    }

    [Fact]
    public void ProtectedAncient_IsNotDamagedBeforeItsProtectorFalls()
    {
        var kernel = Siege().Start();
        MatchUnit ancient = kernel.State.FindStructure("dusk.ancient")!;

        kernel.RunUntilFinished(20_000);

        long towerFell = kernel.TimedEventsOf<StructureDestroyed>().Single(e => e.Event.PlacementId == "dusk.mid.t1").Tick;
        long firstAncientHit = kernel.TimedEventsOf<DamageDealt>().First(e => e.Event.Target == ancient.Id).Tick;
        Assert.True(firstAncientHit > towerFell);
    }

    [Fact]
    public void IsVulnerable_FollowsTheProtectionRule()
    {
        var kernel = new RulesetDraft().Start();
        MatchUnit ancient = kernel.State.FindStructure("dusk.ancient")!;
        MatchUnit tower = kernel.State.FindStructure("dusk.mid.t1")!;

        Assert.True(kernel.State.IsVulnerable(tower));
        Assert.False(kernel.State.IsVulnerable(ancient));
    }

    [Fact]
    public void DestroyingTheAncient_EndsTheMatchForTheOtherTeam()
    {
        var kernel = Siege().Start();

        kernel.RunUntilFinished(20_000);

        Assert.True(kernel.IsFinished);
        Assert.Equal(Team.Dawn, kernel.State.Winner);
        MatchFinished finished = Assert.Single(kernel.EventsOf<MatchFinished>());
        Assert.Equal(Team.Dawn, finished.Winner);
        Assert.Equal("dusk.ancient", finished.DestroyedAncient);
        Assert.IsType<MatchFinished>(kernel.Events.Records[^1].Event);
        Assert.Contains("dusk.ancient", kernel.State.DestroyedStructures);
        Assert.Null(kernel.State.FindStructure("dusk.ancient"));
    }

    [Fact]
    public void WithoutAttackers_TheMatchDoesNotEnd()
    {
        var kernel = new RulesetDraft().WithoutWaves().Start();

        kernel.RunUntilFinished(3000);

        Assert.False(kernel.IsFinished);
        Assert.Null(kernel.State.Winner);
    }
}
