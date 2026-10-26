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
    public void BaseHeroPool_IsLargeEnoughForADraftWithBans()
    {
        // 10 picks and 8 bans leave a real choice only with a pool well above 18 heroes.
        var heroes = BaseRuleset.Create().Heroes;

        Assert.Equal(24, heroes.Count);
        Assert.Equal(heroes.Count, heroes.Select(h => h.Id).Distinct().Count());
        foreach (string role in new[] { "carry", "mid", "offlane", "support" })
            Assert.True(heroes.Count(h => h.Roles.Contains(role)) >= 5, role + " heroes: " + heroes.Count(h => h.Roles.Contains(role)));
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
    public void LaneTowerIds_NameTheLaneTheyPhysicallyStandOn()
    {
        RulesetContent ruleset = BaseRuleset.Create();

        foreach (StructurePlacement tower in ruleset.Map.Structures.Where(s => s.Id.Split('.')[1] is "top" or "mid" or "bot"))
        {
            string named = tower.Id.Split('.')[1];
            LaneDefinition nearest = ruleset.Map.Lanes
                .OrderBy(l => l.Waypoints.Zip(l.Waypoints.Skip(1), (a, b) => DistanceToSegment(tower.Position, a, b)).Min())
                .First();
            Assert.True(named == nearest.Id, $"{tower.Id} stands on lane '{nearest.Id}'");
        }
    }

    private static double DistanceToSegment(FixedVector2 p, FixedVector2 a, FixedVector2 b)
    {
        double px = p.X.ToDouble(), py = p.Y.ToDouble(), ax = a.X.ToDouble(), ay = a.Y.ToDouble(), bx = b.X.ToDouble(), by = b.Y.ToDouble();
        double dx = bx - ax, dy = by - ay;
        double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(px - (ax + t * dx), 2) + Math.Pow(py - (ay + t * dy), 2));
    }

    /// <summary>
    /// The jungle is Dota's: per half two small, four medium, four large and two ancient camps that
    /// spawn on the clock every minute from 1:00, worth about 1100 gold a minute per half when all are
    /// cleared, and placed clear of the lanes and towers.
    /// </summary>
    [Fact]
    public void BaseJungle_HasDotaCampsOnTheClock()
    {
        RulesetContent ruleset = BaseRuleset.Create();
        var dawn = ruleset.Map.Camps.Where(c => c.Id.StartsWith("camp.dawn.")).ToList();

        Assert.Equal(new[] { ("ancient", 2), ("large", 4), ("medium", 4), ("small", 2) },
            dawn.GroupBy(c => c.Id.Split('.')[2]).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())));
        Assert.All(ruleset.Map.Camps, c => Assert.True(c.AlignedToClock && c.FirstSpawnMs == 60000 && c.RespawnMs == 60000, c.Id));
        double gold = dawn.Sum(c => c.Creeps.Sum(e => e.Count * (ruleset.FindCreepType(e.CreepTypeId)!.Bounty.GoldMin + ruleset.FindCreepType(e.CreepTypeId)!.Bounty.GoldMax) / 2.0));
        Assert.InRange(gold, 1000, 1300);
        foreach (NeutralCampDefinition camp in ruleset.Map.Camps)
        {
            Assert.All(ruleset.Map.Structures, s => Assert.True(FixedVector2.Distance(s.Position, camp.Position) > Fixed.FromInt(800), camp.Id + " near " + s.Id));
            foreach (LaneDefinition lane in ruleset.Map.Lanes)
                for (int i = 0; i + 1 < lane.Waypoints.Count; i++)
                    Assert.True(DistanceToSegment(camp.Position, lane.Waypoints[i], lane.Waypoints[i + 1]) > 700, camp.Id + " near lane " + lane.Id);
        }
    }

    /// <summary>Experience as in Dota: its level table to 25, a 1500 share radius, hero kills worth 100 + 13% of the victim's XP, and every hero attacks faster with levels.</summary>
    [Fact]
    public void BaseHeroRules_FollowDotaExperience()
    {
        RulesetContent ruleset = BaseRuleset.Create();
        HeroRules rules = ruleset.HeroRules!;

        Assert.Equal(new[] { 0, 240, 640, 1160, 1760, 2440, 3200, 4000, 4900, 5900, 7000, 8200, 9500, 10900, 12400, 14000, 15700, 17500, 19400, 21400, 23600, 26000, 28600, 31400, 34400 },
            rules.XpForLevel);
        Assert.Equal(1500, rules.XpShareRadius);
        Assert.Equal((100, 0, 13), (rules.HeroKillXpBase, rules.HeroKillXpPerLevel, rules.HeroKillXpPercentOfVictim));
        Assert.All(ruleset.Heroes, h => Assert.True(h.AttackSpeedPerLevel > 0, h.Id));
    }

    [Fact]
    public void BaseCamps_AreMirroredBetweenTheHalves()
    {
        var camps = BaseRuleset.Create().Map.Camps;

        Assert.NotEmpty(camps);
        foreach (NeutralCampDefinition camp in camps)
        {
            NeutralCampDefinition mirror = Assert.Single(camps, c => c.Position == camp.Position.ReflectThrough(Center));
            Assert.Equal(camp.Creeps.Select(e => (e.CreepTypeId, e.Count)), mirror.Creeps.Select(e => (e.CreepTypeId, e.Count)));
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
