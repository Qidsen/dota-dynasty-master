using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// What a rune gives the hero who picks it up: a modifier, effects run with the hero as caster and target (Dota's water
    /// rune heals, the illusion rune), gold for it, or gold for every hero of its team (Dota's bounty rune: a base amount and
    /// more for every interval of the match passed when the rune appeared).
    /// </summary>
    public sealed class RuneType
    {
        public RuneType(string id, ModifierSpec? effect = null, int gold = 0, IReadOnlyList<EffectDefinition>? effects = null, int teamGold = 0,
            int teamGoldPerInterval = 0, int teamGoldIntervalMs = 0)
        {
            Id = id;
            Effect = effect;
            Gold = gold;
            Effects = (effects ?? System.Array.Empty<EffectDefinition>()).ToArray();
            TeamGold = teamGold;
            TeamGoldPerInterval = teamGoldPerInterval;
            TeamGoldIntervalMs = teamGoldIntervalMs;
        }

        public string Id { get; }
        public ModifierSpec? Effect { get; }
        public int Gold { get; }
        public IReadOnlyList<EffectDefinition> Effects { get; }
        public int TeamGold { get; }
        public int TeamGoldPerInterval { get; }
        public int TeamGoldIntervalMs { get; }

        /// <summary>The gold for each hero of the team, for a rune that appeared at this match time (Dota 7.40: by its creation).</summary>
        public int TeamGoldAt(long spawnedMs) => TeamGold + (TeamGoldIntervalMs > 0 ? TeamGoldPerInterval * (int)(spawnedMs / TeamGoldIntervalMs) : 0);

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteBool(Effect != null);
            Effect?.WriteCanonical(w);
            w.WriteInt64(Gold);
            w.WriteInt64(Effects.Count);
            foreach (EffectDefinition effect in Effects)
                effect.WriteCanonical(w);
            w.WriteInt64(TeamGold);
            w.WriteInt64(TeamGoldPerInterval);
            w.WriteInt64(TeamGoldIntervalMs);
        }
    }

    /// <summary>Where a schedule puts its runes.</summary>
    public enum RunePlacement
    {
        /// <summary>A rune at every spot (Dota's bounty and water runes).</summary>
        EverySpot = 0,

        /// <summary>One rune at a randomly chosen spot (Dota's power runes).</summary>
        OneRandomSpot = 1,
    }

    /// <summary>
    /// Dota's rune timings (Fandom «Runes», 2025-09): runes of one kind appear at their spots from <see cref="FirstSpawnMs"/> every
    /// <see cref="IntervalMs"/> (0 = once) up to <see cref="UntilMs"/> (0 = no end); the types are drawn at random, without a repeat
    /// within a cycle and never the same twice in a row when <see cref="NoRepeat"/>; runes not taken are gone when the next come,
    /// unless they <see cref="Stay"/>.
    /// </summary>
    public sealed class RuneSchedule
    {
        public RuneSchedule(string id, IReadOnlyList<FixedVector2> spots, int firstSpawnMs, int intervalMs, int untilMs, IReadOnlyList<RuneType> types,
            RunePlacement placement, bool noRepeat, bool stay)
        {
            Id = id;
            Spots = spots.ToArray();
            FirstSpawnMs = firstSpawnMs;
            IntervalMs = intervalMs;
            UntilMs = untilMs;
            Types = types.ToArray();
            Placement = placement;
            NoRepeat = noRepeat;
            Stay = stay;
        }

        public string Id { get; }
        public IReadOnlyList<FixedVector2> Spots { get; }
        public int FirstSpawnMs { get; }
        public int IntervalMs { get; }
        public int UntilMs { get; }
        public IReadOnlyList<RuneType> Types { get; }
        public RunePlacement Placement { get; }
        public bool NoRepeat { get; }
        public bool Stay { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteInt64(Spots.Count);
            foreach (FixedVector2 spot in Spots)
                w.WriteVector(spot);
            foreach (int value in new[] { FirstSpawnMs, IntervalMs, UntilMs, (int)Placement })
                w.WriteInt64(value);
            w.WriteBool(NoRepeat);
            w.WriteBool(Stay);
            w.WriteInt64(Types.Count);
            foreach (RuneType type in Types)
                type.WriteCanonical(w);
        }
    }

    /// <summary>
    /// Power-up runes: on every interval each empty spot gets a rune of a random type (stream
    /// "runes"); a hero standing within <see cref="PickupRadius"/> picks it up — the closest one,
    /// exact ties decided by the same stream, never by processing order.
    /// </summary>
    public sealed class RuneRules
    {
        public RuneRules(IReadOnlyList<FixedVector2> spots, int firstSpawnMs, int intervalMs, IReadOnlyList<RuneType> types, int pickupRadius = 100,
            IReadOnlyList<RuneSchedule>? schedules = null)
        {
            Schedules = (schedules ?? System.Array.Empty<RuneSchedule>()).ToArray();
            Spots = spots.ToArray();
            FirstSpawnMs = firstSpawnMs;
            IntervalMs = intervalMs;
            Types = types.ToArray();
            PickupRadius = pickupRadius;
        }

        public IReadOnlyList<FixedVector2> Spots { get; }
        public int FirstSpawnMs { get; }
        public int IntervalMs { get; }
        public IReadOnlyList<RuneType> Types { get; }
        public int PickupRadius { get; }

        /// <summary>Dota's rune timings; when given, they replace the spots, interval and types above.</summary>
        public IReadOnlyList<RuneSchedule> Schedules { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Schedules.Count);
            foreach (RuneSchedule schedule in Schedules)
                schedule.WriteCanonical(w);
            w.WriteInt64(Spots.Count);
            foreach (FixedVector2 spot in Spots)
                w.WriteVector(spot);
            w.WriteInt64(FirstSpawnMs);
            w.WriteInt64(IntervalMs);
            w.WriteInt64(Types.Count);
            foreach (RuneType type in Types)
                type.WriteCanonical(w);
            w.WriteInt64(PickupRadius);
        }
    }
}
