using System;
using System.Collections.Generic;
using System.Drawing;
using System.Timers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace VibeTest
{
    public class WireDisplayManager : GH_Component
    {
        private WireMonitor _wireMonitor;
        private double _lastFaintThreshold = 300.0;
        private double _lastHiddenThreshold = 900.0;
        private bool _lastDebug = false;
        private bool _lastRefresh = false;
        private bool _autoUpdate = false;
        private GH_Document _subscribedDocument;
        private HashSet<Guid> _subscribedObjects = new HashSet<Guid>();
        private bool _isProcessing = false;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private static readonly TimeSpan _minUpdateInterval = TimeSpan.FromMilliseconds(50);
        private System.Timers.Timer _updateTimer;
        private Dictionary<Guid, PointF> _lastPositions = new Dictionary<Guid, PointF>();

        public WireDisplayManager()
          : base("Wire Display Manager", "WireDisplay",
            "Automatically manages wire display based on length thresholds",
            "VibeTest", "Display")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Faint Threshold", "Faint", "Wire length threshold for faint display (pixels)", GH_ParamAccess.item, 300.0);
            pManager.AddNumberParameter("Hidden Threshold", "Hidden", "Wire length threshold for hidden display (pixels)", GH_ParamAccess.item, 900.0);
            pManager.AddBooleanParameter("Auto Update", "Auto", "Enable automatic updates when canvas changes", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Refresh", "Refresh", "Click to manually refresh wire displays", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Debug", "Debug", "Enable debug logging", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "Current status of the wire monitor", GH_ParamAccess.item);
            pManager.AddTextParameter("Log", "Log", "Debug log messages", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double faintThreshold = 300;
            double hiddenThreshold = 900;
            bool autoUpdate = false;
            bool refresh = false;
            bool debug = false;

            DA.GetData(0, ref faintThreshold);
            DA.GetData(1, ref hiddenThreshold);
            DA.GetData(2, ref autoUpdate);
            DA.GetData(3, ref refresh);
            DA.GetData(4, ref debug);

            var doc = OnPingDocument();
            if (doc == null)
            {
                DA.SetData(0, "Error: No document");
                DA.SetData(1, "");
                return;
            }

            var settingsChanged = Math.Abs(faintThreshold - _lastFaintThreshold) > 0.001 || 
                              Math.Abs(hiddenThreshold - _lastHiddenThreshold) > 0.001 ||
                              debug != _lastDebug;

            var refreshTriggered = refresh && !_lastRefresh;
            var autoUpdateChanged = autoUpdate != _autoUpdate;

            if (autoUpdateChanged)
            {
                _autoUpdate = autoUpdate;
                if (autoUpdate)
                {
                    SubscribeToDocumentEvents(doc);
                    StartUpdateTimer();
                }
                else
                {
                    UnsubscribeFromDocumentEvents();
                    StopUpdateTimer();
                }
            }

            if (_autoUpdate && _subscribedDocument == null)
            {
                SubscribeToDocumentEvents(doc);
                StartUpdateTimer();
            }

            if (settingsChanged || refreshTriggered || (_autoUpdate && _wireMonitor == null))
            {
                _wireMonitor?.Dispose();
                _wireMonitor = new WireMonitor(doc, faintThreshold, hiddenThreshold, debug);
                ProcessWiresSafe();
            }

            _lastFaintThreshold = faintThreshold;
            _lastHiddenThreshold = hiddenThreshold;
            _lastDebug = debug;
            _lastRefresh = refresh;

            string status;
            if (_wireMonitor != null)
            {
                var wireCount = _wireMonitor.GetWireCount();
                var modifiedCount = _wireMonitor.GetModifiedCount();
                status = $"Processed {wireCount} wires, {modifiedCount} modified";
                if (_autoUpdate)
                {
                    status += " (Auto-update ON)";
                }
            }
            else
            {
                status = _autoUpdate ? "Auto-update ON - Processing..." : "Ready - Toggle Refresh to update";
            }
            
            DA.SetData(0, status);
            DA.SetData(1, _wireMonitor?.GetDebugLog() ?? "");
        }

        private void StartUpdateTimer()
        {
            if (_updateTimer == null)
            {
                _updateTimer = new System.Timers.Timer(100);
                _updateTimer.Elapsed += OnUpdateTimerElapsed;
                _updateTimer.AutoReset = true;
            }
            _updateTimer.Start();
        }

        private void StopUpdateTimer()
        {
            _updateTimer?.Stop();
        }

        private void OnUpdateTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (!_autoUpdate || _isProcessing || _subscribedDocument == null) return;

            try
            {
                bool needsUpdate = false;
                var currentPositions = new Dictionary<Guid, PointF>();

                foreach (var obj in _subscribedDocument.Objects)
                {
                    if (obj?.Attributes == null) continue;
                    
                    var pivot = obj.Attributes.Pivot;
                    currentPositions[obj.InstanceGuid] = pivot;
                    
                    if (_lastPositions.TryGetValue(obj.InstanceGuid, out var lastPos))
                    {
                        if (Math.Abs(pivot.X - lastPos.X) > 0.5 || Math.Abs(pivot.Y - lastPos.Y) > 0.5)
                        {
                            needsUpdate = true;
                        }
                    }
                    else
                    {
                        _lastPositions[obj.InstanceGuid] = pivot;
                    }
                }

                if (needsUpdate)
                {
                    _lastPositions = currentPositions;
                    ProcessWiresSafe();
                }
            }
            catch { }
        }

        private void SubscribeToDocumentEvents(GH_Document doc)
        {
            if (_subscribedDocument == doc) return;
            
            UnsubscribeFromDocumentEvents();
            
            _subscribedDocument = doc;
            _subscribedDocument.ObjectsAdded += OnObjectsAdded;
            _subscribedDocument.ObjectsDeleted += OnObjectsDeleted;
            _subscribedDocument.SolutionEnd += OnSolutionEnd;
            
            foreach (var obj in _subscribedDocument.Objects)
            {
                SubscribeToObjectEvents(obj);
            }
            
            if (_lastDebug)
            {
                Rhino.RhinoApp.WriteLine("[WireDisplayManager] Subscribed to document events");
            }
        }

        private void UnsubscribeFromDocumentEvents()
        {
            if (_subscribedDocument == null) return;
            
            _subscribedDocument.ObjectsAdded -= OnObjectsAdded;
            _subscribedDocument.ObjectsDeleted -= OnObjectsDeleted;
            _subscribedDocument.SolutionEnd -= OnSolutionEnd;
            
            foreach (var guid in _subscribedObjects)
            {
                var obj = _subscribedDocument.FindObject(guid, false);
                if (obj != null)
                {
                    obj.ObjectChanged -= OnObjectChanged;
                }
            }
            _subscribedObjects.Clear();
            _lastPositions.Clear();
            
            if (_lastDebug)
            {
                Rhino.RhinoApp.WriteLine("[WireDisplayManager] Unsubscribed from document events");
            }
            
            _subscribedDocument = null;
        }

        private void SubscribeToObjectEvents(IGH_DocumentObject obj)
        {
            if (obj == null || _subscribedObjects.Contains(obj.InstanceGuid)) return;
            
            obj.ObjectChanged += OnObjectChanged;
            _subscribedObjects.Add(obj.InstanceGuid);
            
            if (obj.Attributes != null)
            {
                _lastPositions[obj.InstanceGuid] = obj.Attributes.Pivot;
            }
        }

        private void OnObjectsAdded(object sender, GH_DocObjectEventArgs e)
        {
            if (!_autoUpdate || _isProcessing) return;
            
            foreach (var obj in e.Objects)
            {
                SubscribeToObjectEvents(obj);
            }
            
            ScheduleWireUpdate();
        }

        private void OnObjectsDeleted(object sender, GH_DocObjectEventArgs e)
        {
            if (!_autoUpdate) return;
            
            foreach (var obj in e.Objects)
            {
                obj.ObjectChanged -= OnObjectChanged;
                _subscribedObjects.Remove(obj.InstanceGuid);
                _lastPositions.Remove(obj.InstanceGuid);
            }
        }

        private void OnObjectChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
        {
            if (!_autoUpdate || _isProcessing) return;
            
            var changeType = e.Type;
            if (changeType == GH_ObjectEventType.Layout ||
                changeType == GH_ObjectEventType.Sources)
            {
                ScheduleWireUpdate();
            }
        }

        private void OnSolutionEnd(object sender, GH_SolutionEventArgs e)
        {
            if (!_autoUpdate || _isProcessing) return;
            
            var now = DateTime.Now;
            if (now - _lastUpdateTime >= _minUpdateInterval)
            {
                ProcessWiresSafe();
            }
        }

        private void ScheduleWireUpdate()
        {
            var now = DateTime.Now;
            if (now - _lastUpdateTime >= _minUpdateInterval)
            {
                ProcessWiresSafe();
            }
        }

        private void ProcessWiresSafe()
        {
            if (_isProcessing || _wireMonitor == null) return;
            
            try
            {
                _isProcessing = true;
                _wireMonitor.ProcessAllWires();
                _lastUpdateTime = DateTime.Now;
                
                Rhino.RhinoApp.InvokeOnUiThread((System.Action)delegate
                {
                    try
                    {
                        ExpireSolution(true);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                if (_lastDebug)
                {
                    Rhino.RhinoApp.WriteLine($"[WireDisplayManager] Error: {ex.Message}");
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            
            if (_autoUpdate)
            {
                SubscribeToDocumentEvents(document);
                StartUpdateTimer();
            }
            
            if (document != null && Params?.Input?.Count > 2)
            {
                System.Windows.Forms.Timer deferTimer = new System.Windows.Forms.Timer();
                deferTimer.Interval = 100;
                deferTimer.Tick += (s, e) =>
                {
                    deferTimer.Stop();
                    deferTimer.Dispose();
                    AddToggleComponent(document);
                };
                deferTimer.Start();
            }
        }

        private void AddToggleComponent(GH_Document document)
        {
            try
            {
                if (document == null || Params?.Input == null || Params.Input.Count < 3)
                {
                    return;
                }
                
                var autoParam = Params.Input[2];
                if (autoParam == null)
                {
                    return;
                }
                
                var toggle = new GH_BooleanToggle();
                toggle.NickName = "Auto";
                toggle.Description = "Toggle to enable/disable auto wire display updates";
                toggle.Value = false;
                toggle.CreateAttributes();
                
                // Place toggle at same Y as the Auto parameter input, to the left
                PointF togglePosition;
                if (autoParam.Attributes != null)
                {
                    // Get the input grip location (where wires connect to the parameter)
                    var inputGrip = autoParam.Attributes.InputGrip;
                    // Place toggle to the left with small spacing
                    togglePosition = new PointF(inputGrip.X - 100, inputGrip.Y - toggle.Attributes.Bounds.Height / 2);
                }
                else if (Attributes?.Pivot != null)
                {
                    // Fallback to component position
                    togglePosition = new PointF(Attributes.Pivot.X - 140, Attributes.Pivot.Y + 30);
                }
                else
                {
                    togglePosition = new PointF(100, 100);
                }
                
                toggle.Attributes.Pivot = togglePosition;
                
                document.AddObject(toggle, false);
                
                try
                {
                    autoParam.AddSource(toggle);
                }
                catch (Exception wireEx)
                {
                    if (_lastDebug)
                    {
                        Rhino.RhinoApp.WriteLine($"[WireDisplayManager] Could not connect wire: {wireEx.Message}");
                    }
                }
                
                document.ScheduleSolution(1);
            }
            catch (Exception ex)
            {
                if (_lastDebug)
                {
                    Rhino.RhinoApp.WriteLine($"[WireDisplayManager] Error adding toggle: {ex.Message}");
                }
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopUpdateTimer();
            _updateTimer?.Dispose();
            _updateTimer = null;
            
            UnsubscribeFromDocumentEvents();
            _wireMonitor?.Dispose();
            _wireMonitor = null;
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }
}
