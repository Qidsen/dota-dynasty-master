using System.Collections.Generic;
using Dynasty.Core.Events;
using Dynasty.Core.Kernel;
using Dynasty.HeroAI;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Events;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Presentation
{
    /// <summary>
    /// A match being watched live: the simulation with its AI, a <see cref="MatchClock"/>, the two
    /// latest frames for interpolation and a feed of public events. The view only reads; the match
    /// plays exactly as it would headless with the same seed (see the session tests).
    /// </summary>
    public sealed class MatchSession
    {
        private readonly List<FeedItem> _feed = new List<FeedItem>();
        private int _eventsRead;
        private Viewpoint _viewpoint = Viewpoint.Observer;

        private readonly IReadOnlyDictionary<(Team, string), string> _handles;

        private MatchSession(SimulationKernel<MatchState> kernel, HeroAiSettings? ai, IReadOnlyDictionary<(Team, string), string>? handles)
        {
            Kernel = kernel;
            AiSettings = ai;
            _handles = handles ?? new Dictionary<(Team, string), string>();
            Clock = new MatchClock(kernel.Config.TickRate.TicksPerSecond);
            Current = Capture();
            Previous = Current;
        }

        public static MatchSession Start(RulesetContent ruleset, ulong seed, MatchLineup lineup, HeroAiSettings? ai = null,
            IReadOnlyDictionary<(Team, string), string>? playerHandles = null)
        {
            SimulationKernel<MatchState> kernel = Match.Create(ruleset, seed, lineup);
            HeroAiSettings? settings = null;
            if (lineup.Dawn.Count + lineup.Dusk.Count > 0)
            {
                settings = ai ?? new HeroAiSettings();
                HeroAi.Attach(kernel, settings);
            }

            return new MatchSession(kernel, settings, playerHandles);
        }

        /// <summary>The simulation; read it, never mutate it from the view.</summary>
        public SimulationKernel<MatchState> Kernel { get; }

        public MatchClock Clock { get; }

        /// <summary>The handle of the player on a team's hero, if the match was set up with players.</summary>
        public string? PlayerHandle(Team team, string heroId) => _handles.TryGetValue((team, heroId), out string? handle) ? handle : null;

        /// <summary>The AI settings the heroes play with (team strategies from the draft), if any.</summary>
        public HeroAiSettings? AiSettings { get; }
        public MatchFrame Previous { get; private set; }
        public MatchFrame Current { get; private set; }
        public bool IsFinished => Kernel.IsFinished;

        /// <summary>Switching viewpoint recaptures the current frame; there is no older frame of the new view to blend from.</summary>
        public Viewpoint Viewpoint
        {
            get => _viewpoint;
            set
            {
                _viewpoint = value;
                Current = Capture();
                Previous = Current;
            }
        }

        /// <summary>
        /// Advances by real (frame) time: runs the ticks the clock allows and refreshes the frames. With a budget, only the ticks
        /// computed within <paramref name="budgetMilliseconds"/> of real time run (at least one) and the rest are dropped: at a speed
        /// the computer cannot keep up with the match runs slower and smoothly instead of freezing and then skipping ahead.
        /// </summary>
        public void Update(double realSeconds, double budgetMilliseconds = double.PositiveInfinity)
        {
            int ticks = Clock.Advance(realSeconds);
            if (ticks <= 0 || Kernel.IsFinished)
                return;

            long before = Kernel.CurrentTick.Value;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 1; i < ticks && !Kernel.IsFinished && watch.Elapsed.TotalMilliseconds < budgetMilliseconds; i++)
                Kernel.Step();
            Previous = Capture();
            if (!Kernel.IsFinished)
                Kernel.Step();
            Current = Capture();
            ReadEvents();
            if (realSeconds > 0)
                ActualSpeed = (Kernel.CurrentTick.Value - before) / (realSeconds * Kernel.Config.TickRate.TicksPerSecond);
        }

        /// <summary>
        /// Match seconds per real second in the last update: the chosen speed, or less when the frames' time budget cannot compute
        /// that much (the viewer shows it next to the chosen speed).
        /// </summary>
        public double ActualSpeed { get; private set; }

        /// <summary>Unit positions blended between the previous and current frame by the clock's alpha.</summary>
        public List<UnitPose> Poses()
        {
            var previous = new Dictionary<long, UnitFrame>(Previous.Units.Count);
            foreach (UnitFrame unit in Previous.Units)
                previous[unit.Id] = unit;

            float t = (float)Clock.Alpha;
            var poses = new List<UnitPose>(Current.Units.Count);
            foreach (UnitFrame unit in Current.Units)
            {
                if (previous.TryGetValue(unit.Id, out UnitFrame? before))
                    poses.Add(new UnitPose(unit.Id, before.X + (unit.X - before.X) * t, before.Y + (unit.Y - before.Y) * t));
                else
                    poses.Add(new UnitPose(unit.Id, unit.X, unit.Y));
            }

            return poses;
        }

        /// <summary>Public events since the last call (kill feed, objectives, day and night), oldest first.</summary>
        public List<FeedItem> TakeFeed()
        {
            var items = new List<FeedItem>(_feed);
            _feed.Clear();
            return items;
        }

        private MatchFrame Capture() => MatchFrame.Capture(Kernel.State, Kernel.CurrentTick.Value, _viewpoint);

        private void ReadEvents()
        {
            IReadOnlyList<EventRecord> records = Kernel.Events.Records;
            MatchState state = Kernel.State;
            for (; _eventsRead < records.Count; _eventsRead++)
            {
                EventRecord record = records[_eventsRead];
                long tick = record.Tick.Value;
                switch (record.Event)
                {
                    case HeroKilled kill:
                        MatchUnit? victim = state.Find(kill.Victim);
                        _feed.Add(new FeedItem(tick, FeedKind.HeroKilled, victim?.Team.Opponent(), NameOf(state, kill.Victim) ?? "", NameOf(state, kill.Killer)));
                        break;
                    case StructureDestroyed structure:
                        _feed.Add(new FeedItem(tick, FeedKind.StructureDestroyed, structure.Team.Opponent(), structure.PlacementId, NameOf(state, structure.Killer)));
                        break;
                    case BossKilled boss:
                        _feed.Add(new FeedItem(tick, FeedKind.BossKilled, boss.Team == Team.Neutral ? (Team?)null : boss.Team, state.Ruleset.Boss?.Id ?? "boss", NameOf(state, boss.Killer)));
                        break;
                    case HeroBoughtBack buyback:
                        _feed.Add(new FeedItem(tick, FeedKind.Buyback, state.Find(buyback.Hero)?.Team, NameOf(state, buyback.Hero) ?? "", null));
                        break;
                    case DayNightChanged change:
                        _feed.Add(new FeedItem(tick, change.IsNight ? FeedKind.NightFell : FeedKind.DayBroke, null, "", null));
                        break;
                    case MatchFinished finished:
                        _feed.Add(new FeedItem(tick, FeedKind.MatchFinished, finished.Winner, finished.Winner.ToString(), null));
                        break;
                }
            }
        }

        /// <summary>Hero name for heroes; null for creeps, towers, neutrals and units already gone.</summary>
        private static string? NameOf(MatchState state, Dynasty.Core.Ids.EntityId id) => state.Find(id)?.Hero?.Definition.Name;
    }

    /// <summary>An interpolated unit position for rendering.</summary>
    public readonly struct UnitPose
    {
        public UnitPose(long id, float x, float y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public long Id { get; }
        public float X { get; }
        public float Y { get; }
    }

    public enum FeedKind
    {
        HeroKilled,
        StructureDestroyed,
        BossKilled,
        Buyback,
        NightFell,
        DayBroke,
        MatchFinished,
    }

    /// <summary>
    /// One public match event for the kill feed and HUD. Only what every viewer may know
    /// (announced kills, objectives, buybacks, time of day). Text is left to the view (localisation).
    /// </summary>
    public sealed class FeedItem
    {
        public FeedItem(long tick, FeedKind kind, Team? team, string subject, string? actor)
        {
            Tick = tick;
            Kind = kind;
            Team = team;
            Subject = subject;
            Actor = actor;
        }

        public long Tick { get; }
        public FeedKind Kind { get; }

        /// <summary>The team that gained (killer's side, winner); null when nobody did.</summary>
        public Team? Team { get; }

        /// <summary>What it happened to: the hero killed, the structure placement, the boss.</summary>
        public string Subject { get; }

        /// <summary>The hero responsible, when it was a hero.</summary>
        public string? Actor { get; }
    }
}
