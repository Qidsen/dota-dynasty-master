using Dynasty.Core.Kernel;
using Dynasty.MatchSimulation;

namespace Dynasty.Presentation.Tests;

internal static class KernelExtensions
{
    public static IEnumerable<T> EventsOf<T>(this SimulationKernel<MatchState> kernel) =>
        kernel.Events.Records.Select(r => r.Event).OfType<T>();
}
