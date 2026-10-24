using System.Collections.Generic;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Navigation
{
    /// <summary>
    /// A* over a <see cref="NavGrid"/>: 8 neighbours, no corner cutting, integer costs (10/14),
    /// octile heuristic and fully deterministic tie-breaking (f, then h, then cell index).
    /// The cell path is string-pulled into as few straight segments as line of sight allows.
    /// </summary>
    public sealed class Pathfinder
    {
        private const int StraightCost = 10;
        private const int DiagonalCost = 14;

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly NavGrid _grid;

        // Search scratch reused across calls. A cell's g/parent/closed are valid only when its stamp
        // equals the current search number, so nothing is cleared between searches.
        private readonly int[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private readonly MinHeap _open = new MinHeap();
        private int _search;

        public Pathfinder(NavGrid grid)
        {
            _grid = grid;
            int count = grid.Columns * grid.Rows;
            _g = new int[count];
            _parent = new int[count];
            _stamp = new int[count];
            _closed = new bool[count];
        }

        /// <summary>
        /// Waypoints after <paramref name="start"/>, ending at the goal (or at the nearest walkable
        /// point when the goal is blocked). Null when no route exists.
        /// </summary>
        public IReadOnlyList<FixedVector2>? FindPath(FixedVector2 start, FixedVector2 goal)
        {
            int startIndex = NearestWalkable(start);
            int goalIndex = NearestWalkable(goal);
            if (startIndex < 0 || goalIndex < 0)
                return null;

            FixedVector2 destination = _grid.IsWalkable(goal) ? goal : _grid.CenterOf(goalIndex);
            if (_grid.IsWalkable(start) && _grid.HasLineOfSight(start, destination))
                return new[] { destination };

            List<int>? cells = Search(startIndex, goalIndex);
            if (cells == null)
                return null;

            var points = new List<FixedVector2>(cells.Count);
            for (int i = 1; i < cells.Count - 1; i++)
                points.Add(_grid.CenterOf(cells[i]));
            points.Add(destination);
            return Smooth(_grid.IsWalkable(start) ? start : _grid.CenterOf(startIndex), points);
        }

        private List<int>? Search(int startIndex, int goalIndex)
        {
            _search++;
            _open.Clear();
            Touch(startIndex);
            _g[startIndex] = 0;
            _open.Push(new Node(Heuristic(startIndex, goalIndex), Heuristic(startIndex, goalIndex), startIndex));

            while (_open.Count > 0)
            {
                Node node = _open.Pop();
                if (_closed[node.Index])
                    continue;
                if (node.Index == goalIndex)
                    return Reconstruct(goalIndex);
                _closed[node.Index] = true;

                int column = node.Index % _grid.Columns;
                int row = node.Index / _grid.Columns;
                for (int dir = 0; dir < 8; dir++)
                {
                    int nc = column + Dx[dir];
                    int nr = row + Dy[dir];
                    if (!_grid.IsWalkableCell(nc, nr))
                        continue;
                    bool diagonal = dir >= 4;
                    if (diagonal && (!_grid.IsWalkableCell(column + Dx[dir], row) || !_grid.IsWalkableCell(column, row + Dy[dir])))
                        continue;

                    int next = nr * _grid.Columns + nc;
                    Touch(next);
                    int cost = _g[node.Index] + (diagonal ? DiagonalCost : StraightCost);
                    if (_closed[next] || cost >= _g[next])
                        continue;

                    _g[next] = cost;
                    _parent[next] = node.Index;
                    int h = Heuristic(next, goalIndex);
                    _open.Push(new Node(cost + h, h, next));
                }
            }

            return null;
        }

        /// <summary>Resets a cell's search data the first time the current search reaches it.</summary>
        private void Touch(int index)
        {
            if (_stamp[index] == _search)
                return;
            _stamp[index] = _search;
            _g[index] = int.MaxValue;
            _parent[index] = -1;
            _closed[index] = false;
        }

        private List<FixedVector2> Smooth(FixedVector2 start, List<FixedVector2> points)
        {
            var result = new List<FixedVector2>();
            FixedVector2 anchor = start;
            int i = 0;
            while (i < points.Count)
            {
                int furthest = i;
                for (int j = points.Count - 1; j > i; j--)
                {
                    if (_grid.HasLineOfSight(anchor, points[j]))
                    {
                        furthest = j;
                        break;
                    }
                }

                result.Add(points[furthest]);
                anchor = points[furthest];
                i = furthest + 1;
            }

            return result;
        }

        private int NearestWalkable(FixedVector2 point)
        {
            int index = _grid.IndexOf(point);
            if (_grid.IsWalkableIndex(index))
                return index;

            int best = -1;
            Fixed bestDistance = Fixed.Zero;
            int count = _grid.Columns * _grid.Rows;
            for (int i = 0; i < count; i++)
            {
                if (!_grid.IsWalkableIndex(i))
                    continue;
                Fixed distance = FixedVector2.DistanceSquared(point, _grid.CenterOf(i));
                if (best < 0 || distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private int Heuristic(int from, int to)
        {
            int dx = System.Math.Abs(from % _grid.Columns - to % _grid.Columns);
            int dy = System.Math.Abs(from / _grid.Columns - to / _grid.Columns);
            int diagonal = dx < dy ? dx : dy;
            return StraightCost * (dx + dy) + (DiagonalCost - 2 * StraightCost) * diagonal;
        }

        private List<int> Reconstruct(int goal)
        {
            var cells = new List<int>();
            for (int at = goal; at >= 0; at = _parent[at])
                cells.Add(at);
            cells.Reverse();
            return cells;
        }

        private readonly struct Node
        {
            public Node(int f, int h, int index)
            {
                F = f;
                H = h;
                Index = index;
            }

            public int F { get; }
            public int H { get; }
            public int Index { get; }

            public bool Before(Node other) =>
                F != other.F ? F < other.F : H != other.H ? H < other.H : Index < other.Index;
        }

        /// <summary>Binary heap; netstandard2.1 has no PriorityQueue.</summary>
        private sealed class MinHeap
        {
            private readonly List<Node> _items = new List<Node>();

            public int Count => _items.Count;

            public void Clear() => _items.Clear();

            public void Push(Node node)
            {
                _items.Add(node);
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!_items[i].Before(_items[parent]))
                        break;
                    (_items[i], _items[parent]) = (_items[parent], _items[i]);
                    i = parent;
                }
            }

            public Node Pop()
            {
                Node top = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = 2 * i + 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left < _items.Count && _items[left].Before(_items[smallest]))
                        smallest = left;
                    if (right < _items.Count && _items[right].Before(_items[smallest]))
                        smallest = right;
                    if (smallest == i)
                        break;
                    (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                    i = smallest;
                }

                return top;
            }
        }
    }
}
