using System;

namespace Dynasty.Core.Ids
{
    /// <summary>Stable identifier of a simulation entity. Never reused within a run.</summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public EntityId(long value)
        {
            Value = value;
        }

        public static EntityId None => default;

        public long Value { get; }

        public bool IsValid => Value > 0;

        public bool Equals(EntityId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => IsValid ? "#" + Value : "#none";

        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
    }

    /// <summary>Issues monotonically increasing entity IDs; its state is a single serializable number.</summary>
    public sealed class EntityIdAllocator
    {
        public EntityIdAllocator()
            : this(1)
        {
        }

        public EntityIdAllocator(long nextValue)
        {
            if (nextValue < 1)
                throw new ArgumentOutOfRangeException(nameof(nextValue), "Next ID must be at least 1.");
            NextValue = nextValue;
        }

        public long NextValue { get; private set; }

        public EntityId Allocate() => new EntityId(NextValue++);
    }
}
