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
}
