using Dynasty.Presentation;

namespace Dynasty.Presentation.Tests;

public class MatchClockTests
{
    [Fact]
    public void AtNormalSpeed_RealTimeBecomesTicks()
    {
        var clock = new MatchClock(ticksPerSecond: 30);

        Assert.Equal(30, clock.Advance(1.0));
        Assert.Equal(3, clock.Advance(0.1));
    }

    [Fact]
    public void LeftoverTimeCarriesOverAndGivesTheInterpolationFraction()
    {
        var clock = new MatchClock(30);

        Assert.Equal(0, clock.Advance(0.02));   // 0.6 of a tick
        Assert.InRange(clock.Alpha, 0.59, 0.61);
        Assert.Equal(1, clock.Advance(0.02));   // 1.2 ticks in total
        Assert.InRange(clock.Alpha, 0.19, 0.21);
    }

    [Fact]
    public void SpeedMultipliesTheTickRate()
    {
        var clock = new MatchClock(30);
        clock.SetSpeed(8);

        Assert.Equal(240, clock.Advance(1.0));
    }

    [Fact]
    public void Paused_RunsNothingUntilStepped()
    {
        var clock = new MatchClock(30);
        clock.Pause();

        Assert.Equal(0, clock.Advance(5.0));
        clock.StepOnce();
        clock.StepOnce();
        Assert.Equal(2, clock.Advance(0.0));
        Assert.Equal(0, clock.Advance(1.0));
        Assert.True(clock.IsPaused);
        Assert.Equal(0.0, clock.Alpha);
    }

    [Fact]
    public void Resume_DoesNotReplayTheTimeSpentPaused()
    {
        var clock = new MatchClock(30);
        clock.Pause();
        clock.Advance(10.0);
        clock.Resume();

        Assert.Equal(3, clock.Advance(0.1));
    }

    [Fact]
    public void ALongFrame_IsCappedSoTheViewNeverFreezesCatchingUp()
    {
        var clock = new MatchClock(30, maxTicksPerAdvance: 100);
        clock.SetSpeed(16);

        Assert.Equal(100, clock.Advance(2.0));   // 960 owed; the rest is dropped, not queued
        Assert.Equal(48, clock.Advance(0.1));
    }

    [Fact]
    public void Speed_IsLimitedToTheSupportedRange()
    {
        var clock = new MatchClock(30);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(MatchClock.MaxSpeed + 1));
    }
}
