using Dynasty.Core.Mathematics;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Content.Tests;

/// <summary>
/// Ruleset packs (spec §27, decision D-048): a JSON file replaces the heroes and hero rules of a base
/// ruleset (the map, creeps and structures stay), numbers are read exactly, mistakes are reported with
/// the path of the bad field, and matches play on the pack's heroes.
/// </summary>
public class RulesetPackTests
{
    private const string Hero1 = @"{
        ""id"": ""antimage"", ""name"": ""Anti-Mage"", ""roles"": [""carry"", ""escape""], ""attackType"": ""melee"",
        ""primaryAttribute"": ""agility"", ""strength"": 21, ""agility"": 25, ""intelligence"": 12,
        ""strengthGain"": 1.6, ""agilityGain"": 2.8, ""intelligenceGain"": 1.8,
        ""baseHealth"": 120, ""baseHealthRegen"": 1.5, ""baseMana"": 75, ""baseManaRegen"": 0, ""baseArmor"": 2, ""magicResist"": 25,
        ""attackDamageMin"": 29, ""attackDamageMax"": 33, ""attackRange"": 150, ""projectileSpeed"": 0,
        ""baseAttackTime"": 1.4, ""baseAttackSpeed"": 100, ""attackPoint"": 0.3, ""moveSpeed"": 315, ""dayVision"": 1800, ""nightVision"": 800 }";

    private const string Hero2 = @"{
        ""id"": ""lion"", ""name"": ""Lion"", ""roles"": [""support"", ""disabler""], ""attackType"": ""ranged"",
        ""primaryAttribute"": ""intelligence"", ""strength"": 18, ""agility"": 15, ""intelligence"": 22,
        ""strengthGain"": 2.3, ""agilityGain"": 1.5, ""intelligenceGain"": 3.5,
        ""baseHealth"": 120, ""baseHealthRegen"": 0.5, ""baseMana"": 75, ""baseManaRegen"": 0, ""baseArmor"": 1, ""magicResist"": 25,
        ""attackDamageMin"": 29, ""attackDamageMax"": 35, ""attackRange"": 600, ""projectileSpeed"": 1000,
        ""baseAttackTime"": 1.7, ""baseAttackSpeed"": 100, ""attackPoint"": 0.43, ""moveSpeed"": 290, ""dayVision"": 1800, ""nightVision"": 800 }";

    /// <summary>Captains mode needs 18 heroes: the pack is filled up with copies of Anti-Mage under other IDs.</summary>
    private static string Pack(string heroes, string? heroRules = null) => @"{
        ""format"": ""dynasty.ruleset-pack"", ""formatVersion"": 1, ""id"": ""dota"", ""version"": 7410, ""name"": ""Dota 2 7.41f"",
        " + (heroRules != null ? @"""heroRules"": " + heroRules + "," : "") + @"
        ""heroes"": [" + heroes + string.Concat(Enumerable.Range(1, 18).Select(i => "," + Hero1.Replace(@"""antimage""", @"""filler" + i + @""""))) + "] }";

    private static RulesetContent Apply(string json) => RulesetPack.Apply(BaseRuleset.Create(), json);

    [Fact]
    public void Apply_ReplacesTheHeroes_AndKeepsTheBaseMap()
    {
        RulesetContent baseRules = BaseRuleset.Create();

        RulesetContent pack = RulesetPack.Apply(baseRules, Pack(Hero1 + "," + Hero2));

        Assert.Equal(new[] { "antimage", "lion" }, pack.Heroes.Take(2).Select(h => h.Id));
        Assert.Equal(20, pack.Heroes.Count);
        Assert.Equal("dota", pack.Id);
        Assert.Equal(7410, pack.Version);
        Assert.Same(baseRules.Map, pack.Map);
        Assert.Equal(baseRules.CreepTypes.Count, pack.CreepTypes.Count);
        Assert.NotEqual(baseRules.ToDescriptor().Fingerprint, pack.ToDescriptor().Fingerprint);
    }

    [Fact]
    public void Hero_ReadsDotaStatsAndAttributesExactly()
    {
        HeroDefinition am = Apply(Pack(Hero1)).Heroes.First();

        Assert.Equal("Anti-Mage", am.Name);
        Assert.Equal(new[] { "carry", "escape" }, am.Roles);
        Assert.Equal(PrimaryAttribute.Agility, am.Attributes!.Primary);
        Assert.Equal((21, 25, 12), (am.Attributes.Strength, am.Attributes.Agility, am.Attributes.Intelligence));
        Assert.Equal(Fixed.FromRatio(28, 10), am.Attributes.AgilityGain);
        Assert.Equal(120, am.BaseStats.MaxHp);
        Assert.Equal(Fixed.FromInt(2), am.BaseStats.Armor);
        Assert.Equal(Fixed.FromRatio(15, 10), am.HpRegenPerSecond);
        Assert.Equal(75, am.BaseMana);
        Assert.Equal((29, 33), (am.BaseStats.AttackDamageMin, am.BaseStats.AttackDamageMax));
        Assert.Equal(1400, am.BaseStats.AttackIntervalMs);
        Assert.Equal(300, am.BaseStats.AttackPointMs);
        Assert.Equal((150, 0, 315, 25), (am.BaseStats.AttackRange, am.BaseStats.ProjectileSpeed, am.BaseStats.MoveSpeed, am.BaseStats.MagicResistPercent));
        Assert.Equal((1800, 800), (am.BaseStats.VisionRange, am.BaseStats.NightVisionRange));
    }

    [Fact]
    public void HeroRules_FromThePackOverrideTheBase_TheRestIsKept()
    {
        string xp = string.Join(",", Enumerable.Range(0, 30).Select(i => i * 1000));
        RulesetContent pack = Apply(Pack(Hero1, @"{ ""xpForLevel"": [" + xp + @"], ""xpShareRadius"": 1500, ""heroKillXpBase"": 100, ""heroKillXpPercentOfVictim"": 13 }"));
        HeroRules baseRules = BaseRuleset.Create().HeroRules!;

        Assert.Equal(30, pack.HeroRules!.MaxLevel);
        Assert.Equal(1500, pack.HeroRules.XpShareRadius);
        Assert.Equal((100, 13), (pack.HeroRules.HeroKillXpBase, pack.HeroRules.HeroKillXpPercentOfVictim));
        Assert.Equal(baseRules.StartingGold, pack.HeroRules.StartingGold);
        Assert.Equal(baseRules.GoldPerMinute, pack.HeroRules.GoldPerMinute);
    }

    [Theory]
    [InlineData(@"{ ""format"": ""something-else"", ""formatVersion"": 1 }", "format")]
    [InlineData(@"{ ""format"": ""dynasty.ruleset-pack"", ""formatVersion"": 99 }", "formatVersion")]
    public void UnknownFormat_IsRejected(string json, string field)
    {
        var error = Assert.Throws<RulesetPackException>(() => Apply(json));
        Assert.Contains(field, error.Message);
    }

    [Fact]
    public void BadField_IsReportedWithItsPath()
    {
        string broken = Hero2.Replace(@"""agility"": 15", @"""agility"": ""fast""");

        var error = Assert.Throws<RulesetPackException>(() => Apply(Pack(Hero1 + "," + broken)));

        Assert.Contains("heroes[1].agility", error.Message);
    }

    [Fact]
    public void Matches_PlayOnThePacksHeroes()
    {
        RulesetContent rules = Apply(Pack(Hero1 + "," + Hero2));
        var lineup = new MatchLineup(new[] { new HeroPick("antimage", "bot"), new HeroPick("lion", "bot") }, new[] { new HeroPick("antimage", "top"), new HeroPick("lion", "top") });
        var kernel = Match.Create(rules, 7, lineup);
        HeroAi.Attach(kernel);

        kernel.RunTicks(30 * 120);

        MatchUnit am = kernel.State.Heroes.First(h => h.Hero!.Definition.Id == "antimage");
        Assert.True(am.MaxHp >= 120 + 22 * 21);   // attributes in play
        Assert.Contains(kernel.State.Heroes, h => h.Hero!.LastHits > 0);
    }

    /// <summary>
    /// A pack may bring its own map (Dota's, D-053): the size, the map cell by cell, each side's path of every lane, the buildings
    /// with their protectors, the fountains, the camps, and the boss pit; its structure types may then be new ones.
    /// 160×160 cells of 64: walkable but cell 0, cell 1 at level 2, cell 2 blocks sight, one tree.
    /// </summary>
    private static string MapSection(string extra = "")
    {
        var walkable = new byte[160 * 160 / 8];
        Array.Fill(walkable, (byte)0xFF);
        walkable[0] = 0xFE;
        var levels = new byte[160 * 160];
        levels[1] = 2;
        var blockers = new byte[160 * 160 / 8];
        blockers[0] = 0x04;
        return @"""map"": {
            ""width"": 10240, ""height"": 10240, ""navCellSize"": 64, ""navClearance"": 0,
            ""terrain"": { ""columns"": 160, ""rows"": 160, ""walkable"": """ + Convert.ToBase64String(walkable) + @""",
                ""visionBlockers"": """ + Convert.ToBase64String(blockers) + @""", ""levels"": """ + Convert.ToBase64String(levels) + @""",
                ""treeSize"": 128, ""trees"": [640, 640] },
            ""lanes"": [ { ""id"": ""mid"", ""dawn"": [500, 500, 9700, 9700], ""dusk"": [9700, 9500, 500, 300] } ],
            ""structures"": [
                { ""id"": ""dawn.mid.t1"", ""team"": ""dawn"", ""type"": ""tower.t1"", ""x"": 3000, ""y"": 3000 },
                { ""id"": ""dawn.mid.ranged_rax"", ""team"": ""dawn"", ""type"": ""barracks.ranged"", ""x"": 1500, ""y"": 1400,
                  ""protectedBy"": [""dawn.mid.t1""], ""barracksLane"": ""mid"" },
                { ""id"": ""dawn.ancient"", ""team"": ""dawn"", ""type"": ""ancient"", ""x"": 1000, ""y"": 1000, ""ancient"": true,
                  ""protectedBy"": [""dawn.mid.t1""], ""vulnerability"": ""allProtectorsDestroyed"" },
                { ""id"": ""dusk.mid.t1"", ""team"": ""dusk"", ""type"": ""tower.t1"", ""x"": 7200, ""y"": 7200 },
                { ""id"": ""dusk.ancient"", ""team"": ""dusk"", ""type"": ""ancient"", ""x"": 9200, ""y"": 9200, ""ancient"": true,
                  ""protectedBy"": [""dusk.mid.t1""], ""vulnerability"": ""allProtectorsDestroyed"" } ],
            ""fountains"": [ { ""team"": ""dawn"", ""x"": 300, ""y"": 300, ""radius"": 1200 }, { ""team"": ""dusk"", ""x"": 9900, ""y"": 9900, ""radius"": 1200 } ],
            ""camps"": [ { ""id"": ""camp.dawn.small"", ""x"": 4000, ""y"": 2000, ""creeps"": [ { ""creep"": ""jungle_small"", ""count"": 2 } ],
                ""firstSpawn"": 60, ""respawn"": 60, ""leashRadius"": 600, ""alignedToClock"": true } ]" + extra + @" },
        ""structureTypes"": [ { ""id"": ""barracks.ranged"", ""health"": 1300, ""armor"": 9, ""visionRange"": 900, ""nightVision"": 600,
            ""upgradesCreeps"": [""ranged""] } ],
        ""boss"": { ""id"": ""colossus"", ""creep"": ""colossus"", ""respawnMin"": 480, ""respawnMax"": 660, ""teamGold"": 200,
            ""position"": { ""x"": 5000, ""y"": 4000 }, ""nightPosition"": { ""x"": 6000, ""y"": 3000 }, ""leashRadius"": 900, ""aegisDrops"": true },";
    }

    private static string PackWith(string sections) => Pack(Hero1).Replace(@"""heroes"": [", sections + @"""heroes"": [");

    [Fact]
    public void Map_FromThePack_ReplacesTheBaseMap()
    {
        RulesetContent pack = Apply(PackWith(MapSection()));
        MapDefinition map = pack.Map;

        Assert.Equal((10240, 10240, 64, 0), (map.Width, map.Height, map.NavCellSize, map.NavClearance));
        MapTerrain terrain = map.Terrain!;
        Assert.Equal((160, 160), (terrain.Columns, terrain.Rows));
        Assert.Equal((false, true, true), (terrain.IsWalkable(0), terrain.IsWalkable(1), terrain.IsWalkable(160 * 160 - 1)));
        Assert.Equal((0, 2), (terrain.LevelOf(0), terrain.LevelOf(1)));
        Assert.Equal((false, true), (terrain.BlocksSight(1), terrain.BlocksSight(2)));
        Assert.Equal(new[] { FixedVector2.FromInts(640, 640) }, terrain.Trees);
        Assert.Equal(128, terrain.TreeSize);

        LaneDefinition lane = Assert.Single(map.Lanes);
        Assert.Equal(new[] { FixedVector2.FromInts(500, 500), FixedVector2.FromInts(9700, 9700) }, lane.PathOf(Team.Dawn));
        Assert.Equal(new[] { FixedVector2.FromInts(9700, 9500), FixedVector2.FromInts(500, 300) }, lane.PathOf(Team.Dusk));

        Assert.Equal(new[] { "dawn.mid.t1", "dawn.mid.ranged_rax", "dawn.ancient", "dusk.mid.t1", "dusk.ancient" }, map.Structures.Select(s => s.Id));
        StructurePlacement rax = map.Structures[1];
        Assert.Equal((Team.Dawn, "barracks.ranged", FixedVector2.FromInts(1500, 1400), false, "mid", VulnerabilityRule.AnyProtectorDestroyed),
            (rax.Team, rax.StructureTypeId, rax.Position, rax.IsAncient, rax.BarracksLane, rax.Vulnerability));
        Assert.Equal(new[] { "dawn.mid.t1" }, rax.ProtectedBy);
        StructurePlacement ancient = map.Structures[2];
        Assert.Equal((true, VulnerabilityRule.AllProtectorsDestroyed), (ancient.IsAncient, ancient.Vulnerability));
        Assert.Equal(new[] { "ranged" }, pack.FindStructureType("barracks.ranged")!.UpgradesCreepTypeIds);

        Assert.Equal(new[] { (Team.Dawn, FixedVector2.FromInts(300, 300), 1200), (Team.Dusk, FixedVector2.FromInts(9900, 9900), 1200) },
            map.Fountains.Select(f => (f.Team, f.Position, f.Radius)));
        NeutralCampDefinition camp = Assert.Single(map.Camps);
        Assert.Equal(("camp.dawn.small", FixedVector2.FromInts(4000, 2000), 60000, 60000, 600, true), (camp.Id, camp.Position, camp.FirstSpawnMs, camp.RespawnMs, camp.LeashRadius, camp.AlignedToClock));
        Assert.Equal(("jungle_small", 2), (camp.Creeps[0].CreepTypeId, camp.Creeps[0].Count));
        Assert.Equal((FixedVector2.FromInts(5000, 4000), FixedVector2.FromInts(6000, 3000), 900, true), (pack.Boss!.Position, pack.Boss.NightPosition, pack.Boss.LeashRadius, pack.Boss.AegisDrops));
    }

    [Fact]
    public void Camps_MayGiveCampTypesOrEvolutionStages()
    {
        string camps = @"""alignedToClock"": true },
            { ""id"": ""camp.dawn.medium"", ""x"": 4000, ""y"": 3000, ""variants"": [ [ { ""creep"": ""jungle_small"", ""count"": 3 } ], [ { ""creep"": ""jungle_medium"", ""count"": 2 } ] ],
              ""firstSpawn"": 60, ""respawn"": 60, ""leashRadius"": 600, ""alignedToClock"": true },
            { ""id"": ""camp.dawn.flooded"", ""x"": 5000, ""y"": 3000, ""evolution"": { ""interval"": 300, ""stages"": [ [ { ""creep"": ""jungle_small"", ""count"": 3 } ],
                [ { ""creep"": ""jungle_small"", ""count"": 2 }, { ""creep"": ""jungle_medium"", ""count"": 1 } ] ] },
              ""firstSpawn"": 60, ""respawn"": 60, ""leashRadius"": 600, ""alignedToClock"": true }";
        string json = PackWith(MapSection()).Replace(@"""alignedToClock"": true }", camps);

        MapDefinition map = Apply(json).Map;

        NeutralCampDefinition medium = map.Camps.Single(c => c.Id == "camp.dawn.medium");
        Assert.Equal(new[] { "jungle_small:3", "jungle_medium:2" }, medium.Variants.Select(v => string.Join(",", v.Select(e => e.CreepTypeId + ":" + e.Count))));
        Assert.Null(medium.Evolution);
        CampEvolution evolution = map.Camps.Single(c => c.Id == "camp.dawn.flooded").Evolution!;
        Assert.Equal(300000, evolution.IntervalMs);
        Assert.Equal(new[] { "jungle_small:3", "jungle_small:2,jungle_medium:1" }, evolution.Stages.Select(v => string.Join(",", v.Select(e => e.CreepTypeId + ":" + e.Count))));
    }

    [Fact]
    public void Map_FromThePack_Plays()
    {
        RulesetContent pack = Apply(PackWith(MapSection()));
        var kernel = Match.Create(pack, 7);

        kernel.RunTicks(30 * 10);

        // Dusk walks its own path, 200 below Dawn's diagonal (creeps spread a little around their paths).
        Assert.Contains(kernel.State.Units, u => u.Kind == UnitKind.Creep && u.Team == Team.Dusk && u.Position.X - u.Position.Y > Fixed.FromInt(150));
        Assert.Contains(kernel.State.Units, u => u.Kind == UnitKind.Creep && u.Team == Team.Dawn && Fixed.Abs(u.Position.X - u.Position.Y) < Fixed.FromInt(50));
    }

    [Fact]
    public void Waves_MayGiveTheSuperAndMegaCreeps()
    {
        string waves = @"""waves"": { ""composition"": [ { ""creep"": ""melee"", ""count"": 3 } ],
            ""superUpgrades"": [ { ""from"": ""melee"", ""to"": ""melee_super"" } ], ""megaUpgrades"": [ { ""from"": ""melee"", ""to"": ""ranged_super"" } ] },";

        WaveRules rules = Apply(PackWith(waves)).Waves;

        Assert.Equal(new[] { ("melee", "melee_super") }, rules.SuperUpgrades.Select(u => (u.FromCreepTypeId, u.ToCreepTypeId)));
        Assert.Equal(new[] { ("melee", "ranged_super") }, rules.MegaUpgrades.Select(u => (u.FromCreepTypeId, u.ToCreepTypeId)));
    }

    [Fact]
    public void Miniboss_IsReadWithItsNumbers()
    {
        string miniboss = @"""miniboss"": { ""id"": ""tormentor"", ""creep"": ""colossus"", ""dayPosition"": { ""x"": 7000, ""y"": 2000 }, ""nightPosition"": { ""x"": 3000, ""y"": 8000 },
            ""firstSpawn"": 1200, ""respawn"": 600, ""teamGold"": 175, ""teamGoldWithoutItem"": 240, ""growthInterval"": 60,
            ""barrier"": 3000, ""barrierPerInterval"": 50, ""barrierRegen"": 20, ""barrierRegenPerInterval"": 5,
            ""reflectPercent"": 30, ""reflectPercentPerInterval"": 1.5, ""reflectRadius"": 1200, ""reflectInterval"": 1,
            ""shiningRadius"": 1000, ""shiningDamage"": 20, ""shiningDamagePerInterval"": 2, ""shiningInterval"": 0.2, ""shiningAfterHit"": 10,
            ""deathModifier"": { ""key"": ""tormentor_alleviation"", ""duration"": 15, ""healthRegenMaxPercent"": 2.25 }, ""deathModifierRadius"": 900 },";

        MinibossDefinition m = Apply(PackWith(miniboss)).Miniboss!;

        Assert.Equal(("tormentor", "colossus", FixedVector2.FromInts(7000, 2000), FixedVector2.FromInts(3000, 8000), 1200000, 600000),
            (m.Id, m.CreepTypeId, m.DayPosition, m.NightPosition, m.FirstSpawnMs, m.RespawnMs));
        Assert.Equal((175, (string?)null, 240, 60000, 3000, 50), (m.TeamGold, m.RewardItemId, m.TeamGoldWithoutItem, m.GrowthIntervalMs, m.Barrier, m.BarrierPerInterval));
        Assert.Equal((Fixed.FromInt(20), Fixed.FromInt(5), Fixed.FromInt(30), Fixed.FromRatio(3, 2), 1200, 1000),
            (m.BarrierRegen, m.BarrierRegenPerInterval, m.ReflectPercent, m.ReflectPercentPerInterval, m.ReflectRadius, m.ReflectIntervalMs));
        Assert.Equal((1000, Fixed.FromInt(20), Fixed.FromInt(2), 200, 10000, 900),
            (m.ShiningRadius, m.ShiningDamage, m.ShiningDamagePerInterval, m.ShiningIntervalMs, m.ShiningAfterHitMs, m.DeathModifierRadius));
        Assert.Equal(("tormentor_alleviation", 15000, Fixed.FromRatio(9, 4)), (m.DeathModifier!.Key, m.DeathModifier.DurationMs, m.DeathModifier.HealPerSecondMaxHealthPercent));
    }

    [Fact]
    public void LotusPools_AreRead()
    {
        string pools = @"""lotusPools"": { ""positions"": [1000, 2000], ""first"": 180, ""interval"": 180, ""max"": 6, ""radius"": 350, ""firstPick"": 1.5,
            ""nextPickPercent"": 70, ""minPick"": 0.3, ""item"": ""cheese"" },
        ""items"": [ { ""id"": ""cheese"", ""name"": ""Cheese"", ""cost"": 0 } ],";

        LotusPoolRules rules = Apply(PackWith(pools)).LotusPools!;

        Assert.Equal(new[] { FixedVector2.FromInts(1000, 2000) }, rules.Positions);
        Assert.Equal((180000, 180000, 6, 350, 1500, 70, 300, "cheese"),
            (rules.FirstMs, rules.IntervalMs, rules.Max, rules.Radius, rules.FirstPickMs, rules.NextPickPercent, rules.MinPickMs, rules.ItemId));
    }

    [Fact]
    public void TheStashAndCourier_AreRead()
    {
        string rules = @"{ ""stash"": { ""backpackSlots"": 3, ""stashSlots"": 6, ""courierSpeed"": 290, ""courierSpeedPerLevel"": 10, ""courierFliesFromLevel"": 4 } }";

        StashRules stash = Apply(Pack(Hero1, rules)).HeroRules!.Stash!;

        Assert.Equal((3, 6, 290, 10, 4), (stash.BackpackSlots, stash.StashSlots, stash.CourierSpeed, stash.CourierSpeedPerLevel, stash.CourierFliesFromLevel));
    }

    [Fact]
    public void TwinGates_AreRead()
    {
        string gates = @"""twinGates"": { ""positions"": [1000, 2000, 9000, 8000], ""radius"": 200, ""channel"": 4, ""manaCost"": 30 },";

        TwinGateRules rules = Apply(PackWith(gates)).TwinGates!;

        Assert.Equal(new[] { FixedVector2.FromInts(1000, 2000), FixedVector2.FromInts(9000, 8000) }, rules.Positions);
        Assert.Equal((200, 4000, 30), (rules.Radius, rules.ChannelMs, rules.ManaCost));
    }

    [Fact]
    public void Outposts_AreRead()
    {
        string outposts = @"""outposts"": { ""positions"": [1000, 2000, 3000, 4000], ""owners"": [""dawn"", ""dusk""], ""radius"": 300, ""capture"": 6 },";

        OutpostRules rules = Apply(PackWith(outposts)).Outposts!;

        Assert.Equal(new[] { FixedVector2.FromInts(1000, 2000), FixedVector2.FromInts(3000, 4000) }, rules.Positions);
        Assert.Equal(new[] { Team.Dawn, Team.Dusk }, rules.Owners);
        Assert.Equal((300, 6000), (rules.Radius, rules.CaptureMs));
    }

    [Fact]
    public void Watchers_AreRead()
    {
        string watchers = @"""watchers"": { ""positions"": [1000, 2000, 3000, 4000], ""radius"": 200, ""channel"": 1, ""on"": 420, ""off"": 120, ""dayVision"": 800, ""nightVision"": 450 },";

        WatcherRules rules = Apply(PackWith(watchers)).Watchers!;

        Assert.Equal(new[] { FixedVector2.FromInts(1000, 2000), FixedVector2.FromInts(3000, 4000) }, rules.Positions);
        Assert.Equal((200, 1000, 420000, 120000, 800, 450), (rules.Radius, rules.ChannelMs, rules.OnMs, rules.OffMs, rules.DayVision, rules.NightVision));
    }

    [Fact]
    public void Shrines_AreRead()
    {
        string shrines = @"""shrines"": { ""positions"": [1000, 2000, 9000, 8000], ""first"": 420, ""interval"": 420, ""radius"": 300, ""capture"": 3, ""xp"": 200, ""xpPerInterval"": 300 },";

        ShrineRules rules = Apply(PackWith(shrines)).Shrines!;

        Assert.Equal(new[] { FixedVector2.FromInts(1000, 2000), FixedVector2.FromInts(9000, 8000) }, rules.Positions);
        Assert.Equal((420000, 420000, 300, 3000, 200, 300), (rules.FirstMs, rules.IntervalMs, rules.Radius, rules.CaptureMs, rules.Xp, rules.XpPerInterval));
    }

    [Fact]
    public void Map_FromThePack_NeedsTheBossPitPlaced()
    {
        string json = PackWith(MapSection());
        int start = json.IndexOf(@"""boss"": {", StringComparison.Ordinal);
        int end = json.IndexOf(@"""aegisDrops"": true },", start, StringComparison.Ordinal) + @"""aegisDrops"": true },".Length;

        var e = Assert.Throws<RulesetPackException>(() => Apply(json.Remove(start, end - start)));
        Assert.Contains("boss", e.Message);
    }

    [Fact]
    public void WithoutItsOwnMap_APackCannotAddStructureTypes()
    {
        string types = @"""structureTypes"": [ { ""id"": ""barracks.ranged"", ""health"": 1300, ""armor"": 9, ""visionRange"": 900 } ],";

        var e = Assert.Throws<RulesetPackException>(() => Apply(PackWith(types)));
        Assert.Contains("barracks.ranged", e.Message);
    }

    [Theory]
    [InlineData(@"""alignedToClock"": true }", @"""alignedToClock"": true, ""weather"": 1 }", "map.camps[0].weather")]
    [InlineData(@"""dawn"": [500, 500, 9700, 9700]", @"""dawn"": [500, 500, 9700]", "map.lanes[0].dawn")]
    [InlineData(@"""team"": ""dawn"", ""type"": ""tower.t1""", @"""team"": ""dawning"", ""type"": ""tower.t1""", "map.structures[0].team")]
    [InlineData(@"""vulnerability"": ""allProtectorsDestroyed""", @"""vulnerability"": ""someday""", "map.structures[2].vulnerability")]
    [InlineData(@"""walkable"": """, @"""walkable"": ""!!", "map.terrain.walkable")]
    public void Map_Mistakes_AreReportedWithTheirPath(string from, string to, string path)
    {
        string json = PackWith(MapSection());
        Assert.Contains(from, json);

        var e = Assert.Throws<RulesetPackException>(() => Apply(json.Replace(from, to)));
        Assert.Contains(path, e.Message);
    }

    [Theory]
    [InlineData(@"""columns"": 160", @"""columns"": 150", "map.terrain.walkable:")]                              // layers longer than the cells
    [InlineData(@"""height"": 10240,", @"""height"": 10304,", "map.terrain: The pack makes an invalid ruleset")]   // cells not covering the map
    public void Map_TerrainOfTheWrongSize_IsRejected(string from, string to, string error)
    {
        string json = PackWith(MapSection());
        Assert.Contains(from, json);

        var e = Assert.Throws<RulesetPackException>(() => Apply(json.Replace(from, to)));
        Assert.Contains(error, e.Message);
    }
}
