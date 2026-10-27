using System;
using System.Collections.Generic;
using System.Linq;
using Dynasty.Core.Hashing;
using Dynasty.Core.Mathematics;

namespace Dynasty.MatchSimulation.Rules
{
    /// <summary>
    /// A map given cell by cell, as Dota's map file gives it (D-053): walkability (gridnav), vision blockers and the
    /// ground level of each cell, plus the trees. Every tree blocks a <see cref="TreeSize"/>-wide square around its
    /// position for walking and sight (Liquipedia «Trees»: 128×128 whatever the model). Cells are the map's
    /// navigation cells, row by row from y = 0.
    /// </summary>
    public sealed class MapTerrain
    {
        private readonly bool[] _walkable;
        private readonly bool[] _blocksSight;
        private readonly int[] _levels;

        public MapTerrain(
            int columns,
            int rows,
            IReadOnlyList<bool> walkable,
            IReadOnlyList<bool>? blocksSight = null,
            IReadOnlyList<int>? levels = null,
            IReadOnlyList<FixedVector2>? trees = null,
            int treeSize = 128)
        {
            Columns = columns;
            Rows = rows;
            _walkable = walkable.ToArray();
            _blocksSight = (blocksSight ?? Array.Empty<bool>()).ToArray();
            _levels = (levels ?? Array.Empty<int>()).ToArray();
            Trees = (trees ?? Array.Empty<FixedVector2>()).ToArray();
            TreeSize = treeSize;
        }

        public int Columns { get; }
        public int Rows { get; }
        public IReadOnlyList<FixedVector2> Trees { get; }

        /// <summary>Side of the square each tree blocks, centered on it.</summary>
        public int TreeSize { get; }

        public int CellCount => Columns * Rows;

        /// <summary>True when every per-cell layer given has one value per cell.</summary>
        public bool IsComplete =>
            Columns > 0 && Rows > 0 && _walkable.Length == CellCount &&
            (_blocksSight.Length == 0 || _blocksSight.Length == CellCount) &&
            (_levels.Length == 0 || _levels.Length == CellCount);

        public bool IsWalkable(int index) => _walkable[index];

        public bool BlocksSight(int index) => _blocksSight.Length != 0 && _blocksSight[index];

        public int LevelOf(int index) => _levels.Length == 0 ? 0 : _levels[index];

        public bool HasLevels => _levels.Length != 0;

        internal void WriteCanonical(CanonicalWriter w)
        {
            w.WriteInt64(Columns);
            w.WriteInt64(Rows);
            WriteBits(w, _walkable);
            WriteBits(w, _blocksSight);
            w.WriteInt64(_levels.Length);
            foreach (int level in _levels)
                w.WriteInt64(level);
            w.WriteInt64(TreeSize);
            w.WriteInt64(Trees.Count);
            foreach (FixedVector2 tree in Trees)
                w.WriteVector(tree);
        }

        /// <summary>64 cells per written number, so a 100k-cell map stays cheap to fingerprint.</summary>
        private static void WriteBits(CanonicalWriter w, bool[] bits)
        {
            w.WriteInt64(bits.Length);
            long word = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i])
                    word |= 1L << (i & 63);
                if ((i & 63) == 63 || i == bits.Length - 1)
                {
                    w.WriteInt64(word);
                    word = 0;
                }
            }
        }
    }
}
