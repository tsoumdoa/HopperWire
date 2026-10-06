using System;
using System.Drawing;
using HopperWire;

internal static class WireGeometryChecks
{
    public static void Run(Action<bool, string> check)
    {
        WirePath Path(PointF start, PointF end) => new WirePath(Guid.NewGuid(), Guid.NewGuid(), start, end);
        var horizontal = Path(new PointF(-100, 0), new PointF(100, 0));
        var vertical = Path(new PointF(0, -50), new PointF(0, 50));
        var backward = Path(new PointF(100, 0), new PointF(-100, 0));
        var curved = Path(new PointF(-100, -50), new PointF(100, 50));
        var degenerate = Path(new PointF(3, 4), new PointF(3, 4));
        check(horizontal.SegmentPoints.Length == 21 && horizontal.P0 == new PointF(-100, 0) &&
            horizontal.P3 == new PointF(100, 0), "Sampling preserves endpoints and segment count");
        check(Math.Abs(horizontal.Length - 200) < 0.001 && Math.Abs(backward.Length - 200) < 0.001,
            "Forward and backward horizontal lengths are accurate");
        check(Math.Abs(vertical.Length - 100) < 0.001 && vertical.Horizontalness == 90 &&
            horizontal.Horizontalness == 0 && backward.Horizontalness == 0, "Axis-aligned angles and lengths");
        check(curved.Length > Math.Sqrt(200 * 200 + 100 * 100), "Curve length includes curvature");
        check(degenerate.Length == 0 && degenerate.Bounds.Width == 4 && degenerate.Bounds.Height == 4,
            "Coincident grips yield zero length and padded bounds");
        foreach (var sample in curved.SegmentPoints)
            check(curved.Bounds.Contains(sample), "Bounds contain every curve sample");

        var straight = new[] { new PointF(-10, 0), new PointF(10, 0) };
        var split = new[] { new PointF(0, -10), new PointF(0, 0), new PointF(0, 10) };
        check(WireGeometry.PathsIntersect(straight, split), "Crossing at one sample vertex is detected");
        check(WireGeometry.PathsIntersect(split, straight), "Vertex intersection is symmetric");
        check(WireGeometry.PathsIntersect(new[] { new PointF(-10, 0), PointF.Empty, new PointF(10, 0) }, split),
            "Crossing at vertices of both paths is detected");
        check(WireGeometry.PathsIntersect(straight, new[] { new PointF(0, -10), new PointF(0, 10) }),
            "Crossing inside two segments is detected");
        check(!WireGeometry.PathsIntersect(straight,
            new[] { new PointF(-5, 5), PointF.Empty, new PointF(5, 5) }), "Tangent vertex touch is ignored");
        check(!WireGeometry.PathsIntersect(straight,
            new[] { PointF.Empty, new PointF(0, 10) }), "A curve endpoint touching an interior is ignored");
        check(!WireGeometry.PathsIntersect(straight,
            new[] { new PointF(10, 0), new PointF(10, 10) }), "Shared geometric endpoints are ignored");
        check(!WireGeometry.PathsIntersect(straight,
            new[] { new PointF(-5, 0), new PointF(5, 0) }), "Collinear overlap is ignored");
        check(!WireGeometry.PathsIntersect(degenerate.SegmentPoints, horizontal.SegmentPoints),
            "Zero-length paths do not cross");
        var bent = new[] { new PointF(-4, -8), PointF.Empty, new PointF(8, 4) };
        var crossingBend = new[] { new PointF(-8, 4), PointF.Empty, new PointF(4, -8) };
        var touchingBend = new[] { new PointF(-8, -4), PointF.Empty, new PointF(-4, -8) };
        foreach (bool reverseFirst in new[] { false, true })
            foreach (bool reverseSecond in new[] { false, true })
            {
                var a = (PointF[])bent.Clone();
                var b = (PointF[])crossingBend.Clone();
                var touch = (PointF[])touchingBend.Clone();
                if (reverseFirst) Array.Reverse(a);
                if (reverseSecond) { Array.Reverse(b); Array.Reverse(touch); }
                check(WireGeometry.PathsIntersect(a, b) && WireGeometry.PathsIntersect(b, a),
                    "Bent sample-vertex crossings survive reversing either path");
                check(!WireGeometry.PathsIntersect(a, touch) && !WireGeometry.PathsIntersect(touch, a),
                    "Bent vertex touches survive reversing either path");
            }

        var crossings = WireCrossings.FindTargetsToFaint(new[] { horizontal, vertical }, 800, 20);
        check(crossings.SetEquals(new[] { vertical.TargetId }), "Sampled curves faint the more vertical target");
        var diagonalA = Path(new PointF(-50, -50), new PointF(50, 50));
        var diagonalB = Path(new PointF(-50, 50), new PointF(50, -50));
        check(WireCrossings.FindTargetsToFaint(new[] { diagonalA, diagonalB }, 800, 20).Count == 0,
            "Equal-angle crossings do not choose a target");
        var sharedSource = new WirePath(horizontal.SourceId, Guid.NewGuid(), vertical.P0, vertical.P3);
        var sharedTarget = new WirePath(Guid.NewGuid(), horizontal.TargetId, vertical.P0, vertical.P3);
        check(WireCrossings.FindTargetsToFaint(new[] { horizontal, sharedSource }, 800, 20).Count == 0,
            "Shared source IDs exclude crossings even with differing grips");
        check(WireCrossings.FindTargetsToFaint(new[] { horizontal, sharedTarget }, 800, 20).Count == 0,
            "Shared target IDs exclude crossings");
        check(WireCrossings.FindTargetsToFaint(new[] { horizontal, vertical }, 100, 20).Contains(vertical.TargetId),
            "A long horizontal candidate still faints a short vertical wire at the threshold");
        check(WireCrossings.FindTargetsToFaint(new[] { vertical, horizontal }, 100, 20).Contains(vertical.TargetId),
            "Short-long crossing detection is independent of input order");
        check(WireCrossings.FindTargetsToFaint(new[] { horizontal, vertical }, 50, 20).Count == 0,
            "Two long wires need no crossing result");
        var longVertical = Path(new PointF(0, -500), new PointF(0, 500));
        check(WireCrossings.FindTargetsToFaint(new[] { horizontal, longVertical }, 200, 20).Count == 0,
            "A long vertical wire does not faint the shorter horizontal wire");
        var otherSource = new WirePath(Guid.NewGuid(), vertical.TargetId, new PointF(30, -50), new PointF(30, 50));
        check(WireCrossings.FindTargetsToFaint(new[] { horizontal, vertical, otherSource }, 800, 20)
            .SetEquals(new[] { vertical.TargetId }), "Distinct incoming paths aggregate to one crossing target");

        foreach (float cellSize in new[] { 20f, 0.000001f, float.Epsilon, float.MaxValue })
        {
            var grid = new SpatialGrid(cellSize);
            grid.Insert(horizontal);
            grid.Insert(vertical);
            grid.Insert(Path(new PointF(1000, 1000), new PointF(1100, 1000)));
            check(grid.Query(new RectangleF(-1, -1, 2, 2)).SetEquals(new[] { horizontal, vertical }),
                "Grid query deduplicates candidates and excludes remote bounds, including fallback");
            check(grid.Query(new RectangleF(-200, -200, 400, 400)).SetEquals(new[] { horizontal, vertical }),
                "Large query fallback returns the same candidates");
            check(WireCrossings.FindTargetsToFaint(new[] { horizontal, vertical }, 800, cellSize)
                .SetEquals(new[] { vertical.TargetId }), "Crossing decisions survive extreme grid sizes");
        }
        var mixedGrid = new SpatialGrid(1);
        var enormous = Path(new PointF(-10000, 0), new PointF(10000, 0));
        mixedGrid.Insert(enormous);
        check(mixedGrid.Query(new RectangleF(-1, -1, 2, 2)).Contains(enormous),
            "Normal query includes a wire exceeding the 4096-cell cap");
        var extreme = Path(new PointF(1e10f, 0), new PointF(1e10f + 20000, 100));
        mixedGrid.Insert(extreme);
        check(mixedGrid.Query(extreme.Bounds).Contains(extreme), "Out-of-integer-range bounds use fallback");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { new SpatialGrid(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Grid rejects invalid cell size");
        }

        foreach (WireDecision first in Enum.GetValues(typeof(WireDecision)))
            foreach (WireDecision second in Enum.GetValues(typeof(WireDecision)))
            {
                var expected = first > second ? first : second;
                check(WireLayoutRules.MostRestrictive(first, second) == expected &&
                    WireLayoutRules.MostRestrictive(second, first) == expected,
                    "Multi-source display aggregation is restrictive and independent of order");
            }
        var hidden = WireLayoutRules.DecideDisplay(1600, 800, 1500, false);
        var faint = WireLayoutRules.DecideDisplay(100, 800, 1500, true);
        var clear = WireLayoutRules.DecideDisplay(100, 800, 1500, false);
        check(WireLayoutRules.MostRestrictive(clear, WireLayoutRules.MostRestrictive(hidden, faint)) == WireDecision.Hidden,
            "A hidden incoming path wins over a short crossing and a clear incoming path");
    }
}
