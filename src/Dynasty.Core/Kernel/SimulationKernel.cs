using System;
using System.Collections.Generic;
using Dynasty.Core.Commands;
using Dynasty.Core.Events;
using Dynasty.Core.Ids;
using Dynasty.Core.Randomness;
using Dynasty.Core.Rulesets;
using Dynasty.Core.Time;

namespace Dynasty.Core.Kernel
{
    /// <summary>Everything that, together with the command log, fully determines a run.</summary>
    public sealed class SimulationConfig
    {
        public SimulationConfig(ulong seed, TickRate tickRate, RulesetDescriptor ruleset)
        {
            if (tickRate.TicksPerSecond <= 0)
                throw new ArgumentException("Tick rate is required.", nameof(tickRate));
            Seed = seed;
            TickRate = tickRate;
            Ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
        }

        public ulong Seed { get; }
        public TickRate TickRate { get; }
        public RulesetDescriptor Ruleset { get; }
    }

    /// <summary>A unit of per-tick game logic (movement, combat, AI...). Systems run in registration order.</summary>
    public interface ISimulationSystem<TState>
        where TState : class
    {
        string Name { get; }
        void Tick(SimulationContext<TState> context);
    }

    /// <summary>The only gateway through which systems and command handlers affect a run.</summary>
    public sealed class SimulationContext<TState>
        where TState : class
    {
        private readonly SimulationKernel<TState> _kernel;

        internal SimulationContext(SimulationKernel<TState> kernel)
        {
            _kernel = kernel;
        }

        public TState State => _kernel.State;
        public SimTick Tick => _kernel.CurrentTick;
        public TickRate TickRate => _kernel.Config.TickRate;
        public RulesetDescriptor Ruleset => _kernel.Config.Ruleset;
        public EntityIdAllocator Ids => _kernel.Ids;

        public DeterministicRandom Random(string stream) => _kernel.Streams.Get(stream);

        public void Emit(IDomainEvent domainEvent) => _kernel.Events.Append(_kernel.CurrentTick, domainEvent);

        /// <summary>Marks the run finished; the current tick still completes for every system.</summary>
        public void EndSimulation() => _kernel.MarkFinished();
    }

    /// <summary>
    /// Authoritative fixed-tick simulation loop, independent of Unity and of frame rate.
    /// Per tick: due commands (validated, then applied, in submission order), then systems.
    /// Live, fast-forward, headless batch and replay all drive this same loop.
    /// </summary>
    public sealed class SimulationKernel<TState>
        where TState : class
    {
        private readonly List<ISimulationSystem<TState>> _systems = new List<ISimulationSystem<TState>>();
        private readonly Dictionary<Type, ICommandDispatcher<TState>> _handlers = new Dictionary<Type, ICommandDispatcher<TState>>();
        private readonly SortedDictionary<long, List<CommandRecord>> _pending = new SortedDictionary<long, List<CommandRecord>>();
        private readonly SimulationContext<TState> _context;
        private bool _started;

        public SimulationKernel(SimulationConfig config, TState state)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Streams = new RandomStreams(config.Seed);
            _context = new SimulationContext<TState>(this);
        }

        public SimulationConfig Config { get; }
        public TState State { get; }

        /// <summary>The next tick to execute; equals the number of ticks already executed.</summary>
        public SimTick CurrentTick { get; private set; }

        public bool IsFinished { get; private set; }
        public EventLog Events { get; } = new EventLog();
        public CommandLog Commands { get; } = new CommandLog();
        public EntityIdAllocator Ids { get; } = new EntityIdAllocator();

        internal RandomStreams Streams { get; }

        public void AddSystem(ISimulationSystem<TState> system)
        {
            if (_started)
                throw new InvalidOperationException("Systems must be added before the first tick.");
            _systems.Add(system ?? throw new ArgumentNullException(nameof(system)));
        }

        public void RegisterHandler<TCommand>(ICommandHandler<TState, TCommand> handler)
            where TCommand : ISimCommand
        {
            _handlers.Add(typeof(TCommand), new CommandDispatcher<TState, TCommand>(handler));
        }

        public void Submit(ISimCommand command, SimTick executeAt)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (executeAt < CurrentTick)
                throw new ArgumentOutOfRangeException(nameof(executeAt), "Cannot schedule a command for an already executed tick.");
            if (!_handlers.ContainsKey(command.GetType()))
                throw new InvalidOperationException("No handler registered for command type '" + command.Type + "'.");

            CommandRecord record = Commands.Append(executeAt, command);
            if (!_pending.TryGetValue(executeAt.Value, out List<CommandRecord>? due))
            {
                due = new List<CommandRecord>();
                _pending.Add(executeAt.Value, due);
            }

            due.Add(record);
        }

        public void Step()
        {
            if (IsFinished)
                throw new InvalidOperationException("The simulation has finished.");
            _started = true;

            ExecuteDueCommands();
            foreach (ISimulationSystem<TState> system in _systems)
                system.Tick(_context);

            CurrentTick = CurrentTick.Next();
        }

        public void RunTicks(long count)
        {
            for (long i = 0; i < count && !IsFinished; i++)
                Step();
        }

        /// <summary>Runs until a system ends the simulation or the tick budget is spent; returns ticks executed.</summary>
        public long RunUntilFinished(long maxTicks)
        {
            long executed = 0;
            while (!IsFinished && executed < maxTicks)
            {
                Step();
                executed++;
            }

            return executed;
        }

        internal void MarkFinished() => IsFinished = true;

        private void ExecuteDueCommands()
        {
            if (!_pending.TryGetValue(CurrentTick.Value, out List<CommandRecord>? due))
                return;
            _pending.Remove(CurrentTick.Value);

            foreach (CommandRecord record in due)
            {
                ICommandDispatcher<TState> handler = _handlers[record.Command.GetType()];
                CommandValidation validation = handler.Validate(State, record.Command);
                if (validation.IsValid)
                    handler.Apply(_context, record.Command);
                else
                    Events.Append(CurrentTick, new CommandRejected(record.Sequence, record.Command.Type, validation.Reason ?? "rejected"));
            }
        }
    }
}
