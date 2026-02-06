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
                targetMode = (GH_ParamWireDisplay)0;
                if (_debug)
                {
                    Log($"  Wire {source.NickName} -> {target.NickName}: {length:F1}px <= {_faintThreshold:F1}px = DEFAULT");
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
