using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Rulesets;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>The two sides of a match. Stable numeric values: they are part of canonical ruleset encoding.</summary>
    public enum Team
    {
        Dawn = 0,
        Dusk = 1,
    }

    public static class TeamExtensions
    {
        public static Team Opponent(this Team team) => team == Team.Dawn ? Team.Dusk : Team.Dawn;
    }

    /// <summary>Combat-relevant numbers of a unit type. Distances in map units, durations in ms.</summary>
    public sealed class UnitStats
    {
        public UnitStats(
            int maxHp,
            Fixed armor,
            int moveSpeed,
            int attackDamageMin,
            int attackDamageMax,
            int attackRange,
            int attackIntervalMs,
            int attackPointMs,
            int acquisitionRange,
            int boundingRadius,
            int magicResistPercent = 0)
        {
            MagicResistPercent = magicResistPercent;
            MaxHp = maxHp;
            Armor = armor;
            MoveSpeed = moveSpeed;
            AttackDamageMin = attackDamageMin;
            AttackDamageMax = attackDamageMax;
            AttackRange = attackRange;
            AttackIntervalMs = attackIntervalMs;
            AttackPointMs = attackPointMs;
            AcquisitionRange = acquisitionRange;
            BoundingRadius = boundingRadius;
        }

        public int MaxHp { get; }
        public Fixed Armor { get; }

        /// <summary>Units per second; 0 for immobile units.</summary>
        public int MoveSpeed { get; }

        public int AttackDamageMin { get; }
        public int AttackDamageMax { get; }
        public bool CanAttack => AttackDamageMax > 0;

        /// <summary>Edge-to-edge reach: center distance minus both bounding radii.</summary>
        public int AttackRange { get; }

        /// <summary>Time between attack starts.</summary>
        public int AttackIntervalMs { get; }

        /// <summary>Wind-up from attack start until damage lands.</summary>
        public int AttackPointMs { get; }

        public int AcquisitionRange { get; }
        public int BoundingRadius { get; }

        /// <summary>Percent reduction of magical damage.</summary>
        public int MagicResistPercent { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(MagicResistPercent);
            w.WriteInt64(MaxHp);
            w.WriteFixed(Armor);
            w.WriteInt64(MoveSpeed);
            w.WriteInt64(AttackDamageMin);
            w.WriteInt64(AttackDamageMax);
            w.WriteInt64(AttackRange);
            w.WriteInt64(AttackIntervalMs);
            w.WriteInt64(AttackPointMs);
            w.WriteInt64(AcquisitionRange);
            w.WriteInt64(BoundingRadius);
        }
    }

    public sealed class CreepType
    {
        public CreepType(string id, UnitStats stats, Bounty? bounty = null)
        {
            Id = id;
            Stats = stats;
            Bounty = bounty ?? Bounty.None;
        }

        public string Id { get; }
        public UnitStats Stats { get; }

        /// <summary>Gold to the hero landing the last hit; XP shared by nearby enemy heroes.</summary>
        public Bounty Bounty { get; }
    }

    public sealed class StructureType
    {
        public StructureType(string id, UnitStats stats)
        {
            Id = id;
            Stats = stats;
        }

        public string Id { get; }
        public UnitStats Stats { get; }
    }

    /// <summary>When a protected structure stops being invulnerable.</summary>
    public enum VulnerabilityRule
    {
        /// <summary>As soon as any protector is destroyed (e.g. base towers after any lane is broken).</summary>
        AnyProtectorDestroyed = 0,

        /// <summary>Only once every protector is destroyed (e.g. the ancient behind its base towers).</summary>
        AllProtectorsDestroyed = 1,
    }

    public sealed class StructurePlacement
    {
        public StructurePlacement(
            string id,
            Team team,
            string structureTypeId,
            FixedVector2 position,
            bool isAncient,
            IReadOnlyList<string> protectedBy,
            VulnerabilityRule vulnerability)
        {
            Id = id;
            Team = team;
            StructureTypeId = structureTypeId;
            Position = position;
            IsAncient = isAncient;
            ProtectedBy = protectedBy.ToArray();
            Vulnerability = vulnerability;
        }

        /// <summary>Stable ID, e.g. "dawn.top.t1". Never derived from display names.</summary>
        public string Id { get; }

        public Team Team { get; }
        public string StructureTypeId { get; }
        public FixedVector2 Position { get; }

        /// <summary>Destroying a team's ancient wins the match for the opponent.</summary>
        public bool IsAncient { get; }

        /// <summary>Placement IDs that keep this structure invulnerable. Empty = always vulnerable.</summary>
        public IReadOnlyList<string> ProtectedBy { get; }

        public VulnerabilityRule Vulnerability { get; }
    }

    public sealed class LaneDefinition
    {
        public LaneDefinition(string id, IReadOnlyList<FixedVector2> waypoints)
        {
            Id = id;
            Waypoints = waypoints.ToArray();
        }

        public string Id { get; }

        /// <summary>Ordered from the Dawn base to the Dusk base; Dusk creeps walk it in reverse.</summary>
        public IReadOnlyList<FixedVector2> Waypoints { get; }
    }

    public enum ObstacleShape
    {
        Circle = 0,
        Rectangle = 1,
    }

    /// <summary>Impassable terrain (cliffs, tree clumps). Structures do not block movement yet.</summary>
    public sealed class ObstacleDefinition
    {
        private ObstacleDefinition(string id, ObstacleShape shape, FixedVector2 min, FixedVector2 max, FixedVector2 center, int radius)
        {
            Id = id;
            Shape = shape;
            Min = min;
            Max = max;
            Center = center;
            Radius = radius;
        }

        public string Id { get; }
        public ObstacleShape Shape { get; }

        /// <summary>Rectangle corners; for circles, the bounding box.</summary>
        public FixedVector2 Min { get; }
        public FixedVector2 Max { get; }

        public FixedVector2 Center { get; }

        /// <summary>Circle radius; 0 for rectangles.</summary>
        public int Radius { get; }

        public static ObstacleDefinition Circle(string id, FixedVector2 center, int radius)
        {
            var extent = new FixedVector2(Fixed.FromInt(radius), Fixed.FromInt(radius));
            return new ObstacleDefinition(id, ObstacleShape.Circle, center - extent, center + extent, center, radius);
        }

        public static ObstacleDefinition Rectangle(string id, FixedVector2 min, FixedVector2 max)
        {
            var center = new FixedVector2((min.X + max.X) / Fixed.FromInt(2), (min.Y + max.Y) / Fixed.FromInt(2));
            return new ObstacleDefinition(id, ObstacleShape.Rectangle, min, max, center, 0);
        }

        /// <summary>Whether the point lies inside the obstacle grown by <paramref name="clearance"/> units.</summary>
        public bool Contains(FixedVector2 point, int clearance)
        {
            Fixed pad = Fixed.FromInt(clearance);
            if (Shape == ObstacleShape.Circle)
            {
                Fixed reach = Fixed.FromInt(Radius) + pad;
                return FixedVector2.DistanceSquared(point, Center) <= reach * reach;
            }

            return point.X >= Min.X - pad && point.X <= Max.X + pad &&
                   point.Y >= Min.Y - pad && point.Y <= Max.Y + pad;
        }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteString(Id);
            w.WriteInt64((int)Shape);
            w.WriteVector(Min);
            w.WriteVector(Max);
            w.WriteInt64(Radius);
        }
    }

    public sealed class MapDefinition
    {
        public MapDefinition(
            int width,
            int height,
            IReadOnlyList<LaneDefinition> lanes,
            IReadOnlyList<StructurePlacement> structures,
            IReadOnlyList<ObstacleDefinition>? obstacles = null,
            int navCellSize = 100,
            int navClearance = 24,
            IReadOnlyList<FountainPlacement>? fountains = null)
        {
            Width = width;
            Height = height;
            Lanes = lanes.ToArray();
            Structures = structures.ToArray();
            Obstacles = (obstacles ?? Array.Empty<ObstacleDefinition>()).ToArray();
            Fountains = (fountains ?? Array.Empty<FountainPlacement>()).ToArray();
            NavCellSize = navCellSize;
            NavClearance = navClearance;
        }

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<LaneDefinition> Lanes { get; }
        public IReadOnlyList<StructurePlacement> Structures { get; }
        public IReadOnlyList<ObstacleDefinition> Obstacles { get; }

        /// <summary>Hero spawn and heal areas, one per team when heroes are used.</summary>
        public IReadOnlyList<FountainPlacement> Fountains { get; }

        public FountainPlacement? FountainOf(Team team) => Fountains.FirstOrDefault(f => f.Team == team);

        /// <summary>Side of a navigation grid cell in map units.</summary>
        public int NavCellSize { get; }

        /// <summary>Obstacles are grown by this many units when building the navigation grid.</summary>
        public int NavClearance { get; }
    }

    public sealed class WaveEntry
    {
        public WaveEntry(string creepTypeId, int count)
        {
            CreepTypeId = creepTypeId;
            Count = count;
        }

        public string CreepTypeId { get; }
        public int Count { get; }
    }

    /// <summary>Creep waves spawned on every lane. Compositions are per team so sandbox/lab rulesets can be asymmetric.</summary>
    public sealed class WaveRules
    {
        public WaveRules(int firstSpawnMs, int intervalMs, IReadOnlyList<WaveEntry> dawnComposition, IReadOnlyList<WaveEntry> duskComposition)
        {
            FirstSpawnMs = firstSpawnMs;
            IntervalMs = intervalMs;
            DawnComposition = dawnComposition.ToArray();
            DuskComposition = duskComposition.ToArray();
        }

        public int FirstSpawnMs { get; }
        public int IntervalMs { get; }
        public IReadOnlyList<WaveEntry> DawnComposition { get; }
        public IReadOnlyList<WaveEntry> DuskComposition { get; }

        public IReadOnlyList<WaveEntry> CompositionFor(Team team) => team == Team.Dawn ? DawnComposition : DuskComposition;
    }

    public sealed class CombatRules
    {
        public CombatRules(Fixed armorFactor)
        {
            ArmorFactor = armorFactor;
        }

        /// <summary>Damage multiplier = 1 - f·armor / (1 + f·|armor|).</summary>
        public Fixed ArmorFactor { get; }
    }

    /// <summary>
    /// Immutable gameplay ruleset snapshot (one patch version). Everything that changes match
    /// outcomes lives here, so a match pinned to this content is reproducible.
    /// </summary>
    public sealed class RulesetContent
    {
        public RulesetContent(
            string id,
            int version,
            int ticksPerSecond,
            CombatRules combat,
            IReadOnlyList<CreepType> creepTypes,
            IReadOnlyList<StructureType> structureTypes,
            MapDefinition map,
            WaveRules waves,
            IReadOnlyList<HeroDefinition>? heroes = null,
            HeroRules? heroRules = null)
        {
            Id = id;
            Version = version;
            TicksPerSecond = ticksPerSecond;
            Combat = combat;
            CreepTypes = creepTypes.ToArray();
            StructureTypes = structureTypes.ToArray();
            Map = map;
            Waves = waves;
            Heroes = (heroes ?? Array.Empty<HeroDefinition>()).ToArray();
            HeroRules = heroRules;
        }

        public IReadOnlyList<HeroDefinition> Heroes { get; }

        /// <summary>Required when <see cref="Heroes"/> is not empty.</summary>
        public HeroRules? HeroRules { get; }

        public HeroDefinition? FindHero(string id) => Heroes.FirstOrDefault(h => h.Id == id);

        public string Id { get; }
        public int Version { get; }
        public int TicksPerSecond { get; }
        public CombatRules Combat { get; }
        public IReadOnlyList<CreepType> CreepTypes { get; }
        public IReadOnlyList<StructureType> StructureTypes { get; }
        public MapDefinition Map { get; }
        public WaveRules Waves { get; }

        public CreepType? FindCreepType(string id) => CreepTypes.FirstOrDefault(c => c.Id == id);

        public StructureType? FindStructureType(string id) => StructureTypes.FirstOrDefault(s => s.Id == id);

        /// <summary>Human-readable problems; empty when the ruleset is usable.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (Version < 1)
                errors.Add("Version must be at least 1.");
            if (TicksPerSecond < 1 || TicksPerSecond > 1000)
                errors.Add("Ticks per second must be within 1..1000.");
            if (Combat.ArmorFactor <= Fixed.Zero)
                errors.Add("Armor factor must be positive.");

            ReportDuplicates(errors, "creep type", CreepTypes.Select(c => c.Id));
            ReportDuplicates(errors, "structure type", StructureTypes.Select(s => s.Id));
            foreach (CreepType creep in CreepTypes)
            {
                ValidateStats(errors, "Creep type '" + creep.Id + "'", creep.Stats);
                if (creep.Stats.MoveSpeed <= 0)
                    errors.Add("Creep type '" + creep.Id + "' must have positive move speed.");
            }

            foreach (StructureType structure in StructureTypes)
                ValidateStats(errors, "Structure type '" + structure.Id + "'", structure.Stats);

            ValidateMap(errors);
            ValidateWaves(errors);
            ValidateHeroes(errors);
            return errors;
        }

        private void ValidateHeroes(List<string> errors)
        {
            foreach (CreepType creep in CreepTypes)
            {
                if (creep.Bounty.GoldMin < 0 || creep.Bounty.GoldMin > creep.Bounty.GoldMax || creep.Bounty.Xp < 0)
                    errors.Add("Creep type '" + creep.Id + "' has an invalid bounty.");
            }

            if (Heroes.Count == 0)
                return;

            ReportDuplicates(errors, "hero", Heroes.Select(h => h.Id));
            foreach (HeroDefinition hero in Heroes)
            {
                ValidateStats(errors, "Hero '" + hero.Id + "'", hero.BaseStats);
                if (hero.BaseStats.MoveSpeed <= 0)
                    errors.Add("Hero '" + hero.Id + "' must have positive move speed.");
            }

            if (HeroRules == null)
            {
                errors.Add("Hero rules are required when heroes are defined.");
                return;
            }

            IReadOnlyList<int> xp = HeroRules.XpForLevel;
            if (xp.Count == 0 || xp[0] != 0)
                errors.Add("XP table must start at 0 for level 1.");
            for (int i = 1; i < xp.Count; i++)
            {
                if (xp[i] <= xp[i - 1])
                    errors.Add("XP table must be strictly increasing (level " + (i + 1) + ").");
            }

            foreach (Team team in new[] { Team.Dawn, Team.Dusk })
            {
                FountainPlacement? fountain = Map.FountainOf(team);
                if (fountain == null)
                    errors.Add("Team " + team + " needs a fountain for heroes.");
                else if (!IsInsideMap(fountain.Position))
                    errors.Add("Team " + team + " fountain is outside the map.");
            }
        }

        public void WriteCanonical(CanonicalWriter writer)
        {
            writer.WriteString("dynasty.ruleset/1");
            writer.WriteString(Id);
            writer.WriteInt64(Version);
            writer.WriteInt64(TicksPerSecond);
            writer.WriteFixed(Combat.ArmorFactor);

            writer.WriteInt64(CreepTypes.Count);
            foreach (CreepType creep in CreepTypes)
            {
                writer.WriteString(creep.Id);
                creep.Stats.WriteCanonical(writer);
                writer.WriteInt64(creep.Bounty.GoldMin);
                writer.WriteInt64(creep.Bounty.GoldMax);
                writer.WriteInt64(creep.Bounty.Xp);
            }

            writer.WriteInt64(StructureTypes.Count);
            foreach (StructureType structure in StructureTypes)
            {
                writer.WriteString(structure.Id);
                structure.Stats.WriteCanonical(writer);
            }

            writer.WriteInt64(Map.Width);
            writer.WriteInt64(Map.Height);
            writer.WriteInt64(Map.Lanes.Count);
            foreach (LaneDefinition lane in Map.Lanes)
            {
                writer.WriteString(lane.Id);
                writer.WriteInt64(lane.Waypoints.Count);
                foreach (FixedVector2 point in lane.Waypoints)
                    writer.WriteVector(point);
            }

            writer.WriteInt64(Map.Structures.Count);
            foreach (StructurePlacement placement in Map.Structures)
            {
                writer.WriteString(placement.Id);
                writer.WriteInt64((int)placement.Team);
                writer.WriteString(placement.StructureTypeId);
                writer.WriteVector(placement.Position);
                writer.WriteBool(placement.IsAncient);
                writer.WriteInt64(placement.ProtectedBy.Count);
                foreach (string protector in placement.ProtectedBy)
                    writer.WriteString(protector);
                writer.WriteInt64((int)placement.Vulnerability);
            }

            writer.WriteInt64(Map.Obstacles.Count);
            foreach (ObstacleDefinition obstacle in Map.Obstacles)
                obstacle.WriteCanonical(writer);
            writer.WriteInt64(Map.NavCellSize);
            writer.WriteInt64(Map.NavClearance);
            writer.WriteInt64(Map.Fountains.Count);
            foreach (FountainPlacement fountain in Map.Fountains)
            {
                writer.WriteInt64((int)fountain.Team);
                writer.WriteVector(fountain.Position);
                writer.WriteInt64(fountain.Radius);
            }

            writer.WriteInt64(Heroes.Count);
            foreach (HeroDefinition hero in Heroes)
                hero.WriteCanonical(writer);
            writer.WriteBool(HeroRules != null);
            HeroRules?.WriteCanonical(writer);

            writer.WriteInt64(Waves.FirstSpawnMs);
            writer.WriteInt64(Waves.IntervalMs);
            WriteComposition(writer, Waves.DawnComposition);
            WriteComposition(writer, Waves.DuskComposition);
        }

        /// <summary>Identity of this exact snapshot. Throws if the content is invalid.</summary>
        public RulesetDescriptor ToDescriptor()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Ruleset '" + Id + "' is invalid: " + string.Join("; ", errors));

            var writer = new CanonicalWriter();
            WriteCanonical(writer);
            return new RulesetDescriptor(Id, Version, writer.ToFingerprint());
        }

        private void ValidateMap(List<string> errors)
        {
            if (Map.Width <= 0 || Map.Height <= 0)
                errors.Add("Map size must be positive.");

            ReportDuplicates(errors, "lane", Map.Lanes.Select(l => l.Id));
            foreach (LaneDefinition lane in Map.Lanes)
            {
                if (lane.Waypoints.Count < 2)
                    errors.Add("Lane '" + lane.Id + "' needs at least two waypoints.");
                if (lane.Waypoints.Any(p => !IsInsideMap(p)))
                    errors.Add("Lane '" + lane.Id + "' has a waypoint outside the map.");
            }

            ReportDuplicates(errors, "structure placement", Map.Structures.Select(s => s.Id));
            var byId = new Dictionary<string, StructurePlacement>();
            foreach (StructurePlacement placement in Map.Structures)
            {
                if (!byId.ContainsKey(placement.Id))
                    byId.Add(placement.Id, placement);
            }

            foreach (StructurePlacement placement in Map.Structures)
            {
                if (FindStructureType(placement.StructureTypeId) == null)
                    errors.Add("Structure '" + placement.Id + "' uses unknown type '" + placement.StructureTypeId + "'.");
                if (!IsInsideMap(placement.Position))
                    errors.Add("Structure '" + placement.Id + "' is outside the map.");

                foreach (string protectorId in placement.ProtectedBy)
                {
                    if (!byId.TryGetValue(protectorId, out StructurePlacement? protector))
                        errors.Add("Structure '" + placement.Id + "' is protected by unknown structure '" + protectorId + "'.");
                    else if (protector.Team != placement.Team)
                        errors.Add("Structure '" + placement.Id + "' is protected by enemy structure '" + protectorId + "'.");
                    else if (protectorId == placement.Id)
                        errors.Add("Structure '" + placement.Id + "' cannot protect itself.");
                }
            }

            if (Map.NavCellSize <= 0 || Map.NavClearance < 0)
                errors.Add("Navigation cell size must be positive and clearance non-negative.");
            ReportDuplicates(errors, "obstacle", Map.Obstacles.Select(o => o.Id));
            foreach (ObstacleDefinition obstacle in Map.Obstacles)
            {
                if (!IsInsideMap(obstacle.Center))
                    errors.Add("Obstacle '" + obstacle.Id + "' is outside the map.");
            }

            foreach (LaneDefinition lane in Map.Lanes)
            {
                ObstacleDefinition? blocker = FindLaneBlocker(lane);
                if (blocker != null)
                    errors.Add("Lane '" + lane.Id + "' passes through obstacle '" + blocker.Id + "'.");
            }

            foreach (Team team in new[] { Team.Dawn, Team.Dusk })
            {
                int ancients = Map.Structures.Count(s => s.Team == team && s.IsAncient);
                if (ancients != 1)
                    errors.Add("Team " + team + " must have exactly one ancient (found " + ancients + ").");
            }
        }

        private void ValidateWaves(List<string> errors)
        {
            if (Waves.FirstSpawnMs < 0)
                errors.Add("First wave time must not be negative.");
            if (Waves.IntervalMs <= 0)
                errors.Add("Wave interval must be positive.");

            foreach (WaveEntry entry in Waves.DawnComposition.Concat(Waves.DuskComposition))
            {
                if (FindCreepType(entry.CreepTypeId) == null)
                    errors.Add("Wave uses unknown creep type '" + entry.CreepTypeId + "'.");
                if (entry.Count <= 0)
                    errors.Add("Wave entry '" + entry.CreepTypeId + "' must have a positive count.");
            }
        }

        private static void ValidateStats(List<string> errors, string owner, UnitStats stats)
        {
            if (stats.MaxHp <= 0)
                errors.Add(owner + " must have positive max HP.");
            if (stats.AttackDamageMin < 0 || stats.AttackDamageMin > stats.AttackDamageMax)
                errors.Add(owner + " has an invalid damage range.");
            if (stats.AttackIntervalMs <= 0)
                errors.Add(owner + " must have a positive attack interval.");
            if (stats.AttackPointMs < 0 || stats.AttackPointMs > stats.AttackIntervalMs)
                errors.Add(owner + " attack point must be within the attack interval.");
            if (stats.AttackRange < 0 || stats.AcquisitionRange < 0 || stats.BoundingRadius < 0 || stats.MoveSpeed < 0)
                errors.Add(owner + " has a negative range, radius or speed.");
        }

        private static void ReportDuplicates(List<string> errors, string kind, IEnumerable<string> ids)
        {
            foreach (IGrouping<string, string> group in ids.GroupBy(id => id).Where(g => g.Count() > 1))
                errors.Add("Duplicate " + kind + " ID '" + group.Key + "'.");
        }

        /// <summary>Samples each lane segment every 50 units against every obstacle.</summary>
        private ObstacleDefinition? FindLaneBlocker(LaneDefinition lane)
        {
            Fixed sampleStep = Fixed.FromInt(50);
            for (int i = 0; i + 1 < lane.Waypoints.Count; i++)
            {
                FixedVector2 point = lane.Waypoints[i];
                FixedVector2 end = lane.Waypoints[i + 1];
                while (true)
                {
                    foreach (ObstacleDefinition obstacle in Map.Obstacles)
                    {
                        if (obstacle.Contains(point, 0))
                            return obstacle;
                    }

                    if (point == end)
                        break;
                    point = FixedVector2.MoveTowards(point, end, sampleStep);
                }
            }

            return null;
        }

        private bool IsInsideMap(FixedVector2 point) =>
            point.X >= Fixed.Zero && point.Y >= Fixed.Zero &&
            point.X <= Fixed.FromInt(Map.Width) && point.Y <= Fixed.FromInt(Map.Height);

        private static void WriteComposition(CanonicalWriter writer, IReadOnlyList<WaveEntry> composition)
        {
            writer.WriteInt64(composition.Count);
            foreach (WaveEntry entry in composition)
            {
                writer.WriteString(entry.CreepTypeId);
                writer.WriteInt64(entry.Count);
            }
        }
    }
}
