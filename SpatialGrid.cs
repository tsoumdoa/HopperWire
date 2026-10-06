using System;
using System.Collections.Generic;
using System.Drawing;

namespace HopperWire
{
    internal sealed class SpatialGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<(int, int), List<WirePath>> _cells = new Dictionary<(int, int), List<WirePath>>();
        private readonly List<WirePath> _allWires = new List<WirePath>();
        private readonly List<WirePath> _largeWires = new List<WirePath>();

        public SpatialGrid(float cellSize)
        {
            if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize;
        }

        public void Insert(WirePath wire)
        {
            _allWires.Add(wire);
            if (!TryGetCellRange(wire.Bounds, out var range))
            {
                _largeWires.Add(wire);
                return;
            }
            for (int x = range.minX; x <= range.maxX; x++)
                for (int y = range.minY; y <= range.maxY; y++)
                {
                    if (!_cells.TryGetValue((x, y), out var wires))
                    {
                        wires = new List<WirePath>();
                        _cells.Add((x, y), wires);
                    }
                    wires.Add(wire);
                }
        }

        public HashSet<WirePath> Query(RectangleF bounds)
        {
            var result = new HashSet<WirePath>();
            if (!TryGetCellRange(bounds, out var range))
            {
                AddOverlapping(_allWires, bounds, result);
                return result;
            }
            for (int x = range.minX; x <= range.maxX; x++)
                for (int y = range.minY; y <= range.maxY; y++)
                    if (_cells.TryGetValue((x, y), out var wires))
                        AddOverlapping(wires, bounds, result);
            AddOverlapping(_largeWires, bounds, result);
            return result;
        }

        private static void AddOverlapping(List<WirePath> wires, RectangleF bounds, HashSet<WirePath> result)
        {
            foreach (var wire in wires)
                if (wire.Bounds.IntersectsWith(bounds)) result.Add(wire);
        }

        private bool TryGetCellRange(RectangleF bounds, out (int minX, int minY, int maxX, int maxY) range)
        {
            range = default;
            double minX = Math.Floor((double)bounds.Left / _cellSize);
            double minY = Math.Floor((double)bounds.Top / _cellSize);
            double maxX = Math.Floor((double)bounds.Right / _cellSize);
            double maxY = Math.Floor((double)bounds.Bottom / _cellSize);
            if (double.IsNaN(minX) || double.IsNaN(minY) || double.IsNaN(maxX) || double.IsNaN(maxY) ||
                minX < int.MinValue || minY < int.MinValue ||
                maxX >= int.MaxValue || maxY >= int.MaxValue ||
                maxX < minX || maxY < minY ||
                (maxX - minX + 1) * (maxY - minY + 1) > 4096)
                return false;
            range = ((int)minX, (int)minY, (int)maxX, (int)maxY);
            return true;
        }
    }
}
