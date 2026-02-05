using System;
using Grasshopper.Kernel;

namespace VibeTest
{
    public class WireDisplayManager : GH_Component
    {
        private WireMonitor _wireMonitor;
        private double _lastFaintThreshold = 300.0;
        private double _lastHiddenThreshold = 900.0;
        private bool _lastDebug = false;
        private bool _lastRefresh = false;

        public WireDisplayManager()
          : base("Wire Display Manager", "WireDisplay",
            "Manually trigger wire display updates based on length thresholds",
            "VibeTest", "Display")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Faint Threshold", "Faint", "Wire length threshold for faint display (pixels)", GH_ParamAccess.item, 300.0);
            pManager.AddNumberParameter("Hidden Threshold", "Hidden", "Wire length threshold for hidden display (pixels)", GH_ParamAccess.item, 900.0);
            pManager.AddBooleanParameter("Refresh", "Refresh", "Click to refresh wire displays (toggle to refresh)", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Debug", "Debug", "Enable debug logging", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "Current status of the wire monitor", GH_ParamAccess.item);
            pManager.AddTextParameter("Log", "Log", "Debug log messages", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double faintThreshold = 800;
            double hiddenThreshold = 1500;
            bool refresh = false;
            bool debug = false;

            DA.GetData(0, ref faintThreshold);
            DA.GetData(1, ref hiddenThreshold);
            DA.GetData(2, ref refresh);
            DA.GetData(3, ref debug);

            var settingsChanged = Math.Abs(faintThreshold - _lastFaintThreshold) > 0.001 || 
                              Math.Abs(hiddenThreshold - _lastHiddenThreshold) > 0.001 ||
                              debug != _lastDebug;

            var refreshTriggered = refresh && !_lastRefresh;

            if (settingsChanged || refreshTriggered)
            {
                if (OnPingDocument() == null) return;

                _wireMonitor = new WireMonitor(OnPingDocument(), faintThreshold, hiddenThreshold, debug);
                _wireMonitor.ProcessAllWires();
            }

            _lastFaintThreshold = faintThreshold;
            _lastHiddenThreshold = hiddenThreshold;
            _lastDebug = debug;
            _lastRefresh = refresh;

            string status = "Ready - Click Refresh to update wire displays";
            if (_wireMonitor != null)
            {
                var wireCount = _wireMonitor.GetWireCount();
                var modifiedCount = _wireMonitor.GetModifiedCount();
                status = $"Processed {wireCount} wires, {modifiedCount} modified";
            }
            
            DA.SetData(0, status);
            DA.SetData(1, _wireMonitor?.GetDebugLog() ?? "");
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _wireMonitor?.Dispose();
            _wireMonitor = null;
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }
}
