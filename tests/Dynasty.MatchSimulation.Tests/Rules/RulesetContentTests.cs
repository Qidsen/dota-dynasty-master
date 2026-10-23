using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;
using static Dynasty.MatchSimulation.Tests.RulesetDraft;

namespace Dynasty.MatchSimulation.Tests.Rules;

public class RulesetContentTests
{
    [Fact]
    public void DefaultDraft_IsValid()
    {
        Assert.Empty(new RulesetDraft().Build().Validate());
    }

    [Fact]
    public void Validate_ReportsUnknownCreepTypeInWave()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Add(new WaveEntry("dragon", 1));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("dragon"));
    }

    [Fact]
    public void Validate_ReportsUnknownStructureType()
    {
        var draft = new RulesetDraft();
        draft.Placements.Add(new StructurePlacement("dawn.x", Team.Dawn, "castle", P(100, 100), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("castle"));
    }

    [Fact]
    public void Validate_ReportsDuplicatePlacementIds()
    {
        var draft = new RulesetDraft();
        draft.Placements.Add(new StructurePlacement("dawn.mid.t1", Team.Dawn, "tower", P(100, 100), false, Array.Empty<string>(), VulnerabilityRule.AnyProtectorDestroyed));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("dawn.mid.t1"));
    }

    [Fact]
    public void Validate_RequiresExactlyOneAncientPerTeam()
    {
        var draft = new RulesetDraft();
        draft.Placements.RemoveAll(p => p.Id == "dusk.ancient");

        Assert.Contains(draft.Build().Validate(), e => e.Contains("Dusk") && e.Contains("ancient"));
    }

    [Fact]
    public void Validate_ReportsProtectorFromOtherTeamOrMissing()
    {
        var draft = new RulesetDraft();
        draft.Placements.Add(new StructurePlacement("dawn.t2", Team.Dawn, "tower", P(2000, 5000), false, new[] { "dusk.mid.t1", "nowhere" }, VulnerabilityRule.AnyProtectorDestroyed));

        IReadOnlyList<string> errors = draft.Build().Validate();

        Assert.Contains(errors, e => e.Contains("dusk.mid.t1"));
        Assert.Contains(errors, e => e.Contains("nowhere"));
    }

    [Fact]
    public void Validate_ReportsLaneWithTooFewWaypoints()
    {
        var draft = new RulesetDraft();
        draft.Lanes.Add(new LaneDefinition("broken", new[] { P(1, 1) }));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("broken"));
    }

    [Fact]
    public void Validate_ReportsPositionsOutsideTheMap()
    {
        var draft = new RulesetDraft();
        draft.Lanes.Add(new LaneDefinition("outside", new[] { P(1, 1), P(20000, 1) }));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("outside"));
    }

    [Fact]
    public void Validate_ReportsInvertedDamageRange()
    {
        var draft = new RulesetDraft();
        draft.Creeps.Add(new CreepType("odd", MeleeStats(dmgMin: 30, dmgMax: 10)));

        Assert.Contains(draft.Build().Validate(), e => e.Contains("odd"));
    }

    [Fact]
    public void ToDescriptor_RefusesInvalidRuleset()
    {
        var draft = new RulesetDraft();
        draft.DuskWave.Add(new WaveEntry("dragon", 1));

        Assert.Throws<InvalidOperationException>(() => draft.Build().ToDescriptor());
    }

    [Fact]
    public void Fingerprint_IsStableForEqualContent()
    {
        Assert.Equal(new RulesetDraft().Build().ToDescriptor(), new RulesetDraft().Build().ToDescriptor());
    }

    [Fact]
    public void Fingerprint_ChangesWhenAnyBalanceValueChanges()
    {
        var nerfed = new RulesetDraft();
        nerfed.Creeps[0] = new CreepType("melee", MeleeStats(dmgMax: 21));

        Assert.NotEqual(new RulesetDraft().Build().ToDescriptor().Fingerprint, nerfed.Build().ToDescriptor().Fingerprint);
    }

    [Fact]
    public void Fingerprint_ChangesWhenMapChanges()
    {
        var moved = new RulesetDraft();
        moved.Lanes[0] = new LaneDefinition("mid", new[] { P(1000, 5000), P(9000, 5001) });

        Assert.NotEqual(new RulesetDraft().Build().ToDescriptor().Fingerprint, moved.Build().ToDescriptor().Fingerprint);
    }

    [Fact]
    public void Descriptor_CarriesIdAndVersion()
    {
        var draft = new RulesetDraft { Id = "dynasty.patch", Version = 7 };

        var descriptor = draft.Build().ToDescriptor();

        Assert.Equal("dynasty.patch", descriptor.Id);
        Assert.Equal(7, descriptor.Version);
    }
}
