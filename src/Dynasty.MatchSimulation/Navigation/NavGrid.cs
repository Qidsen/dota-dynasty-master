using System.Collections.Generic;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Navigation
{
    /// <summary>
    /// Square-cell walkability grid built from map obstacles. A cell is walkable when its center
    /// is outside every obstacle grown by the clearance. Immutable once built.
    /// </summary>
    public sealed class NavGrid
    {
        private readonly bool[] _walkable;

        private NavGrid(int columns, int rows, int cellSize, bool[] walkable)
        {
            Columns = columns;
            Rows = rows;
            CellSize = cellSize;
            _walkable = walkable;
        }

        public int Columns { get; }
        public int Rows { get; }
        public int CellSize { get; }

        public static NavGrid FromMap(MapDefinition map) =>
            Build(map.Width, map.Height, map.NavCellSize, map.Obstacles, map.NavClearance);

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

        /// <summary>
        /// Checks evenly spaced points from <paramref name="from"/> to <paramref name="to"/> inclusive,
        /// <paramref name="perCell"/> per cell length, by integer interpolation on raw fixed-point
        /// values (no square root, no fixed-point division per sample).
        /// </summary>
        private bool SamplesWalkable(FixedVector2 from, FixedVector2 to, int perCell)
        {
            long dx = to.X.Raw - from.X.Raw;
            long dy = to.Y.Raw - from.Y.Raw;
            long span = System.Math.Max(System.Math.Abs((to - from).X.ToIntFloor()), System.Math.Abs((to - from).Y.ToIntFloor()));
            long samples = span * perCell / CellSize + 1;
            for (long i = 0; i <= samples; i++)
            {
                long x = from.X.Raw + dx * i / samples;
                long y = from.Y.Raw + dy * i / samples;
                if (x < 0 || y < 0 || !IsWalkableCell((int)((x >> Fixed.FractionalBits) / CellSize), (int)((y >> Fixed.FractionalBits) / CellSize)))
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
