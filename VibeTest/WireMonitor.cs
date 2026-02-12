using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;

namespace VibeTest
{
    public class WireInfo
    {
        public IGH_Param Target { get; set; }
        public IGH_Param Source { get; set; }
        public PointF P0 { get; set; } // Start point
        public PointF P1 { get; set; } // Control point 1
        public PointF P2 { get; set; } // Control point 2
        public PointF P3 { get; set; } // End point
        public RectangleF Bounds { get; set; }
        public double Length { get; set; }
        public PointF Midpoint { get; set; }
        public double Horizontalness { get; set; } // Angle in degrees (0 = horizontal, 90 = vertical)
        public int Segments { get; set; } = 15;
        
        public PointF[] GetSegmentPoints()
        {
            var points = new PointF[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                double t = (double)i / Segments;
                points[i] = EvaluateBezier(t);
            }
            return points;
        }
        
        private PointF EvaluateBezier(double t)
        {
            double mt = 1 - t;
            double mt2 = mt * mt;
            double mt3 = mt2 * mt;
            double t2 = t * t;
            double t3 = t2 * t;

            float x = (float)(mt3 * P0.X + 3 * mt2 * t * P1.X + 3 * mt * t2 * P2.X + t3 * P3.X);
            float y = (float)(mt3 * P0.Y + 3 * mt2 * t * P1.Y + 3 * mt * t2 * P2.Y + t3 * P3.Y);

            return new PointF(x, y);
        }
    }
    
    public class SpatialGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<(int, int), List<WireInfo>> _cells;
        
        public SpatialGrid(float cellSize)
        {
            _cellSize = cellSize;
            _cells = new Dictionary<(int, int), List<WireInfo>>();
        }
        
        public void Insert(WireInfo wire)
        {
            var cellRange = GetCellRange(wire.Bounds);
            for (int x = cellRange.minX; x <= cellRange.maxX; x++)
            {
                for (int y = cellRange.minY; y <= cellRange.maxY; y++)
                {
                    var key = (x, y);
                    if (!_cells.ContainsKey(key))
                        _cells[key] = new List<WireInfo>();
                    _cells[key].Add(wire);
                }
            }
        }
        
        public List<WireInfo> Query(RectangleF bounds)
        {
            var result = new HashSet<WireInfo>();
            var cellRange = GetCellRange(bounds);
            
            for (int x = cellRange.minX; x <= cellRange.maxX; x++)
            {
                for (int y = cellRange.minY; y <= cellRange.maxY; y++)
                {
                    var key = (x, y);
                    if (_cells.ContainsKey(key))
                    {
                        foreach (var wire in _cells[key])
                        {
                            if (wire.Bounds.IntersectsWith(bounds))
                                result.Add(wire);
                        }
                    }
                }
            }
            
            return new List<WireInfo>(result);
        }
        
        private (int minX, int minY, int maxX, int maxY) GetCellRange(RectangleF bounds)
        {
            int minX = (int)Math.Floor(bounds.Left / _cellSize);
            int minY = (int)Math.Floor(bounds.Top / _cellSize);
            int maxX = (int)Math.Floor(bounds.Right / _cellSize);
            int maxY = (int)Math.Floor(bounds.Bottom / _cellSize);
            return (minX, minY, maxX, maxY);
        }
    }

    public class WireMonitor
    {
        private GH_Document _document;
        private double _faintThreshold;
        private double _hiddenThreshold;
        private float _spatialGridSize;
        private bool _debug;
        private Dictionary<Guid, GH_ParamWireDisplay> _modifiedWires;
        private StringBuilder _debugLog;
        private int _wireCount;
        private int _modifiedCount;

        public WireMonitor(GH_Document document, double faintThreshold, double hiddenThreshold, float spatialGridSize, bool debug)
        {
            _document = document;
            _faintThreshold = faintThreshold;
            _hiddenThreshold = hiddenThreshold;
            _spatialGridSize = spatialGridSize;
            _debug = debug;
            _modifiedWires = new Dictionary<Guid, GH_ParamWireDisplay>();
            _debugLog = new StringBuilder();
            _wireCount = 0;
            _modifiedCount = 0;

            if (_debug)
            {
                Log("WireMonitor created (manual trigger mode)");
                Log($"  Faint Threshold: {_faintThreshold:F1} pixels");
                Log($"  Hidden Threshold: {_hiddenThreshold:F1} pixels");
                Log($"  Spatial Grid Size: {_spatialGridSize:F1} pixels");
                Log($"  Debug Mode: {_debug}");
            }
        }

        public void ProcessAllWires()
        {
            if (_document == null) return;

            var currentWires = new Dictionary<Guid, GH_ParamWireDisplay>();
            _wireCount = 0;
            _modifiedCount = 0;
            
            var processedConnections = new HashSet<string>();
            
            // Collect ALL unique connections (source -> target pairs)
            var allConnections = new List<KeyValuePair<IGH_Param, IGH_Param>>();
            
            if (_debug)
            {
                int componentCount = 0;
                int paramCount = 0;
                
                foreach (var obj in _document.Objects)
                {
                    if (obj is IGH_Component) componentCount++;
                    if (obj is IGH_Param) paramCount++;
                }
                
                Log($"Document has {_document.ObjectCount} objects");
                Log($"  Objects by type:");
                Log($"    Components: {componentCount}");
                Log($"    Parameters: {paramCount}");
            }

            // THOROUGH APPROACH: Collect ALL unique connections first
            foreach (var obj in _document.Objects)
            {
                // Process floating parameters directly in the document
                if (obj is IGH_Param param)
                {
                    AddParamConnections(param, allConnections);
                }
                
                // Process component parameters (nested inputs/outputs)
                if (obj is IGH_Component component)
                {
                    foreach (var input in component.Params.Input)
                    {
                        AddParamConnections(input, allConnections);
                    }
                    foreach (var output in component.Params.Output)
                    {
                        AddParamConnections(output, allConnections);
                    }
                }
            }
            
            if (_debug)
            {
                Log($"Collected {allConnections.Count} unique connections");
            }

            // Build wire info list for crossing detection
            var wireInfos = BuildWireInfoList(allConnections);
            
            if (_debug)
            {
                Log($"Built {wireInfos.Count} wire info objects");
            }

            // Build spatial grid and detect crossings
            var grid = new SpatialGrid(_spatialGridSize);
            foreach (var wireInfo in wireInfos)
            {
                grid.Insert(wireInfo);
            }
            
            if (_debug)
            {
                Log($"Spatial grid built with {_spatialGridSize:F1}px cell size");
            }

            // Detect crossings and mark wires to faint
            var wiresToFaintFromCrossing = new HashSet<Guid>();
            var checkedPairs = new HashSet<(Guid, Guid)>();
            
            if (_debug)
            {
                Log($"Starting crossing detection with {wireInfos.Count} wires");
            }
            
            for (int i = 0; i < wireInfos.Count; i++)
            {
                var wireInfo = wireInfos[i];
                
                // Only check wires below faint threshold for crossing-based fainting
                if (wireInfo.Length >= _faintThreshold)
                {
                    if (_debug)
                    {
                        Log($"  Skipping {wireInfo.Source.NickName} -> {wireInfo.Target.NickName}: length {wireInfo.Length:F1}px >= threshold {_faintThreshold:F1}px (already faint/hidden)");
                    }
                    continue;
                }
                
                if (_debug)
                {
                    Log($"  Checking {wireInfo.Source.NickName} -> {wireInfo.Target.NickName} ({wireInfo.Horizontalness:F1}°, {wireInfo.Length:F1}px) for crossings");
                }
                
                // Query grid for overlapping wires
                var overlappingWires = grid.Query(wireInfo.Bounds);
                
                if (_debug)
                {
                    Log($"    Found {overlappingWires.Count} overlapping wires");
                }
                
                foreach (var otherWire in overlappingWires)
                {
                    if (wireInfo == otherWire)
                        continue;
                    
                    // Skip wires that share the same output param (they naturally converge)
                    if (wireInfo.Source == otherWire.Source)
                    {
                        if (_debug)
                        {
                            Log($"    Skipped: wires share same source {wireInfo.Source.NickName}");
                        }
                        continue;
                    }
                    
                    // Check each pair only once
                    var pairKey = wireInfo.Target.InstanceGuid.CompareTo(otherWire.Target.InstanceGuid) < 0
                        ? (wireInfo.Target.InstanceGuid, otherWire.Target.InstanceGuid)
                        : (otherWire.Target.InstanceGuid, wireInfo.Target.InstanceGuid);
                    
                    if (checkedPairs.Contains(pairKey))
                        continue;
                    checkedPairs.Add(pairKey);
                    
                    if (_debug)
                    {
                        Log($"    Checking pair: {wireInfo.Source.NickName} ({wireInfo.Horizontalness:F1}°, {wireInfo.Length:F1}px) vs {otherWire.Source.NickName} ({otherWire.Horizontalness:F1}°, {otherWire.Length:F1}px)");
                    }
                    
                    // Check for intersection
                    if (WiresIntersect(wireInfo, otherWire))
                    {
                        // Determine which wire is more horizontal (smaller angle = more horizontal)
                        WireInfo wireToFaint = wireInfo.Horizontalness > otherWire.Horizontalness ? wireInfo : otherWire;
                        
                        // Only mark as faint if the wire to faint is below the faint threshold (i.e., would normally be DEFAULT)
                        if (wireToFaint.Length < _faintThreshold)
                        {
                            wiresToFaintFromCrossing.Add(wireToFaint.Target.InstanceGuid);
                            if (_debug)
                            {
                                Log($"      Crossing detected: {wireInfo.Source.NickName} -> {wireInfo.Target.NickName} ({wireInfo.Horizontalness:F1}°) crosses {otherWire.Source.NickName} -> {otherWire.Target.NickName} ({otherWire.Horizontalness:F1}°) - more vertical wire FAINTED");
                            }
                        }
                        else
                        {
                            if (_debug)
                            {
                                Log($"      Crossing detected (ignored): {wireInfo.Source.NickName} -> {wireInfo.Target.NickName} ({wireInfo.Horizontalness:F1}°) crosses {otherWire.Source.NickName} -> {otherWire.Target.NickName} ({otherWire.Horizontalness:F1}°) - wire to faint already faint/hidden");
                            }
                        }
                    }
                }
            }
            
            if (_debug)
            {
                Log($"Detected {wiresToFaintFromCrossing.Count} wires to faint from crossings");
            }

            // Now process each unique connection with crossing info
            foreach (var kvp in allConnections)
            {
                ProcessConnection(kvp.Key, kvp.Value, currentWires, processedConnections, wiresToFaintFromCrossing);
            }

            var wiresToRestore = new List<KeyValuePair<Guid, GH_ParamWireDisplay>>();
            
            foreach (var wire in _modifiedWires)
            {
                if (!currentWires.ContainsKey(wire.Key))
                {
                    wiresToRestore.Add(wire);
                }
            }

            foreach (var wire in wiresToRestore)
            {
                var obj = _document.FindObject(wire.Key, false);
                if (obj is IGH_Param param)
                {
                    RestoreWireDisplay(param, wire.Value);
                }
            }

            _modifiedWires = currentWires;
            
            if (_debug)
            {
                Log($"Processed {_wireCount} unique connections");
                Log($"  Modified: {_modifiedCount} connections");
            }
        }

        public string GetDebugLog()
        {
            return _debugLog.ToString();
        }

        public int GetWireCount()
        {
            return _wireCount;
        }

        public int GetModifiedCount()
        {
            return _modifiedCount;
        }

        public void Dispose()
        {
            RestoreAllWires();
        }

        private void Log(string message)
        {
            if (_debug)
            {
                _debugLog.AppendLine(message);
            }
        }

        private List<WireInfo> BuildWireInfoList(List<KeyValuePair<IGH_Param, IGH_Param>> connections)
        {
            var wireInfos = new List<WireInfo>();
            
            foreach (var kvp in connections)
            {
                var target = kvp.Key;
                var source = kvp.Value;
                
                if (source?.Attributes == null || target?.Attributes == null)
                    continue;
                
                var sourceGrip = source.Attributes.OutputGrip;
                var targetGrip = target.Attributes.InputGrip;
                
                PointF p0 = sourceGrip;
                PointF p3 = targetGrip;
                
                double dx = p3.X - p0.X;
                double dy = p3.Y - p0.Y;
                double controlOffset = Math.Abs(dx) * 0.3;
                
                PointF p1, p2;
                if (dx > 0)
                {
                    p1 = new PointF(p0.X + (float)controlOffset, p0.Y);
                    p2 = new PointF(p3.X - (float)controlOffset, p3.Y);
                }
                else
                {
                    p1 = new PointF(p0.X - (float)controlOffset, p0.Y);
                    p2 = new PointF(p3.X + (float)controlOffset, p3.Y);
                }
                
                double length = CalculateBezierLength(p0, p1, p2, p3, 20);
                
                // Calculate bounding box by sampling the actual curve
                var samplePoints = new PointF[21];
                for (int i = 0; i <= 20; i++)
                {
                    double t = (double)i / 20;
                    samplePoints[i] = EvaluateBezier(p0, p1, p2, p3, t);
                }
                
                float minX = samplePoints[0].X, maxX = samplePoints[0].X;
                float minY = samplePoints[0].Y, maxY = samplePoints[0].Y;
                
                for (int i = 1; i < samplePoints.Length; i++)
                {
                    minX = Math.Min(minX, samplePoints[i].X);
                    maxX = Math.Max(maxX, samplePoints[i].X);
                    minY = Math.Min(minY, samplePoints[i].Y);
                    maxY = Math.Max(maxY, samplePoints[i].Y);
                }
                
                // Add small padding to bounding box to catch near misses
                const float padding = 2f;
                var bounds = new RectangleF(minX - padding, minY - padding, maxX - minX + padding * 2, maxY - minY + padding * 2);
                var midpoint = new PointF((minX + maxX) / 2, (minY + maxY) / 2);
                
                // Calculate horizontalness (angle from horizontal, in degrees)
                double horizontalAngle = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                if (horizontalAngle > 90)
                    horizontalAngle = 180 - horizontalAngle; // Keep angle in 0-90 range
                
                wireInfos.Add(new WireInfo
                {
                    Source = source,
                    Target = target,
                    P0 = p0,
                    P1 = p1,
                    P2 = p2,
                    P3 = p3,
                    Bounds = bounds,
                    Length = length,
                    Midpoint = midpoint,
                    Horizontalness = horizontalAngle
                });
                
                if (_debug)
                {
                    Log($"  Wire: {source.NickName} -> {target.NickName}");
                    Log($"    Length: {length:F1}px, Threshold: {_faintThreshold:F1}px");
                    Log($"    Horizontalness: {horizontalAngle:F1}° (0=horizontal, 90=vertical)");
                    Log($"    Bounds: {bounds.X:F1},{bounds.Y:F1} {bounds.Width:F1}x{bounds.Height:F1}");
                }
            }
            
            return wireInfos;
        }
        
        private bool WiresIntersect(WireInfo wire1, WireInfo wire2)
        {
            var segments1 = wire1.GetSegmentPoints();
            var segments2 = wire2.GetSegmentPoints();
            
            if (_debug)
            {
                Log($"      Testing intersection with {wire1.Segments} x {wire2.Segments} segments");
            }
            
            for (int i = 0; i < wire1.Segments; i++)
            {
                for (int j = 0; j < wire2.Segments; j++)
                {
                    if (SegmentsIntersect(
                        segments1[i], segments1[i + 1],
                        segments2[j], segments2[j + 1]))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        private bool SegmentsIntersect(PointF a1, PointF a2, PointF b1, PointF b2)
        {
            float ccw(PointF a, PointF b, PointF c)
            {
                return (c.Y - a.Y) * (b.X - a.X) - (b.Y - a.Y) * (c.X - a.X);
            }
            
            return (ccw(a1, a2, b1) * ccw(a1, a2, b2) <= 0) &&
                   (ccw(b1, b2, a1) * ccw(b1, b2, a2) <= 0);
        }

        private void ProcessConnection(IGH_Param target, IGH_Param source, Dictionary<Guid, GH_ParamWireDisplay> currentWires, HashSet<string> processedConnections, HashSet<Guid> wiresToFaintFromCrossing)
        {
            var connectionId = GetConnectionId(source, target);
            
            if (processedConnections.Contains(connectionId))
            {
                if (_debug)
                {
                    Log($"  Skipping duplicate wire: {source.NickName} -> {target.NickName}");
                }
                return;
            }

            processedConnections.Add(connectionId);
            double length = CalculateWireLength(source, target);
            _wireCount++;

            GH_ParamWireDisplay targetMode;

            if (length > _hiddenThreshold)
            {
                targetMode = GH_ParamWireDisplay.hidden;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px > {_hiddenThreshold:F1}px = HIDDEN");
                }
            }
            else if (length > _faintThreshold)
            {
                targetMode = GH_ParamWireDisplay.faint;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px > {_faintThreshold:F1}px = FAINT");
                }
            }
            else
            {
                // Check if this wire should be fainted due to crossing
                if (wiresToFaintFromCrossing.Contains(target.InstanceGuid))
                {
                    targetMode = GH_ParamWireDisplay.faint;
                    if (_debug)
                    {
                        Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px = FAINT (from crossing)");
                    }
                }
                else
                {
                    targetMode = (GH_ParamWireDisplay)0;
                    if (_debug)
                    {
                        Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px = DEFAULT");
                    }
                }
            }

            if (!_modifiedWires.ContainsKey(target.InstanceGuid))
            {
                if (target.WireDisplay != targetMode)
                {
                    currentWires[target.InstanceGuid] = target.WireDisplay;
                    SetWireDisplay(target, targetMode, target.WireDisplay);
                    _modifiedCount++;
                }
            }
            else
            {
                var originalMode = _modifiedWires[target.InstanceGuid];
                currentWires[target.InstanceGuid] = originalMode;
                
                if (target.WireDisplay != targetMode)
                {
                    SetWireDisplay(target, targetMode, target.WireDisplay);
                }
            }
        }

        private string GetConnectionId(IGH_Param source, IGH_Param target)
        {
            return $"{source.InstanceGuid}_{target.InstanceGuid}";
        }

        private void AddParamConnections(IGH_Param param, List<KeyValuePair<IGH_Param, IGH_Param>> connections)
        {
            if (param == null) return;
            
            if (param.SourceCount > 0)
            {
                for (int i = 0; i < param.SourceCount; i++)
                {
                    var source = param.Sources[i];
                    if (source != null)
                    {
                        connections.Add(new KeyValuePair<IGH_Param, IGH_Param>(param, source));
                    }
                }
            }
        }

        private double CalculateWireLength(IGH_Param source, IGH_Param target)
        {
            if (source?.Attributes == null || target?.Attributes == null)
                return 0;

            var sourceGrip = source.Attributes.OutputGrip;
            var targetGrip = target.Attributes.InputGrip;

            // Approximate wire as a cubic Bezier curve
            // P0 = source grip (start)
            // P3 = target grip (end)
            // P1, P2 = control points that create the curved shape

            PointF p0 = sourceGrip;
            PointF p3 = targetGrip;

            // Calculate horizontal distance
            double dx = p3.X - p0.X;
            double dy = p3.Y - p0.Y;

            // Control points are typically placed at horizontal intervals
            // For a smooth curve from left to right or right to left
            double controlOffset = Math.Abs(dx) * 0.3;

            PointF p1, p2;

            if (dx > 0)
            {
                // Flowing right
                p1 = new PointF(p0.X + (float)controlOffset, p0.Y);
                p2 = new PointF(p3.X - (float)controlOffset, p3.Y);
            }
            else
            {
                // Flowing left
                p1 = new PointF(p0.X - (float)controlOffset, p0.Y);
                p2 = new PointF(p3.X + (float)controlOffset, p3.Y);
            }

            // Calculate Bezier curve length by sampling points along it
            return CalculateBezierLength(p0, p1, p2, p3, 20);
        }

        private double CalculateBezierLength(PointF p0, PointF p1, PointF p2, PointF p3, int segments)
        {
            if (segments < 1) segments = 1;

            double totalLength = 0;
            PointF prevPoint = p0;

            for (int i = 1; i <= segments; i++)
            {
                double t = (double)i / segments;
                PointF currentPoint = EvaluateBezier(p0, p1, p2, p3, t);

                double dx = currentPoint.X - prevPoint.X;
                double dy = currentPoint.Y - prevPoint.Y;
                totalLength += Math.Sqrt(dx * dx + dy * dy);

                prevPoint = currentPoint;
            }

            return totalLength;
        }

        private PointF EvaluateBezier(PointF p0, PointF p1, PointF p2, PointF p3, double t)
        {
            double mt = 1 - t;
            double mt2 = mt * mt;
            double mt3 = mt2 * mt;
            double t2 = t * t;
            double t3 = t2 * t;

            float x = (float)(mt3 * p0.X + 3 * mt2 * t * p1.X + 3 * mt * t2 * p2.X + t3 * p3.X);
            float y = (float)(mt3 * p0.Y + 3 * mt2 * t * p1.Y + 3 * mt * t2 * p2.Y + t3 * p3.Y);

            return new PointF(x, y);
        }

        private void SetWireDisplay(IGH_Param param, GH_ParamWireDisplay newMode, GH_ParamWireDisplay oldMode)
        {
            if (param == null || _document == null) return;

            var record = new GH_UndoRecord("Set Wire Display");
            record.AddAction(new GH_WireDisplayAction(param));
            _document.UndoServer.PushUndoRecord(record);

            param.WireDisplay = newMode;
        }

        private void RestoreWireDisplay(IGH_Param param, GH_ParamWireDisplay originalMode)
        {
            if (param == null || _document == null) return;

            var record = new GH_UndoRecord("Restore Wire Display");
            record.AddAction(new GH_WireDisplayAction(param));
            _document.UndoServer.PushUndoRecord(record);

            param.WireDisplay = originalMode;
        }

        private void RestoreAllWires()
        {
            if (_debug) Log("Restoring all connections");
            
            var wiresToRestore = new List<KeyValuePair<Guid, GH_ParamWireDisplay>>(_modifiedWires);
            
            foreach (var wire in wiresToRestore)
            {
                var obj = _document.FindObject(wire.Key, false);
                if (obj is IGH_Param param)
                {
                    RestoreWireDisplay(param, wire.Value);
                }
            }
            
            _modifiedWires.Clear();
        }
    }
}
