using System;
using System.Collections.Generic;
using Dynasty.Core.Events;
using Dynasty.Core.Hashing;
using Dynasty.Core.Kernel;
using Dynasty.Core.Time;

namespace Dynasty.Core.Commands
{
    /// <summary>
    /// An intent submitted to the simulation (AI decision, manager instruction, debug action).
    /// Commands never touch state directly: they are validated at their execution tick, then applied.
    /// </summary>
    public interface ISimCommand
    {
        /// <summary>Stable type name, e.g. "match.move_unit".</summary>
        string Type { get; }

        void WritePayload(ref Fnv1a64 hash);
    }

    public readonly struct CommandValidation
    {
        private CommandValidation(bool isValid, string? reason)
        {
            IsValid = isValid;
            Reason = reason;
        }

        public static CommandValidation Valid => new CommandValidation(true, null);

        public bool IsValid { get; }

        public string? Reason { get; }

        public static CommandValidation Reject(string reason) => new CommandValidation(false, reason);
    }

    public interface ICommandHandler<TState, in TCommand>
        where TState : class
        where TCommand : ISimCommand
    {
        /// <summary>Must not mutate state.</summary>
        CommandValidation Validate(TState state, TCommand command);

        void Apply(SimulationContext<TState> context, TCommand command);
    }

    public sealed class CommandRecord
    {
        internal CommandRecord(long sequence, SimTick executeAt, ISimCommand command)
        {
            Sequence = sequence;
            ExecuteAt = executeAt;
            Command = command;
        }

        /// <summary>Submission order; breaks ties between commands for the same tick.</summary>
        public long Sequence { get; }

        public SimTick ExecuteAt { get; }

        public ISimCommand Command { get; }
    }

    /// <summary>Every submitted command in submission order; together with seed and ruleset this is a replay.</summary>
    public sealed class CommandLog
    {
        private readonly List<CommandRecord> _records = new List<CommandRecord>();
        private Fnv1a64 _checksum;

        public IReadOnlyList<CommandRecord> Records => _records;

        public ulong Checksum => _checksum.Value;

        internal CommandRecord Append(SimTick executeAt, ISimCommand command)
        {
            var record = new CommandRecord(_records.Count + 1, executeAt, command);
            _records.Add(record);

            _checksum.AddInt64(record.Sequence);
            _checksum.AddInt64(executeAt.Value);
            _checksum.AddString(command.Type);
            command.WritePayload(ref _checksum);

            return record;
        }
    }

    public sealed class CommandRejected : IDomainEvent
    {
        public CommandRejected(long commandSequence, string commandType, string reason)
        {
            CommandSequence = commandSequence;
            CommandType = commandType;
            Reason = reason;
        }

        public long CommandSequence { get; }
        public string CommandType { get; }
        public string Reason { get; }

        public string Type => "core.command_rejected";
        public int SchemaVersion => 1;

        public void WritePayload(ref Fnv1a64 hash)
        {
            hash.AddInt64(CommandSequence);
            hash.AddString(CommandType);
            hash.AddString(Reason);
        }
    }

    /// <summary>Type-erased handler so the kernel can dispatch by the command's runtime type.</summary>
    internal interface ICommandDispatcher<TState>
        where TState : class
    {
        CommandValidation Validate(TState state, ISimCommand command);
        void Apply(SimulationContext<TState> context, ISimCommand command);
    }

    internal sealed class CommandDispatcher<TState, TCommand> : ICommandDispatcher<TState>
        where TState : class
        where TCommand : ISimCommand
    {
        private readonly ICommandHandler<TState, TCommand> _handler;

        public CommandDispatcher(ICommandHandler<TState, TCommand> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public CommandValidation Validate(TState state, ISimCommand command) => _handler.Validate(state, (TCommand)command);

        public void Apply(SimulationContext<TState> context, ISimCommand command) => _handler.Apply(context, (TCommand)command);
    }
}
