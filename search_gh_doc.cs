using System;
using Grasshopper.Kernel;

class Program {
    static void Main() {
        // This is just to show the expected signature based on usage
        GH_Document doc = null;
        
        // SaveDocument() - Saves the document to its current path
        // Signature: public void SaveDocument()
        
        // Alternative overload might be:
        // public void SaveDocument(string path)
        // public bool SaveDocument(string path, bool archive)
    }
}
