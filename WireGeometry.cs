using System;
using System.Drawing;

namespace HopperWire
{
    internal static class WireGeometry
    {
        public static bool PathsIntersect(PointF[] first, PointF[] second)
        {
            for (int i = 0; i < first.Length - 1; i++)
                for (int j = 0; j < second.Length - 1; j++)
                    if (SegmentsCross(first, i, second, j)) return true;
            return false;
        }

        private static bool SegmentsCross(PointF[] a, int i, PointF[] b, int j)
        {
            double ax = (double)a[i + 1].X - a[i].X, ay = (double)a[i + 1].Y - a[i].Y;
            double bx = (double)b[j + 1].X - b[j].X, by = (double)b[j + 1].Y - b[j].Y;
            double denominator = ax * by - ay * bx;
            // Collinear overlap and degenerate segments have no transverse crossing.
            if (denominator == 0) return false;
            double dx = (double)b[j].X - a[i].X, dy = (double)b[j].Y - a[i].Y;
            double t = (dx * by - dy * bx) / denominator;
            double u = (dx * ay - dy * ax) / denominator;
            if (t < 0 || t > 1 || u < 0 || u > 1 ||
                (i == 0 && t == 0) || (i == a.Length - 2 && t == 1) ||
                (j == 0 && u == 0) || (j == b.Length - 2 && u == 1))
                return false;
            if (t > 0 && t < 1 && u > 0 && u < 1) return true;

            // At a sample vertex, the four outgoing rays must alternate by path.
            // This admits crossings at vertices while excluding a tangent touch.
            var aBefore = t == 0 ? a[i - 1] : a[i];
            var aAfter = t == 1 ? a[i + 2] : a[i + 1];
            var bBefore = u == 0 ? b[j - 1] : b[j];
            var bAfter = u == 1 ? b[j + 2] : b[j + 1];
            double x = a[i].X + t * ax, y = a[i].Y + t * ay;
            double a1 = Math.Atan2(aBefore.Y - y, aBefore.X - x);
            double a2 = Math.Atan2(aAfter.Y - y, aAfter.X - x);
            double b1 = Math.Atan2(bBefore.Y - y, bBefore.X - x);
            double b2 = Math.Atan2(bAfter.Y - y, bAfter.X - x);
            if (a1 == b1 || a1 == b2 || a2 == b1 || a2 == b2) return false;
            return Between(b1, a1, a2) != Between(b2, a1, a2);
        }

        private static bool Between(double angle, double first, double second)
        {
            return angle > Math.Min(first, second) && angle < Math.Max(first, second);
        }
    }
}
