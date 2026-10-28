using System.Collections.Generic;
using Dynasty.MatchSimulation;
using Dynasty.MatchSimulation.Combat;
using Dynasty.MatchSimulation.Rules;
using Dynasty.MatchSimulation.Vision;

namespace Dynasty.Presentation
{
    /// <summary>Whose eyes a frame is captured through.</summary>
    public enum Viewpoint
    {
        /// <summary>Everything (spectator / analyst mode, when the rules allow it).</summary>
        Observer,

        /// <summary>Only what Dawn knows: its own units, what it sees, last-known enemy hero positions.</summary>
        Dawn,

        /// <summary>Only what Dusk knows.</summary>
        Dusk,
    }

    /// <summary>
    /// An immutable copy of what one viewpoint can know at one tick: the data both the 3D broadcast
    /// and the Tactical 2D map draw from (spec §13). Captured from the authoritative state, never
    /// written back. Team viewpoints follow the team's fog of war, so nothing hidden leaks into the view.
    /// </summary>
    public sealed class MatchFrame
    {
        private MatchFrame(long tick, float timeSeconds, Viewpoint viewpoint, bool isNight, Team? winner, int dawnKills, int duskKills,
            List<UnitFrame> units, List<GhostFrame> ghosts, List<ProjectileFrame> projectiles, int[] lotusCounts, int[] shrineCharges, List<WatcherFrame> watchers)
        {
            LotusCounts = lotusCounts;
            ShrineCharges = shrineCharges;
            Watchers = watchers;
            Tick = tick;
            TimeSeconds = timeSeconds;
            Viewpoint = viewpoint;
            IsNight = isNight;
            Winner = winner;
            DawnKills = dawnKills;
            DuskKills = duskKills;
            Units = units;
            Ghosts = ghosts;
            Projectiles = projectiles;
        }

        public long Tick { get; }
        public float TimeSeconds { get; }
        public Viewpoint Viewpoint { get; }
        public bool IsNight { get; }
        public Team? Winner { get; }
        public int DawnKills { get; }
        public int DuskKills { get; }

        /// <summary>Living units this viewpoint can see, in ascending ID order.</summary>
        public IReadOnlyList<UnitFrame> Units { get; }

        /// <summary>Enemy heroes out of sight, at the position the team last saw them (team viewpoints only).</summary>
        public IReadOnlyList<GhostFrame> Ghosts { get; }

        public IReadOnlyList<ProjectileFrame> Projectiles { get; }

        /// <summary>Lotuses in each Lotus Pool, in the rules' order (public in Dota: they are seen in the pool).</summary>
        public IReadOnlyList<int> LotusCounts { get; }

        /// <summary>Fillings waiting at each Shrine of Wisdom, in the rules' order (a shrine glows while it has some).</summary>
        public IReadOnlyList<int> ShrineCharges { get; }

        /// <summary>Each Watcher's state, in the rules' order.</summary>
        public IReadOnlyList<WatcherFrame> Watchers { get; }

        public static MatchFrame Capture(MatchState state, long tick, Viewpoint viewpoint)
        {
            Team? team = viewpoint == Viewpoint.Dawn ? Team.Dawn : viewpoint == Viewpoint.Dusk ? Team.Dusk : (Team?)null;

            var units = new List<UnitFrame>(state.Units.Count);
            foreach (MatchUnit unit in state.Units)
            {
                if (!unit.IsAlive || (team != null && unit.Team != team && !state.IsVisibleTo(team.Value, unit)))
                    continue;
                units.Add(UnitFrame.From(state, unit));
            }

            var ghosts = new List<GhostFrame>();
            int dawnKills = 0, duskKills = 0;
            foreach (MatchUnit hero in state.Heroes)
            {
                if (hero.Team == Team.Dawn)
                    dawnKills += hero.Hero!.Kills;
                else
                    duskKills += hero.Hero!.Kills;

                if (team == null || hero.Team == team || (hero.IsAlive && state.IsVisibleTo(team.Value, hero)))
                    continue;
                LastSeen? seen = state.KnowledgeOf(team.Value).LastSeenOf(hero.Id);
                if (seen != null)
                    ghosts.Add(new GhostFrame(hero.Id.Value, hero.Team, hero.Hero!.Definition.Name, (float)seen.Position.X.ToDouble(), (float)seen.Position.Y.ToDouble(), seen.Tick));
            }

            var projectiles = new List<ProjectileFrame>(state.Projectiles.Count);
            foreach (Projectile projectile in state.Projectiles)
            {
                MatchUnit? source = state.Find(projectile.Source);
                MatchUnit? target = state.Find(projectile.Target);
                if (team != null && !Known(state, team.Value, source) && !Known(state, team.Value, target))
                    continue;
                projectiles.Add(new ProjectileFrame(projectile.Id, source?.Team, (float)projectile.Position.X.ToDouble(), (float)projectile.Position.Y.ToDouble(), projectile.IsAbility));
            }

            var lotuses = new int[state.Ruleset.LotusPools?.Positions.Count ?? 0];
            for (int i = 0; i < lotuses.Length; i++)
                lotuses[i] = state.LotusCount(i);
            var charges = new int[state.Ruleset.Shrines?.Positions.Count ?? 0];
            for (int i = 0; i < charges.Length; i++)
                charges[i] = state.ShrineCharges(i);
            var watchers = new List<WatcherFrame>(state.Ruleset.Watchers?.Positions.Count ?? 0);
            for (int i = 0; i < (state.Ruleset.Watchers?.Positions.Count ?? 0); i++)
                watchers.Add(new WatcherFrame(state.WatcherOwner(i), state.WatcherOff(i)));

            float time = tick / (float)state.Ruleset.TicksPerSecond;
            return new MatchFrame(tick, time, viewpoint, state.IsNight(tick), state.Winner, dawnKills, duskKills, units, ghosts, projectiles,
                lotuses, charges, watchers);
        }

        private static bool Known(MatchState state, Team team, MatchUnit? unit) =>
            unit != null && (unit.Team == team || state.IsVisibleTo(team, unit));
    }

    /// <summary>A Watcher in a frame: the team it is on for (Neutral when ready or off) and whether it is off.</summary>
    public readonly struct WatcherFrame
    {
        public WatcherFrame(Team owner, bool off)
        {
            Owner = owner;
            Off = off;
        }

        public Team Owner { get; }
        public bool Off { get; }
    }

    /// <summary>One visible unit in a frame. Positions are map coordinates (presentation floats).</summary>
    public sealed class UnitFrame
    {
        public UnitFrame(long id, Team team, UnitKind kind, string typeId, string? name, float x, float y, int hp, int maxHp, int mana, int maxMana,
            int level, int elevation, bool seenByDawn, bool seenByDusk, bool invisible)
        {
            Id = id;
            Team = team;
            Kind = kind;
            TypeId = typeId;
            Name = name;
            X = x;
            Y = y;
            Hp = hp;
            MaxHp = maxHp;
            Mana = mana;
            MaxMana = maxMana;
            Level = level;
            Elevation = elevation;
            SeenByDawn = seenByDawn;
            SeenByDusk = seenByDusk;
            Invisible = invisible;
        }

        public long Id { get; }
        public Team Team { get; }
        public UnitKind Kind { get; }
        public string TypeId { get; }

        /// <summary>Hero display name; null for other units.</summary>
        public string? Name { get; }

        public float X { get; }
        public float Y { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public int Mana { get; }
        public int MaxMana { get; }

        /// <summary>Hero level; 0 for other units.</summary>
        public int Level { get; }

        public int Elevation { get; }
        public bool SeenByDawn { get; }
        public bool SeenByDusk { get; }
        public bool Invisible { get; }

        internal static UnitFrame From(MatchState state, MatchUnit unit)
        {
            var hero = unit.Hero;
            return new UnitFrame(
                unit.Id.Value, unit.Team, unit.Kind, unit.TypeId, hero?.Definition.Name,
                (float)unit.Position.X.ToDouble(), (float)unit.Position.Y.ToDouble(),
                unit.Hp.ToIntFloor(), unit.MaxHp,
                hero?.Mana.ToIntFloor() ?? 0, hero?.MaxMana ?? 0, hero?.Level ?? 0,
                state.ElevationAt(unit.Position),
                state.IsVisibleTo(Team.Dawn, unit), state.IsVisibleTo(Team.Dusk, unit),
                (unit.Flags & ModifierFlags.Invisible) != 0);
        }
    }

    /// <summary>Where a team last saw an enemy hero that is now out of sight.</summary>
    public sealed class GhostFrame
    {
        public GhostFrame(long heroId, Team team, string name, float x, float y, long seenAtTick)
        {
            HeroId = heroId;
            Team = team;
            Name = name;
            X = x;
            Y = y;
            SeenAtTick = seenAtTick;
        }

        public long HeroId { get; }
        public Team Team { get; }
        public string Name { get; }
        public float X { get; }
        public float Y { get; }
        public long SeenAtTick { get; }
    }

    /// <summary>A projectile in flight.</summary>
    public sealed class ProjectileFrame
    {
        public ProjectileFrame(long id, Team? team, float x, float y, bool isAbility)
        {
            Id = id;
            Team = team;
            X = x;
            Y = y;
            IsAbility = isAbility;
        }

        public long Id { get; }

        /// <summary>Team of the unit that launched it, if that unit still exists.</summary>
        public Team? Team { get; }

        public float X { get; }
        public float Y { get; }
        public bool IsAbility { get; }
    }
}
