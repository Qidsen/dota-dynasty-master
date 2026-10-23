using Dynasty.Core.Events;
using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Tests;

public static class MatchTestKit
{
    public static IEnumerable<T> EventsOf<T>(this SimulationKernel<MatchState> kernel) =>
        kernel.Events.Records.Select(r => r.Event).OfType<T>();

    public static IEnumerable<(long Tick, T Event)> TimedEventsOf<T>(this SimulationKernel<MatchState> kernel) =>
        kernel.Events.Records.Where(r => r.Event is T).Select(r => (r.Tick.Value, (T)r.Event));

    public static IEnumerable<MatchUnit> Creeps(this MatchState state, Team team) =>
        state.Units.Where(u => u.Kind == UnitKind.Creep && u.Team == team);

    public static SimulationKernel<MatchState> Start(this RulesetDraft draft, ulong seed = 1) =>
        Match.Create(draft.Build(), seed);

    public static long StepUntil(this SimulationKernel<MatchState> kernel, Func<bool> condition, long maxTicks)
    {
        for (long i = 0; i < maxTicks; i++)
        {
            if (condition())
                return i;
            kernel.Step();
        }

        throw new Xunit.Sdk.XunitException("Condition not reached within " + maxTicks + " ticks.");
    }

    /// <summary>
    /// Replays StructureDestroyed events in order and asserts every structure was vulnerable
    /// (by its placement's protection rule) at the moment it fell.
    /// </summary>
    public static void AssertProtectionOrderRespected(SimulationKernel<MatchState> kernel)
    {
        var placements = kernel.State.Ruleset.Map.Structures.ToDictionary(p => p.Id);
        var destroyed = new HashSet<string>();

        foreach (var e in kernel.EventsOf<Events.StructureDestroyed>())
        {
            StructurePlacement placement = placements[e.PlacementId];
            if (placement.ProtectedBy.Count > 0)
            {
                bool vulnerable = placement.Vulnerability == VulnerabilityRule.AnyProtectorDestroyed
                    ? placement.ProtectedBy.Any(destroyed.Contains)
                    : placement.ProtectedBy.All(destroyed.Contains);
                Assert.True(vulnerable, e.PlacementId + " was destroyed while still protected.");
            }

            destroyed.Add(e.PlacementId);
        }
    }
}
