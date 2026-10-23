using System;
using System.Collections.Generic;
using Dynasty.Core.Hashing;
using Dynasty.Core.Time;

namespace Dynasty.Core.Events
{
    /// <summary>
    /// Immutable fact produced by the simulation. Entity IDs and context live in the payload;
    /// tick and sequence number are assigned by the <see cref="EventLog"/>.
    /// </summary>
    public interface IDomainEvent
    {
        /// <summary>Stable type name, e.g. "match.tower_destroyed".</summary>
        string Type { get; }

        int SchemaVersion { get; }

        /// <summary>Writes every payload field in a fixed order; feeds determinism checksums.</summary>
        void WritePayload(ref Fnv1a64 hash);
    }

    public sealed class EventRecord
    {
        internal EventRecord(long sequence, SimTick tick, IDomainEvent domainEvent)
        {
            Sequence = sequence;
            Tick = tick;
            Event = domainEvent;
        }

        public long Sequence { get; }
        public SimTick Tick { get; }
        public IDomainEvent Event { get; }

        public override string ToString() => "[" + Tick + " #" + Sequence + "] " + Event.Type;
    }

    /// <summary>Append-only, tick-ordered event stream with a running checksum.</summary>
    public sealed class EventLog
    {
        private readonly List<EventRecord> _records = new List<EventRecord>();
        private Fnv1a64 _checksum;

        public IReadOnlyList<EventRecord> Records => _records;

        /// <summary>Hash of the whole stream so far; equal streams give equal checksums.</summary>
        public ulong Checksum => _checksum.Value;

        public EventRecord Append(SimTick tick, IDomainEvent domainEvent)
        {
            if (domainEvent == null)
                throw new ArgumentNullException(nameof(domainEvent));
            if (_records.Count > 0 && tick < _records[_records.Count - 1].Tick)
                throw new InvalidOperationException("Events must be appended in tick order.");

            var record = new EventRecord(_records.Count + 1, tick, domainEvent);
            _records.Add(record);

            _checksum.AddInt64(record.Sequence);
            _checksum.AddInt64(tick.Value);
            _checksum.AddString(domainEvent.Type);
            _checksum.AddInt64(domainEvent.SchemaVersion);
            domainEvent.WritePayload(ref _checksum);

            return record;
        }
    }
}
