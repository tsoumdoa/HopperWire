using System;
using System.Collections.Generic;

namespace HopperWire
{
    internal static class WireCrossings
    {
        public static HashSet<Guid> FindTargetsToFaint(IReadOnlyList<WirePath> wires,
            double faintThreshold, float gridSize)
        {
            var grid = new SpatialGrid(gridSize);
            foreach (var wire in wires) grid.Insert(wire);
            var targets = new HashSet<Guid>();
            var checkedPairs = new HashSet<((Guid, Guid), (Guid, Guid))>();
            foreach (var wire in wires)
            {
                // Long wires remain in the grid: they may cross a short, more vertical wire.
                if (wire.Length > faintThreshold) continue;
                foreach (var other in grid.Query(wire.Bounds))
                {
                    if (wire.SourceId == other.SourceId || wire.TargetId == other.TargetId ||
                        wire.Horizontalness == other.Horizontalness) continue;
                    var first = (wire.SourceId, wire.TargetId);
                    var second = (other.SourceId, other.TargetId);
                    var pair = first.Item1.CompareTo(second.Item1) < 0 ||
                        (first.Item1 == second.Item1 && first.Item2.CompareTo(second.Item2) < 0)
                        ? (first, second) : (second, first);
                    if (!checkedPairs.Add(pair)) continue;
                    var vertical = wire.Horizontalness > other.Horizontalness ? wire : other;
                    if (vertical.Length <= faintThreshold &&
                        WireGeometry.PathsIntersect(wire.SegmentPoints, other.SegmentPoints))
                        targets.Add(vertical.TargetId);
                }
            }
            return targets;
        }
    }
}
