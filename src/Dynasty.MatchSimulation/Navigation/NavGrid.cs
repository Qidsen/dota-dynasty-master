using System.Collections.Generic;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Navigation
{
    /// <summary>
    /// Square-cell walkability grid built from map obstacles. A cell is walkable when its center
    /// is outside every obstacle grown by the clearance. Immutable once built.
    /// On a map given cell by cell (<see cref="MapTerrain"/>) the cells come from it, trees blocking theirs.
    /// </summary>
    public sealed class NavGrid
    {
        private readonly bool[] _walkable;

        // Ground level of each cell on a sight grid of a terrain with levels: a line is not clear across
        // ground higher than the one looking. Null otherwise.
        private readonly int[]? _levels;

        private NavGrid(int columns, int rows, int cellSize, bool[] walkable, int[]? levels = null)
        {
            Columns = columns;
            Rows = rows;
            CellSize = cellSize;
            _walkable = walkable;
            _levels = levels;
        }

        public int Columns { get; }
        public int Rows { get; }
        public int CellSize { get; }

        public static NavGrid FromMap(MapDefinition map) =>
            map.Terrain == null
                ? Build(map.Width, map.Height, map.NavCellSize, map.Obstacles, map.NavClearance)
                : FromTerrain(map, map.Terrain, sight: false);

        /// <summary>What blocks sight: obstacles without clearance; on a terrain, its vision blockers and trees.</summary>
        public static NavGrid SightOf(MapDefinition map) =>
            map.Terrain == null
                ? Build(map.Width, map.Height, map.NavCellSize, map.Obstacles, clearance: 0)
                : FromTerrain(map, map.Terrain, sight: true);

        private static NavGrid FromTerrain(MapDefinition map, MapTerrain terrain, bool sight)
        {
            int count = terrain.CellCount;
            var open = new bool[count];
            int[]? levels = sight && terrain.HasLevels ? new int[count] : null;
            var grid = new NavGrid(terrain.Columns, terrain.Rows, map.NavCellSize, open, levels);
            int clearance = sight ? 0 : map.NavClearance;
            for (int i = 0; i < count; i++)
            {
                open[i] = sight ? !terrain.BlocksSight(i) : terrain.IsWalkable(i);
                if (levels != null)
                    levels[i] = terrain.LevelOf(i);
                if (!open[i] || map.Obstacles.Count == 0)
                    continue;
                FixedVector2 center = grid.CenterOf(i);
                foreach (ObstacleDefinition obstacle in map.Obstacles)
                {
                    if (obstacle.Contains(center, clearance))
                    {
                        open[i] = false;
                        break;
                    }
                }
            }

            // A cell is under a tree when its center is inside the tree's square.
            int half = terrain.TreeSize / 2;
            int cell = map.NavCellSize;
            foreach (FixedVector2 tree in terrain.Trees)
            {
                int x = tree.X.ToIntFloor();
                int y = tree.Y.ToIntFloor();
                for (int row = System.Math.Max(0, (y - half) / cell); row < terrain.Rows && row * cell + cell / 2 < y + half; row++)
                for (int column = System.Math.Max(0, (x - half) / cell); column < terrain.Columns && column * cell + cell / 2 < x + half; column++)
                {
                    if (column * cell + cell / 2 > x - half && row * cell + cell / 2 > y - half)
                        open[row * terrain.Columns + column] = false;
                }
            }

            return grid;
        }

        public static NavGrid Build(int width, int height, int cellSize, IReadOnlyList<ObstacleDefinition> obstacles, int clearance)
        {
            int columns = (width + cellSize - 1) / cellSize;
            int rows = (height + cellSize - 1) / cellSize;
            var walkable = new bool[columns * rows];
            var grid = new NavGrid(columns, rows, cellSize, walkable);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    FixedVector2 center = grid.CenterOf(column, row);
                    bool free = true;
                    foreach (ObstacleDefinition obstacle in obstacles)
                    {
                        if (obstacle.Contains(center, clearance))
                        {
                            free = false;
                            break;
                        }
                    }

                    walkable[row * columns + column] = free;
                }
            }

            return grid;
        }

        public bool IsWalkable(FixedVector2 point)
        {
            int column = point.X.ToIntFloor() / CellSize;
            int row = point.Y.ToIntFloor() / CellSize;
            return point.X >= Fixed.Zero && point.Y >= Fixed.Zero && IsWalkableCell(column, row);
        }

        /// <summary>Samples the segment every quarter cell; true when every sample is walkable.</summary>
        public bool HasLineOfSight(FixedVector2 from, FixedVector2 to) => SamplesWalkable(from, to, 4);

        /// <summary>
        /// Coarser than <see cref="HasLineOfSight"/> (hot path for vision): samples every half cell.
        /// </summary>
        public bool HasClearLine(FixedVector2 from, FixedVector2 to) => SamplesWalkable(from, to, 2);

        /// <summary>As <see cref="HasClearLine(FixedVector2, FixedVector2)"/>, also blocked by ground above <paramref name="level"/>.</summary>
        public bool HasClearLine(FixedVector2 from, FixedVector2 to, int level) => SamplesWalkable(from, to, 2, level);

        /// <summary>
        /// Checks evenly spaced points from <paramref name="from"/> to <paramref name="to"/> inclusive,
        /// <paramref name="perCell"/> per cell length, by integer interpolation on raw fixed-point
        /// values (no square root, no fixed-point division per sample).
        /// </summary>
        private bool SamplesWalkable(FixedVector2 from, FixedVector2 to, int perCell, int level = int.MaxValue)
        {
            long dx = to.X.Raw - from.X.Raw;
            long dy = to.Y.Raw - from.Y.Raw;
            long span = System.Math.Max(System.Math.Abs((to - from).X.ToIntFloor()), System.Math.Abs((to - from).Y.ToIntFloor()));
            long samples = span * perCell / CellSize + 1;
            for (long i = 0; i <= samples; i++)
            {
                long x = from.X.Raw + dx * i / samples;
                long y = from.Y.Raw + dy * i / samples;
                int column = (int)((x >> Fixed.FractionalBits) / CellSize);
                int row = (int)((y >> Fixed.FractionalBits) / CellSize);
                if (x < 0 || y < 0 || !IsWalkableCell(column, row))
                    return false;
                if (_levels != null && _levels[row * Columns + column] > level)
                    return false;
            }

            return true;
        }

        internal bool IsWalkableCell(int column, int row) =>
            column >= 0 && row >= 0 && column < Columns && row < Rows && _walkable[row * Columns + column];

        internal int IndexOf(FixedVector2 point)
        {
            int column = Clamp(point.X.ToIntFloor() / CellSize, Columns);
            int row = Clamp(point.Y.ToIntFloor() / CellSize, Rows);
            return row * Columns + column;
        }

        internal FixedVector2 CenterOf(int column, int row) =>
            FixedVector2.FromInts(column * CellSize + CellSize / 2, row * CellSize + CellSize / 2);

        internal FixedVector2 CenterOf(int index) => CenterOf(index % Columns, index / Columns);

        internal bool IsWalkableIndex(int index) => _walkable[index];

        private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;
    }
}
