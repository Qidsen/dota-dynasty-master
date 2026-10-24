using Dynasty.Core.Randomness;
using Dynasty.Players;

namespace Dynasty.Players.Tests;

public class PlayerModelTests
{
    private static PlayerProfile Generate(ulong seed, int position = 1, int quality = 12) =>
        PlayerGenerator.Generate(new DeterministicRandom(seed), "p" + seed, position, quality, new[] { "alpha", "beta", "gamma", "delta" });

    [Fact]
    public void Attributes_CoverMechanicsMacroAndPsychology()
    {
        Assert.Equal(9, Attributes.All.Count(a => Attributes.GroupOf(a) == AttributeGroup.Mechanics));
        Assert.Equal(9, Attributes.All.Count(a => Attributes.GroupOf(a) == AttributeGroup.Macro));
        Assert.Equal(10, Attributes.All.Count(a => Attributes.GroupOf(a) == AttributeGroup.Psychology));
    }

    [Fact]
    public void AttributeSet_RejectsValuesOutsideTheScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AttributeSet.Uniform(10).With(Attribute.LastHit, 21));
        Assert.Throws<ArgumentOutOfRangeException>(() => AttributeSet.Uniform(10).With(Attribute.LastHit, 0));
        Assert.Equal(17, AttributeSet.Uniform(10).With(Attribute.Reaction, 17)[Attribute.Reaction]);
    }

    [Fact]
    public void Generator_IsDeterministicAndRespectsQuality()
    {
        PlayerProfile a = Generate(5);
        PlayerProfile b = Generate(5);
        Assert.Equal(Attributes.All.Select(x => a.Attributes[x]), Attributes.All.Select(x => b.Attributes[x]));

        double Average(int quality) => Enumerable.Range(1, 20).Average(s => Attributes.All.Average(x => Generate((ulong)s, 1, quality).Attributes[x]));
        Assert.True(Average(16) > Average(8) + 4);
    }

    [Fact]
    public void Generator_FavoursThePlayersPositionAndGivesAHeroPool()
    {
        PlayerProfile carry = Generate(3, position: 1);

        Assert.Equal(carry.PositionProficiency.Max(), carry.Proficiency(1));
        Assert.NotEmpty(carry.HeroPool);
        Assert.All(carry.HeroPool, h => Assert.InRange(h.Mastery, 0, 100));
        Assert.True(carry.Hidden.Potential >= carry.Attributes.Average(AttributeGroup.Mechanics) - 1, "potential is a ceiling");
    }

    [Fact]
    public void HeroRecord_IsLookedUpByHero()
    {
        PlayerProfile player = Generate(7);
        HeroPoolRecord first = player.HeroPool.First();

        Assert.Same(first, player.Hero(first.HeroId));
        Assert.Null(player.Hero("nobody"));
    }

    [Fact]
    public void ScoutReport_ShowsIntervalsThatContainTheTruth_NarrowerForBetterScouts()
    {
        PlayerProfile player = Generate(9, quality: 14);

        ScoutReport rough = ScoutReport.Create(player, scoutAccuracy: 4, new DeterministicRandom(1));
        ScoutReport sharp = ScoutReport.Create(player, scoutAccuracy: 18, new DeterministicRandom(1));

        foreach (Attribute attribute in Attributes.All)
        {
            Assert.InRange(player.Attributes[attribute], rough.Range(attribute).Min, rough.Range(attribute).Max);
            Assert.InRange(player.Attributes[attribute], sharp.Range(attribute).Min, sharp.Range(attribute).Max);
        }

        Assert.True(Attributes.All.Average(a => sharp.Range(a).Width) < Attributes.All.Average(a => rough.Range(a).Width));
        Assert.True(sharp.ConfidencePercent > rough.ConfidencePercent);
    }
}
