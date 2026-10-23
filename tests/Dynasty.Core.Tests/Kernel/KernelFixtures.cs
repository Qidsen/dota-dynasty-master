using System.Text;
using Dynasty.Core.Commands;
using Dynasty.Core.Events;
using Dynasty.Core.Hashing;
using Dynasty.Core.Ids;
using Dynasty.Core.Kernel;
using Dynasty.Core.Rulesets;
using Dynasty.Core.Tests.Events;
using Dynasty.Core.Time;

namespace Dynasty.Core.Tests.Kernel;

/// <summary>Toy world: a counter that systems and commands mutate.</summary>
public sealed class CounterState
{
    public long Total { get; set; }
    public List<string> Trace { get; } = new();
}

/// <summary>Adds a random amount every tick and reports it as an event.</summary>
public sealed class RandomGrowthSystem : ISimulationSystem<CounterState>
{
    public string Name => "random-growth";

    public void Tick(SimulationContext<CounterState> context)
    {
        int amount = context.Random("growth").NextInt(0, 100);
        context.State.Total += amount;
        context.Emit(new TestPinged(EntityId.None, amount));
    }
}

public sealed class TraceSystem : ISimulationSystem<CounterState>
{
    private readonly string _name;

    public TraceSystem(string name) => _name = name;

    public string Name => _name;

    public void Tick(SimulationContext<CounterState> context) =>
        context.State.Trace.Add(_name + "@" + context.Tick.Value);
}

public sealed class EndAtTickSystem : ISimulationSystem<CounterState>
{
    private readonly long _endTick;

    public EndAtTickSystem(long endTick) => _endTick = endTick;

    public string Name => "end-at";

    public void Tick(SimulationContext<CounterState> context)
    {
        if (context.Tick.Value == _endTick)
            context.EndSimulation();
    }
}

public sealed class AddAmount : ISimCommand
{
    public AddAmount(long amount) => Amount = amount;

    public long Amount { get; }

    public string Type => "test.add_amount";

    public void WritePayload(ref Fnv1a64 hash) => hash.AddInt64(Amount);
}

/// <summary>Accepts positive amounts only.</summary>
public sealed class AddAmountHandler : ICommandHandler<CounterState, AddAmount>
{
    public CommandValidation Validate(CounterState state, AddAmount command) =>
        command.Amount > 0 ? CommandValidation.Valid : CommandValidation.Reject("amount must be positive");

    public void Apply(SimulationContext<CounterState> context, AddAmount command)
    {
        context.State.Total += command.Amount;
        context.State.Trace.Add("cmd" + command.Amount + "@" + context.Tick.Value);
        context.Emit(new TestPinged(EntityId.None, command.Amount));
    }
}

public static class KernelFactory
{
    public static RulesetDescriptor TestRuleset { get; } =
        new("dynasty.test", 1, RulesetFingerprint.Compute(Encoding.UTF8.GetBytes("test-ruleset")));

    public static SimulationKernel<CounterState> Create(ulong seed = 1, params ISimulationSystem<CounterState>[] systems)
    {
        var kernel = new SimulationKernel<CounterState>(
            new SimulationConfig(seed, new TickRate(30), TestRuleset),
            new CounterState());
        foreach (var system in systems)
            kernel.AddSystem(system);
        kernel.RegisterHandler(new AddAmountHandler());
        return kernel;
    }
}
