using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace VibeTest
{
    public class VibeTestInfo : GH_AssemblyInfo
    {
        public override string Name => "VibeTest";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Wire display management plugin for Grasshopper";

        public override Guid Id => new Guid("ad130c1c-030b-441d-b903-e0eed8e4f849");

        //Return a string identifying you or your company.
        public override string AuthorName => "VibeTest";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();

        public override void Load(GH_LibraryVersion version)
        {
            base.Load(version);
            
            Instances.ComponentServer.AddComponent(typeof(WireDisplayManager), this);
        }
    }
}
