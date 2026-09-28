using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;

namespace HopperWire
{
    internal class WireInfo
    {
        public IGH_Param Target { get; set; }
        public IGH_Param Source { get; set; }
        public PointF P0 { get; set; } // Start point
        public PointF P1 { get; set; } // Control point 1
        public PointF P2 { get; set; } // Control point 2
        public PointF P3 { get; set; } // End point
        public RectangleF Bounds { get; set; }
        public double Length { get; set; }
        public double Horizontalness { get; set; } // Angle in degrees (0 = horizontal, 90 = vertical)
        public int Segments { get; set; } = 15;
        private PointF[] _segmentPoints;
        
        public PointF[] GetSegmentPoints()
        {
            if (_segmentPoints != null) return _segmentPoints;
            var points = new PointF[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                double t = (double)i / Segments;
                points[i] = EvaluateBezier(t);
            }
            _segmentPoints = points;
            return _segmentPoints;
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
    
    internal class SpatialGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<(int, int), List<WireInfo>> _cells;
        private readonly List<WireInfo> _allWires = new List<WireInfo>();
        private readonly List<WireInfo> _largeWires = new List<WireInfo>();
        
        public SpatialGrid(float cellSize)
        {
            if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize;
            _cells = new Dictionary<(int, int), List<WireInfo>>();
        }
        
        public void Insert(WireInfo wire)
        {
            _allWires.Add(wire);
            if (!TryGetCellRange(wire.Bounds, out var cellRange))
            {
                _largeWires.Add(wire);
                return;
            }
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
            if (!TryGetCellRange(bounds, out var cellRange))
            {
                foreach (var wire in _allWires)
                    if (wire.Bounds.IntersectsWith(bounds)) result.Add(wire);
                return new List<WireInfo>(result);
            }
            
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

            foreach (var wire in _largeWires)
                if (wire.Bounds.IntersectsWith(bounds)) result.Add(wire);
            
            return new List<WireInfo>(result);
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

    internal sealed class WireMonitor
    {
        private GH_Document _document;
        private double _faintThreshold;
        private double _hiddenThreshold;
        private float _spatialGridSize;
        private bool _debug;
        private bool _skipUndo;
        private Dictionary<Guid, GH_ParamWireDisplay> _modifiedWires;
        private Dictionary<Guid, GH_ParamWireDisplay> _appliedModes;
        private StringBuilder _debugLog;
        private int _wireCount;
        private int _modifiedCount;

        public WireMonitor(GH_Document document, double faintThreshold, double hiddenThreshold, float spatialGridSize, bool debug, bool skipUndo = false)
        {
            _document = document;
            _faintThreshold = faintThreshold;
            _hiddenThreshold = hiddenThreshold;
            _spatialGridSize = spatialGridSize;
            _debug = debug;
            _skipUndo = skipUndo;
            _modifiedWires = new Dictionary<Guid, GH_ParamWireDisplay>();
            _appliedModes = new Dictionary<Guid, GH_ParamWireDisplay>();
            _debugLog = new StringBuilder();
            _wireCount = 0;
            _modifiedCount = 0;

            if (_debug)
            {
                Log("WireMonitor created");
                Log($"  Faint Threshold: {_faintThreshold:F1} pixels");
                Log($"  Hidden Threshold: {_hiddenThreshold:F1} pixels");
                Log($"  Spatial Grid Size: {_spatialGridSize:F1} pixels");
                Log($"  Debug Mode: {_debug}");
            }
        }

        public void UpdateSettings(double faintThreshold, double hiddenThreshold, float spatialGridSize, bool debug, bool skipUndo)
        {
            _faintThreshold = faintThreshold;
            _hiddenThreshold = hiddenThreshold;
            _spatialGridSize = spatialGridSize;
            _debug = debug;
            _skipUndo = skipUndo;
            _debugLog.Clear();
        }

        public void ProcessAllWires()
        {
            if (_document == null) return;

            _debugLog.Clear();
            var targetModes = new Dictionary<Guid, GH_ParamWireDisplay>();
            var targets = new Dictionary<Guid, IGH_Param>();
            _wireCount = 0;
            _modifiedCount = 0;
            
            var seenConnections = new HashSet<(Guid, Guid)>();
            
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
                    AddParamConnections(param, allConnections, seenConnections);
                }
                
                // Process component parameters (nested inputs/outputs)
                if (obj is IGH_Component component)
                {
                    foreach (var input in component.Params.Input)
                    {
                        AddParamConnections(input, allConnections, seenConnections);
                    }
                    foreach (var output in component.Params.Output)
                    {
                        AddParamConnections(output, allConnections, seenConnections);
                    }
                }
            }
            
            if (_debug)
            {
                Log($"Collected {allConnections.Count} connection entries");
            }

            // Build wire info list for crossing detection
            var wireInfos = BuildWireInfoList(allConnections);
            var wireLengths = new Dictionary<(Guid, Guid), double>();
            foreach (var wire in wireInfos)
                wireLengths.Add((wire.Source.InstanceGuid, wire.Target.InstanceGuid), wire.Length);
            var layoutFaintReasons = FindLayoutFaintReasons(wireInfos);
            
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
            var checkedPairs = new HashSet<((Guid, Guid), (Guid, Guid))>();
            
            if (_debug)
            {
                Log($"Starting crossing detection with {wireInfos.Count} wires");
            }
            
            for (int i = 0; i < wireInfos.Count; i++)
            {
                var wireInfo = wireInfos[i];
                
                // Only check wires below faint threshold for crossing-based fainting
                if (wireInfo.Length > _faintThreshold)
                {
                    if (_debug)
                    {
                        Log($"  Skipping {wireInfo.Source.NickName} -> {wireInfo.Target.NickName}: length {wireInfo.Length:F1}px > threshold {_faintThreshold:F1}px (already faint/hidden)");
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
                    if (wireInfo.Source == otherWire.Source || wireInfo.Target == otherWire.Target)
                    {
                        if (_debug)
                        {
                            Log($"    Skipped: wires share same source {wireInfo.Source.NickName}");
                        }
                        continue;
                    }
                    
                    // A target can have multiple sources, so identify the actual wires.
                    var first = (wireInfo.Source.InstanceGuid, wireInfo.Target.InstanceGuid);
                    var second = (otherWire.Source.InstanceGuid, otherWire.Target.InstanceGuid);
                    var pairKey = first.Item1.CompareTo(second.Item1) < 0 ||
                                  (first.Item1 == second.Item1 && first.Item2.CompareTo(second.Item2) < 0)
                        ? (first, second) : (second, first);
                    
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
                        // Equal angles provide no basis for choosing one wire.
                        if (wireInfo.Horizontalness == otherWire.Horizontalness)
                            continue;
                        // Determine which wire is more horizontal (smaller angle = more horizontal)
                        WireInfo wireToFaint = wireInfo.Horizontalness > otherWire.Horizontalness ? wireInfo : otherWire;
                        
                        // Only mark as faint if the wire to faint is below the faint threshold (i.e., would normally be DEFAULT)
                        if (wireToFaint.Length <= _faintThreshold)
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
                wireLengths.TryGetValue((kvp.Value.InstanceGuid, kvp.Key.InstanceGuid), out var length);
                ProcessConnection(kvp.Key, kvp.Value, length, targetModes, targets,
                    wiresToFaintFromCrossing, layoutFaintReasons);
            }

            // WireDisplay belongs to the target parameter, not to each incoming source.
            // The most restrictive mode therefore wins across all its connections.
            foreach (var mode in targetModes)
            {
                ApplyTargetMode(targets[mode.Key], mode.Value);
            }

            foreach (var wire in new List<KeyValuePair<Guid, GH_ParamWireDisplay>>(_modifiedWires))
            {
                if (targetModes.ContainsKey(wire.Key)) continue;
                if (_document.FindObject(wire.Key, false) is IGH_Param param &&
                    _appliedModes.TryGetValue(wire.Key, out var applied) && param.WireDisplay == applied)
                {
                    RestoreWireDisplay(param, wire.Value);
                    _modifiedCount++;
                }
                _modifiedWires.Remove(wire.Key);
                _appliedModes.Remove(wire.Key);
            }

            if (_modifiedCount > 0)
                _document.IsModified = true;
            
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
            // Wire styles are document data and must survive removal of this component.
            _modifiedWires.Clear();
            _appliedModes.Clear();
            _document = null;
        }

        private void Log(string message)
        {
            if (_debug)
            {
                _debugLog.AppendLine(message);
            }
        }

        private Dictionary<Guid, string> FindLayoutFaintReasons(List<WireInfo> wires)
        {
            var ownerByParam = new Dictionary<Guid, Guid>();
            var componentBounds = new List<(Guid Id, RectangleF Bounds)>();
            var groupsByOwner = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var obj in _document.Objects)
            {
                if (obj is IGH_Component component)
                {
                    var id = component.InstanceGuid;
                    foreach (var input in component.Params.Input)
                        ownerByParam[input.InstanceGuid] = id;
                    foreach (var output in component.Params.Output)
                        ownerByParam[output.InstanceGuid] = id;
                    if (component.Attributes != null)
                        componentBounds.Add((id, component.Attributes.Bounds));
                }
                else if (obj is GH_Group group)
                {
                    foreach (var member in group.ObjectsRecursive())
                    {
                        if (member == null) continue;
                        if (!groupsByOwner.TryGetValue(member.InstanceGuid, out var memberships))
                        {
                            memberships = new HashSet<Guid>();
                            groupsByOwner.Add(member.InstanceGuid, memberships);
                        }
                        memberships.Add(group.InstanceGuid);
                    }
                }
            }

            var reasons = new Dictionary<Guid, string>();
            foreach (var wire in wires)
            {
                // These rules describe an individual path. A multi-source target has
                // only one display setting, so changing it would affect other paths.
                if (wire.Target.SourceCount != 1 || wire.Length > _faintThreshold)
                    continue;

                Guid sourceOwner = ownerByParam.TryGetValue(wire.Source.InstanceGuid, out var sourceId)
                    ? sourceId : wire.Source.InstanceGuid;
                Guid targetOwner = ownerByParam.TryGetValue(wire.Target.InstanceGuid, out var targetId)
                    ? targetId : wire.Target.InstanceGuid;
                groupsByOwner.TryGetValue(sourceOwner, out var sourceGroups);
                groupsByOwner.TryGetValue(targetOwner, out var targetGroups);

                bool groupBoundary = WireLayoutRules.CrossesGroupBoundary(sourceGroups, targetGroups);
                bool backward = WireLayoutRules.IsBackward(wire.P0, wire.P3);
                bool throughComponent = false;
                if (_debug || (!groupBoundary && !backward))
                {
                    foreach (var component in componentBounds)
                    {
                        if (component.Id == sourceOwner || component.Id == targetOwner ||
                            !component.Bounds.IntersectsWith(wire.Bounds))
                            continue;
                        if (WireLayoutRules.PassesThroughRectangle(wire.GetSegmentPoints(), component.Bounds))
                        {
                            throughComponent = true;
                            break;
                        }
                    }
                }
                if (!groupBoundary && !backward && !throughComponent) continue;
                if (_debug)
                {
                    var labels = new List<string>();
                    if (groupBoundary) labels.Add("group boundary");
                    if (backward) labels.Add("backward flow");
                    if (throughComponent) labels.Add("through component");
                    reasons[wire.Target.InstanceGuid] = string.Join(", ", labels);
                }
                else reasons[wire.Target.InstanceGuid] = "";
            }
            return reasons;
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
        
        private static bool SegmentsIntersect(PointF a1, PointF a2, PointF b1, PointF b2)
        {
            double Cross(PointF a, PointF b, PointF c)
            {
                return ((double)b.X - a.X) * (c.Y - a.Y) -
                       ((double)b.Y - a.Y) * (c.X - a.X);
            }

            // Shared endpoints and collinear overlap are not wire crossings.
            var ab1 = Cross(a1, a2, b1);
            var ab2 = Cross(a1, a2, b2);
            var ba1 = Cross(b1, b2, a1);
            var ba2 = Cross(b1, b2, a2);
            return ((ab1 > 0 && ab2 < 0) || (ab1 < 0 && ab2 > 0)) &&
                   ((ba1 > 0 && ba2 < 0) || (ba1 < 0 && ba2 > 0));
        }

        private void ProcessConnection(IGH_Param target, IGH_Param source, double length,
            Dictionary<Guid, GH_ParamWireDisplay> targetModes, Dictionary<Guid, IGH_Param> targets,
            HashSet<Guid> wiresToFaintFromCrossing, Dictionary<Guid, string> layoutFaintReasons)
        {
            _wireCount++;

            bool crossing = wiresToFaintFromCrossing.Contains(target.InstanceGuid);
            bool layout = layoutFaintReasons.TryGetValue(target.InstanceGuid, out var reason);
            var decision = WireLayoutRules.DecideDisplay(length, _faintThreshold, _hiddenThreshold,
                crossing || layout);
            GH_ParamWireDisplay targetMode = decision == WireDecision.Hidden
                ? GH_ParamWireDisplay.hidden
                : decision == WireDecision.Faint ? GH_ParamWireDisplay.faint : GH_ParamWireDisplay.@default;
            if (_debug)
            {
                string explanation = decision == WireDecision.Hidden ? "length" :
                    length > _faintThreshold ? "length" :
                    (crossing ? "crossing" : "") + (crossing && layout ? ", " : "") +
                    (layout ? reason : "");
                Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px = {decision}" +
                    (explanation.Length > 0 ? $" ({explanation})" : ""));
            }

            targets[target.InstanceGuid] = target;
            if (!targetModes.TryGetValue(target.InstanceGuid, out var previous) || targetMode > previous)
                targetModes[target.InstanceGuid] = targetMode;
        }

        private void ApplyTargetMode(IGH_Param target, GH_ParamWireDisplay mode)
        {
            var id = target.InstanceGuid;
            bool wasManaged = _modifiedWires.TryGetValue(id, out var saved);
            if (!wasManaged && mode == GH_ParamWireDisplay.@default)
                return; // A short wire must not erase a user's existing display choice.
            var original = wasManaged ? saved : target.WireDisplay;
            if (_appliedModes.TryGetValue(id, out var applied) && target.WireDisplay != applied)
                original = target.WireDisplay; // A user changed this parameter since our last pass.

            if (mode == GH_ParamWireDisplay.@default)
                mode = original;
            else if (mode < original)
                mode = original; // Keep a user's more restrictive display choice.

            if (target.WireDisplay != mode)
            {
                SetWireDisplay(target, mode);
                _modifiedCount++;
            }

            if (mode == original)
            {
                _modifiedWires.Remove(id);
                _appliedModes.Remove(id);
            }
            else
            {
                _modifiedWires[id] = original;
                _appliedModes[id] = mode;
            }
        }

        private void AddParamConnections(IGH_Param param, List<KeyValuePair<IGH_Param, IGH_Param>> connections,
            HashSet<(Guid, Guid)> seen)
        {
            if (param == null) return;
            
            if (param.SourceCount > 0)
            {
                for (int i = 0; i < param.SourceCount; i++)
                {
                    var source = param.Sources[i];
                    if (source != null && seen.Add((source.InstanceGuid, param.InstanceGuid)))
                    {
                        connections.Add(new KeyValuePair<IGH_Param, IGH_Param>(param, source));
                    }
                }
            }
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

        private void SetWireDisplay(IGH_Param param, GH_ParamWireDisplay newMode)
        {
            if (param == null || _document == null) return;

            if (!_skipUndo)
            {
                var record = new GH_UndoRecord("Set Wire Display");
                record.AddAction(new GH_WireDisplayAction(param));
                _document.UndoServer.PushUndoRecord(record);
            }

            param.WireDisplay = newMode;
        }

        private void RestoreWireDisplay(IGH_Param param, GH_ParamWireDisplay originalMode)
        {
            if (param == null || _document == null) return;

            if (!_skipUndo)
            {
                var record = new GH_UndoRecord("Restore Wire Display");
                record.AddAction(new GH_WireDisplayAction(param));
                _document.UndoServer.PushUndoRecord(record);
            }

            param.WireDisplay = originalMode;
        }

    }
}
