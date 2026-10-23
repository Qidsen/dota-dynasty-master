using System;

namespace Dynasty.Core.Time
{
    /// <summary>A point in simulation time, counted in fixed ticks from the start of the run.</summary>
    public readonly struct SimTick : IEquatable<SimTick>, IComparable<SimTick>
    {
        public SimTick(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Tick must not be negative.");
            Value = value;
        }

        public static SimTick Zero => default;

        public long Value { get; }

        public SimTick Next() => new SimTick(Value + 1);

        public bool Equals(SimTick other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is SimTick other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public int CompareTo(SimTick other) => Value.CompareTo(other.Value);

        public override string ToString() => "tick " + Value;

        public static SimTick operator +(SimTick tick, long ticks) => new SimTick(tick.Value + ticks);
        public static bool operator ==(SimTick a, SimTick b) => a.Value == b.Value;
        public static bool operator !=(SimTick a, SimTick b) => a.Value != b.Value;
        public static bool operator <(SimTick a, SimTick b) => a.Value < b.Value;
        public static bool operator >(SimTick a, SimTick b) => a.Value > b.Value;
        public static bool operator <=(SimTick a, SimTick b) => a.Value <= b.Value;
        public static bool operator >=(SimTick a, SimTick b) => a.Value >= b.Value;
    }
}
