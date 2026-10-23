using System.Text;
using Dynasty.Core.Rulesets;

namespace Dynasty.Core.Tests.Rulesets;

public class RulesetDescriptorTests
{
    private static RulesetFingerprint Fp(string content) => RulesetFingerprint.Compute(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Fingerprint_IsLowercaseSha256Hex()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Fp("abc").Hex);
    }

    [Fact]
    public void Fingerprint_ChangesWhenContentChanges()
    {
        Assert.NotEqual(Fp("{\"armor\":2}"), Fp("{\"armor\":3}"));
        Assert.Equal(Fp("{\"armor\":2}"), Fp("{\"armor\":2}"));
    }

    [Fact]
    public void Descriptor_EqualityCoversIdVersionAndFingerprint()
    {
        var a = new RulesetDescriptor("dynasty.base", 1, Fp("x"));

        Assert.Equal(a, new RulesetDescriptor("dynasty.base", 1, Fp("x")));
        Assert.NotEqual(a, new RulesetDescriptor("dynasty.base", 2, Fp("x")));
        Assert.NotEqual(a, new RulesetDescriptor("dynasty.base", 1, Fp("y")));
        Assert.NotEqual(a, new RulesetDescriptor("dynasty.other", 1, Fp("x")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dynasty.Base")]
    [InlineData("dynasty base")]
    [InlineData("../escape")]
    public void Descriptor_RejectsIdsThatAreNotStableLowercaseTokens(string id)
    {
        Assert.Throws<ArgumentException>(() => new RulesetDescriptor(id, 1, Fp("x")));
    }

    [Fact]
    public void Descriptor_RejectsVersionBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RulesetDescriptor("dynasty.base", 0, Fp("x")));
    }

    [Fact]
    public void Descriptor_RejectsMissingFingerprint()
    {
        Assert.Throws<ArgumentException>(() => new RulesetDescriptor("dynasty.base", 1, default));
    }
}
