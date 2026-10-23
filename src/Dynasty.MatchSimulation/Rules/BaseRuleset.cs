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
            }.Concat(BaseContent.SummonTypes).ToArray();

            var structureTypes = new[]
            {
                new StructureType("tower.t1", Stats(hp: 1800, armor: 12, speed: 0, dmgMin: 100, dmgMax: 120, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("tower.t2", Stats(hp: 2500, armor: 14, speed: 0, dmgMin: 120, dmgMax: 140, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("tower.t3", Stats(hp: 2500, armor: 16, speed: 0, dmgMin: 140, dmgMax: 160, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("tower.t4", Stats(hp: 2600, armor: 18, speed: 0, dmgMin: 140, dmgMax: 160, range: 700, intervalMs: 1000, pointMs: 300, acquisition: 700, radius: 100)),
                new StructureType("ancient", Stats(hp: 4500, armor: 15, speed: 0, dmgMin: 0, dmgMax: 0, range: 0, intervalMs: 1000, pointMs: 0, acquisition: 0, radius: 200)),
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
                }),
                waves: new WaveRules(firstSpawnMs: 0, intervalMs: 30000, dawnWave, duskWave),
                heroes: BaseContent.Heroes,
                heroRules: BaseContent.Rules,
                abilities: BaseContent.Abilities,
                items: BaseContent.Items);
        }

        private static StructurePlacement MirrorToDusk(StructurePlacement dawn) => new StructurePlacement(
            ToDusk(dawn.Id),
            Team.Dusk,
            dawn.StructureTypeId,
            dawn.Position.ReflectThrough(Center),
            dawn.IsAncient,
            dawn.ProtectedBy.Select(ToDusk).ToArray(),
            dawn.Vulnerability);

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

        private static ObstacleDefinition Obstacle(string id, int x, int y, int radius) =>
            ObstacleDefinition.Circle(id, FixedVector2.FromInts(x, y), radius);

        private static FixedVector2[] Points(params (int X, int Y)[] points) =>
            points.Select(p => FixedVector2.FromInts(p.X, p.Y)).ToArray();

        private static UnitStats Stats(int hp, int armor, int speed, int dmgMin, int dmgMax, int range, int intervalMs, int pointMs, int acquisition, int radius) =>
            new UnitStats(hp, Fixed.FromInt(armor), speed, dmgMin, dmgMax, range, intervalMs, pointMs, acquisition, radius,
                visionRange: speed == 0 ? 900 : 800,
                trueSightRange: speed == 0 && dmgMax > 0 ? 700 : 0);
    }
}
