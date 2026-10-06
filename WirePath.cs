using System;
using System.Drawing;

namespace HopperWire
{
    // Geometry and connection identity can be checked without a live Grasshopper document.
    internal class WirePath
    {
        public Guid SourceId { get; }
        public Guid TargetId { get; }
        public PointF P0 => SegmentPoints[0];
        public PointF P3 => SegmentPoints[SegmentPoints.Length - 1];
        public PointF[] SegmentPoints { get; }
        public RectangleF Bounds { get; }
        public double Length { get; }
        public double Horizontalness { get; }

        public WirePath(Guid sourceId, Guid targetId, PointF sourceGrip, PointF targetGrip)
        {
            SourceId = sourceId;
            TargetId = targetId;
            double dx = (double)targetGrip.X - sourceGrip.X;
            double dy = (double)targetGrip.Y - sourceGrip.Y;
            float offset = (float)(dx * 0.3);
            var p1 = new PointF(sourceGrip.X + offset, sourceGrip.Y);
            var p2 = new PointF(targetGrip.X - offset, targetGrip.Y);
            SegmentPoints = new PointF[21];
            for (int i = 0; i < SegmentPoints.Length; i++)
                SegmentPoints[i] = EvaluateBezier(sourceGrip, p1, p2, targetGrip, i / 20.0);

            float minX = sourceGrip.X, maxX = sourceGrip.X;
            float minY = sourceGrip.Y, maxY = sourceGrip.Y;
            double length = 0;
            for (int i = 1; i < SegmentPoints.Length; i++)
            {
                double x = (double)SegmentPoints[i].X - SegmentPoints[i - 1].X;
                double y = (double)SegmentPoints[i].Y - SegmentPoints[i - 1].Y;
                length += Math.Sqrt(x * x + y * y);
                minX = Math.Min(minX, SegmentPoints[i].X);
                maxX = Math.Max(maxX, SegmentPoints[i].X);
                minY = Math.Min(minY, SegmentPoints[i].Y);
                maxY = Math.Max(maxY, SegmentPoints[i].Y);
            }
            Length = length;
            const float padding = 2;
            Bounds = new RectangleF(minX - padding, minY - padding,
                maxX - minX + padding * 2, maxY - minY + padding * 2);
            double angle = Math.Abs(Math.Atan2(dy, dx) * 180 / Math.PI);
            Horizontalness = angle > 90 ? 180 - angle : angle;
        }

        private static PointF EvaluateBezier(PointF p0, PointF p1, PointF p2, PointF p3, double t)
        {
            double mt = 1 - t;
            double mt2 = mt * mt, t2 = t * t;
            return new PointF(
                (float)(mt2 * mt * p0.X + 3 * mt2 * t * p1.X + 3 * mt * t2 * p2.X + t2 * t * p3.X),
                (float)(mt2 * mt * p0.Y + 3 * mt2 * t * p1.Y + 3 * mt * t2 * p2.Y + t2 * t * p3.Y));
        }
    }
}
