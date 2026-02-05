using System;
using Grasshopper.Kernel;

namespace VibeTest
{
    public struct WireDisplayState
    {
        public Guid ParamId;
        public int SourceIndex;
        public GH_ParamWireDisplay OriginalDisplay;
    }
}
