using System;

namespace Dynasty.Presentation
{
    /// <summary>
    /// Turns real (frame) time into simulation ticks for live viewing: speed multiplier, pause,
    /// single-step, and the fraction toward the next tick for interpolation. It only decides how
    /// many whole ticks to run, so the simulation stays the same at any speed or frame rate.
    /// Presentation code: <see cref="double"/> wall-clock time is fine here, never in the simulation.
    /// </summary>
    public sealed class MatchClock
    {
        public const int MaxSpeed = 64;

        // Guards against rounding just below a whole tick (0.1 s × 30 = 2.9999...).
        private const double Epsilon = 1e-9;

        private readonly int _ticksPerSecond;
        private readonly int _maxTicksPerAdvance;
        private double _pendingTicks;
        private int _queuedSteps;

        public MatchClock(int ticksPerSecond, int maxTicksPerAdvance = 2000)
        {
            if (ticksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (maxTicksPerAdvance <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTicksPerAdvance));
            _ticksPerSecond = ticksPerSecond;
            _maxTicksPerAdvance = maxTicksPerAdvance;
            Speed = 1;
        }

        /// <summary>Real-time multiplier while running (1 = live).</summary>
        public int Speed { get; private set; }

        public bool IsPaused { get; private set; }

        /// <summary>Progress from the last executed tick toward the next one, 0..1 (0 while paused).</summary>
        public double Alpha => IsPaused ? 0.0 : _pendingTicks;

        public void SetSpeed(int multiplier)
        {
            if (multiplier < 1 || multiplier > MaxSpeed)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "Speed must be 1.." + MaxSpeed + ".");
            Speed = multiplier;
        }

        public void Pause()
        {
            IsPaused = true;
            _pendingTicks = 0;
        }

        public void Resume() => IsPaused = false;

        /// <summary>While paused, runs exactly one more tick on the next <see cref="Advance"/>.</summary>
        public void StepOnce() => _queuedSteps++;

        /// <summary>Real seconds since the last call → whole ticks to run now.</summary>
        public int Advance(double realSeconds)
        {
            if (realSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(realSeconds));

            if (IsPaused)
            {
                int steps = Math.Min(_queuedSteps, _maxTicksPerAdvance);
                _queuedSteps = 0;
                return steps;
            }

            _pendingTicks += realSeconds * _ticksPerSecond * Speed;
            int ticks = (int)Math.Floor(_pendingTicks + Epsilon);
            _pendingTicks = Math.Max(0.0, _pendingTicks - ticks);
            if (ticks > _maxTicksPerAdvance)
            {
                // Falling behind (a slow frame or a huge speed): drop the excess instead of queueing it.
                ticks = _maxTicksPerAdvance;
                _pendingTicks = 0;
            }

            return ticks;
        }
    }
}
