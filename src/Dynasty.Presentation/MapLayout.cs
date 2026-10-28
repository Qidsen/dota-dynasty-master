using System.Collections.Generic;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.Presentation
{
    /// <summary>
    /// The static map of a ruleset as plain numbers for drawing (terrain, trees, lanes, high ground,
    /// fountains, camps, boss pit). Map coordinates, the same as <see cref="UnitFrame"/> positions.
    /// </summary>
    public sealed class MapLayout
    {
        private MapLayout(int width, int height, List<MapObstacle> obstacles, List<MapLane> lanes, List<MapCircle> plateaus,
            List<MapFountain> fountains, List<MapPoint> camps, MapPoint? bossPit, List<MapMarker> markers, MapGround? ground)
        {
            Ground = ground;
            Markers = markers;
            Width = width;
            Height = height;
            Obstacles = obstacles;
            Lanes = lanes;
            Plateaus = plateaus;
            Fountains = fountains;
            Camps = camps;
            BossPit = bossPit;
        }

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<MapObstacle> Obstacles { get; }
        public IReadOnlyList<MapLane> Lanes { get; }

        /// <summary>High-ground zones (radius in map units, level above the low ground).</summary>
        public IReadOnlyList<MapCircle> Plateaus { get; }

        public IReadOnlyList<MapFountain> Fountains { get; }
        public IReadOnlyList<MapPoint> Camps { get; }
        public MapPoint? BossPit { get; }

        /// <summary>The map cell by cell (Dota's terrain: walkable, levels, trees); null for a map of shapes.</summary>
        public MapGround? Ground { get; }

        /// <summary>Map objects that are places, not units: boss and Tormentor pits, shrines, lotus pools, watchers, Twin Gates.</summary>
        public IReadOnlyList<MapMarker> Markers { get; }

        public static MapLayout From(RulesetContent ruleset)
        {
            MapDefinition map = ruleset.Map;
            var obstacles = new List<MapObstacle>(map.Obstacles.Count);
            foreach (ObstacleDefinition o in map.Obstacles)
                obstacles.Add(new MapObstacle(o.Shape == ObstacleShape.Circle, F(o.Center.X), F(o.Center.Y), o.Radius, F(o.Min.X), F(o.Min.Y), F(o.Max.X), F(o.Max.Y)));

            var lanes = new List<MapLane>(map.Lanes.Count);
            foreach (LaneDefinition lane in map.Lanes)
            {
                var points = new List<MapPoint>(lane.Waypoints.Count);
                foreach (FixedVector2 p in lane.Waypoints)
                    points.Add(P(p));
                lanes.Add(new MapLane(lane.Id, points));
            }

            var plateaus = new List<MapCircle>(map.Elevation.Count);
            foreach (ElevationZone zone in map.Elevation)
                plateaus.Add(new MapCircle(F(zone.Center.X), F(zone.Center.Y), zone.Radius, zone.Level));

            var fountains = new List<MapFountain>(map.Fountains.Count);
            foreach (FountainPlacement f in map.Fountains)
                fountains.Add(new MapFountain(f.Team, F(f.Position.X), F(f.Position.Y), f.Radius));

            var camps = new List<MapPoint>(map.Camps.Count);
            foreach (NeutralCampDefinition camp in map.Camps)
                camps.Add(P(camp.Position));

            MapPoint? boss = ruleset.Boss != null ? P(ruleset.Boss.Position) : (MapPoint?)null;
            var markers = new List<MapMarker>();
            void Mark(MapMarkerKind kind, IEnumerable<FixedVector2> positions)
            {
                foreach (FixedVector2 p in positions)
                    markers.Add(new MapMarker(kind, F(p.X), F(p.Y)));
            }

            if (ruleset.Boss != null)
                Mark(MapMarkerKind.BossPit, ruleset.Boss.NightPosition is FixedVector2 night ? new[] { ruleset.Boss.Position, night } : new[] { ruleset.Boss.Position });
            if (ruleset.Miniboss != null)
                Mark(MapMarkerKind.Tormentor, new[] { ruleset.Miniboss.DayPosition, ruleset.Miniboss.NightPosition });
            Mark(MapMarkerKind.Shrine, ruleset.Shrines?.Positions ?? new FixedVector2[0]);
            Mark(MapMarkerKind.LotusPool, ruleset.LotusPools?.Positions ?? new FixedVector2[0]);
            Mark(MapMarkerKind.Watcher, ruleset.Watchers?.Positions ?? new FixedVector2[0]);
            Mark(MapMarkerKind.TwinGate, ruleset.TwinGates?.Positions ?? new FixedVector2[0]);
            MapGround? ground = map.Terrain != null ? MapGround.From(map.Terrain, map.NavCellSize) : null;
            return new MapLayout(map.Width, map.Height, obstacles, lanes, plateaus, fountains, camps, boss, markers, ground);
        }

        private static float F(Fixed value) => (float)value.ToDouble();

        private static MapPoint P(FixedVector2 p) => new MapPoint(F(p.X), F(p.Y));
    }

    /// <summary>A map given cell by cell, as plain numbers for the views: walkable cells, ground levels, trees.</summary>
    public sealed class MapGround
    {
        private MapGround(int columns, int rows, int cellSize, bool[] walkable, int[] levels, List<MapPoint> trees, int treeSize)
        {
            Columns = columns;
            Rows = rows;
            CellSize = cellSize;
            Walkable = walkable;
            Levels = levels;
            Trees = trees;
            TreeSize = treeSize;
        }

        public int Columns { get; }
        public int Rows { get; }

        /// <summary>Side of a cell in map units; cells go row by row from y = 0.</summary>
        public int CellSize { get; }

        public IReadOnlyList<bool> Walkable { get; }
        public IReadOnlyList<int> Levels { get; }
        public IReadOnlyList<MapPoint> Trees { get; }

        /// <summary>Side of the square each tree covers.</summary>
        public int TreeSize { get; }

        public static MapGround From(MapTerrain terrain, int cellSize)
        {
            var walkable = new bool[terrain.CellCount];
            var levels = new int[terrain.CellCount];
            for (int i = 0; i < terrain.CellCount; i++)
            {
                walkable[i] = terrain.IsWalkable(i);
                levels[i] = terrain.LevelOf(i);
            }

            var trees = new List<MapPoint>(terrain.Trees.Count);
            foreach (FixedVector2 tree in terrain.Trees)
                trees.Add(new MapPoint((float)tree.X.ToDouble(), (float)tree.Y.ToDouble()));
            return new MapGround(terrain.Columns, terrain.Rows, cellSize, walkable, levels, trees, terrain.TreeSize);
        }

        /// <summary>The cell index at a map point, clamped to the map.</summary>
        public int IndexAt(float x, float y)
        {
            int column = System.Math.Min(Columns - 1, System.Math.Max(0, (int)(x / CellSize)));
            int row = System.Math.Min(Rows - 1, System.Math.Max(0, (int)(y / CellSize)));
            return row * Columns + column;
        }

        public int LevelAt(float x, float y) => Levels[IndexAt(x, y)];
    }

    /// <summary>What a <see cref="MapMarker"/> marks.</summary>
    public enum MapMarkerKind
    {
        BossPit,
        Tormentor,
        Shrine,
        LotusPool,
        Watcher,
        TwinGate,
    }

    public readonly struct MapMarker
    {
        public MapMarker(MapMarkerKind kind, float x, float y)
        {
            Kind = kind;
            X = x;
            Y = y;
        }

        public MapMarkerKind Kind { get; }
        public float X { get; }
        public float Y { get; }
    }

    public readonly struct MapPoint
    {
        public MapPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
    }

    public sealed class MapObstacle
    {
        public MapObstacle(bool isCircle, float centerX, float centerY, float radius, float minX, float minY, float maxX, float maxY)
        {
            IsCircle = isCircle;
            CenterX = centerX;
            CenterY = centerY;
            Radius = radius;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public bool IsCircle { get; }
        public float CenterX { get; }
        public float CenterY { get; }
        public float Radius { get; }
        public float MinX { get; }
        public float MinY { get; }
        public float MaxX { get; }
        public float MaxY { get; }
    }

    public sealed class MapLane
    {
        public MapLane(string id, IReadOnlyList<MapPoint> points)
        {
            Id = id;
            Points = points;
        }

        public string Id { get; }
        public IReadOnlyList<MapPoint> Points { get; }
    }

    public sealed class MapCircle
    {
        public MapCircle(float x, float y, float radius, int level)
        {
            X = x;
            Y = y;
            Radius = radius;
            Level = level;
        }

        public float X { get; }
        public float Y { get; }
        public float Radius { get; }
        public int Level { get; }
    }

    public sealed class MapFountain
    {
        public MapFountain(Team team, float x, float y, float radius)
        {
            Team = team;
            X = x;
            Y = y;
            Radius = radius;
        }

        public Team Team { get; }
        public float X { get; }
        public float Y { get; }
        public float Radius { get; }
    }
}
