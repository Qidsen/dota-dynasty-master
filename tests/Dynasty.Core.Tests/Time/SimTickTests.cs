using Dynasty.Core.Time;

namespace Dynasty.Core.Tests.Time;

public class SimTickTests
{
    [Fact]
    public void Zero_IsTheStartOfTime()
    {
        Assert.Equal(0, SimTick.Zero.Value);
    }

    [Fact]
    public void Next_AdvancesByOne()
    {
        Assert.Equal(new SimTick(8), new SimTick(7).Next());
    }

    [Fact]
    public void Constructor_RejectsNegativeTicks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimTick(-1));
    }

    [Fact]
    public void Comparison_FollowsTickOrder()
    {
        Assert.True(new SimTick(3) < new SimTick(4));
        Assert.True(new SimTick(4) >= new SimTick(4));
        Assert.Equal(-1, new SimTick(3).CompareTo(new SimTick(9)));
    }

    [Fact]
    public void AddTicks_ProducesLaterTick()
    {
        Assert.Equal(new SimTick(15), new SimTick(10) + 5);
    }
}
