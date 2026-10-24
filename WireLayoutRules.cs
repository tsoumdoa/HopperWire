using System;
using System.Collections.Generic;
using System.Drawing;

namespace HopperWire
{
    internal enum WireDecision { Default, Faint, Hidden }

    internal sealed class WireTargetDecision
    {
        private WireDecision _mode;
        private bool _hasNearbyConnection;
        private bool _crossesCoordinateAxes;

        public WireDecision Mode => _crossesCoordinateAxes ? WireDecision.Hidden :
            _hasNearbyConnection && _mode == WireDecision.Hidden ? WireDecision.Faint : _mode;

        public WireDecision Add(WirePath wire, double faintThreshold, double hiddenThreshold,
            bool faintForLayout, int connectionCount)
        {
            bool crossesAxes = WireLayoutRules.CrossesCoordinateAxes(wire.P0, wire.P3);
            var decision = WireLayoutRules.DecideDisplay(wire.Length, faintThreshold, hiddenThreshold,
                faintForLayout, connectionCount, crossesAxes);
            _mode = WireLayoutRules.MostRestrictive(_mode, decision);
            _hasNearbyConnection |= wire.Length <= faintThreshold;
            _crossesCoordinateAxes |= crossesAxes;
            return decision;
        }
    }

    internal static class WireLayoutRules
    {
        public static WireDecision MostRestrictive(WireDecision first, WireDecision second)
        {
            return first > second ? first : second;
        }

        public static WireDecision DecideDisplay(double length, double faintThreshold,
            double hiddenThreshold, bool faintForLayout, int connectionCount = 1,
            bool crossesCoordinateAxes = false)
        {
            if (crossesCoordinateAxes) return WireDecision.Hidden;
            if (length > faintThreshold && (length > hiddenThreshold || connectionCount > 8))
                return WireDecision.Hidden;
            if (length > faintThreshold || faintForLayout || connectionCount > 3)
                return WireDecision.Faint;
            return WireDecision.Default;
        }

        public static bool CrossesCoordinateAxes(PointF sourceGrip, PointF targetGrip)
        {
            return OppositeSigns(sourceGrip.X, targetGrip.X) || OppositeSigns(sourceGrip.Y, targetGrip.Y);
        }

        private static bool OppositeSigns(float first, float second)
        {
            return (first < 0 && second > 0) || (first > 0 && second < 0);
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
