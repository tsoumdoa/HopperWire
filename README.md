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

Each processing pass applies the display computed from the current layout and thresholds, including returning short, clear connections to Default. Manual display changes last until the next processing pass; HopperWire does not infer or preserve user preferences. Removing the component retains the last applied wire displays. Changes made with Auto Update off are recorded in the Grasshopper undo stack; changes with Auto Update on do not add undo records. The next pass recomputes displays after undo too.

HopperWire saves managed input IDs and their last applied displays with the component. Previously managed inputs that become disconnected reset to Default, including after reopening. They remain tracked while they exist so undo cannot leave a disconnected input stuck on an old style. Deleted inputs are dropped from the records. Older files are supported: connected inputs are recomputed on the next pass without a manual reset, and any saved original-setting preferences are ignored.

Use one unlocked HopperWire component per document. If several are unlocked, updates pause with a warning until the extras are locked or removed. This prevents components with different thresholds from overwriting each other's results. After resolving a conflict, use Refresh if Auto Update is off.

Auto Update checks the layout after a save. If that check changes wire displays or managed-input records, HopperWire saves the document again to persist them. Use Refresh for an immediate update without saving.

## Build

Run `dotnet restore` and `dotnet build HopperWire.csproj`. Run the layout rule checks with `dotnet run --project tests/WireLayoutRules.Tests.csproj`. The plugin targets `net48`, `net7.0`, and `net7.0-windows` and produces a `.gha` for each framework. The project references the Grasshopper 8 NuGet package, so test the resulting plugin in the matching Rhino/Grasshopper environment before distributing it.

## Package

Run `pwsh -File scripts/Package.ps1` to build the Release `net7.0` plugin and generate a Rhino 8 package under `artifacts/yak/`. The script stages the plugin, root icon, and `yak/manifest.yml` in a fresh directory and checks that the project and manifest versions match. It uses Yak from your PATH or the standard Rhino 8 Windows installation; pass `-YakPath` for another installation. Use `-StageOnly` to prepare the files without Yak.

Generated plugins, icon copies, and packages stay out of source control. Download released packages from [GitHub releases](https://github.com/tsoumdoa/HopperWire/releases). The 0.4.0 and 0.5.0 packages remain in `yak/` as historical copies because their availability outside this repository has not been verified.
