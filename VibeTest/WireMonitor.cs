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

            foreach (var obj in _document.Objects)
            {
                if (obj is IGH_Param param)
                {
                    ProcessParameter(param, currentWires);
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
                Log($"Processed {_wireCount} wires");
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

        private void ProcessParameter(IGH_Param param, Dictionary<Guid, GH_ParamWireDisplay> currentWires)
        {
            if (param == null || param.SourceCount == 0) return;

            var currentMode = param.WireDisplay;

            for (int i = 0; i < param.SourceCount; i++)
            {
                var source = param.Sources[i];
                if (source == null) continue;

                double length = CalculateWireLength(source, param);
                _wireCount++;

                GH_ParamWireDisplay targetMode;

                if (length > _hiddenThreshold)
                {
                    targetMode = GH_ParamWireDisplay.hidden;
                    if (_debug)
                    {
                        Log($"  Wire {source.NickName} -> {param.NickName}: {length:F1}px > {_hiddenThreshold:F1}px = HIDDEN");
                    }
                }
                else if (length > _faintThreshold)
                {
                    targetMode = GH_ParamWireDisplay.faint;
                    if (_debug)
                    {
                        Log($"  Wire {source.NickName} -> {param.NickName}: {length:F1}px > {_faintThreshold:F1}px = FAINT");
                    }
                }
                else
                {
                    targetMode = currentMode;
                    if (_modifiedWires.ContainsKey(param.InstanceGuid) && _debug)
                    {
                        Log($"  Wire {source.NickName} -> {param.NickName}: {length:F1}px <= {_faintThreshold:F1}px = RESTORE");
                    }
                }

                if (targetMode != currentMode)
                {
                    if (targetMode == GH_ParamWireDisplay.hidden || targetMode == GH_ParamWireDisplay.faint)
                    {
                        if (!_modifiedWires.ContainsKey(param.InstanceGuid))
                        {
                            SetWireDisplay(param, targetMode, currentMode);
                            currentWires[param.InstanceGuid] = currentMode;
                            _modifiedCount++;
                        }
                    }
                    else if (_modifiedWires.ContainsKey(param.InstanceGuid))
                    {
                        var originalMode = _modifiedWires[param.InstanceGuid];
                        RestoreWireDisplay(param, originalMode);
                    }
                }
                else if (_modifiedWires.ContainsKey(param.InstanceGuid))
                {
                    var originalMode = _modifiedWires[param.InstanceGuid];
                    RestoreWireDisplay(param, originalMode);
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
