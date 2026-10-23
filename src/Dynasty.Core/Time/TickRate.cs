using System;

namespace Dynasty.Core.Time
{
    /// <summary>
    /// Fixed simulation frequency. All durations from rulesets are given in milliseconds and
    /// converted here with integer math only, so conversions are identical on every platform.
    /// </summary>
    public readonly struct TickRate
    {
        public TickRate(int ticksPerSecond)
        {
            if (ticksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "Tick rate must be positive.");
            TicksPerSecond = ticksPerSecond;
        }

        public int TicksPerSecond { get; }

        /// <summary>Duration in ticks, rounded half up.</summary>
        public long TicksFor(int milliseconds) => ((long)milliseconds * TicksPerSecond + 500) / 1000;

        /// <summary>Wall-clock match time at the given tick, rounded down.</summary>
        public long ElapsedMilliseconds(SimTick tick) => tick.Value * 1000 / TicksPerSecond;
    }
}
