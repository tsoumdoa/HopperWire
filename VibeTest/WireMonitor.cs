using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;
using Rhino.Geometry;

namespace VibeTest
{
    public class WireMonitor
    {
        private GH_Document _document;
        private double _lengthThreshold;
        private Dictionary<Guid, GH_ParamWireDisplay> _modifiedWires;

        public WireMonitor(GH_Document document, double lengthThreshold)
        {
            _document = document;
            _lengthThreshold = lengthThreshold;
            _modifiedWires = new Dictionary<Guid, GH_ParamWireDisplay>();
        }

        public void InitializeEvents()
        {
            if (_document == null) return;

            _document.ObjectsAdded += OnObjectsAdded;
            _document.ObjectsDeleted += OnObjectsDeleted;
            _document.SettingsChanged += OnSettingsChanged;
        }

        public void DisposeEvents()
        {
            if (_document == null) return;

            _document.ObjectsAdded -= OnObjectsAdded;
            _document.ObjectsDeleted -= OnObjectsDeleted;
            _document.SettingsChanged -= OnSettingsChanged;

            RestoreAllWires();
        }

        public void UpdateThreshold(double newThreshold)
        {
            _lengthThreshold = newThreshold;
            ProcessAllWires();
        }

        private void OnObjectsAdded(object sender, GH_DocObjectEventArgs e)
        {
            ProcessAllWires();
        }

        private void OnObjectsDeleted(object sender, GH_DocObjectEventArgs e)
        {
            CleanDeletedWires();
            ProcessAllWires();
        }

        private void OnSettingsChanged(object sender, GH_DocSettingsEventArgs e)
        {
            ProcessAllWires();
        }

        private void CleanDeletedWires()
        {
            var keysToRemove = new List<Guid>();
            
            foreach (var paramId in _modifiedWires.Keys)
            {
                var obj = _document.FindObject(paramId, false);
                if (obj == null)
                {
                    keysToRemove.Add(paramId);
                }
            }

            foreach (var key in keysToRemove)
            {
                _modifiedWires.Remove(key);
            }
        }

        private void ProcessAllWires()
        {
            if (_document == null) return;

            var currentWires = new Dictionary<Guid, GH_ParamWireDisplay>();

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

                if (length > _lengthThreshold)
                {
                    if (!_modifiedWires.ContainsKey(param.InstanceGuid))
                    {
                        if (currentMode != GH_ParamWireDisplay.faint)
                        {
                            SetWireDisplay(param, GH_ParamWireDisplay.faint, currentMode);
                            currentWires[param.InstanceGuid] = currentMode;
                        }
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

            var record = new GH_UndoRecord("Set Wire Faint");
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
