using System.Drawing;
using Grasshopper.Kernel;

namespace HopperWire
{
    public class HopperWireInfo : GH_AssemblyInfo
    {
        public override string Name => "HopperWire";

        public override Bitmap Icon => null;

        public override string Description => "Wire display management plugin for Grasshopper";

        public override string AuthorName => "AEC Tooling";

        public override string AuthorContact => "";

        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}
