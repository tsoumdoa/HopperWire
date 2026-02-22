using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace VibeTest
{
    public class WireDisplayManager : GH_Component
    {
        private WireMonitor _wireMonitor;
        private double _lastFaintThreshold = 800;
        private double _lastHiddenThreshold = 1500;
        private float _lastSpatialGridSize = 200;
        private bool _lastDebug = false;
        private bool _lastRefresh = false;
        private bool _autoUpdate = false;
        private GH_Document _subscribedDocument;
        private bool _isProcessing = false;
        private bool _isSaving = false;




        public WireDisplayManager()
          : base("Wire Display Manager", "WireDisplay",
            "Automatically manages wire display based on length thresholds",
            "Params", "Util")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Faint Threshold", "Faint", "Wire length threshold for faint display (pixels)", GH_ParamAccess.item, _lastFaintThreshold);
            pManager.AddNumberParameter("Hidden Threshold", "Hidden", "Wire length threshold for hidden display (pixels)", GH_ParamAccess.item, _lastHiddenThreshold);
            pManager.AddNumberParameter("Spatial Grid Size", "Grid", "Cell size for spatial grid optimization (pixels)", GH_ParamAccess.item, _lastSpatialGridSize);
            pManager.AddBooleanParameter("Auto Update", "Auto", "Enable automatic updates when canvas changes", GH_ParamAccess.item, true);
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
            double faintThreshold = 900;
            double hiddenThreshold = 1200;
            double spatialGridSize = 200;
            bool autoUpdate = false;
            bool refresh = false;
            bool debug = false;

            DA.GetData(0, ref faintThreshold);
            DA.GetData(1, ref hiddenThreshold);
            DA.GetData(2, ref spatialGridSize);
            DA.GetData(3, ref autoUpdate);
            DA.GetData(4, ref refresh);
            DA.GetData(5, ref debug);

            var doc = OnPingDocument();
            if (doc == null)
            {
                DA.SetData(0, "Error: No document");
                DA.SetData(1, "");
                return;
            }

            var settingsChanged = Math.Abs(faintThreshold - _lastFaintThreshold) > 0.001 || 
                              Math.Abs(hiddenThreshold - _lastHiddenThreshold) > 0.001 ||
                              Math.Abs(spatialGridSize - _lastSpatialGridSize) > 0.001 ||
                              debug != _lastDebug;

            var refreshTriggered = refresh && !_lastRefresh;
            var autoUpdateChanged = autoUpdate != _autoUpdate;

            if (autoUpdateChanged)
            {
                _autoUpdate = autoUpdate;
                if (autoUpdate)
                {
                    SubscribeToDocumentEvents(doc);
                }
                else
                {
                    UnsubscribeFromDocumentEvents();
                }
            }

            if (_autoUpdate && _subscribedDocument == null)
            {
                SubscribeToDocumentEvents(doc);
            }

            if (settingsChanged || refreshTriggered || (_autoUpdate && _wireMonitor == null))
            {
                _wireMonitor?.Dispose();
                _wireMonitor = new WireMonitor(doc, faintThreshold, hiddenThreshold, (float)spatialGridSize, debug);
                ProcessWiresSafe();
            }

            _lastFaintThreshold = faintThreshold;
            _lastHiddenThreshold = hiddenThreshold;
            _lastSpatialGridSize = (float)spatialGridSize;
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



        private void SubscribeToDocumentEvents(GH_Document doc)
        {
            if (_subscribedDocument == doc) return;
            
            UnsubscribeFromDocumentEvents();
            
            _subscribedDocument = doc;
            _subscribedDocument.ModifiedChanged += OnDocumentModifiedChanged;
            
            if (_lastDebug)
            {
                Rhino.RhinoApp.WriteLine("[WireDisplayManager] Subscribed to document save events");
            }
        }

        private void UnsubscribeFromDocumentEvents()
        {
            if (_subscribedDocument == null) return;
            
            _subscribedDocument.ModifiedChanged -= OnDocumentModifiedChanged;
            
            if (_lastDebug)
            {
                Rhino.RhinoApp.WriteLine("[WireDisplayManager] Unsubscribed from document save events");
            }
            
            _subscribedDocument = null;
        }

        private void OnDocumentModifiedChanged(object sender, GH_DocModifiedEventArgs e)
        {
            if (!_autoUpdate || _isProcessing || _isSaving || this.Locked ) return;
            
            // Only trigger when document is saved (Modified changes from true to false)
            if (!e.Modified)
            {
                if (_lastDebug)
                {
                    Rhino.RhinoApp.WriteLine("[WireDisplayManager] Document saved - updating wire displays");
                }
                
                ProcessWiresSafe();
                
                // Check if wire processing made any changes
                bool madeChanges = _wireMonitor != null && _wireMonitor.GetModifiedCount() > 0;
                
                if (madeChanges)
                {
                    // Save the document after wire changes to persist them
                    Rhino.RhinoApp.InvokeOnUiThread((System.Action)delegate
                    {
                        try
                        {
                            var doc = OnPingDocument();
                            bool isModified = (doc != null) && doc.IsModified;
                            if (isModified)
                            {
                                if (_lastDebug)
                                {
                                    Rhino.RhinoApp.WriteLine("[WireDisplayManager] Saving document to persist wire changes");
                                }
                                // Use Rhino command to save the current Grasshopper document
                                _isSaving = true;
                                Rhino.RhinoApp.RunScript("_GrasshopperSave", true);
                                _isSaving = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _isSaving = false;
                            if (_lastDebug)
                            {
                                Rhino.RhinoApp.WriteLine($"[WireDisplayManager] Error saving document: {ex.Message}");
                            }
                        }
                    });
                }
            }
        }



        private void ProcessWiresSafe()
        {
            if (_isProcessing || _wireMonitor == null) return;
            
            try
            {
                _isProcessing = true;
                _wireMonitor.ProcessAllWires();
                
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
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            UnsubscribeFromDocumentEvents();
            _wireMonitor?.Dispose();
            _wireMonitor = null;
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                
                foreach (var resourceName in resourceNames)
                {
                    if (resourceName.EndsWith("icon.png"))
                    {
                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream != null)
                            {
                                return new System.Drawing.Bitmap(stream);
                            }
                        }
                    }
                }
                
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }
}
