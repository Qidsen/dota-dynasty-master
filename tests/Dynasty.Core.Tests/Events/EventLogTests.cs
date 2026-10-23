using Dynasty.Core.Events;
using Dynasty.Core.Ids;
using Dynasty.Core.Time;

namespace Dynasty.Core.Tests.Events;

public class EventLogTests
{
    [Fact]
    public void Append_AssignsSequentialIdsAndKeepsTick()
    {
        var log = new EventLog();

        EventRecord first = log.Append(new SimTick(3), new TestPinged(new EntityId(1), 10));
        EventRecord second = log.Append(new SimTick(3), new TestPinged(new EntityId(2), 20));

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(new SimTick(3), second.Tick);
        Assert.Equal(2, log.Records.Count);
    }

    [Fact]
    public void Append_RejectsEventsFromThePast()
    {
        var log = new EventLog();
        log.Append(new SimTick(5), new TestPinged(new EntityId(1), 1));

        Assert.Throws<InvalidOperationException>(() => log.Append(new SimTick(4), new TestPinged(new EntityId(1), 1)));
    }

    [Fact]
    public void Checksum_IsEqualForIdenticalStreams()
    {
        Assert.Equal(BuildLog(10).Checksum, BuildLog(10).Checksum);
    }

    [Fact]
    public void Checksum_ChangesWhenAnyPayloadChanges()
    {
        Assert.NotEqual(BuildLog(10).Checksum, BuildLog(11).Checksum);
    }

    [Fact]
    public void Checksum_ChangesWhenSchemaVersionChanges()
    {
        var v1 = new EventLog();
        v1.Append(SimTick.Zero, new TestPinged(new EntityId(1), 5));
        var v2 = new EventLog();
        v2.Append(SimTick.Zero, new TestPinged(new EntityId(1), 5, schemaVersion: 2));

        Assert.NotEqual(v1.Checksum, v2.Checksum);
    }

    private static EventLog BuildLog(int lastValue)
    {
        var log = new EventLog();
        log.Append(new SimTick(1), new TestPinged(new EntityId(1), 5));
        log.Append(new SimTick(2), new TestPinged(new EntityId(2), lastValue));
        return log;
    }
}
