# Use reflection to inspect GH_Document class
Add-Type -TypeDefinition @'
using System;
using System.Reflection;

public class Inspector {
    public static void Inspect() {
        try {
            // Try to find loaded Grasshopper assembly
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                string name = new AssemblyName(args.Name).Name;
                if (name.Contains("Grasshopper")) {
                    return Assembly.LoadFrom(@"C:\Users\tomosandego\.nuget\packages\grasshopper\8.0.23304.9001\lib\net48\Grasshopper.dll");
                }
                if (name.Contains("GH_IO")) {
                    return Assembly.LoadFrom(@"C:\Users\tomosandego\.nuget\packages\grasshopper\8.0.23304.9001\lib\net48\GH_IO.dll");
                }
                return null;
            };
            
            var assembly = Assembly.LoadFrom(@"C:\Users\tomosandego\.nuget\packages\grasshopper\8.0.23304.9001\lib\net48\Grasshopper.dll");
            var type = assembly.GetType("Grasshopper.Kernel.GH_Document");
            
            if (type != null) {
                Console.WriteLine("GH_Document found!");
                
                // Find all methods with "Save" in the name
                foreach (var method in type.GetMethods()) {
                    if (method.Name.Contains("Save")) {
                        Console.WriteLine("\nMethod: " + method.Name);
                        Console.WriteLine("  IsPublic: " + method.IsPublic);
                        Console.WriteLine("  IsStatic: " + method.IsStatic);
                        Console.WriteLine("  ReturnType: " + method.ReturnType);
                        Console.WriteLine("  Parameters:");
                        foreach (var param in method.GetParameters()) {
                            Console.WriteLine("    " + param.ParameterType.Name + " " + param.Name);
                        }
                    }
                }
                
                // Find all methods with "Write" in the name
                foreach (var method in type.GetMethods()) {
                    if (method.Name.Contains("Write")) {
                        Console.WriteLine("\nMethod: " + method.Name);
                        Console.WriteLine("  IsPublic: " + method.IsPublic);
                        Console.WriteLine("  IsStatic: " + method.IsStatic);
                        Console.WriteLine("  ReturnType: " + method.ReturnType);
                        Console.WriteLine("  Parameters:");
                        foreach (var param in method.GetParameters()) {
                            Console.WriteLine("    " + param.ParameterType.Name + " " + param.Name);
                        }
                    }
                }
            } else {
                Console.WriteLine("GH_Document type not found!");
            }
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
            if (ex.InnerException != null) {
                Console.WriteLine("Inner: " + ex.InnerException.Message);
            }
        }
    }
}
'@

[Inspector]::Inspect()
