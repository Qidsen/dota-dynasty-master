using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Simulation;

public class BarracksTests
{
    /// <summary>Default draft plus a Dusk barracks on mid and a "melee → brute" super upgrade.</summary>
    private static RulesetDraft WithBarracks()
    {
        var draft = new RulesetDraft { IntervalMs = 1000 };
        draft.Creeps.Add(new CreepType("brute", MeleeStats(hp: 900, dmgMin: 40, dmgMax: 40)));
        draft.StructureTypes.Add(new StructureType("barracks", AncientStats(hp: 1500)));
        draft.Placements.Add(new StructurePlacement("dusk.mid.barracks", Team.Dusk, "barracks", P(8000, 5200), false,
            new[] { "dusk.mid.t1" }, VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "mid"));
        draft.SuperUpgrades.Add(new CreepUpgrade("melee", "brute"));
        return draft;
    }

    private static void Destroy(SimulationKernel<MatchState> kernel, long tick, params string[] placements) =>
        kernel.AddSystem(new LambdaSystem(tick, ctx =>
        {
            foreach (string id in placements)
                Damage.Deal(ctx, EntityId.None, ctx.State.FindStructure(id)!, Fixed.FromInt(99999), DamageType.Pure);
        }));

    private static IEnumerable<(long Tick, CreepSpawned Event)> Spawns(SimulationKernel<MatchState> kernel, Team team) =>
        kernel.TimedEventsOf<CreepSpawned>().Where(e => e.Event.Team == team);

    [Fact]
    public void WavesAreNormal_WhileTheEnemyBarracksStand()
    {
        var kernel = WithBarracks().Start();

        kernel.RunTicks(61);

        Assert.All(Spawns(kernel, Team.Dawn), s => Assert.Equal("melee", s.Event.CreepTypeId));
    }

    [Fact]
    public void DestroyingBarracks_UpgradesTheAttackersWavesOnThatLane()
    {
        var kernel = WithBarracks().Start();
        Destroy(kernel, 0, "dusk.mid.t1");
        Destroy(kernel, 2, "dusk.mid.barracks");

        kernel.RunTicks(61);

        Assert.Equal("melee", Spawns(kernel, Team.Dawn).First(s => s.Tick == 0).Event.CreepTypeId);
        Assert.All(Spawns(kernel, Team.Dawn).Where(s => s.Tick >= 30), s => Assert.Equal("brute", s.Event.CreepTypeId));
        Assert.All(Spawns(kernel, Team.Dusk), s => Assert.Equal("melee", s.Event.CreepTypeId));
    }

    [Fact]
    public void Barracks_AreProtectedLikeOtherStructures()
    {
        var kernel = WithBarracks().Start();

        Assert.False(kernel.State.IsVulnerable(kernel.State.FindStructure("dusk.mid.barracks")!));
    }

    [Fact]
    public void Validation_RejectsUnknownUpgradeTypesAndBarracksLanes()
    {
        var draft = WithBarracks();
        draft.SuperUpgrades.Add(new CreepUpgrade("ranged", "titan"));
        draft.Placements.Add(new StructurePlacement("dawn.river.barracks", Team.Dawn, "barracks", P(2000, 5200), false,
            Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed, barracksLane: "river"));

        IReadOnlyList<string> errors = draft.Build().Validate();

        Assert.Contains(errors, e => e.Contains("titan"));
        Assert.Contains(errors, e => e.Contains("river"));
    }

    [Fact]
    public void BaseMap_HasBarracksBehindEveryLaneT3()
    {
        RulesetContent ruleset = BaseRuleset.Create();

        foreach (Team team in new[] { Team.Dawn, Team.Dusk })
        {
            foreach (string lane in new[] { "top", "mid", "bot" })
            {
                string prefix = team == Team.Dawn ? "dawn" : "dusk";
                StructurePlacement barracks = Assert.Single(ruleset.Map.Structures, s => s.Team == team && s.BarracksLane == lane);
                Assert.Equal(new[] { $"{prefix}.{lane}.t3" }, barracks.ProtectedBy);
            }
        }

        Assert.NotEmpty(ruleset.Waves.SuperUpgrades);
    }
}
