using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Rulesets;
using Dynasty.Core.Time;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Systems;

namespace Dynasty.MatchSimulation
{
    public static class Match
    {
        /// <summary>
        /// Builds a ready-to-run match pinned to <paramref name="ruleset"/>. Structures are placed
        /// from the ruleset before the first tick; everything else happens through the systems.
        /// </summary>
        public static SimulationKernel<MatchState> Create(RulesetContent ruleset, ulong seed)
        {
            RulesetDescriptor descriptor = ruleset.ToDescriptor();
            var rate = new TickRate(ruleset.TicksPerSecond);
            var state = new MatchState(ruleset, rate);
            var kernel = new SimulationKernel<MatchState>(new SimulationConfig(seed, rate, descriptor), state);

            foreach (StructurePlacement placement in ruleset.Map.Structures)
                state.SpawnStructure(kernel.Ids.Allocate(), placement, ruleset.FindStructureType(placement.StructureTypeId)!);

            kernel.AddSystem(new WaveSpawnSystem());
            kernel.AddSystem(new TargetingSystem());
            kernel.AddSystem(new MovementSystem());
            kernel.AddSystem(new AttackSystem());
            kernel.AddSystem(new CleanupSystem());
            return kernel;
        }
    }

    public static class CombatMath
    {
        /// <summary>Damage multiplier for armor: 1 - f·a / (1 + f·|a|). Negative armor amplifies damage.</summary>
        public static Fixed ArmorMultiplier(Fixed armor, Fixed armorFactor) =>
            Fixed.One - armorFactor * armor / (Fixed.One + armorFactor * Fixed.Abs(armor));
    }
}
