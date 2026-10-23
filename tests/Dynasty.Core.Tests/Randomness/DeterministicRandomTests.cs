using Dynasty.Core.Randomness;

namespace Dynasty.Core.Tests.Randomness;

public class DeterministicRandomTests
{
    [Fact]
    public void SplitMix64_MatchesReferenceVectorForSeedZero()
    {
        ulong state = 0;

        Assert.Equal(0xE220A8397B1DCDAFUL, SplitMix64.Next(ref state));
        Assert.Equal(0x6E789E6AA1B965F4UL, SplitMix64.Next(ref state));
        Assert.Equal(0x06C45D188009454FUL, SplitMix64.Next(ref state));
    }

    [Fact]
    public void Xoshiro256StarStar_MatchesHandComputedReferenceFromKnownState()
    {
        var rng = DeterministicRandom.FromState(1, 2, 3, 4);

        Assert.Equal(11520UL, rng.NextUInt64());
        Assert.Equal(0UL, rng.NextUInt64());
        Assert.Equal(1509978240UL, rng.NextUInt64());
    }

    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(43);

        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void NextInt_StaysWithinHalfOpenRangeAndCoversIt()
    {
        var rng = new DeterministicRandom(7);
        var seen = new bool[6];

        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(-3, 3);
            Assert.InRange(value, -3, 2);
            seen[value + 3] = true;
        }

        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void NextInt_RejectsEmptyRange()
    {
        var rng = new DeterministicRandom(7);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void State_CanBeCapturedAndRestored()
    {
        var rng = new DeterministicRandom(99);
        rng.NextUInt64();
        RandomState saved = rng.State;
        ulong expected = rng.NextUInt64();

        var restored = DeterministicRandom.FromState(saved);

        Assert.Equal(expected, restored.NextUInt64());
    }
}
