using Dynasty.Core.Events;
using Dynasty.Core.Hashing;
using Dynasty.Core.Ids;

namespace Dynasty.Core.Tests.Events;

/// <summary>Minimal domain event used by core tests.</summary>
public sealed class TestPinged : IDomainEvent
{
    public TestPinged(EntityId source, long value, int schemaVersion = 1)
    {
        Source = source;
        Value = value;
        SchemaVersion = schemaVersion;
    }

    public EntityId Source { get; }
    public long Value { get; }

    public string Type => "test.pinged";
    public int SchemaVersion { get; }

    public void WritePayload(ref Fnv1a64 hash)
    {
        hash.AddInt64(Source.Value);
        hash.AddInt64(Value);
    }
}
