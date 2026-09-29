# HopperWire

HopperWire is a Grasshopper component that keeps local connections clear and reduces the visual weight of long or awkward paths.

## Use

Place **Hopper Wire** from **Params > Util** on a Grasshopper canvas. Its inputs are:

| Input | Default | Effect |
| --- | ---: | --- |
| Faint Threshold | 800 | Longer wires become faint |
| Hidden Threshold | 1500 | Longer wires become hidden |
| Spatial Grid Size | 200 | Cell size used to find crossing candidates |
| Auto Update | true | Recheck layout when the Grasshopper document is saved |
| Refresh | false | Recheck on a false-to-true transition |
| Debug | false | Include processing details in the Log output |

Lengths and grid sizes use canvas coordinates. Thresholds must be finite and nonnegative, Hidden must be at least Faint, and Grid must be positive.

## Display rules

HopperWire measures an approximate Bézier curve length between parameter grips. A length **above Hidden Threshold** hides the connection; a length **above Faint Threshold** faints it. Equality does not cross either threshold.

At or below Faint Threshold, any of these layout conditions makes a connection faint:

- It crosses another distinct wire and is the more vertical of the two. Equal-angle crossings and shared source or target endpoints are ignored.
- It enters or leaves an explicit Grasshopper group, or connects objects in groups with no shared membership. Connections between two ungrouped objects, or within any shared group, keep their other computed style.
- Its source output grip lies to the right of its target input grip, creating a backward path.
- Its sampled curve passes through the interior of an unrelated component's bounds. Touching an edge does not count.

Only length can make a new wire **hidden**. Group, backward, and through-component rules apply only to inputs with one source. Grasshopper has one display setting per target parameter, even if several sources connect to it, so HopperWire chooses the most restrictive result among their length and crossing results: **hidden > faint > default**. Crossing and curve measurements are approximate.

When a wire no longer needs a changed display, HopperWire restores the parameter's prior setting while the component is active. It saves both the original and applied settings with the component, so this also works after reopening the document. A user's more restrictive display choice is preserved. It retains wire display changes if the component is removed. Changes made with Auto Update off are recorded in the Grasshopper undo stack; changes with Auto Update on do not add undo records.

Files saved by older versions do not record which styles HopperWire applied. Those existing styles are preserved as user choices; set affected inputs' wire display back to Default once, then use Refresh to let HopperWire manage them again.

Auto Update checks the layout after a save. If that check changes wire displays, HopperWire saves the document again to persist them. Use Refresh for an immediate update without saving.

## Build

Run `dotnet restore` and `dotnet build HopperWire.csproj`. Run the layout rule checks with `dotnet run --project tests/WireLayoutRules.Tests.csproj`. The plugin targets `net48`, `net7.0`, and `net7.0-windows` and produces a `.gha` for each framework. The project references the Grasshopper 8 NuGet package, so test the resulting plugin in the matching Rhino/Grasshopper environment before distributing it.
