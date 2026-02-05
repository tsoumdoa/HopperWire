using System;
using Grasshopper.Kernel;

namespace VibeTest
{
    public class WireDisplayManager : GH_Component
    {
        private WireMonitor _wireMonitor;
        private double _lastThreshold = 100.0;
        private bool _isActive = false;

        public WireDisplayManager()
          : base("Wire Display Manager", "WireDisplay",
            "Automatically sets wires longer than the threshold to Faint display",
            "VibeTest", "Display")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Length Threshold", "Threshold", "Wire length threshold (pixels)", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Active", "Active", "Enable or disable the wire monitor", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "Current status of the wire monitor", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double threshold = 100.0;
            double active = 1.0;

            if (!DA.GetData(0, ref threshold)) return;
            if (!DA.GetData(1, ref active)) return;

            var shouldBeActive = active > 0.5;
            var thresholdChanged = Math.Abs(threshold - _lastThreshold) > 0.001;
            var activeChanged = (shouldBeActive != _isActive);

            if (activeChanged && !shouldBeActive)
            {
                DeactivateMonitor();
            }
            else if (activeChanged && shouldBeActive)
            {
                ActivateMonitor(threshold);
            }
            else if (shouldBeActive && thresholdChanged)
            {
                _wireMonitor?.UpdateThreshold(threshold);
            }

            _lastThreshold = threshold;
            _isActive = shouldBeActive;

            string status = _isActive 
                ? $"Active - Monitoring wires > {threshold:F1} pixels" 
                : "Inactive";
            
            DA.SetData(0, status);
        }

        private void ActivateMonitor(double threshold)
        {
            DeactivateMonitor();

            if (OnPingDocument() == null) return;

            _wireMonitor = new WireMonitor(OnPingDocument(), threshold);
            _wireMonitor.InitializeEvents();
        }

        private void DeactivateMonitor()
        {
            if (_wireMonitor != null)
            {
                _wireMonitor.DisposeEvents();
                _wireMonitor = null;
            }
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            
            if (_isActive && document != null)
            {
                ActivateMonitor(_lastThreshold);
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            DeactivateMonitor();
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }
}
