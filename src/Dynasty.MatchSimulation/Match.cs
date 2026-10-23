using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>A match without heroes (creeps and structures only).</summary>
        public static SimulationKernel<MatchState> Create(RulesetContent ruleset, ulong seed) =>
            Create(ruleset, seed, MatchLineup.Empty);

        /// <summary>
        /// Builds a ready-to-run match pinned to <paramref name="ruleset"/>. Structures and heroes are
        /// placed before the first tick; everything else happens through the systems.
        /// </summary>
        public static SimulationKernel<MatchState> Create(RulesetContent ruleset, ulong seed, MatchLineup lineup)
        {
            RulesetDescriptor descriptor = ruleset.ToDescriptor();
            ValidateLineup(ruleset, lineup);

            var rate = new TickRate(ruleset.TicksPerSecond);
            var state = new MatchState(ruleset, rate);
            var kernel = new SimulationKernel<MatchState>(new SimulationConfig(seed, rate, descriptor), state);

            foreach (StructurePlacement placement in ruleset.Map.Structures)
                state.SpawnStructure(kernel.Ids.Allocate(), placement, ruleset.FindStructureType(placement.StructureTypeId)!);

            SpawnHeroes(kernel, state, Team.Dawn, lineup.Dawn);
            SpawnHeroes(kernel, state, Team.Dusk, lineup.Dusk);

            kernel.AddSystem(new WaveSpawnSystem());
            kernel.AddSystem(new ModifierSystem());
            kernel.AddSystem(new HeroEconomySystem());
            kernel.AddSystem(new TargetingSystem());
            kernel.AddSystem(new HeroOrderSystem());
            kernel.AddSystem(new AbilitySystem());
            kernel.AddSystem(new MovementSystem());
            kernel.AddSystem(new AttackSystem());
            kernel.AddSystem(new CleanupSystem());
            return kernel;
        }

        private static void SpawnHeroes(SimulationKernel<MatchState> kernel, MatchState state, Team team, IReadOnlyList<HeroPick> picks)
        {
            if (picks.Count == 0)
                return;

            FixedVector2 fountain = state.Ruleset.Map.FountainOf(team)!.Position;
            int gold = state.Ruleset.HeroRules!.StartingGold;
            foreach (HeroPick pick in picks)
                state.SpawnHero(kernel.Ids.Allocate(), team, state.Ruleset.FindHero(pick.HeroId)!, pick.LaneId, fountain, gold);
        }

        private static void ValidateLineup(RulesetContent ruleset, MatchLineup lineup)
        {
            var errors = new List<string>();
            foreach ((Team team, IReadOnlyList<HeroPick> picks) in new[] { (Team.Dawn, lineup.Dawn), (Team.Dusk, lineup.Dusk) })
            {
                if (picks.Count > MatchLineup.MaxHeroesPerTeam)
                    errors.Add(team + " has more than " + MatchLineup.MaxHeroesPerTeam + " heroes.");
                foreach (HeroPick pick in picks)
                {
                    if (ruleset.FindHero(pick.HeroId) == null)
                        errors.Add(team + " picked unknown hero '" + pick.HeroId + "'.");
                    if (ruleset.Map.Lanes.All(l => l.Id != pick.LaneId))
                        errors.Add(team + " assigned '" + pick.HeroId + "' to unknown lane '" + pick.LaneId + "'.");
                }

                foreach (IGrouping<string, HeroPick> duplicate in picks.GroupBy(p => p.HeroId).Where(g => g.Count() > 1))
                    errors.Add(team + " picked '" + duplicate.Key + "' more than once.");
            }

            bool anyHeroes = lineup.Dawn.Count + lineup.Dusk.Count > 0;
            if (anyHeroes && (ruleset.HeroRules == null || ruleset.Map.FountainOf(Team.Dawn) == null || ruleset.Map.FountainOf(Team.Dusk) == null))
                errors.Add("Ruleset '" + ruleset.Id + "' does not support heroes (missing hero rules or fountains).");

            if (errors.Count > 0)
                throw new InvalidOperationException("Invalid lineup: " + string.Join("; ", errors));
        }
    }

    public static class CombatMath
    {
        /// <summary>Damage multiplier for armor; see <see cref="Combat.DamageMath.ArmorMultiplier"/>.</summary>
        public static Fixed ArmorMultiplier(Fixed armor, Fixed armorFactor) =>
            Combat.DamageMath.ArmorMultiplier(armor, armorFactor);
    }
}
