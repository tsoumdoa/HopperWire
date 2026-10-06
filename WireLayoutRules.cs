using System;
using System.Collections.Generic;
using System.Drawing;

namespace HopperWire
{
    internal enum WireDecision { Default, Faint, Hidden }

    internal static class WireLayoutRules
    {
        public static WireDecision MostRestrictive(WireDecision first, WireDecision second)
        {
            return first > second ? first : second;
        }

        public static WireDecision DecideDisplay(double length, double faintThreshold,
            double hiddenThreshold, bool faintForLayout)
        {
            if (length > hiddenThreshold) return WireDecision.Hidden;
            if (length > faintThreshold || faintForLayout) return WireDecision.Faint;
            return WireDecision.Default;
        }

        public static bool IsBackward(PointF sourceGrip, PointF targetGrip)
        {
            return sourceGrip.X > targetGrip.X;
        }

        public static bool CrossesGroupBoundary(ISet<Guid> sourceGroups, ISet<Guid> targetGroups)
        {
            bool sourceGrouped = sourceGroups != null && sourceGroups.Count > 0;
            bool targetGrouped = targetGroups != null && targetGroups.Count > 0;
            if (!sourceGrouped && !targetGrouped) return false;
            if (!sourceGrouped || !targetGrouped) return true;

            foreach (var group in sourceGroups)
                if (targetGroups.Contains(group)) return false;
            return true;
        }

        public static bool PassesThroughRectangle(PointF[] curve, RectangleF rectangle)
        {
            if (curve == null || curve.Length < 2 || rectangle.Width <= 0 || rectangle.Height <= 0)
                return false;
            for (int i = 0; i < curve.Length - 1; i++)
                if (SegmentPassesThroughRectangle(curve[i], curve[i + 1], rectangle))
                    return true;
            return false;
        }

        private static bool SegmentPassesThroughRectangle(PointF start, PointF end, RectangleF r)
        {
            double dx = (double)end.X - start.X;
            double dy = (double)end.Y - start.Y;
            double enter = 0, leave = 1;
            if (!Clip(-dx, start.X - r.Left, ref enter, ref leave) ||
                !Clip(dx, r.Right - start.X, ref enter, ref leave) ||
                !Clip(-dy, start.Y - r.Top, ref enter, ref leave) ||
                !Clip(dy, r.Bottom - start.Y, ref enter, ref leave) ||
                enter >= leave)
                return false;

            // The clipped segment must pass through the interior, not merely touch an edge.
            double middle = (enter + leave) / 2;
            double x = start.X + middle * dx;
            double y = start.Y + middle * dy;
            return x > r.Left && x < r.Right && y > r.Top && y < r.Bottom;
        }

        private static bool Clip(double p, double q, ref double enter, ref double leave)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0)
            {
                if (t > leave) return false;
                if (t > enter) enter = t;
            }
            else
            {
                if (t < enter) return false;
                if (t < leave) leave = t;
            }
            return true;
        }
    }
}
