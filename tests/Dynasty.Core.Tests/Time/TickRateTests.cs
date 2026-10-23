using Dynasty.Core.Time;

namespace Dynasty.Core.Tests.Time;

public class TickRateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Constructor_RejectsNonPositiveRate(int ticksPerSecond)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickRate(ticksPerSecond));
    }

    [Theory]
    [InlineData(30, 1000, 30)]
    [InlineData(30, 1500, 45)]
    [InlineData(30, 50, 2)]   // 1.5 ticks rounds half up
    [InlineData(30, 40, 1)]   // 1.2 ticks rounds down
    [InlineData(30, 0, 0)]
    public void TicksFor_ConvertsMillisecondsRoundingHalfUp(int rate, int milliseconds, long expectedTicks)
    {
        Assert.Equal(expectedTicks, new TickRate(rate).TicksFor(milliseconds));
    }

    [Fact]
    public void ElapsedMilliseconds_FloorsTickTime()
    {
        var rate = new TickRate(30);

        Assert.Equal(1000, rate.ElapsedMilliseconds(new SimTick(30)));
        Assert.Equal(33, rate.ElapsedMilliseconds(new SimTick(1)));
    }
}
