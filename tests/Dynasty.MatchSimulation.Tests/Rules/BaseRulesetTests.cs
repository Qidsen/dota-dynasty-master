using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests.Rules;

public class BaseRulesetTests
{
    private static readonly FixedVector2 Center = FixedVector2.FromInts(5000, 5000);

    [Fact]
    public void BaseRuleset_IsValid()
    {
        Assert.Empty(BaseRuleset.Create().Validate());
    }

    [Fact]
    public void BaseRuleset_HasThreeLanes()
    {
        Assert.Equal(new[] { "top", "mid", "bot" }, BaseRuleset.Create().Map.Lanes.Select(l => l.Id));
    }

    [Fact]
    public void BaseMap_IsPointSymmetricForStructures()
    {
        var structures = BaseRuleset.Create().Map.Structures;

        foreach (StructurePlacement dawn in structures.Where(s => s.Team == Team.Dawn))
        {
            StructurePlacement? mirror = structures.SingleOrDefault(s =>
                s.Team == Team.Dusk && s.Position == dawn.Position.ReflectThrough(Center));

            Assert.NotNull(mirror);
            Assert.Equal(dawn.StructureTypeId, mirror!.StructureTypeId);
            Assert.Equal(dawn.IsAncient, mirror.IsAncient);
            Assert.Equal(dawn.ProtectedBy.Count, mirror.ProtectedBy.Count);
        }

        Assert.Equal(structures.Count(s => s.Team == Team.Dawn), structures.Count(s => s.Team == Team.Dusk));
    }

    [Fact]
    public void BaseMap_LanesAreSymmetricForBothTeams()
    {
        var lanes = BaseRuleset.Create().Map.Lanes;

        foreach (LaneDefinition lane in lanes)
        {
            var mirrored = lane.Waypoints.Reverse().Select(p => p.ReflectThrough(Center)).ToList();
            Assert.Contains(lanes, other => other.Waypoints.SequenceEqual(mirrored));
        }
    }

    [Fact]
    public void BaseWaves_AreIdenticalForBothTeams()
    {
        var waves = BaseRuleset.Create().Waves;

        Assert.Equal(
            waves.DawnComposition.Select(e => (e.CreepTypeId, e.Count)),
            waves.DuskComposition.Select(e => (e.CreepTypeId, e.Count)));
    }

    [Fact]
    public void LabRuleset_IsValidAndHasItsOwnIdentity()
    {
        RulesetContent lab = BaseRuleset.CreateDawnSiegeLab();

        Assert.Empty(lab.Validate());
        Assert.NotEqual(BaseRuleset.Create().ToDescriptor(), lab.ToDescriptor());
    }
}
