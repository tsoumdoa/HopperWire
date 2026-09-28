using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace HopperWire
{
    public class HopperWire : GH_Component
    {
        private const double DefaultFaint = 800;
        private const double DefaultHidden = 1500;
        private const double DefaultGrid = 200;
        private WireMonitor _monitor;
        private GH_Document _monitorDocument;
        private GH_Document _document;
        private readonly Dictionary<Guid, RectangleF> _bounds = new Dictionary<Guid, RectangleF>();
        private readonly HashSet<(Guid, Guid)> _connections = new HashSet<(Guid, Guid)>();
        private readonly HashSet<(Guid, Guid)> _groupMembership = new HashSet<(Guid, Guid)>();
        private double _lastFaint = DefaultFaint, _lastHidden = DefaultHidden, _lastGrid = DefaultGrid;
        private bool _lastDebug, _lastRefresh, _autoUpdate, _processing, _saving;
        private bool _snapshotReady;
        private string _processError;

        public HopperWire() : base("Hopper Wire", "HopperWire",
            "Manage wire display by length and canvas layout", "Params", "Util") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("Faint Threshold", "Faint", "Faint above this canvas length", GH_ParamAccess.item, DefaultFaint);
            p.AddNumberParameter("Hidden Threshold", "Hidden", "Hide above this canvas length", GH_ParamAccess.item, DefaultHidden);
            p.AddNumberParameter("Spatial Grid Size", "Grid", "Cell size for crossing detection", GH_ParamAccess.item, DefaultGrid);
            p.AddBooleanParameter("Auto Update", "Auto", "Check for layout changes when the document is saved", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Refresh", "Refresh", "Toggle false to true to update", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Debug", "Debug", "Include detailed processing messages", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Status", "Status", "Wire monitor status", GH_ParamAccess.item);
            p.AddTextParameter("Log", "Log", "Debug log", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            double faint = DefaultFaint, hidden = DefaultHidden, grid = DefaultGrid;
            bool auto = true, refresh = false, debug = false;
            da.GetData(0, ref faint);
            da.GetData(1, ref hidden);
            da.GetData(2, ref grid);
            da.GetData(3, ref auto);
            da.GetData(4, ref refresh);
            da.GetData(5, ref debug);
            if (!Finite(faint) || faint < 0 || !Finite(hidden) || hidden < faint ||
                !Finite(grid) || grid <= 0 || grid > float.MaxValue)
            {
                const string message = "Use finite thresholds with 0 <= Faint <= Hidden and a positive grid size.";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                da.SetData(0, message);
                da.SetData(1, "");
                return;
            }
            var doc = OnPingDocument();
            if (doc == null)
            {
                da.SetData(0, "No document");
                da.SetData(1, "");
                return;
            }

            bool settingsChanged = faint != _lastFaint || hidden != _lastHidden ||
                                   grid != _lastGrid || debug != _lastDebug;
            bool autoChanged = auto != _autoUpdate;
            bool refreshTriggered = refresh && !_lastRefresh;
            _lastFaint = faint; _lastHidden = hidden; _lastGrid = grid;
            _lastDebug = debug; _lastRefresh = refresh; _autoUpdate = auto;

            // A monitor keeps the original display modes while settings change.
            bool newMonitor = _monitor == null || _monitorDocument != doc;
            if (newMonitor)
            {
                _monitor?.Dispose();
                _monitor = new WireMonitor(doc, faint, hidden, (float)grid, debug, auto);
                _monitorDocument = doc;
            }
            else if (settingsChanged || autoChanged)
                _monitor.UpdateSettings(faint, hidden, (float)grid, debug, auto);

            if (auto) Subscribe(doc);
            else Unsubscribe();
            if (settingsChanged || refreshTriggered || (auto && (newMonitor || autoChanged)))
                Process(doc, false);
            da.SetData(0, _processError == null
                ? $"Processed {_monitor.GetWireCount()} wires, {_monitor.GetModifiedCount()} changed" +
                  (auto ? " (Auto update ON)" : "")
                : $"Error: {_processError}");
            da.SetData(1, _monitor.GetDebugLog());
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private void Subscribe(GH_Document doc)
        {
            if (_document != doc)
            {
                Unsubscribe();
                _document = doc;
                doc.ModifiedChanged += OnModifiedChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_document != null) _document.ModifiedChanged -= OnModifiedChanged;
            _document = null;
            _bounds.Clear(); _connections.Clear();
            _groupMembership.Clear();
            _snapshotReady = false;
        }

        private void OnModifiedChanged(object sender, GH_DocModifiedEventArgs e)
        {
            var doc = sender as GH_Document;
            if (e.Modified || !_autoUpdate || _processing || _saving || Locked ||
                doc == null || doc != _document || doc != OnPingDocument()) return;
            if (!HasCanvasChanged(doc) || !Process(doc, true) || _monitor.GetModifiedCount() == 0)
                return;
            SaveAfterUpdate(doc);
        }

        private void SaveAfterUpdate(GH_Document doc)
        {
            _saving = true;
            try
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    try
                    {
                        if (doc != OnPingDocument() || !_autoUpdate) return;
                        if (Grasshopper.Instances.ActiveCanvas?.Document != doc)
                            throw new InvalidOperationException("This document is no longer active.");
                        var grasshopper = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
                            as Grasshopper.Plugin.GH_RhinoScriptInterface;
                        if (grasshopper == null) throw new InvalidOperationException("Grasshopper save service is unavailable.");
                        if (!grasshopper.SaveDocument())
                            throw new InvalidOperationException("Grasshopper did not save the document.");
                    }
                    catch (Exception ex)
                    {
                        ReportSaveError(doc, ex);
                    }
                    finally { _saving = false; }
                }));
            }
            catch (Exception ex)
            {
                _saving = false;
                ReportSaveError(doc, ex);
            }
        }

        private void ReportSaveError(GH_Document doc, Exception ex)
        {
            if (doc != OnPingDocument()) return;
            _processError = $"Wire changes were applied, but the follow-up save failed: {ex.Message}";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _processError);
            ScheduleOutputs(doc);
        }

        private bool HasCanvasChanged(GH_Document doc)
        {
            var bounds = new Dictionary<Guid, RectangleF>();
            var connections = new HashSet<(Guid, Guid)>();
            var groupMembership = new HashSet<(Guid, Guid)>();
            foreach (var obj in doc.Objects)
            {
                if (obj == null) continue;
                if (obj.Attributes != null) bounds[obj.InstanceGuid] = obj.Attributes.Bounds;
                if (obj is GH_Group group)
                    foreach (var memberId in group.ObjectIDs)
                        groupMembership.Add((group.InstanceGuid, memberId));
                if (obj is IGH_Param param) CollectConnections(param, connections);
                if (obj is IGH_Component component)
                {
                    foreach (var input in component.Params.Input)
                    {
                        if (input.Attributes != null) bounds[input.InstanceGuid] = input.Attributes.Bounds;
                        CollectConnections(input, connections);
                    }
                    foreach (var output in component.Params.Output)
                    {
                        if (output.Attributes != null) bounds[output.InstanceGuid] = output.Attributes.Bounds;
                        CollectConnections(output, connections);
                    }
                }
            }
            bool changed = !_snapshotReady || bounds.Count != _bounds.Count || !connections.SetEquals(_connections) ||
                           !groupMembership.SetEquals(_groupMembership);
            if (!changed)
                foreach (var entry in bounds)
                    if (!_bounds.TryGetValue(entry.Key, out var old) || old != entry.Value)
                    {
                        changed = true;
                        break;
                    }
            _bounds.Clear();
            foreach (var entry in bounds) _bounds.Add(entry.Key, entry.Value);
            _connections.Clear();
            _connections.UnionWith(connections);
            _groupMembership.Clear();
            _groupMembership.UnionWith(groupMembership);
            _snapshotReady = true;
            return changed;
        }

        private static void CollectConnections(IGH_Param target, HashSet<(Guid, Guid)> connections)
        {
            foreach (var source in target.Sources)
                if (source != null) connections.Add((source.InstanceGuid, target.InstanceGuid));
        }

        private bool Process(GH_Document doc, bool updateOutputs)
        {
            if (_processing || _monitor == null) return false;
            try
            {
                _processing = true;
                _processError = null;
                _monitor.ProcessAllWires();
                if (_autoUpdate) HasCanvasChanged(doc);
                return true;
            }
            catch (Exception ex)
            {
                _snapshotReady = false;
                _processError = ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _processError);
                return false;
            }
            finally
            {
                _processing = false;
                if (updateOutputs) ScheduleOutputs(doc);
            }
        }

        private void ScheduleOutputs(GH_Document doc)
        {
            ExpireSolution(false);
            doc.ScheduleSolution(1);
        }

        public override void RemovedFromDocument(GH_Document doc)
        {
            Unsubscribe();
            _monitor?.Dispose();
            _monitor = null;
            _monitorDocument = null;
            base.RemovedFromDocument(doc);
        }

        protected override Bitmap Icon
        {
            get
            {
#if NET7_0
                if (!OperatingSystem.IsWindows()) return null;
#endif
                var assembly = GetType().Assembly;
                foreach (var name in assembly.GetManifestResourceNames())
                    if (name.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase))
                        using (var stream = assembly.GetManifestResourceStream(name))
                            if (stream != null) return new Bitmap(stream);
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("A100BFA4-603D-4609-8657-184298E7194C");
    }
}
