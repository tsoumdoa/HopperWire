using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;

namespace VibeTest
{
    public class WireMonitor
    {
        private GH_Document _document;
        private double _faintThreshold;
        private double _hiddenThreshold;
        private bool _debug;
        private Dictionary<Guid, GH_ParamWireDisplay> _modifiedWires;
        private StringBuilder _debugLog;
        private int _wireCount;
        private int _modifiedCount;
        private HashSet<Guid> _paramsWithRelays;
        private Dictionary<Guid, Guid> _parameterToIncomingRelayMap;
        private Dictionary<Guid, Guid> _parameterToOutgoingRelayMap;

        public WireMonitor(GH_Document document, double faintThreshold, double hiddenThreshold, bool debug)
        {
            _document = document;
            _faintThreshold = faintThreshold;
            _hiddenThreshold = hiddenThreshold;
            _debug = debug;
            _modifiedWires = new Dictionary<Guid, GH_ParamWireDisplay>();
            _debugLog = new StringBuilder();
            _wireCount = 0;
            _modifiedCount = 0;
            _paramsWithRelays = new HashSet<Guid>();
            _parameterToIncomingRelayMap = new Dictionary<Guid, Guid>();
            _parameterToOutgoingRelayMap = new Dictionary<Guid, Guid>();

            if (_debug)
            {
                Log("WireMonitor created (manual trigger mode)");
                Log($"  Faint Threshold: {_faintThreshold:F1} pixels");
                Log($"  Hidden Threshold: {_hiddenThreshold:F1} pixels");
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

            // Now process each unique connection
            foreach (var kvp in allConnections)
            {
                ProcessConnection(kvp.Key, kvp.Value, currentWires, processedConnections);
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

        public void ProcessRelaysForNewObjects()
        {
            ProcessRelays();
            RouteNewConnectionsThroughRelays();
        }

        private void RouteNewConnectionsThroughRelays()
        {
            if (_debug)
            {
                Log("Routing new connections through existing relays");
            }

            var routedConnections = new HashSet<string>();

            foreach (var obj in _document.Objects)
            {
                IGH_Param param = null;

                if (obj is IGH_Param p)
                {
                    param = p;
                    CheckAndRouteConnections(param, routedConnections);
                }
                else if (obj is IGH_Component component)
                {
                    foreach (var input in component.Params.Input)
                    {
                        CheckAndRouteConnections(input, routedConnections);
                    }
                    foreach (var output in component.Params.Output)
                    {
                        CheckAndRouteConnections(output, routedConnections);
                    }
                }
            }
        }

        private void CheckAndRouteConnections(IGH_Param param, HashSet<string> routedConnections)
        {
            if (param == null) return;

            var paramGuid = param.InstanceGuid;

            IGH_Param relay = null;

            if (_parameterToIncomingRelayMap.ContainsKey(paramGuid))
            {
                var relayGuid = _parameterToIncomingRelayMap[paramGuid];
                relay = _document.FindObject(relayGuid, false) as IGH_Param;
            }

            if (relay == null && _parameterToOutgoingRelayMap.ContainsKey(paramGuid))
            {
                var relayGuid = _parameterToOutgoingRelayMap[paramGuid];
                relay = _document.FindObject(relayGuid, false) as IGH_Param;
            }

            if (relay == null) return;

            for (int i = 0; i < param.SourceCount; i++)
            {
                var source = param.Sources[i];
                if (source == null) continue;

                var sourceGuid = source.InstanceGuid;
                var connectionId = $"{sourceGuid}_{paramGuid}";

                if (routedConnections.Contains(connectionId)) continue;

                if (source != relay && !IsRelay(source))
                {
                    try
                    {
                        if (_debug)
                        {
                            Log($"  Routing {source.NickName} -> {param.NickName} through relay {relay.NickName}");
                        }

                        param.RemoveSource(source);
                        param.AddSource(relay);
                        routedConnections.Add(connectionId);
                    }
                    catch (Exception ex)
                    {
                        if (_debug)
                        {
                            Log($"  Error routing connection: {ex.Message}");
                        }
                    }
                }
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
            _paramsWithRelays.Clear();
            _parameterToIncomingRelayMap.Clear();
            _parameterToOutgoingRelayMap.Clear();
            RestoreAllWires();
        }

        private void Log(string message)
        {
            if (!_debug) return;
            
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _debugLog.AppendLine($"[{timestamp}] {message}");
            
            try
            {
                Rhino.RhinoApp.WriteLine($"[WireMonitor] {message}");
            }
            catch
            {
                // Ignore errors when Rhino is not available
            }
        }

        private void ProcessConnection(IGH_Param target, IGH_Param source, Dictionary<Guid, GH_ParamWireDisplay> currentWires, HashSet<string> processedConnections)
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
            int sourceWireCount = (source != null && source.Recipients != null) ? source.Recipients.Count : 0;
            int targetWireCount = target.SourceCount;
            int connectionCount = Math.Max(sourceWireCount, targetWireCount);
            _wireCount++;

            if (_debug)
            {
                Log($"  Checking {source.NickName} -> {target.NickName}: sourceOut={sourceWireCount}, targetIn={targetWireCount}, max={connectionCount}");
            }

            GH_ParamWireDisplay targetMode;

            if (length > _hiddenThreshold)
            {
                targetMode = GH_ParamWireDisplay.hidden;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px > {_hiddenThreshold:F1}px, {connectionCount} conns = HIDDEN (by length)");
                }
            }
            else if (length > _faintThreshold)
            {
                targetMode = GH_ParamWireDisplay.faint;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px > {_faintThreshold:F1}px, {connectionCount} conns = FAINT (by length)");
                }
            }
            else
            {
                targetMode = (GH_ParamWireDisplay)0;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px <= {_faintThreshold:F1}px, {connectionCount} conns = DEFAULT");
                }
            }

            if (connectionCount > 8)
            {
                targetMode = GH_ParamWireDisplay.hidden;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {connectionCount} connections > 8 = HIDDEN (overriding length-based)");
                }
            }
            else if (connectionCount > 3)
            {
                targetMode = GH_ParamWireDisplay.faint;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {connectionCount} connections > 3 = FAINT (overriding length-based)");
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

        private IGH_Param CreateRelay(IGH_Param referenceParam, string nameSuffix)
        {
            if (referenceParam == null) return null;

            IGH_Param relay = new Param_GenericObject();
            
            relay.NickName = $"{referenceParam.NickName}{nameSuffix}";
            relay.MutableNickName = true;
            relay.CreateAttributes();

            return relay;
        }

        private void ProcessRelays()
        {
            var parametersNeedingRelay = new List<IGH_Param>();
            var currentRunRelays = new HashSet<Guid>();

            foreach (var obj in _document.Objects)
            {
                IGH_Param param = null;

                if (obj is IGH_Param p)
                {
                    param = p;
                }
                else if (obj is IGH_Component component)
                {
                    foreach (var input in component.Params.Input)
                    {
                        if (NeedsRelay(input) && !_paramsWithRelays.Contains(input.InstanceGuid) && !IsRelay(input) && !currentRunRelays.Contains(input.InstanceGuid))
                        {
                            parametersNeedingRelay.Add(input);
                        }
                    }
                    foreach (var output in component.Params.Output)
                    {
                        if (NeedsRelay(output) && !_paramsWithRelays.Contains(output.InstanceGuid) && !IsRelay(output) && !currentRunRelays.Contains(output.InstanceGuid))
                        {
                            parametersNeedingRelay.Add(output);
                        }
                    }
                }

                if (param != null && NeedsRelay(param) && !_paramsWithRelays.Contains(param.InstanceGuid) && !IsRelay(param) && !currentRunRelays.Contains(param.InstanceGuid))
                {
                    parametersNeedingRelay.Add(param);
                }
            }

            foreach (var param in parametersNeedingRelay)
            {
                var relayGuid = AddRelayForParameter(param);
                if (relayGuid.HasValue)
                {
                    currentRunRelays.Add(relayGuid.Value);
                    _paramsWithRelays.Add(param.InstanceGuid);
                }
            }
        }

        private bool IsRelay(IGH_Param param)
        {
            if (param == null) return false;
            return param.NickName != null && param.NickName.EndsWith("_Relay");
        }

        private bool NeedsRelay(IGH_Param param)
        {
            if (param == null) return false;

            int incomingCount = param.SourceCount;
            int outgoingCount = (param.Recipients != null) ? param.Recipients.Count : 0;

            return incomingCount > 3 || outgoingCount > 3;
        }

        private Guid? AddRelayForParameter(IGH_Param param)
        {
            if (param == null || param.Attributes == null) return null;

            var incomingCount = param.SourceCount;
            var outgoingCount = (param.Recipients != null) ? param.Recipients.Count : 0;

            var relayForIncoming = incomingCount > 3;
            var relayForOutgoing = outgoingCount > 3;

            Guid? relayGuid = null;

            if (relayForIncoming)
            {
                relayGuid = AddIncomingRelay(param);
                if (relayGuid.HasValue)
                {
                    _parameterToIncomingRelayMap[param.InstanceGuid] = relayGuid.Value;
                }
            }

            if (relayForOutgoing)
            {
                relayGuid = AddOutgoingRelay(param);
                if (relayGuid.HasValue)
                {
                    _parameterToOutgoingRelayMap[param.InstanceGuid] = relayGuid.Value;
                }
            }

            return relayGuid;
        }

        private Guid? AddIncomingRelay(IGH_Param targetParam)
        {
            if (targetParam == null || targetParam.Attributes == null) return null;

            var sources = new List<IGH_Param>();
            for (int i = 0; i < targetParam.SourceCount; i++)
            {
                var source = targetParam.Sources[i];
                if (source != null)
                {
                    sources.Add(source);
                }
            }

            if (sources.Count == 0) return null;

            var relay = CreateRelay(targetParam, "_Relay");
            if (relay == null) return null;

            var targetGrip = targetParam.Attributes.InputGrip;
            float totalX = 0;
            float totalY = 0;

            foreach (var source in sources)
            {
                if (source?.Attributes != null)
                {
                    totalX += source.Attributes.OutputGrip.X;
                    totalY += source.Attributes.OutputGrip.Y;
                }
            }

            float avgX = totalX / sources.Count;
            float avgY = totalY / sources.Count;

            var relayX = (avgX + targetGrip.X) / 2f;
            var relayY = (avgY + targetGrip.Y) / 2f;

            if (relay.Attributes != null)
            {
                relay.Attributes.Pivot = new PointF(relayX, relayY);
            }

            _document.AddObject(relay, true);

            if (_debug)
            {
                Log($"  Added incoming relay {relay.NickName} for {targetParam.NickName} ({sources.Count} sources)");
                Log($"  Relay position: ({relayX:F1}, {relayY:F1})");
            }

            foreach (var source in sources)
            {
                try
                {
                    targetParam.RemoveSource(source);
                    relay.AddSource(source);
                }
                catch (Exception ex)
                {
                    if (_debug)
                    {
                        Log($"  Error rewiring {source?.NickName} -> {targetParam.NickName}: {ex.Message}");
                    }
                }
            }

            try
            {
                targetParam.AddSource(relay);
            }
            catch (Exception ex)
            {
                if (_debug)
                {
                    Log($"  Error connecting relay -> targetParam: {ex.Message}");
                }
            }

            return relay.InstanceGuid;
        }

        private Guid? AddOutgoingRelay(IGH_Param sourceParam)
        {
            if (sourceParam == null || sourceParam.Attributes == null || sourceParam.Recipients == null) return null;

            var targets = new List<IGH_Param>(sourceParam.Recipients);

            if (targets.Count == 0) return null;

            var relay = CreateRelay(sourceParam, "_Relay");
            if (relay == null) return null;

            var sourceGrip = sourceParam.Attributes.OutputGrip;
            float totalX = 0;
            float totalY = 0;

            foreach (var target in targets)
            {
                if (target?.Attributes != null)
                {
                    totalX += target.Attributes.InputGrip.X;
                    totalY += target.Attributes.InputGrip.Y;
                }
            }

            float avgX = totalX / targets.Count;
            float avgY = totalY / targets.Count;

            var relayX = (avgX + sourceGrip.X) / 2f;
            var relayY = (avgY + sourceGrip.Y) / 2f;

            if (relay.Attributes != null)
            {
                relay.Attributes.Pivot = new PointF(relayX, relayY);
            }

            _document.AddObject(relay, true);

            var relayGuid = relay.InstanceGuid;

            if (_debug)
            {
                Log($"  Added outgoing relay {relay.NickName} for {sourceParam.NickName} ({targets.Count} targets)");
                Log($"  Relay position: ({relayX:F1}, {relayY:F1})");
            }

            if (_debug)
            {
                Log($"  About to rewire {targets.Count} targets from {sourceParam.NickName}");
                Log($"  Relay instance GUID: {relayGuid}");
            }

            foreach (var target in targets)
            {
                try
                {
                    target.RemoveSource(sourceParam);
                    target.AddSource(relay);
                    _parameterToOutgoingRelayMap[target.InstanceGuid] = relayGuid;
                    if (_debug)
                    {
                        Log($"  Rewired {sourceParam.NickName} -> {relay.NickName} -> {target.NickName}");
                        Log($"  Mapped target {target.NickName} to relay {relay.NickName}");
                    }
                }
                catch (Exception ex)
                {
                    if (_debug)
                    {
                        Log($"  Error rewiring {sourceParam.NickName} -> {target?.NickName}: {ex.Message}");
                    }
                }
            }

            if (_debug)
            {
                Log($"  About to connect {sourceParam.NickName} -> {relay.NickName}");
                Log($"  Relay current source count: {relay.SourceCount}");
                Log($"  Source param recipients: {sourceParam.Recipients?.Count ?? 0}");
            }

            try
            {
                relay.AddSource(sourceParam);
                if (_debug)
                {
                    Log($"  Successfully connected {sourceParam.NickName} -> {relay.NickName}");
                    Log($"  Relay now has {relay.SourceCount} sources");
                    Log($"  Source param now has {sourceParam.Recipients?.Count ?? 0} recipients");
                    Log($"  Relay now has {relay.Recipients?.Count ?? 0} recipients");
                }
            }
            catch (Exception ex)
            {
                if (_debug)
                {
                    Log($"  ERROR connecting sourceParam -> relay: {ex.GetType().Name}: {ex.Message}");
                    Log($"  Stack trace: {ex.StackTrace}");
                }
            }

            return relayGuid;
        }
    }
}
