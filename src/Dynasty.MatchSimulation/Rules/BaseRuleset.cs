using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// The original built-in ruleset "dynasty.base": a 10000×10000 map, point-symmetric through
    /// its center, with three lanes, three towers per lane, two base towers and an ancient per team.
    /// Numbers are original; they are inspired by the genre, not copied from any game.
    /// </summary>
    public static class BaseRuleset
    {
        public const string Id = "dynasty.base";
        public const int Version = 1;

        private static readonly FixedVector2 Center = FixedVector2.FromInts(5000, 5000);

        public static RulesetContent Create() => Build(Id, Version, StandardWave(), StandardWave());

        /// <summary>
        /// Match Laboratory scenario: Dawn waves also bring two siege creeps per lane. The
        /// asymmetry is in the rules, not in the outcome — Dawn still has to win the fights.
        /// </summary>
        public static RulesetContent CreateDawnSiegeLab()
        {
            var dawn = StandardWave().ToList();
            dawn.Add(new WaveEntry("siege", 2));
            return Build("dynasty.lab.dawn-siege", 1, dawn, StandardWave());
        }

        private static IReadOnlyList<WaveEntry> StandardWave() => new[]
        {
            new WaveEntry("melee", 3),
            new WaveEntry("ranged", 1),
        };

        private static RulesetContent Build(string id, int version, IReadOnlyList<WaveEntry> dawnWave, IReadOnlyList<WaveEntry> duskWave)
        {
            var creeps = new[]
            {
                new CreepType("melee", Stats(hp: 550, armor: 2, speed: 325, dmgMin: 19, dmgMax: 23, range: 100, intervalMs: 1000, pointMs: 500, acquisition: 500, radius: 16), new Bounty(38, 46, 57)),
                new CreepType("ranged", Stats(hp: 300, armor: 0, speed: 325, dmgMin: 21, dmgMax: 26, range: 500, intervalMs: 1000, pointMs: 300, acquisition: 500, radius: 16), new Bounty(45, 52, 69)),
                new CreepType("siege", Stats(hp: 900, armor: 0, speed: 325, dmgMin: 140, dmgMax: 170, range: 690, intervalMs: 2700, pointMs: 700, acquisition: 800, radius: 24), new Bounty(70, 80, 88)),
                new CreepType("melee_super", Stats(hp: 700, armor: 3, speed: 325, dmgMin: 36, dmgMax: 44, range: 100, intervalMs: 1000, pointMs: 500, acquisition: 500, radius: 16), new Bounty(20, 26, 30)),
                new CreepType("ranged_super", Stats(hp: 475, armor: 1, speed: 325, dmgMin: 40, dmgMax: 46, range: 500, intervalMs: 1000, pointMs: 300, acquisition: 500, radius: 16), new Bounty(20, 26, 30)),
            }.Concat(BaseContent.SummonTypes).ToArray();

            var structureTypes = new[]
            {
                new StructureType("tower.t1", Stats(hp: 1800, armor: 12, speed: 0, dmgMin: 100, dmgMax: 120, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("tower.t2", Stats(hp: 2500, armor: 14, speed: 0, dmgMin: 120, dmgMax: 140, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("tower.t3", Stats(hp: 2500, armor: 16, speed: 0, dmgMin: 140, dmgMax: 160, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("tower.t4", Stats(hp: 2600, armor: 18, speed: 0, dmgMin: 140, dmgMax: 160, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("ancient", Stats(hp: 4500, armor: 15, speed: 0, dmgMin: 0, dmgMax: 0, range: 0, intervalMs: 1000, pointMs: 0, acquisition: 0, radius: 200)),
                new StructureType("barracks", Stats(hp: 1500, armor: 10, speed: 0, dmgMin: 0, dmgMax: 0, range: 0, intervalMs: 1000, pointMs: 0, acquisition: 0, radius: 120)),
            };

            var lanes = new[]
            {
                new LaneDefinition("top", Points((1300, 2200), (1000, 6000), (1200, 8800), (4000, 9000), (7800, 8700))),
                new LaneDefinition("mid", Points((2000, 2000), (5000, 5000), (8000, 8000))),
                new LaneDefinition("bot", Points((2200, 1300), (6000, 1000), (8800, 1200), (9000, 4000), (8700, 7800))),
            };

            var dawnStructures = new List<StructurePlacement>();
            foreach ((string lane, int x, int y, string tier) in new[]
            {
                ("top", 1050, 5800, "t1"), ("top", 1150, 4200, "t2"), ("top", 1250, 2700, "t3"),
                ("mid", 4200, 4200, "t1"), ("mid", 3300, 3300, "t2"), ("mid", 2400, 2400, "t3"),
                ("bot", 5800, 1050, "t1"), ("bot", 4200, 1150, "t2"), ("bot", 2700, 1250, "t3"),
            })
            {
                string[] protectors = tier == "t1"
                    ? Array.Empty<string>()
                    : new[] { "dawn." + lane + "." + (tier == "t2" ? "t1" : "t2") };
                dawnStructures.Add(Placement("dawn." + lane + "." + tier, "tower." + tier, x, y, false, protectors, VulnerabilityRule.AnyProtectorDestroyed));
            }

            // Barracks sit just behind each lane's T3; losing them upgrades the enemy waves on that lane.
            foreach ((string lane, int x, int y) in new[] { ("top", 1550, 2450), ("mid", 2100, 2250), ("bot", 2450, 1550) })
                dawnStructures.Add(new StructurePlacement("dawn." + lane + ".barracks", Team.Dawn, "barracks", FixedVector2.FromInts(x, y), false,
                    new[] { "dawn." + lane + ".t3" }, VulnerabilityRule.AnyProtectorDestroyed, barracksLane: lane));

            string[] laneT3s = { "dawn.top.t3", "dawn.mid.t3", "dawn.bot.t3" };
            dawnStructures.Add(Placement("dawn.base.t4a", "tower.t4", 1700, 1350, false, laneT3s, VulnerabilityRule.AnyProtectorDestroyed));
            dawnStructures.Add(Placement("dawn.base.t4b", "tower.t4", 1350, 1700, false, laneT3s, VulnerabilityRule.AnyProtectorDestroyed));
            dawnStructures.Add(Placement("dawn.ancient", "ancient", 1200, 1200, true, new[] { "dawn.base.t4a", "dawn.base.t4b" }, VulnerabilityRule.AllProtectorsDestroyed));

            var structures = dawnStructures.Concat(dawnStructures.Select(MirrorToDusk)).ToList();

            // Tree clumps in the jungle between lanes; mirrored so both halves are identical.
            var dawnObstacles = new[]
            {
                Obstacle("jungle.dawn.1", 3500, 2000, 300),
                Obstacle("jungle.dawn.2", 5500, 2600, 400),
                Obstacle("jungle.dawn.3", 2000, 3500, 300),
                Obstacle("jungle.dawn.4", 2600, 5500, 400),
                Obstacle("jungle.dawn.5", 4000, 3000, 250),
                Obstacle("jungle.dawn.6", 3000, 4000, 250),
            };
            var obstacles = dawnObstacles
                .Concat(dawnObstacles.Select(o => ObstacleDefinition.Circle(o.Id.Replace(".dawn.", ".dusk."), o.Center.ReflectThrough(Center), o.Radius)))
                .ToList();

            return new RulesetContent(
                id,
                version,
                ticksPerSecond: 30,
                combat: new CombatRules(Fixed.FromRatio(6, 100)),
                creepTypes: creeps,
                structureTypes: structureTypes,
                map: new MapDefinition(10000, 10000, lanes, structures, obstacles, fountains: new[]
                {
                    new FountainPlacement(Team.Dawn, FixedVector2.FromInts(600, 600), 500),
                    new FountainPlacement(Team.Dusk, FixedVector2.FromInts(9400, 9400), 500),
                }, camps: Camps(), elevation: new[]
                {
                    // Base plateaus up to and including the T3 towers; lanes climb them implicitly.
                    new ElevationZone("plateau.dawn", FixedVector2.FromInts(1500, 1500), 1700, 1),
                    new ElevationZone("plateau.dusk", FixedVector2.FromInts(8500, 8500), 1700, 1),
                }),
                waves: new WaveRules(firstSpawnMs: 0, intervalMs: 30000, dawnWave, duskWave, new[]
                {
                    new CreepUpgrade("melee", "melee_super"),
                    new CreepUpgrade("ranged", "ranged_super"),
                }),
                heroes: BaseContent.Heroes,
                heroRules: BaseContent.Rules,
                abilities: BaseContent.Abilities,
                items: BaseContent.Items,
                boss: BaseContent.Boss,
                runes: BaseContent.Runes,
                dayNight: new DayNightRules(dayMs: 300000, nightMs: 300000));
        }

        private static StructurePlacement MirrorToDusk(StructurePlacement dawn) => new StructurePlacement(
            ToDusk(dawn.Id),
            Team.Dusk,
            dawn.StructureTypeId,
            dawn.Position.ReflectThrough(Center),
            dawn.IsAncient,
            dawn.ProtectedBy.Select(ToDusk).ToArray(),
            dawn.Vulnerability,
            // Point reflection maps the top lane onto the bot lane.
            dawn.BarracksLane == "top" ? "bot" : dawn.BarracksLane == "bot" ? "top" : dawn.BarracksLane);

        /// <summary>
        /// Dusk ID of a mirrored Dawn placement. Point reflection maps the Dawn top lane onto the
        /// bot lane (and vice versa), so the lane token must swap to keep IDs truthful.
        /// </summary>
        private static string ToDusk(string dawnId)
        {
            string rest = dawnId.Substring("dawn".Length);
            if (rest.StartsWith(".top."))
                rest = ".bot." + rest.Substring(".top.".Length);
            else if (rest.StartsWith(".bot."))
                rest = ".top." + rest.Substring(".bot.".Length);
            return "dusk" + rest;
        }

        private static StructurePlacement Placement(string id, string type, int x, int y, bool isAncient, string[] protectedBy, VulnerabilityRule rule) =>
            new StructurePlacement(id, Team.Dawn, type, FixedVector2.FromInts(x, y), isAncient, protectedBy, rule);

        /// <summary>Four camps per half (two small, two large), mirrored through the map center.</summary>
        private static IReadOnlyList<NeutralCampDefinition> Camps()
        {
            WaveEntry[] small = { new WaveEntry("jungle_small", 2) };
            WaveEntry[] large = { new WaveEntry("jungle_large", 1), new WaveEntry("jungle_small", 1) };
            var dawnHalf = new[]
            {
                new NeutralCampDefinition("camp.dawn.small.1", FixedVector2.FromInts(4500, 1900), small, 30000, 60000, 600),
                new NeutralCampDefinition("camp.dawn.small.2", FixedVector2.FromInts(1900, 4500), small, 30000, 60000, 600),
                new NeutralCampDefinition("camp.dawn.large.1", FixedVector2.FromInts(4300, 2700), large, 30000, 60000, 600),
                new NeutralCampDefinition("camp.dawn.large.2", FixedVector2.FromInts(2700, 4300), large, 30000, 60000, 600),
            };
            return dawnHalf
                .Concat(dawnHalf.Select(c => new NeutralCampDefinition(c.Id.Replace(".dawn.", ".dusk."), c.Position.ReflectThrough(Center), c.Creeps, c.FirstSpawnMs, c.RespawnMs, c.LeashRadius)))
                .ToArray();
        }

        private static ObstacleDefinition Obstacle(string id, int x, int y, int radius) =>
            ObstacleDefinition.Circle(id, FixedVector2.FromInts(x, y), radius);

        private static FixedVector2[] Points(params (int X, int Y)[] points) =>
            points.Select(p => FixedVector2.FromInts(p.X, p.Y)).ToArray();

        private static UnitStats Stats(int hp, int armor, int speed, int dmgMin, int dmgMax, int range, int intervalMs, int pointMs, int acquisition, int radius) =>
            new UnitStats(hp, Fixed.FromInt(armor), speed, dmgMin, dmgMax, range, intervalMs, pointMs, acquisition, radius,
                visionRange: speed == 0 ? 900 : 800,
                trueSightRange: speed == 0 && dmgMax > 0 ? 700 : 0,
                // Ranged attackers (towers, ranged and siege creeps) fire projectiles; melee hits land at the attack point.
                projectileSpeed: range >= 400 ? (speed == 0 ? 750 : 900) : 0,
                nightVisionRange: speed == 0 ? -1 : 600);
    }
}
