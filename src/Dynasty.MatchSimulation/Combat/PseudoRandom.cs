using System.Collections.Generic;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Combat
{
    /// <summary>
    /// Dota's pseudo-random distribution (dota2.fandom «Random Distribution»): the chance on the N-th try since the
    /// last proc is C × N, with C chosen so that the procs average the nominal chance; tries that could not proc do
    /// not count. C is solved once per nominal chance by bisection in <see cref="decimal"/> (exact, platform-independent
    /// arithmetic, not floating point) and kept as parts per million, so the rolls stay integer.
    /// </summary>
    public static class PseudoRandom
    {
        private const int Million = 1000000;
        private static readonly Dictionary<int, int> PartsPerMillion = new Dictionary<int, int>();

        /// <summary>The constant C for a nominal chance in hundredths of a percent.</summary>
        public static Fixed ConstantFor(int chanceHundredths) => Fixed.FromRatio(ConstantPpm(chanceHundredths), Million);

        private static int ConstantPpm(int chanceHundredths)
        {
            lock (PartsPerMillion)
            {
                if (!PartsPerMillion.TryGetValue(chanceHundredths, out int ppm))
                    PartsPerMillion[chanceHundredths] = ppm = Solve(chanceHundredths / 10000m);
                return ppm;
            }
        }

        /// <summary>The C whose average proc rate is <paramref name="chance"/>, in parts per million.</summary>
        private static int Solve(decimal chance)
        {
            decimal low = 0m, high = chance;
            for (int i = 0; i < 60; i++)
            {
                decimal c = (low + high) / 2;
                if (RateOf(c) < chance)
                    low = c;
                else
                    high = c;
            }

            return (int)decimal.Round((low + high) / 2 * Million, 0, System.MidpointRounding.AwayFromZero);
        }

        /// <summary>The average proc rate for a constant: 1 / E[N], with P(proc on try N | none before) = min(1, C × N).</summary>
        private static decimal RateOf(decimal c)
        {
            if (c <= 0m)
                return 0m;
            decimal expected = 0m, none = 1m;
            for (int n = 1; none > 0m; n++)
            {
                decimal p = System.Math.Min(1m, c * n);
                expected += n * p * none;
                none *= 1m - p;
            }

            return 1m / expected;
        }

        /// <summary>One try: counts it, and on a proc resets the count.</summary>
        internal static bool Roll(SimulationContext<MatchState> context, int chanceHundredths, ref int tries)
        {
            if (chanceHundredths >= 10000)
                return true;
            if (chanceHundredths <= 0)
                return false;
            tries++;
            long chance = (long)ConstantPpm(chanceHundredths) * tries;
            if (context.Random("combat").NextInt(0, Million) >= chance)
                return false;
            tries = 0;
            return true;
        }
    }
}
