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

    /// <summary>A pack of 30 heroes, six played on each position (its position shares), none of the base heroes.</summary>
    private static string PositionedPack() => @"{
        ""format"": ""dynasty.ruleset-pack"", ""formatVersion"": 1, ""id"": ""dota"", ""version"": 7410,
        ""heroes"": [" + string.Join(",", Enumerable.Range(0, 30).Select(i => Hero
            .Replace(@"""antimage""", @"""hero" + i + @"""")
            .Replace(@"""roles"":", @"""positions"": [" + string.Join(", ", Enumerable.Range(0, 5).Select(p => p == i % 5 ? 100 : 0)) + @"], ""roles"":"))) + "] }";

    /// <summary>
    /// On a pack's rules the demo teams' players have pools over the pack's heroes, and two AI teams draft them and play the match
    /// (the viewer's draft and match on the Dota pack, B-118).
    /// </summary>
    [Fact]
    public void OnAPack_TheDemoTeamsDraftAndPlayThePacksHeroes()
    {
        string path = Path.Combine(Path.GetTempPath(), "dynasty-pack-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, PositionedPack());
        try
        {
            RulesetContent rules = RulesetSource.Load(path);
            var heroes = rules.Heroes.Select(h => h.Id).ToHashSet();

            var (dawn, dusk) = DemoMatch.Players(rules);
            Assert.All(dawn.Concat(dusk), p => Assert.All(p.HeroPool, h => Assert.Contains(h.HeroId, heroes)));

            var watch = DraftWatch.Start(rules, seed: 1, secondsPerStep: 0.5);
            Assert.All(watch.DawnRoster.Players.Concat(watch.DuskRoster.Players), p => Assert.All(p.Pool, e => Assert.Contains(e.HeroId, heroes)));
            watch.Finish();
            MatchSession match = watch.StartMatch(1);
            match.Kernel.RunTicks(300);
            Assert.All(match.Kernel.State.Heroes, h => Assert.Contains(h.Hero!.Definition.Id, heroes));
        }
        finally
        {
            File.Delete(path);
        }
    }

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
