using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Grasshopper.Kernel;
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

            foreach (var obj in _document.Objects)
            {
                if (obj is IGH_Param param)
                {
                    ProcessParameter(param, currentWires, processedConnections);
                }
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
                Log($"Processed {_wireCount} unique wires");
                Log($"  Modified: {_modifiedCount} wires");
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

        private void ProcessParameter(IGH_Param param, Dictionary<Guid, GH_ParamWireDisplay> currentWires, HashSet<string> processedConnections)
        {
            if (param == null) return;

            if (param.SourceCount > 0)
            {
                for (int i = 0; i < param.SourceCount; i++)
                {
                    var source = param.Sources[i];
                    if (source == null) continue;

                    var connectionId = GetConnectionId(source, param);
                    
                    if (processedConnections.Contains(connectionId))
                    {
                        if (_debug)
                        {
                            Log($"  Skipping duplicate wire: {source.NickName} -> {param.NickName}");
                        }
                        continue;
                    }

                    processedConnections.Add(connectionId);
                    double length = CalculateWireLength(source, param);
                    _wireCount++;

                    // Determine target display mode based on length
                    GH_ParamWireDisplay targetMode;
                    bool shouldApplyChange = false;

                    if (length > _hiddenThreshold)
                    {
                        targetMode = GH_ParamWireDisplay.hidden;
                        shouldApplyChange = (param.WireDisplay != GH_ParamWireDisplay.hidden);
                        if (_debug)
                        {
                            Log($"  Wire {source.NickName} -> {param.NickName}: {length:F1}px > {_hiddenThreshold:F1}px = HIDDEN");
                        }
                    }
                    else if (length > _faintThreshold)
                    {
                        targetMode = GH_ParamWireDisplay.faint;
                        shouldApplyChange = (param.WireDisplay != GH_ParamWireDisplay.faint);
                        if (_debug)
                        {
                            Log($"  Wire {source.NickName} -> {param.NickName}: {length:F1}px > {_faintThreshold:F1}px = FAINT");
                        }
                    }
                    else
                    {
                        // Wire is under faint threshold - should be DEFAULT
                        targetMode = (GH_ParamWireDisplay)0; // Use 0 to avoid 'default' keyword
                        shouldApplyChange = (param.WireDisplay != (GH_ParamWireDisplay)0);
                        if (_debug)
                        {
                            Log($"  Wire {source.NickName} -> {param.NickName}: {length:F1}px <= {_faintThreshold:F1}px = DEFAULT");
                        }
                    }

                    // Apply change if needed
                    if (shouldApplyChange)
                    {
                        if (!_modifiedWires.ContainsKey(param.InstanceGuid))
                        {
                            // First time modifying this param - save original mode
                            SetWireDisplay(param, targetMode, param.WireDisplay);
                            currentWires[param.InstanceGuid] = param.WireDisplay;
                            _modifiedCount++;
                        }
                        else
                        {
                            // Param was already modified - restore to its original mode
                            var originalMode = _modifiedWires[param.InstanceGuid];
                            
                            // Only restore if we're changing away from what we set
                            if (param.WireDisplay != targetMode)
                            {
                                RestoreWireDisplay(param, originalMode);
                            }
                        }
                    }
                    else if (_modifiedWires.ContainsKey(param.InstanceGuid))
                    {
                        // Wire already has correct display, remove from tracking
                        var originalMode = _modifiedWires[param.InstanceGuid];
                        RestoreWireDisplay(param, originalMode);
                    }
                }
            }
        }

        private string GetConnectionId(IGH_Param source, IGH_Param target)
        {
            return $"{source.InstanceGuid}_{target.InstanceGuid}";
        }

        private double CalculateWireLength(IGH_Param source, IGH_Param target)
        {
            if (source?.Attributes == null || target?.Attributes == null)
                return 0;

            PointF sourcePos = source.Attributes.Pivot;
            PointF targetPos = target.Attributes.Pivot;

            double dx = targetPos.X - sourcePos.X;
            double dy = targetPos.Y - sourcePos.Y;

            return Math.Sqrt(dx * dx + dy * dy);
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
            if (_debug) Log("Restoring all wires");
            
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
