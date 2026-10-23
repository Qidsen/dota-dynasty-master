using Dynasty.Core.Randomness;

namespace Dynasty.Core.Tests.Randomness;

public class RandomStreamsTests
{
    [Fact]
    public void NamedStream_DoesNotDependOnOrderStreamsAreRequested()
    {
        var first = new RandomStreams(1234);
        ulong combatA = first.Get("combat").NextUInt64();
        ulong aiA = first.Get("ai").NextUInt64();

        var second = new RandomStreams(1234);
        ulong aiB = second.Get("ai").NextUInt64();
        ulong combatB = second.Get("combat").NextUInt64();

        Assert.Equal(combatA, combatB);
        Assert.Equal(aiA, aiB);
    }

    [Fact]
    public void DifferentStreamNames_AreIndependent()
    {
        var streams = new RandomStreams(1234);

        Assert.NotEqual(streams.Get("combat").NextUInt64(), streams.Get("ai").NextUInt64());
    }

    [Fact]
    public void Get_ReturnsSameInstanceSoStreamAdvances()
    {
        var streams = new RandomStreams(1234);
        var reference = new RandomStreams(1234).Get("combat");
        reference.NextUInt64();

        streams.Get("combat").NextUInt64();

        Assert.Equal(reference.NextUInt64(), streams.Get("combat").NextUInt64());
    }

    [Fact]
    public void DifferentMasterSeeds_ChangeEveryStream()
    {
        Assert.NotEqual(
            new RandomStreams(1).Get("combat").NextUInt64(),
            new RandomStreams(2).Get("combat").NextUInt64());
    }
}
