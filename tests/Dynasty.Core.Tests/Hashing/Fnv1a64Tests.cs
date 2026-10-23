using Dynasty.Core.Hashing;

namespace Dynasty.Core.Tests.Hashing;

public class Fnv1a64Tests
{
    [Theory]
    [InlineData("", 0xCBF29CE484222325UL)]
    [InlineData("a", 0xAF63DC4C8601EC8CUL)]
    [InlineData("foobar", 0x85944171F73967E8UL)]
    public void HashString_MatchesReferenceVectors(string input, ulong expected)
    {
        Assert.Equal(expected, Fnv1a64.HashString(input));
    }

    [Fact]
    public void Incremental_LengthPrefixedStringsDoNotCollideOnConcatenation()
    {
        var ab = new Fnv1a64();
        ab.AddString("ab");
        ab.AddString("c");

        var a = new Fnv1a64();
        a.AddString("a");
        a.AddString("bc");

        Assert.NotEqual(ab.Value, a.Value);
    }

    [Fact]
    public void Incremental_SameWritesProduceSameValue()
    {
        var x = new Fnv1a64();
        x.AddInt64(-5);
        x.AddString("tower");

        var y = new Fnv1a64();
        y.AddInt64(-5);
        y.AddString("tower");

        Assert.Equal(x.Value, y.Value);
    }

    [Fact]
    public void Incremental_OrderMatters()
    {
        var x = new Fnv1a64();
        x.AddInt64(1);
        x.AddInt64(2);

        var y = new Fnv1a64();
        y.AddInt64(2);
        y.AddInt64(1);

        Assert.NotEqual(x.Value, y.Value);
    }
}
