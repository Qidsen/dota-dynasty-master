using Dynasty.Core.Commands;
using Dynasty.Core.Events;
using Dynasty.Core.Time;

namespace Dynasty.Core.Tests.Kernel;

public class SimulationKernelTests
{
    [Fact]
    public void EmptyHeadlessSimulation_RunsFixedTicksWithoutEvents()
    {
        var kernel = KernelFactory.Create();

        kernel.RunTicks(1000);

        Assert.Equal(new SimTick(1000), kernel.CurrentTick);
        Assert.Empty(kernel.Events.Records);
    }

    [Fact]
    public void Systems_RunEveryTickInRegistrationOrder()
    {
        var kernel = KernelFactory.Create(1, new TraceSystem("a"), new TraceSystem("b"));

        kernel.RunTicks(2);

        Assert.Equal(new[] { "a@0", "b@0", "a@1", "b@1" }, kernel.State.Trace);
    }

    [Fact]
    public void AddSystem_IsRejectedAfterSimulationStarted()
    {
        var kernel = KernelFactory.Create();
        kernel.Step();

        Assert.Throws<InvalidOperationException>(() => kernel.AddSystem(new TraceSystem("late")));
    }

    [Fact]
    public void Events_AreStampedWithTheTickThatProducedThem()
    {
        var kernel = KernelFactory.Create(1, new RandomGrowthSystem());

        kernel.RunTicks(3);

        Assert.Equal(new long[] { 0, 1, 2 }, kernel.Events.Records.Select(r => r.Tick.Value));
    }

    [Fact]
    public void SameSeed_ProducesIdenticalEventStreamAndState()
    {
        var a = KernelFactory.Create(77, new RandomGrowthSystem());
        var b = KernelFactory.Create(77, new RandomGrowthSystem());

        a.RunTicks(500);
        b.RunTicks(500);

        Assert.Equal(a.Events.Checksum, b.Events.Checksum);
        Assert.Equal(a.State.Total, b.State.Total);
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentEventStream()
    {
        var a = KernelFactory.Create(77, new RandomGrowthSystem());
        var b = KernelFactory.Create(78, new RandomGrowthSystem());

        a.RunTicks(500);
        b.RunTicks(500);

        Assert.NotEqual(a.Events.Checksum, b.Events.Checksum);
    }

    [Fact]
    public void Commands_ExecuteAtTheirTickBeforeSystemsInSubmissionOrder()
    {
        var kernel = KernelFactory.Create(1, new TraceSystem("sys"));
        kernel.Submit(new AddAmount(5), new SimTick(1));
        kernel.Submit(new AddAmount(7), new SimTick(1));

        kernel.RunTicks(2);

        Assert.Equal(new[] { "sys@0", "cmd5@1", "cmd7@1", "sys@1" }, kernel.State.Trace);
        Assert.Equal(12, kernel.State.Total);
    }

    [Fact]
    public void InvalidCommand_IsRejectedWithEventAndDoesNotChangeState()
    {
        var kernel = KernelFactory.Create();
        kernel.Submit(new AddAmount(-3), SimTick.Zero);

        kernel.Step();

        Assert.Equal(0, kernel.State.Total);
        var rejected = Assert.IsType<CommandRejected>(Assert.Single(kernel.Events.Records).Event);
        Assert.Equal("test.add_amount", rejected.CommandType);
        Assert.Equal("amount must be positive", rejected.Reason);
    }

    [Fact]
    public void Submit_RejectsCommandsForTicksAlreadyProcessed()
    {
        var kernel = KernelFactory.Create();
        kernel.RunTicks(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Submit(new AddAmount(1), new SimTick(2)));
    }

    [Fact]
    public void Submit_RejectsCommandTypesWithoutHandler()
    {
        var kernel = KernelFactory.Create();

        Assert.Throws<InvalidOperationException>(() => kernel.Submit(new UnhandledCommand(), SimTick.Zero));
    }

    [Fact]
    public void ReplayingRecordedCommands_ReproducesTheExactRun()
    {
        var original = KernelFactory.Create(2026, new RandomGrowthSystem());
        original.Submit(new AddAmount(10), new SimTick(5));
        original.Submit(new AddAmount(-1), new SimTick(9));
        original.RunTicks(50);
        original.Submit(new AddAmount(3), new SimTick(60));
        original.RunTicks(50);

        var replay = KernelFactory.Create(2026, new RandomGrowthSystem());
        foreach (CommandRecord record in original.Commands.Records)
            replay.Submit(record.Command, record.ExecuteAt);
        replay.RunTicks(100);

        Assert.Equal(original.Events.Checksum, replay.Events.Checksum);
        Assert.Equal(original.State.Total, replay.State.Total);
        Assert.Equal(original.Commands.Checksum, replay.Commands.Checksum);
    }

    [Fact]
    public void EndSimulation_StopsTheRunAndFurtherStepsAreRejected()
    {
        var kernel = KernelFactory.Create(1, new EndAtTickSystem(4));

        long executed = kernel.RunUntilFinished(maxTicks: 1000);

        Assert.True(kernel.IsFinished);
        Assert.Equal(5, executed);
        Assert.Equal(new SimTick(5), kernel.CurrentTick);
        Assert.Throws<InvalidOperationException>(() => kernel.Step());
    }

    [Fact]
    public void RunUntilFinished_GivesUpAfterMaxTicks()
    {
        var kernel = KernelFactory.Create();

        long executed = kernel.RunUntilFinished(maxTicks: 10);

        Assert.False(kernel.IsFinished);
        Assert.Equal(10, executed);
    }

    [Fact]
    public void Kernel_ExposesThePinnedRuleset()
    {
        var kernel = KernelFactory.Create();

        Assert.Equal(KernelFactory.TestRuleset, kernel.Config.Ruleset);
    }

    private sealed class UnhandledCommand : ISimCommand
    {
        public string Type => "test.unhandled";

        public void WritePayload(ref Dynasty.Core.Hashing.Fnv1a64 hash)
        {
        }
    }
}
