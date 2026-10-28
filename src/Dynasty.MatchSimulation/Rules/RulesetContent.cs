using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;
using Dynasty.Core.Rulesets;
using Dynasty.MatchSimulation.Combat;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>The two sides of a match. Stable numeric values: they are part of canonical ruleset encoding.</summary>
    public enum Team
    {
        Dawn = 0,
        Dusk = 1,

        /// <summary>Neutral objectives; hostile to both sides.</summary>
        Neutral = 2,
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
            int magicResistPercent = 0,
            int visionRange = 800,
            int trueSightRange = 0,
            ModifierFlags innateFlags = ModifierFlags.None,
            int projectileSpeed = 0,
            int nightVisionRange = -1,
            AttackClass attackClass = AttackClass.Basic)
        {
            AttackClass = attackClass;
            ProjectileSpeed = projectileSpeed;
            NightVisionRange = nightVisionRange < 0 ? visionRange : nightVisionRange;
            MagicResistPercent = magicResistPercent;
            VisionRange = visionRange;
            TrueSightRange = trueSightRange;
            InnateFlags = innateFlags;
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

        /// <summary>How far this unit reveals the map for its team (blocked by obstacles).</summary>
        public int VisionRange { get; }

        /// <summary>Radius in which this unit reveals invisible enemies (detection).</summary>
        public int TrueSightRange { get; }

        /// <summary>Status flags the unit always has, e.g. <see cref="ModifierFlags.Invisible"/> for wards.</summary>
        public ModifierFlags InnateFlags { get; }

        /// <summary>Speed of the attack projectile in units per second; 0 = the hit lands at the attack point (melee).</summary>
        public int ProjectileSpeed { get; }

        /// <summary>Vision range at night (defaults to the day range).</summary>
        public int NightVisionRange { get; }

        /// <summary>Attack class of a non-hero, non-structure unit (heroes and structures have fixed classes).</summary>
        public AttackClass AttackClass { get; }

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64((int)AttackClass);
            w.WriteInt64(ProjectileSpeed);
            w.WriteInt64(NightVisionRange);
            w.WriteInt64(MagicResistPercent);
            w.WriteInt64(VisionRange);
            w.WriteInt64(TrueSightRange);
            w.WriteInt64((int)InnateFlags);
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
        public CreepType(string id, UnitStats stats, Bounty? bounty = null, IReadOnlyList<ModifierSpec>? modifiers = null, AreaBounty? areaBounty = null)
        {
            Id = id;
            Stats = stats;
            Bounty = bounty ?? Bounty.None;
            Modifiers = (modifiers ?? Array.Empty<ModifierSpec>()).ToArray();
            AreaBounty = areaBounty;
        }

        public string Id { get; }
        public UnitStats Stats { get; }

        /// <summary>Gold to the hero landing the last hit; XP shared by nearby enemy heroes.</summary>
        public Bounty Bounty { get; }

        /// <summary>Its own modifiers from the start (Dota's flagbearer: Inspiration Aura).</summary>
        public IReadOnlyList<ModifierSpec> Modifiers { get; }

        /// <summary>Gold its death gives the enemy heroes around (Dota's flagbearer); null = none.</summary>
        public AreaBounty? AreaBounty { get; }
    }

    /// <summary>
    /// Dota's flagbearer bounty (Liquipedia «Lane Creeps», 7.40–7.41): killed by an enemy, it gives the killing player (always,
    /// even through a summon) and every enemy hero within the radius the base gold plus a percent of its bounty; denied, nothing.
    /// </summary>
    public sealed class AreaBounty
    {
        public AreaBounty(int baseGold, int bountyPercent, int radius)
        {
            BaseGold = baseGold;
            BountyPercent = bountyPercent;
            Radius = radius;
        }

        public int BaseGold { get; }
        public int BountyPercent { get; }
        public int Radius { get; }
    }

    public sealed class StructureType
    {
        public StructureType(string id, UnitStats stats, StructureBounty? bounty = null, int denyHealthPercent = 0,
            IReadOnlyList<ModifierSpec>? modifiers = null, int armorPerStandingBarracks = 0, BackdoorProtection? backdoor = null,
            int glyphExtraTargets = 0, bool resetsGlyphOnFirstLoss = false, IReadOnlyList<string>? upgradesCreepTypeIds = null)
        {
            UpgradesCreepTypeIds = upgradesCreepTypeIds?.ToArray();
            Backdoor = backdoor;
            GlyphExtraTargets = glyphExtraTargets;
            ResetsGlyphOnFirstLoss = resetsGlyphOnFirstLoss;
            Id = id;
            Stats = stats;
            Bounty = bounty;
            DenyHealthPercent = denyHealthPercent;
            Modifiers = (modifiers ?? Array.Empty<ModifierSpec>()).ToArray();
            ArmorPerStandingBarracks = armorPerStandingBarracks;
            if (armorPerStandingBarracks > 0)
                BarracksReinforcement = new ModifierSpec("barracks_reinforcement", -1, armorBonus: Fixed.FromInt(armorPerStandingBarracks), maxStacks: 100);
        }

        /// <summary>
        /// For a barracks: the enemy's creep types whose waves on its lane its loss upgrades (Dota: melee barracks the melee creeps,
        /// ranged barracks the ranged and siege creeps); null = every type.
        /// </summary>
        public IReadOnlyList<string>? UpgradesCreepTypeIds { get; }

        /// <summary>Its own modifiers from the start (Dota: Tower Protection, an aura).</summary>
        public IReadOnlyList<ModifierSpec> Modifiers { get; }

        /// <summary>Armor for each standing barracks of its team (Dota 7.40: 4 on tier 4 towers); 0 = none.</summary>
        public int ArmorPerStandingBarracks { get; }

        internal ModifierSpec? BarracksReinforcement { get; }

        /// <summary>Dota's Backdoor Protection; null = none (Dota: tier 1 towers).</summary>
        public BackdoorProtection? Backdoor { get; }

        /// <summary>Under the glyph, each of its attacks also strikes this many other enemies in range (Dota: 5 for towers of tier 2 and up).</summary>
        public int GlyphExtraTargets { get; }

        /// <summary>Losing the team's first building of this type resets the glyph's cooldown (Dota: tier 1, tier 2, melee barracks).</summary>
        public bool ResetsGlyphOnFirstLoss { get; }

        /// <summary>The structure type of the fountains: when a ruleset has it, each fountain is also an invulnerable building (Dota).</summary>
        public const string FountainId = "fountain";

        public string Id { get; }
        public UnitStats Stats { get; }

        /// <summary>Dota's gold for destroying it; null = the hero rules' flat team gold.</summary>
        public StructureBounty? Bounty { get; }

        /// <summary>Allied heroes may attack it below this percent of its health (Dota: 10, never the ancient); 0 = never.</summary>
        public int DenyHealthPercent { get; }

        internal void WriteCanonical(CanonicalWriter writer)
        {
            writer.WriteString(Id);
            Stats.WriteCanonical(writer);
            writer.WriteBool(Bounty != null);
            if (Bounty != null)
            {
                foreach (int value in new[] { Bounty.TeamGold, Bounty.LastHitGoldMin, Bounty.LastHitGoldMax, Bounty.DeniedEnemyGoldPercent, Bounty.DeniedOwnGoldPercent })
                    writer.WriteInt64(value);
                writer.WriteBool(Bounty.LastHitterGetsTeamGold);
            }

            writer.WriteInt64(DenyHealthPercent);
            writer.WriteInt64(Modifiers.Count);
            foreach (ModifierSpec modifier in Modifiers)
                modifier.WriteCanonical(writer);
            writer.WriteInt64(ArmorPerStandingBarracks);
            writer.WriteBool(Backdoor != null);
            if (Backdoor != null)
            {
                foreach (int value in new[] { Backdoor.Radius, Backdoor.DamageReductionPercent, Backdoor.SummonDamageReductionPercent, Backdoor.HealPerSecond, Backdoor.ReactivationMs })
                    writer.WriteInt64(value);
                writer.WriteBool(Backdoor.AroundAncient);
            }

            writer.WriteInt64(GlyphExtraTargets);
            writer.WriteBool(ResetsGlyphOnFirstLoss);
            writer.WriteInt64(UpgradesCreepTypeIds?.Count ?? -1);
            foreach (string creepTypeId in UpgradesCreepTypeIds ?? Array.Empty<string>())
                writer.WriteString(creepTypeId);
        }
    }

    /// <summary>
    /// Dota's lane aggro (Liquipedia «Lane Creeps» and «Buildings», 7.41): an attack order on an enemy hero draws the enemy
    /// lane creeps within <see cref="DrawRadius"/> of the attacker to chase it for <see cref="ForcedMs"/> (once per
    /// <see cref="CooldownMs"/> per hero), a started attack on a hero draws them without the chase; creeps spawned before
    /// <see cref="LockedUntilMs"/> cannot be drawn until within <see cref="UnlockRadius"/> of their team's tier 1 tower on their
    /// lane; towers turn on a hero within <see cref="TowerProtectRadius"/> of them attacking a hero, and weigh threats within
    /// <see cref="TowerThreatRadius"/>.
    /// </summary>
    public sealed class DotaAggroRules
    {
        public DotaAggroRules(int drawRadius, int forcedMs, int cooldownMs, int lockedUntilMs, int unlockRadius, string unlockStructureTypeId,
            int towerProtectRadius, int towerThreatRadius)
        {
            DrawRadius = drawRadius;
            ForcedMs = forcedMs;
            CooldownMs = cooldownMs;
            LockedUntilMs = lockedUntilMs;
            UnlockRadius = unlockRadius;
            UnlockStructureTypeId = unlockStructureTypeId;
            TowerProtectRadius = towerProtectRadius;
            TowerThreatRadius = towerThreatRadius;
        }

        public int DrawRadius { get; }
        public int ForcedMs { get; }
        public int CooldownMs { get; }
        public int LockedUntilMs { get; }
        public int UnlockRadius { get; }

        /// <summary>The structure type of the tier 1 towers (found on a lane by the placement id «team.lane.…»).</summary>
        public string UnlockStructureTypeId { get; }

        public int TowerProtectRadius { get; }
        public int TowerThreatRadius { get; }
    }

    /// <summary>
    /// Dota's Glyph of Fortification (Liquipedia «Buildings», 7.41f): a team action that makes its buildings take no damage
    /// for a while and its lane creeps for a shorter while; first usable after the initial cooldown from the horn.
    /// </summary>
    public sealed class GlyphRules
    {
        public GlyphRules(int cooldownMs, int initialCooldownMs, int buildingDurationMs, int creepDurationMs, int resetDelayMs)
        {
            CooldownMs = cooldownMs;
            InitialCooldownMs = initialCooldownMs;
            BuildingDurationMs = buildingDurationMs;
            CreepDurationMs = creepDurationMs;
            ResetDelayMs = resetDelayMs;
        }

        public int CooldownMs { get; }
        public int InitialCooldownMs { get; }
        public int BuildingDurationMs { get; }
        public int CreepDurationMs { get; }

        /// <summary>The cooldown left after a reset (losing a first building of a resetting type).</summary>
        public int ResetDelayMs { get; }
    }

    /// <summary>
    /// Dota's Backdoor Protection (Liquipedia «Buildings», 7.41f; D-051): while no enemy lane creep is within the radius
    /// (around the building, or around its team's ancient for the buildings in the base) and for the reactivation time
    /// after the last one left, the building takes less damage (even less from illusions and summons) and heals the
    /// damage enemies dealt to it.
    /// </summary>
    public sealed class BackdoorProtection
    {
        public BackdoorProtection(int radius, bool aroundAncient, int damageReductionPercent, int summonDamageReductionPercent, int healPerSecond, int reactivationMs)
        {
            Radius = radius;
            AroundAncient = aroundAncient;
            DamageReductionPercent = damageReductionPercent;
            SummonDamageReductionPercent = summonDamageReductionPercent;
            HealPerSecond = healPerSecond;
            ReactivationMs = reactivationMs;
        }

        public int Radius { get; }
        public bool AroundAncient { get; }
        public int DamageReductionPercent { get; }
        public int SummonDamageReductionPercent { get; }
        public int HealPerSecond { get; }
        public int ReactivationMs { get; }
    }

    /// <summary>
    /// Dota's gold for a destroyed building (Liquipedia «Buildings», 7.41f): the team bounty goes to every enemy hero, the
    /// last-hitting hero gets a further random bounty (and the team bounty too, unless it is a barracks); a denied building
    /// gives each hero of its enemies and of its own team a percent of the team bounty instead.
    /// </summary>
    public sealed class StructureBounty
    {
        public StructureBounty(int teamGold, int lastHitGoldMin, int lastHitGoldMax, bool lastHitterGetsTeamGold, int deniedEnemyGoldPercent, int deniedOwnGoldPercent)
        {
            TeamGold = teamGold;
            LastHitGoldMin = lastHitGoldMin;
            LastHitGoldMax = lastHitGoldMax;
            LastHitterGetsTeamGold = lastHitterGetsTeamGold;
            DeniedEnemyGoldPercent = deniedEnemyGoldPercent;
            DeniedOwnGoldPercent = deniedOwnGoldPercent;
        }

        public int TeamGold { get; }
        public int LastHitGoldMin { get; }
        public int LastHitGoldMax { get; }
        public bool LastHitterGetsTeamGold { get; }
        public int DeniedEnemyGoldPercent { get; }
        public int DeniedOwnGoldPercent { get; }
    }

    /// <summary>When a protected structure stops being invulnerable.</summary>
    public enum VulnerabilityRule
    {
        /// <summary>As soon as any protector is destroyed (e.g. base towers after any lane is broken).</summary>
        AnyProtectorDestroyed = 0,

        /// <summary>Only once every protector is destroyed (e.g. the ancient behind its base towers).</summary>
        AllProtectorsDestroyed = 1,

        /// <summary>Never (Dota's fountain).</summary>
        Never = 2,
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
            VulnerabilityRule vulnerability,
            string? barracksLane = null)
        {
            BarracksLane = barracksLane;
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

        /// <summary>For barracks: the lane whose enemy waves are upgraded once this structure falls.</summary>
        public string? BarracksLane { get; }
    }

    /// <summary>Wave creep replacement applied to a team's waves on a lane whose enemy barracks fell.</summary>
    public sealed class CreepUpgrade
    {
        public CreepUpgrade(string fromCreepTypeId, string toCreepTypeId)
        {
            FromCreepTypeId = fromCreepTypeId;
            ToCreepTypeId = toCreepTypeId;
        }

        public string FromCreepTypeId { get; }
        public string ToCreepTypeId { get; }
    }

    /// <summary>
    /// Dota's lane creep upgrades (Liquipedia «Lane Creeps», 7.41): a creep spawned after n whole intervals of the match gets n
    /// times its type's step — health and damage for at most <see cref="MaxStatCycles"/> intervals, gold and XP without limit.
    /// </summary>
    public sealed class CreepUpgradeRules
    {
        public CreepUpgradeRules(int intervalMs, int maxStatCycles, IReadOnlyList<CreepUpgradeStep> steps)
        {
            IntervalMs = intervalMs;
            MaxStatCycles = maxStatCycles;
            Steps = steps.ToArray();
        }

        public int IntervalMs { get; }
        public int MaxStatCycles { get; }
        public IReadOnlyList<CreepUpgradeStep> Steps { get; }

        public CreepUpgradeStep? StepFor(string creepTypeId) => Steps.FirstOrDefault(s => s.CreepTypeId == creepTypeId);
    }

    /// <summary>One creep type's upgrade per interval; gold in hundredths (Dota's super melee creeps: +1.5).</summary>
    public sealed class CreepUpgradeStep
    {
        public CreepUpgradeStep(string creepTypeId, int health, int damage, int goldHundredths, int xp,
            int auraMagicResistPerCycle = 0, int auraMaxCycles = 0, int auraRadius = 0)
        {
            AuraMagicResistPerCycle = auraMagicResistPerCycle;
            AuraMaxCycles = auraMaxCycles;
            AuraRadius = auraRadius;
            CreepTypeId = creepTypeId;
            Health = health;
            Damage = damage;
            GoldHundredths = goldHundredths;
            Xp = xp;
        }

        public string CreepTypeId { get; }
        public int Health { get; }
        public int Damage { get; }
        public int GoldHundredths { get; }
        public int Xp { get; }

        /// <summary>Dota's flagbearer: allied creeps within <see cref="AuraRadius"/> get this magic resistance per cycle, up to <see cref="AuraMaxCycles"/> cycles.</summary>
        public int AuraMagicResistPerCycle { get; }
        public int AuraMaxCycles { get; }
        public int AuraRadius { get; }
    }

    public sealed class LaneDefinition
    {
        public LaneDefinition(string id, IReadOnlyList<FixedVector2> waypoints, IReadOnlyList<FixedVector2>? duskWaypoints = null)
        {
            Id = id;
            Waypoints = waypoints.ToArray();
            DuskWaypoints = duskWaypoints?.ToArray();
        }

        public string Id { get; }

        /// <summary>Ordered from the Dawn base to the Dusk base; Dusk creeps walk it in reverse unless they have their own path.</summary>
        public IReadOnlyList<FixedVector2> Waypoints { get; }

        /// <summary>
        /// Dusk's own path of the lane, from the Dusk base to the Dawn base (Dota's map has one per side); null to walk
        /// <see cref="Waypoints"/> backwards.
        /// </summary>
        public IReadOnlyList<FixedVector2>? DuskWaypoints { get; }

        /// <summary>The waypoints a side's creeps walk, in walking order.</summary>
        public IReadOnlyList<FixedVector2> PathOf(Team team) =>
            team == Team.Dawn ? Waypoints : DuskWaypoints ?? Waypoints.Reverse().ToArray();
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
            IReadOnlyList<FountainPlacement>? fountains = null,
            IReadOnlyList<NeutralCampDefinition>? camps = null,
            IReadOnlyList<ElevationZone>? elevation = null,
            MapTerrain? terrain = null)
        {
            Terrain = terrain;
            Camps = (camps ?? Array.Empty<NeutralCampDefinition>()).ToArray();
            Elevation = (elevation ?? Array.Empty<ElevationZone>()).ToArray();
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

        /// <summary>Neutral jungle camps.</summary>
        public IReadOnlyList<NeutralCampDefinition> Camps { get; }

        /// <summary>Raised ground; units only see targets on their own level or below.</summary>
        public IReadOnlyList<ElevationZone> Elevation { get; }

        /// <summary>The map cell by cell (Dota's map file), on top of the obstacles and elevation zones; null without one.</summary>
        public MapTerrain? Terrain { get; }

        /// <summary>Side of a navigation grid cell in map units.</summary>
        public int NavCellSize { get; }

        /// <summary>Obstacles are grown by this many units when building the navigation grid.</summary>
        public int NavClearance { get; }
    }

    /// <summary>
    /// Creeps of one type in a wave. Dota's waves grow and vary (Liquipedia «Lane Creeps»): an entry may start at a wave,
    /// come only every n-th wave from there, join from a match time, and take the place of creeps of another type.
    /// </summary>
    public sealed class WaveEntry
    {
        public WaveEntry(string creepTypeId, int count, int firstWave = 1, int everyWaves = 1, int fromMs = 0, string? replacesCreepTypeId = null)
        {
            CreepTypeId = creepTypeId;
            Count = count;
            FirstWave = firstWave;
            EveryWaves = everyWaves;
            FromMs = fromMs;
            ReplacesCreepTypeId = replacesCreepTypeId;
        }

        public string CreepTypeId { get; }
        public int Count { get; }

        /// <summary>The first wave (1 = the first one) with these creeps.</summary>
        public int FirstWave { get; }

        /// <summary>From <see cref="FirstWave"/> on, every this many waves.</summary>
        public int EveryWaves { get; }

        /// <summary>Only in waves spawned at or after this match time.</summary>
        public int FromMs { get; }

        /// <summary>These creeps replace as many of this type in the wave (Dota's flagbearer replaces a melee creep).</summary>
        public string? ReplacesCreepTypeId { get; }

        /// <summary>Whether the wave with this number (1-based), spawned at this match time, has these creeps.</summary>
        public bool In(int wave, long timeMs) =>
            wave >= FirstWave && (wave - FirstWave) % EveryWaves == 0 && timeMs >= FromMs;
    }

    /// <summary>Creep waves spawned on every lane. Compositions are per team so sandbox/lab rulesets can be asymmetric.</summary>
    public sealed class WaveRules
    {
        public WaveRules(int firstSpawnMs, int intervalMs, IReadOnlyList<WaveEntry> dawnComposition, IReadOnlyList<WaveEntry> duskComposition,
            IReadOnlyList<CreepUpgrade>? superUpgrades = null, CreepUpgradeRules? upgrades = null, IReadOnlyList<CreepUpgrade>? megaUpgrades = null)
        {
            MegaUpgrades = (megaUpgrades ?? Array.Empty<CreepUpgrade>()).ToArray();
            Upgrades = upgrades;
            SuperUpgrades = (superUpgrades ?? Array.Empty<CreepUpgrade>()).ToArray();
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

        /// <summary>Creep replacements for waves on lanes where the enemy barracks are destroyed.</summary>
        public IReadOnlyList<CreepUpgrade> SuperUpgrades { get; }

        /// <summary>Creep replacements on every lane once all the enemy barracks are destroyed (Dota's mega creeps); over the super ones.</summary>
        public IReadOnlyList<CreepUpgrade> MegaUpgrades { get; }

        /// <summary>Dota's periodic creep upgrades; null = none.</summary>
        public CreepUpgradeRules? Upgrades { get; }
    }

    public sealed class CombatRules
    {
        public CombatRules(Fixed armorFactor, bool heroAggro = false, AttackClassTable? attackClasses = null, bool dotaStacking = false,
            int maxAttackSpeed = 0, int illusionBuildingPercent = 0, int summonBuildingPercent = 0, GlyphRules? glyph = null, DotaAggroRules? dotaAggro = null)
        {
            Glyph = glyph;
            DotaAggro = dotaAggro;
            DotaStacking = dotaStacking;
            MaxAttackSpeed = maxAttackSpeed;
            IllusionBuildingPercent = illusionBuildingPercent;
            SummonBuildingPercent = summonBuildingPercent;
            ArmorFactor = armorFactor;
            HeroAggro = heroAggro;
            AttackClasses = attackClasses ?? AttackClassTable.Uniform;
        }

        /// <summary>Damage multiplier = 1 - f·armor / (1 + f·|armor|).</summary>
        public Fixed ArmorFactor { get; }

        /// <summary>
        /// Lane aggro: creeps and towers pick enemy heroes that attack one of their heroes first, then
        /// any other enemy unit before enemy heroes (otherwise simply the nearest attackable enemy).
        /// </summary>
        public bool HeroAggro { get; }

        /// <summary>Attack damage percent by attack class and defense class (decision log D-043).</summary>
        public AttackClassTable AttackClasses { get; }

        /// <summary>
        /// Dota's stacking (docs/DOTA_RULES_AUDIT.md): magic resistance sources multiply (1 − (1 − base) × Π(1 − source)),
        /// evasion sources stack diminishingly (1 − Π(1 − source)), and critical strike sources roll from the strongest
        /// multiplier to the weakest until one procs. Otherwise they add up and only the highest crit chance counts.
        /// </summary>
        public bool DotaStacking { get; }

        /// <summary>The total attack speed cap (Dota: 700, i.e. 100 base + 600); 0 = none.</summary>
        public int MaxAttackSpeed { get; }

        /// <summary>Attack damage percent of illusions and of summons against buildings (Dota: 60 and 90); 0 = by attack class.</summary>
        public int IllusionBuildingPercent { get; }
        public int SummonBuildingPercent { get; }

        /// <summary>Dota's Glyph of Fortification; null = none.</summary>
        public GlyphRules? Glyph { get; }

        /// <summary>Dota's lane aggro for lane creeps and buildings (replaces <see cref="HeroAggro"/>); null = the engine's rule.</summary>
        public DotaAggroRules? DotaAggro { get; }
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
            HeroRules? heroRules = null,
            IReadOnlyList<AbilityDefinition>? abilities = null,
            IReadOnlyList<ItemDefinition>? items = null,
            BossDefinition? boss = null,
            RuneRules? runes = null,
            DayNightRules? dayNight = null,
            DraftRules? draft = null,
            MinibossDefinition? miniboss = null,
            ShrineRules? shrines = null,
            LotusPoolRules? lotusPools = null,
            WatcherRules? watchers = null)
        {
            Watchers = watchers;
            LotusPools = lotusPools;
            Shrines = shrines;
            Miniboss = miniboss;
            DayNight = dayNight;
            Draft = draft;
            Boss = boss;
            Runes = runes;
            Abilities = (abilities ?? Array.Empty<AbilityDefinition>()).ToArray();
            Items = (items ?? Array.Empty<ItemDefinition>()).ToArray();
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

        /// <summary>Dota's Watchers; null without them.</summary>
        public WatcherRules? Watchers { get; }

        /// <summary>Dota's Lotus Pools; null without them.</summary>
        public LotusPoolRules? LotusPools { get; }

        /// <summary>Dota's Shrines of Wisdom; null without them.</summary>
        public ShrineRules? Shrines { get; }

        /// <summary>Dota's Tormentor; null without one.</summary>
        public MinibossDefinition? Miniboss { get; }

        /// <summary>Required when <see cref="Heroes"/> is not empty.</summary>
        public HeroRules? HeroRules { get; }

        public HeroDefinition? FindHero(string id) => Heroes.FirstOrDefault(h => h.Id == id);

        public IReadOnlyList<AbilityDefinition> Abilities { get; }

        public AbilityDefinition? FindAbility(string id) => Abilities.FirstOrDefault(a => a.Id == id);

        public IReadOnlyList<ItemDefinition> Items { get; }

        public ItemDefinition? FindItem(string id) => Items.FirstOrDefault(i => i.Id == id);

        /// <summary>Optional neutral boss objective.</summary>
        public BossDefinition? Boss { get; }

        /// <summary>Optional power-up runes.</summary>
        public RuneRules? Runes { get; }

        /// <summary>Optional day/night cycle; without it, it is always day.</summary>
        public DayNightRules? DayNight { get; }

        /// <summary>Pick/ban format; null when the ruleset does not define drafting.</summary>
        public DraftRules? Draft { get; }

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
            var waveCreeps = new HashSet<string>(Waves.DawnComposition.Concat(Waves.DuskComposition).Select(e => e.CreepTypeId));
            foreach (CreepType creep in CreepTypes)
            {
                ValidateStats(errors, "Creep type '" + creep.Id + "'", creep.Stats);
                // Immobile creep types are allowed (wards, stationary summons) but cannot walk a lane.
                if (creep.Stats.MoveSpeed <= 0 && waveCreeps.Contains(creep.Id))
                    errors.Add("Creep type '" + creep.Id + "' is used in waves and must have positive move speed.");
            }

            foreach (StructureType structure in StructureTypes)
            {
                ValidateStats(errors, "Structure type '" + structure.Id + "'", structure.Stats);
                foreach (string creepTypeId in structure.UpgradesCreepTypeIds ?? Array.Empty<string>())
                {
                    if (FindCreepType(creepTypeId) == null)
                        errors.Add("Structure type '" + structure.Id + "' upgrades unknown creep type '" + creepTypeId + "'.");
                }
            }

            ValidateMap(errors);
            ValidateWaves(errors);
            ValidateHeroes(errors);
            ValidateAbilities(errors);
            ValidateItems(errors);
            if (DayNight != null && (DayNight.DayMs <= 0 || DayNight.NightMs <= 0))
                errors.Add("Day and night phases must both be positive.");
            Draft?.Validate(errors, Heroes);

            if (Runes != null && Runes.Schedules.Count > 0)
            {
                foreach (RuneSchedule schedule in Runes.Schedules)
                {
                    if (schedule.Types.Count == 0 || schedule.Spots.Count == 0)
                        errors.Add("Rune schedule '" + schedule.Id + "' needs at least one rune type and one spot.");
                    if (schedule.IntervalMs < 0 || schedule.FirstSpawnMs < 0 || schedule.UntilMs < 0)
                        errors.Add("Rune schedule '" + schedule.Id + "' has negative timings.");
                    if (schedule.Spots.Any(s => !IsInsideMap(s)))
                        errors.Add("A spot of rune schedule '" + schedule.Id + "' is outside the map.");
                }

                if (Runes.PickupRadius <= 0)
                    errors.Add("Rune rules need a positive pickup radius.");
            }
            else if (Runes != null)
            {
                if (Runes.Types.Count == 0 || Runes.Spots.Count == 0)
                    errors.Add("Rune rules need at least one rune type and one spot.");
                if (Runes.IntervalMs <= 0 || Runes.FirstSpawnMs < 0 || Runes.PickupRadius <= 0)
                    errors.Add("Rune rules need a positive interval and pickup radius.");
                if (Runes.Spots.Any(s => !IsInsideMap(s)))
                    errors.Add("A rune spot is outside the map.");
                ReportDuplicates(errors, "rune type", Runes.Types.Select(t => t.Id));
            }

            if (Boss != null)
            {
                if (FindCreepType(Boss.CreepTypeId) == null)
                    errors.Add("Boss '" + Boss.Id + "' uses unknown creep type '" + Boss.CreepTypeId + "'.");
                if (!IsInsideMap(Boss.Position) || Boss.LeashRadius <= 0)
                    errors.Add("Boss '" + Boss.Id + "' needs a pit inside the map and a positive leash radius.");
                if (Boss.NightPosition is FixedVector2 nightPit && !IsInsideMap(nightPit))
                    errors.Add("Boss '" + Boss.Id + "' needs its night pit inside the map.");
                foreach (BossDrop drop in Boss.Drops.Where(d => FindItem(d.ItemId) == null))
                    errors.Add("Boss '" + Boss.Id + "' drops unknown item '" + drop.ItemId + "'.");
                if (Boss.FirstSpawnMs < 0 || Boss.RespawnMinMs <= 0 || Boss.RespawnMinMs > Boss.RespawnMaxMs)
                    errors.Add("Boss '" + Boss.Id + "' has invalid spawn timings.");
            }

            if (Miniboss != null)
            {
                if (FindCreepType(Miniboss.CreepTypeId) == null)
                    errors.Add("Miniboss '" + Miniboss.Id + "' uses unknown creep type '" + Miniboss.CreepTypeId + "'.");
                if (Miniboss.RewardItemId != null && FindItem(Miniboss.RewardItemId) == null)
                    errors.Add("Miniboss '" + Miniboss.Id + "' gives unknown item '" + Miniboss.RewardItemId + "'.");
                if (!IsInsideMap(Miniboss.DayPosition) || !IsInsideMap(Miniboss.NightPosition))
                    errors.Add("Miniboss '" + Miniboss.Id + "' must stand inside the map.");
                if (Miniboss.RespawnMs <= 0 || Miniboss.ReflectIntervalMs <= 0 || Miniboss.ShiningIntervalMs <= 0 || Miniboss.GrowthIntervalMs <= 0)
                    errors.Add("Miniboss '" + Miniboss.Id + "' needs positive respawn, reflect, burn and growth intervals.");
            }

            if (Shrines != null && (Shrines.Positions.Count == 0 || Shrines.Positions.Any(p => !IsInsideMap(p)) || Shrines.IntervalMs <= 0 ||
                                    Shrines.FirstMs < 0 || Shrines.Radius <= 0 || Shrines.CaptureMs <= 0))
                errors.Add("Shrines of Wisdom need places inside the map, a positive interval, radius and capture time.");
            if (Watchers != null && (Watchers.Positions.Count == 0 || Watchers.Positions.Any(p => !IsInsideMap(p)) || Watchers.Radius <= 0 || Watchers.ChannelMs <= 0 ||
                                     Watchers.OnMs <= 0 || Watchers.OffMs <= 0 || Watchers.DayVision < 0 || Watchers.NightVision < 0))
                errors.Add("Watchers need places inside the map, a positive radius and times.");
            if (LotusPools != null)
            {
                if (FindItem(LotusPools.ItemId) == null)
                    errors.Add("Lotus Pools give unknown item '" + LotusPools.ItemId + "'.");
                if (LotusPools.Positions.Count == 0 || LotusPools.Positions.Any(p => !IsInsideMap(p)) || LotusPools.IntervalMs <= 0 || LotusPools.FirstMs < 0 ||
                    LotusPools.Max <= 0 || LotusPools.Radius <= 0 || LotusPools.FirstPickMs <= 0 || LotusPools.MinPickMs <= 0 ||
                    LotusPools.NextPickPercent <= 0 || LotusPools.NextPickPercent > 100)
                    errors.Add("Lotus Pools need places inside the map, a positive interval, maximum, radius and picking times.");
            }

            return errors;
        }

        private void ValidateItems(List<string> errors)
        {
            ReportDuplicates(errors, "item", Items.Select(i => i.Id));
            foreach (ItemDefinition item in Items)
            {
                string owner = "Item '" + item.Id + "'";
                if (item.Cost < 0)
                    errors.Add(owner + " cannot have a negative cost.");   // free items exist (Dota's observer ward)
                int componentCost = 0;
                foreach (string componentId in item.Components)
                {
                    ItemDefinition? component = FindItem(componentId);
                    if (component == null || componentId == item.Id)
                        errors.Add(owner + " uses unknown component '" + componentId + "'.");
                    else
                        componentCost += component.Cost;
                }

                if (componentCost > item.Cost)
                    errors.Add(owner + " costs less than its components.");
                if (item.ActiveAbilityId != null)
                {
                    AbilityDefinition? active = FindAbility(item.ActiveAbilityId);
                    if (active == null)
                        errors.Add(owner + " uses unknown active ability '" + item.ActiveAbilityId + "'.");
                    else if (active.Targeting == AbilityTargeting.Passive)
                        errors.Add(owner + " active ability cannot be passive.");
                }
            }
        }

        private void ValidateAbilities(List<string> errors)
        {
            ReportDuplicates(errors, "ability", Abilities.Select(a => a.Id));
            foreach (AbilityDefinition ability in Abilities)
            {
                string owner = "Ability '" + ability.Id + "'";
                if (ability.MaxLevel == 0)
                    errors.Add(owner + " needs at least one level.");
                if (ability.Targeting == AbilityTargeting.Passive)
                {
                    if (ability.PassiveModifiers.Count != ability.MaxLevel)
                        errors.Add(owner + " must define one passive modifier per level.");
                    continue;
                }

                if (ability.CooldownMs.Count != ability.MaxLevel || ability.ManaCost.Count != ability.MaxLevel)
                    errors.Add(owner + " must define cooldown and mana cost per level.");
                if (ability.CastRange < 0 || ability.CastPointMs < 0)
                    errors.Add(owner + " has a negative cast range or cast point.");
                if (ability.Effects.Count == 0)
                    errors.Add(owner + " has no effects.");

                foreach (EffectDefinition effect in ability.Effects.SelectMany(e => e.SelfAndDescendants()))
                {
                    bool needsAmounts = effect.Kind == EffectKind.Damage || effect.Kind == EffectKind.Heal ||
                                        effect.Kind == EffectKind.Knockback || effect.Kind == EffectKind.Summon;
                    if (needsAmounts && effect.Amounts.Count == 0)
                        errors.Add(owner + " has a " + effect.Kind + " effect without amounts.");
                    if (effect.Kind == EffectKind.ApplyModifier && effect.Modifiers.Count == 0)
                        errors.Add(owner + " has a modifier effect without modifiers.");
                    if (effect.Kind == EffectKind.Summon && (effect.SummonTypeId == null || FindCreepType(effect.SummonTypeId) == null))
                        errors.Add(owner + " summons unknown creep type '" + effect.SummonTypeId + "'.");
                    if (effect.Kind == EffectKind.Area && effect.Radius <= 0)
                        errors.Add(owner + " has an area effect without a radius.");
                    if (effect.AtPoint && ability.Targeting != AbilityTargeting.Point)
                        errors.Add(owner + " summons at a point but is not point-targeted.");
                    if (effect.Kind == EffectKind.Projectile && (ability.Targeting != AbilityTargeting.Unit || effect.Speed <= 0 || effect.Inner.Count == 0))
                        errors.Add(owner + " has a projectile but is not unit-targeted, or lacks speed or on-hit effects.");
                    if (effect.Kind == EffectKind.Teleport && (ability.Targeting != AbilityTargeting.Unit || !ability.TargetsStructures))
                        errors.Add(owner + " teleports but does not target allied structures.");
                }
            }

            foreach (HeroDefinition hero in Heroes)
            {
                foreach (string abilityId in hero.AbilityIds.Concat(hero.SkillPriority).Distinct())
                {
                    if (FindAbility(abilityId) == null)
                        errors.Add("Hero '" + hero.Id + "' references unknown ability '" + abilityId + "'.");
                }

                foreach (string itemId in hero.ItemBuild.Distinct())
                {
                    if (FindItem(itemId) == null)
                        errors.Add("Hero '" + hero.Id + "' item build references unknown item '" + itemId + "'.");
                }
            }
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
            writer.WriteBool(Combat.HeroAggro);
            Combat.AttackClasses.WriteCanonical(writer);
            writer.WriteBool(Combat.DotaStacking);
            writer.WriteInt64(Combat.MaxAttackSpeed);
            writer.WriteInt64(Combat.IllusionBuildingPercent);
            writer.WriteInt64(Combat.SummonBuildingPercent);
            writer.WriteBool(Combat.Glyph != null);
            if (Combat.Glyph != null)
            {
                GlyphRules glyph = Combat.Glyph;
                foreach (int value in new[] { glyph.CooldownMs, glyph.InitialCooldownMs, glyph.BuildingDurationMs, glyph.CreepDurationMs, glyph.ResetDelayMs })
                    writer.WriteInt64(value);
            }

            writer.WriteBool(Combat.DotaAggro != null);
            if (Combat.DotaAggro != null)
            {
                DotaAggroRules aggro = Combat.DotaAggro;
                foreach (int value in new[] { aggro.DrawRadius, aggro.ForcedMs, aggro.CooldownMs, aggro.LockedUntilMs, aggro.UnlockRadius, aggro.TowerProtectRadius, aggro.TowerThreatRadius })
                    writer.WriteInt64(value);
                writer.WriteString(aggro.UnlockStructureTypeId);
            }

            writer.WriteInt64(CreepTypes.Count);
            foreach (CreepType creep in CreepTypes)
            {
                writer.WriteString(creep.Id);
                creep.Stats.WriteCanonical(writer);
                writer.WriteInt64(creep.Bounty.GoldMin);
                writer.WriteInt64(creep.Bounty.GoldMax);
                writer.WriteInt64(creep.Bounty.Xp);
                writer.WriteInt64(creep.Modifiers.Count);
                foreach (ModifierSpec modifier in creep.Modifiers)
                    modifier.WriteCanonical(writer);
                writer.WriteBool(creep.AreaBounty != null);
                if (creep.AreaBounty != null)
                {
                    writer.WriteInt64(creep.AreaBounty.BaseGold);
                    writer.WriteInt64(creep.AreaBounty.BountyPercent);
                    writer.WriteInt64(creep.AreaBounty.Radius);
                }
            }

            writer.WriteInt64(StructureTypes.Count);
            foreach (StructureType structure in StructureTypes)
                structure.WriteCanonical(writer);

            writer.WriteInt64(Map.Width);
            writer.WriteInt64(Map.Height);
            writer.WriteInt64(Map.Lanes.Count);
            foreach (LaneDefinition lane in Map.Lanes)
            {
                writer.WriteString(lane.Id);
                writer.WriteInt64(lane.Waypoints.Count);
                foreach (FixedVector2 point in lane.Waypoints)
                    writer.WriteVector(point);
                writer.WriteInt64(lane.DuskWaypoints?.Count ?? -1);
                foreach (FixedVector2 point in lane.DuskWaypoints ?? Array.Empty<FixedVector2>())
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
                writer.WriteString(placement.BarracksLane ?? "");
            }

            writer.WriteInt64(Map.Obstacles.Count);
            foreach (ObstacleDefinition obstacle in Map.Obstacles)
                obstacle.WriteCanonical(writer);
            writer.WriteInt64(Map.NavCellSize);
            writer.WriteInt64(Map.NavClearance);
            writer.WriteInt64(Map.Elevation.Count);
            foreach (ElevationZone zone in Map.Elevation)
                zone.WriteCanonical(writer);
            writer.WriteBool(Map.Terrain != null);
            Map.Terrain?.WriteCanonical(writer);

            writer.WriteInt64(Map.Camps.Count);
            foreach (NeutralCampDefinition camp in Map.Camps)
                camp.WriteCanonical(writer);

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
            writer.WriteInt64(Abilities.Count);
            foreach (AbilityDefinition ability in Abilities)
                ability.WriteCanonical(writer);
            writer.WriteInt64(Items.Count);
            foreach (ItemDefinition item in Items)
                item.WriteCanonical(writer);
            writer.WriteBool(Boss != null);
            Boss?.WriteCanonical(writer);
            writer.WriteBool(Runes != null);
            Runes?.WriteCanonical(writer);
            writer.WriteBool(Miniboss != null);
            Miniboss?.WriteCanonical(writer);
            writer.WriteBool(Shrines != null);
            Shrines?.WriteCanonical(writer);
            writer.WriteBool(LotusPools != null);
            LotusPools?.WriteCanonical(writer);
            writer.WriteBool(Watchers != null);
            Watchers?.WriteCanonical(writer);
            writer.WriteBool(DayNight != null);
            DayNight?.WriteCanonical(writer);
            writer.WriteBool(Draft != null);
            Draft?.WriteCanonical(writer);

            writer.WriteInt64(Waves.FirstSpawnMs);
            writer.WriteInt64(Waves.IntervalMs);
            WriteComposition(writer, Waves.DawnComposition);
            WriteComposition(writer, Waves.DuskComposition);
            writer.WriteBool(Waves.Upgrades != null);
            if (Waves.Upgrades != null)
            {
                writer.WriteInt64(Waves.Upgrades.IntervalMs);
                writer.WriteInt64(Waves.Upgrades.MaxStatCycles);
                writer.WriteInt64(Waves.Upgrades.Steps.Count);
                foreach (CreepUpgradeStep step in Waves.Upgrades.Steps)
                {
                    writer.WriteString(step.CreepTypeId);
                    foreach (int value in new[] { step.Health, step.Damage, step.GoldHundredths, step.Xp, step.AuraMagicResistPerCycle, step.AuraMaxCycles, step.AuraRadius })
                        writer.WriteInt64(value);
                }
            }

            writer.WriteInt64(Waves.SuperUpgrades.Count);
            foreach (CreepUpgrade upgrade in Waves.SuperUpgrades)
            {
                writer.WriteString(upgrade.FromCreepTypeId);
                writer.WriteString(upgrade.ToCreepTypeId);
            }

            writer.WriteInt64(Waves.MegaUpgrades.Count);
            foreach (CreepUpgrade upgrade in Waves.MegaUpgrades)
            {
                writer.WriteString(upgrade.FromCreepTypeId);
                writer.WriteString(upgrade.ToCreepTypeId);
            }
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
                if (lane.DuskWaypoints != null && (lane.DuskWaypoints.Count < 2 || lane.DuskWaypoints.Any(p => !IsInsideMap(p))))
                    errors.Add("Lane '" + lane.Id + "' needs a Dusk path of at least two waypoints inside the map.");
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

            ReportDuplicates(errors, "elevation zone", Map.Elevation.Select(z => z.Id));
            foreach (ElevationZone zone in Map.Elevation)
            {
                if (zone.Level < 0 || zone.Radius <= 0 || !IsInsideMap(zone.Center))
                    errors.Add("Elevation zone '" + zone.Id + "' needs a non-negative level, a positive radius and a center inside the map.");
            }

            ReportDuplicates(errors, "camp", Map.Camps.Select(c => c.Id));
            foreach (NeutralCampDefinition camp in Map.Camps)
            {
                foreach (WaveEntry entry in camp.Creeps.Concat(camp.Variants.SelectMany(v => v)).Concat(camp.Evolution?.Stages.SelectMany(s => s) ?? Enumerable.Empty<WaveEntry>()))
                {
                    if (FindCreepType(entry.CreepTypeId) == null || entry.Count <= 0)
                        errors.Add("Camp '" + camp.Id + "' uses unknown creep type '" + entry.CreepTypeId + "' or a non-positive count.");
                }

                if (camp.Evolution != null && (camp.Evolution.IntervalMs <= 0 || camp.Evolution.Stages.Count == 0 || camp.Evolution.Stages.Any(s => s.Count == 0)))
                    errors.Add("Camp '" + camp.Id + "' needs a positive evolution interval and creeps in every stage.");
                if (camp.Variants.Any(v => v.Count == 0))
                    errors.Add("Camp '" + camp.Id + "' has a camp type without creeps.");

                if (!IsInsideMap(camp.Position) || Map.Obstacles.Any(o => o.Contains(camp.Position, 0)))
                    errors.Add("Camp '" + camp.Id + "' must be inside the map and outside obstacles.");
                if (camp.Creeps.Count == 0 || camp.RespawnMs <= 0 || camp.LeashRadius <= 0 || camp.FirstSpawnMs < 0)
                    errors.Add("Camp '" + camp.Id + "' needs creeps, a positive respawn time and leash radius.");
            }

            if (Map.NavCellSize <= 0 || Map.NavClearance < 0)
                errors.Add("Navigation cell size must be positive and clearance non-negative.");
            MapTerrain? terrain = Map.Terrain;
            if (terrain != null && Map.NavCellSize > 0 &&
                (!terrain.IsComplete || terrain.Columns != (Map.Width + Map.NavCellSize - 1) / Map.NavCellSize ||
                 terrain.Rows != (Map.Height + Map.NavCellSize - 1) / Map.NavCellSize || terrain.TreeSize <= 0 ||
                 terrain.Trees.Any(t => !IsInsideMap(t))))
                errors.Add("The map terrain needs one value per navigation cell of the map in each layer, a positive tree size and trees inside the map.");
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
                if (entry.FirstWave < 1 || entry.EveryWaves < 1 || entry.FromMs < 0)
                    errors.Add("Wave entry '" + entry.CreepTypeId + "' needs a first wave and a period of at least 1 and a non-negative start time.");
                if (entry.ReplacesCreepTypeId != null && FindCreepType(entry.ReplacesCreepTypeId) == null)
                    errors.Add("Wave entry '" + entry.CreepTypeId + "' replaces unknown creep type '" + entry.ReplacesCreepTypeId + "'.");
            }

            foreach (CreepUpgrade upgrade in Waves.SuperUpgrades.Concat(Waves.MegaUpgrades))
            {
                foreach (string id in new[] { upgrade.FromCreepTypeId, upgrade.ToCreepTypeId })
                {
                    CreepType? type = FindCreepType(id);
                    if (type == null)
                        errors.Add("Super creep upgrade uses unknown creep type '" + id + "'.");
                    else if (type.Stats.MoveSpeed <= 0)
                        errors.Add("Super creep upgrade type '" + id + "' must be able to walk a lane.");
                }
            }

            foreach (StructurePlacement barracks in Map.Structures.Where(s => s.BarracksLane != null))
            {
                if (Map.Lanes.All(l => l.Id != barracks.BarracksLane))
                    errors.Add("Barracks '" + barracks.Id + "' refers to unknown lane '" + barracks.BarracksLane + "'.");
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
                writer.WriteInt64(entry.FirstWave);
                writer.WriteInt64(entry.EveryWaves);
                writer.WriteInt64(entry.FromMs);
                writer.WriteString(entry.ReplacesCreepTypeId ?? "");
            }
        }
    }
}
