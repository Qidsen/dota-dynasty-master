using Dynasty.Core.Kernel;

namespace Dynasty.MatchSimulation.Tests;

/// <summary>Runs a test action at the end of a given tick, with full access to the simulation context.</summary>
public sealed class LambdaSystem : ISimulationSystem<MatchState>
{
    private readonly long _tick;
    private readonly Action<SimulationContext<MatchState>> _action;

    public LambdaSystem(long tick, Action<SimulationContext<MatchState>> action)
    {
        _tick = tick;
        _action = action;
    }

    public string Name => "test-lambda";

    public void Tick(SimulationContext<MatchState> context)
    {
        if (context.Tick.Value == _tick)
            _action(context);
    }
}

/// <summary>Runs a test action once, at the end of the first tick where the condition holds.</summary>
public sealed class TriggerSystem : ISimulationSystem<MatchState>
{
    private readonly Func<SimulationContext<MatchState>, bool> _condition;
    private readonly Action<SimulationContext<MatchState>> _action;
    private bool _fired;

    public TriggerSystem(Func<SimulationContext<MatchState>, bool> condition, Action<SimulationContext<MatchState>> action)
    {
        _condition = condition;
        _action = action;
    }

    public string Name => "test-trigger";

    public void Tick(SimulationContext<MatchState> context)
    {
        if (_fired || !_condition(context))
            return;
        _fired = true;
        _action(context);
    }
}
