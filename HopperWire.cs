using System;
using System.Drawing;
using System.IO;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace HopperWire
{
    public class HopperWire : GH_Component
    {
        private const double DefaultFaint = 800;
        private const double DefaultHidden = 1500;
        private const double DefaultGrid = 200;
        private WireMonitor _monitor;
        private readonly WireDisplayState _displayState = new WireDisplayState();
        private GH_Document _monitorDocument;
        private GH_Document _document;
        private (string Path, long WriteTicks, long Length)? _lastSavedFile;
        private double _lastFaint = DefaultFaint, _lastHidden = DefaultHidden, _lastGrid = DefaultGrid;
        private bool _lastDebug, _lastRefresh, _autoUpdate, _processing, _saving;
        private string _processError;
        private const string ControllerConflict = "Multiple unlocked HopperWire components: lock or remove extras to resume wire updates.";

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
                !Finite(grid) || (float)grid <= 0 || grid > float.MaxValue)
            {
                _autoUpdate = false;
                Unsubscribe();
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

            // Keep tracking managed targets while settings change.
            bool newMonitor = _monitor == null || _monitorDocument != doc;
            if (newMonitor)
            {
                _monitor?.Dispose();
                if (_monitorDocument != null && _monitorDocument != doc) _displayState.Clear();
                _monitor = new WireMonitor(doc, faint, hidden, (float)grid, debug, auto, _displayState);
                _monitorDocument = doc;
            }
            else if (settingsChanged || autoChanged)
                _monitor.UpdateSettings(faint, hidden, (float)grid, debug, auto);

            if (auto) Subscribe(doc);
            else Unsubscribe();
            // Stay subscribed during conflicts so the next save can resume after they are resolved.
            bool wasConflict = _processError == ControllerConflict;
            if (HasControllerConflict(doc))
            {
                _processError = ControllerConflict;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ControllerConflict);
                da.SetData(0, ControllerConflict);
                da.SetData(1, "");
                return;
            }
            if (wasConflict) _processError = null;
            if (settingsChanged || refreshTriggered || (auto && (newMonitor || autoChanged || wasConflict)))
                Process(doc, false);
            da.SetData(0, _processError == null
                ? $"Processed {_monitor.WireCount} wires, {_monitor.ModifiedCount} changed" +
                  (auto ? " (Auto update ON)" : "")
                : $"Error: {_processError}");
            da.SetData(1, _monitor.DebugLog);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool HasControllerConflict(GH_Document doc)
        {
            int count = 0;
            foreach (var obj in doc.Objects)
                if (obj is HopperWire controller && !controller.Locked && ++count > 1)
                    return true;
            return false;
        }

        public override bool Write(GH_IWriter writer)
        {
            _displayState.Write(writer);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            Unsubscribe();
            _monitor?.Dispose();
            _monitor = null;
            _monitorDocument = null;
            _displayState.Read(reader);
            return base.Read(reader);
        }

        private void Subscribe(GH_Document doc)
        {
            if (_document != doc)
            {
                Unsubscribe();
                _document = doc;
                _lastSavedFile = GetFileStamp(doc);
                doc.ModifiedChanged += OnModifiedChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_document != null) _document.ModifiedChanged -= OnModifiedChanged;
            _document = null;
            _lastSavedFile = null;
        }

        private void OnModifiedChanged(object sender, GH_DocModifiedEventArgs e)
        {
            var doc = sender as GH_Document;
            if (e.Modified || !_autoUpdate || _processing || _saving || Locked ||
                doc == null || doc != _document || doc != OnPingDocument()) return;
            var stamp = GetFileStamp(doc);
            if (stamp == null || stamp == _lastSavedFile) return;
            _lastSavedFile = stamp;
            if (!Process(doc, true) || (_monitor.ModifiedCount == 0 && !_monitor.HasStateChanges))
                return;
            SaveAfterUpdate(doc);
        }

        private static (string Path, long WriteTicks, long Length)? GetFileStamp(GH_Document doc)
        {
            if (string.IsNullOrEmpty(doc.FilePath)) return null;
            try
            {
                var file = new FileInfo(doc.FilePath);
                return file.Exists ? (file.FullName, file.LastWriteTimeUtc.Ticks, file.Length) :
                    ((string Path, long WriteTicks, long Length)?)null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
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
                        if (doc != OnPingDocument() || !_autoUpdate || Locked || HasControllerConflict(doc)) return;
                        if (Grasshopper.Instances.ActiveCanvas?.Document != doc)
                            throw new InvalidOperationException("This document is no longer active.");
                        var grasshopper = Rhino.RhinoApp.GetPlugInObject("Grasshopper")
                            as Grasshopper.Plugin.GH_RhinoScriptInterface;
                        if (grasshopper == null) throw new InvalidOperationException("Grasshopper save service is unavailable.");
                        if (!grasshopper.SaveDocument())
                            throw new InvalidOperationException("Grasshopper did not save the document.");
                        _lastSavedFile = GetFileStamp(doc);
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

        private bool Process(GH_Document doc, bool updateOutputs)
        {
            if (_processing || _monitor == null || Locked) return false;
            // Also guard save callbacks, which can run before another component solves.
            if (HasControllerConflict(doc))
            {
                _processError = ControllerConflict;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ControllerConflict);
                if (updateOutputs) ScheduleOutputs(doc);
                return false;
            }
            try
            {
                _processing = true;
                _processError = null;
                _monitor.ProcessAllWires();
                return true;
            }
            catch (Exception ex)
            {
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
            _displayState.Clear();
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
