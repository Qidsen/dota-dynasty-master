using Dynasty.MatchSimulation.Rules;
using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

/// <summary>The viewer's rules (backlog B-118): the base ruleset, or the base with a ruleset pack from a file applied.</summary>
public class RulesetSourceTests
{
    private const string Hero = @"{
        ""id"": ""antimage"", ""name"": ""Anti-Mage"", ""roles"": [""carry"", ""escape""], ""attackType"": ""melee"",
        ""primaryAttribute"": ""agility"", ""strength"": 21, ""agility"": 25, ""intelligence"": 12,
        ""strengthGain"": 1.6, ""agilityGain"": 2.8, ""intelligenceGain"": 1.8,
        ""baseHealth"": 120, ""baseHealthRegen"": 1.5, ""baseMana"": 75, ""baseManaRegen"": 0, ""baseArmor"": 2, ""magicResist"": 25,
        ""attackDamageMin"": 29, ""attackDamageMax"": 33, ""attackRange"": 150, ""projectileSpeed"": 0,
        ""baseAttackTime"": 1.4, ""baseAttackSpeed"": 100, ""attackPoint"": 0.3, ""moveSpeed"": 315, ""dayVision"": 1800, ""nightVision"": 800 }";

    private static string Pack() => @"{
        ""format"": ""dynasty.ruleset-pack"", ""formatVersion"": 1, ""id"": ""dota"", ""version"": 7410,
        ""heroes"": [" + Hero + string.Concat(Enumerable.Range(1, 18).Select(i => "," + Hero.Replace(@"""antimage""", @"""filler" + i + @""""))) + "] }";

    [Fact]
    public void NoPack_GivesTheBaseRules()
    {
        RulesetContent rules = RulesetSource.Load(null);

        Assert.Equal(BaseRuleset.Create().ToDescriptor(), rules.ToDescriptor());
    }

    [Fact]
    public void APackFile_IsAppliedOverTheBaseRules()
    {
        string path = Path.Combine(Path.GetTempPath(), "dynasty-pack-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, Pack());
        try
        {
            RulesetContent rules = RulesetSource.Load(path);

            Assert.Contains(rules.Heroes, h => h.Id == "antimage");
            Assert.DoesNotContain(rules.Heroes, h => h.Id == "ironvow");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingPackFile_IsAnError_NotSilentlyTheBaseRules()
    {
        Assert.Throws<FileNotFoundException>(() => RulesetSource.Load(Path.Combine(Path.GetTempPath(), "no-such-pack-" + Guid.NewGuid().ToString("N") + ".json")));
    }
}
