using System.Collections.Generic;
using Dynasty.Core.Kernel;
using Dynasty.Core.Mathematics;
using Dynasty.MatchSimulation.Rules;

namespace Dynasty.MatchSimulation.Systems
{
    /// <summary>
    /// Soft collision: overlapping mobile units push each other apart (half the overlap each, at
    /// most <see cref="MaxPush"/> per tick), never into unwalkable cells. All pushes are computed
    /// from positions before any is applied, so the result does not depend on processing order.
    /// Neighbours come from a uniform grid, not an all-pairs scan. Structures do not block: lanes
    /// run past them and lane creeps do not path around (see MATCH_SIMULATION.md limitations).
    /// </summary>
    internal sealed class SeparationSystem : ISimulationSystem<MatchState>
    {
        private const int CellSize = 128;

        /// <summary>
        /// Deliberately weaker than any unit's step per tick: a moving unit squeezes through a crowd
        /// (net progress) instead of being pushed back exactly as far as it walks and getting stuck.
        /// </summary>
        private static readonly Fixed MaxPush = Fixed.FromInt(4);
        private static readonly FixedVector2[] TieBreakDirections =
        {
            FixedVector2.FromInts(1, 0), FixedVector2.FromInts(0, 1), FixedVector2.FromInts(-1, 0), FixedVector2.FromInts(0, -1),
        };

        private readonly List<MatchUnit> _mobile = new List<MatchUnit>();

        // Uniform grid as intrusive lists: _head[cell] is the first unit index, _next[i] the following one.
        private int _columns;
        private int _rows;
        private int[] _head = new int[0];
        private int[] _next = new int[0];
        private int[] _cellOf = new int[0];
        private Fixed[] _dx = new Fixed[0];
        private Fixed[] _dy = new Fixed[0];

        public string Name => "separation";

        public void Tick(SimulationContext<MatchState> context)
        {
            MatchState state = context.State;
            if (_head.Length == 0)
            {
                _columns = state.Ruleset.Map.Width / CellSize + 1;
                _rows = state.Ruleset.Map.Height / CellSize + 1;
                _head = new int[_columns * _rows];
                for (int c = 0; c < _head.Length; c++)
                    _head[c] = -1;
            }

            Collect(state);

            PushUnitsApart();

            for (int i = 0; i < _mobile.Count; i++)
            {
                _head[_cellOf[i]] = -1;
                if (_dx[i] == Fixed.Zero && _dy[i] == Fixed.Zero)
                    continue;
                MatchUnit unit = _mobile[i];
                var moved = new FixedVector2(unit.Position.X + _dx[i], unit.Position.Y + _dy[i]);
                if (state.Nav.IsWalkable(moved))
                    unit.Position = moved;
            }
        }

        private void Collect(MatchState state)
        {
            _mobile.Clear();
            foreach (MatchUnit unit in state.Units)
            {
                if (!unit.IsAlive)
                    continue;
                if (unit.Kind == UnitKind.Structure || unit.Kind == UnitKind.Rune || unit.Stats.MoveSpeed <= 0)
                    continue;
                _mobile.Add(unit);
            }

            if (_next.Length < _mobile.Count)
            {
                int capacity = _mobile.Count * 2;
                _next = new int[capacity];
                _cellOf = new int[capacity];
                _dx = new Fixed[capacity];
                _dy = new Fixed[capacity];
            }

            for (int i = 0; i < _mobile.Count; i++)
            {
                int cell = CellIndex(Column(_mobile[i].Position.X), Row(_mobile[i].Position.Y));
                _cellOf[i] = cell;
                _next[i] = _head[cell];
                _head[cell] = i;
                _dx[i] = Fixed.Zero;
                _dy[i] = Fixed.Zero;
            }
        }

        private void PushUnitsApart()
        {
            for (int i = 0; i < _mobile.Count; i++)
            {
                MatchUnit a = _mobile[i];
                int cx = Column(a.Position.X);
                int cy = Row(a.Position.Y);
                for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (cx + ox < 0 || cx + ox >= _columns || cy + oy < 0 || cy + oy >= _rows)
                        continue;
                    for (int j = _head[CellIndex(cx + ox, cy + oy)]; j >= 0; j = _next[j])
                    {
                        if (j <= i)
                            continue;
                        MatchUnit b = _mobile[j];
                        Fixed reach = Fixed.FromInt(a.BoundingRadius + b.BoundingRadius);
                        FixedVector2 delta = b.Position - a.Position;
                        Fixed distanceSquared = delta.LengthSquared;
                        if (distanceSquared >= reach * reach)
                            continue;

                        FixedVector2 direction;
                        Fixed distance = Fixed.Sqrt(distanceSquared);
                        if (distance == Fixed.Zero)
                        {
                            // Same spot: a direction derived from the pair's IDs, not from processing order.
                            long hash = (a.Id.Value * 73856093L) ^ (b.Id.Value * 19349663L);
                            direction = TieBreakDirections[(int)(hash & 3)];
                        }
                        else
                        {
                            direction = new FixedVector2(delta.X / distance, delta.Y / distance);
                        }

                        Fixed push = Fixed.Min((reach - distance) / Fixed.FromInt(2), MaxPush);
                        _dx[i] -= direction.X * push;
                        _dy[i] -= direction.Y * push;
                        _dx[j] += direction.X * push;
                        _dy[j] += direction.Y * push;
                    }
                }
            }
        }

        private int Column(Fixed x) => Clamp(x.ToIntFloor() / CellSize, _columns);

        private int Row(Fixed y) => Clamp(y.ToIntFloor() / CellSize, _rows);

        private static int Clamp(int value, int count) => value < 0 ? 0 : value >= count ? count - 1 : value;

        private int CellIndex(int column, int row) => row * _columns + column;
    }
}
