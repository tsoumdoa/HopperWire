# VibeTest - Wire Display Manager for Grasshopper

A Grasshopper plugin that manually triggers wire display updates based on length thresholds, with full undo/redo support and debug logging.

## Features

- **Manual Trigger**: Click the Refresh button to update wire displays (no complex event listening!)
- **Two-Level Thresholds**: Separate thresholds for Faint and Hidden display modes
- **Real-Time Updates**: Process wires whenever you click Refresh
- **Full Undo/Redo Support**: All wire display changes can be undone and redone
- **Auto-Restore**: Automatically restores original display when wires are shortened below threshold or removed
- **Debug Logging**: Optional debug mode with detailed logging to Rhino command history
- **Simple and Reliable**: No complex event handling - just click to refresh!

## Component

### Wire Display Manager
- **Category**: VibeTest > Display
- **Inputs**:
  - `Faint Threshold` (Number): Wire length threshold for faint display in pixels (default: 300)
  - `Hidden Threshold` (Number): Wire length threshold for hidden display in pixels (default: 900)
  - `Refresh` (Boolean): **Click this button to refresh wire displays!** (toggle to refresh)
  - `Debug` (Boolean): Enable debug logging (default: false)
- **Outputs**:
  - `Status` (Text): Current status message
  - `Log` (Text): Debug log messages

## How It Works

1. Place **Wire Display Manager** component on your Grasshopper canvas
2. Set **Faint Threshold** (default: 300px) for when wires should become faint
3. Set **Hidden Threshold** (default: 900px) for when wires should be completely hidden
4. Optional: Set **Debug** to true for detailed logging
5. **Click Refresh button** (toggle false → true) to process all wires!
6. The component will:
   - Scan all wire connections in document
   - Calculate straight-line distance between connected component centers
   - **Length ≤ 300px**: Normal display (default thickness)
   - **300px < Length ≤ 900px**: Set to "Faint" display
   - **Length > 900px**: Set to "Hidden" display
   - Record all changes for undo/redo
7. Move components, add new ones, then **click Refresh again** to update!

## Display Modes

- **Default** (length ≤ 300px): Normal wire display with default thickness ✅ NOW RESTORES CORRECTLY
- **Faint** (300px < length ≤ 900px): Thin, transparent wires
- **Hidden** (length > 900px): Wires completely invisible

## Using the Refresh Button

The Refresh button is a boolean input:
- Start with it set to `false`
- Click it to change it to `true` → This triggers wire processing!
- It will automatically process wires once when toggled
- Toggle back to `false` and then to `true` to refresh again
- Or just change the threshold values → This also triggers a refresh!

## Debug Mode

When **Debug** is set to `true`, the plugin logs all activities:
- Initialization and configuration changes
- Every wire that gets modified (Faint or Hidden)
- Wires that are restored
- Processing statistics

Debug messages are:
1. Shown in the `Log` output parameter
2. Written to Rhino command history (can be viewed with `Echo` command)

Example debug output:
```
[14:32:15.123] WireMonitor created (manual trigger mode)
[14:32:15.124]   Faint Threshold: 300.0 pixels
[14:32:15.125]   Hidden Threshold: 900.0 pixels
[14:32:15.126]   Debug Mode: True
[14:32:15.127] Document has 45 objects
[14:32:15.128]   Objects by type:
[14:32:15.129]     Components: 12
[14:32:15.130]     Parameters: 33
[14:32:15.131]   Skipping parameter Faint (excluded)
[14:32:15.132]   Skipping parameter Hidden (excluded)
[14:32:15.133]   Skipping parameter Refresh (excluded)
[14:32:15.134]   Skipping parameter Debug (excluded)
[14:32:15.789] Processed 15 unique wires
[14:32:15.790]   Modified: 5 wires
[14:32:15.791]   Wire Number -> Curve: 950.5px > 900.0px = HIDDEN
[14:32:15.792]   Wire Point -> List: 350.3px > 300.0px = FAINT
[14:32:15.793]   Wire List -> Panel: 180.7px > 300.0px = FAINT
```

## Technical Details

- **Comprehensive Connection Detection**: Thoroughly scans ALL parameters in document and processes ALL their source connections ✅ IMPROVED
- **Proper Component Exclusion**: Searches through all components to find and exclude plugin's own parameters accurately ✅ FIXED
- **Duplicate Prevention**: Uses unique connection IDs to avoid processing same wire twice
- **Proper Restoration**: Correctly restores wires to default display when below threshold
- **Wire Length Calculation**: Straight-line distance between parameter centers (pixels)
- **Display Modes**: Uses `GH_ParamWireDisplay.faint` and `GH_ParamWireDisplay.hidden` and `GH_ParamWireDisplay.default`
- **Manual Trigger**: No event listening - just click Refresh to process!
- **Undo/Redo**: Uses built-in `GH_WireDisplayAction` for proper undo support
- **Performance**: Process on-demand only - no background monitoring overhead
- **Debug Mode**: Shows detailed information about document structure, excluded parameters, and all wire processing when enabled

## Building

```bash
dotnet build
```

The build will produce `.gha` files for each target framework:
- `bin/Debug/net48/VibeTest.gha` (Rhino 6/7)
- `bin/Debug/net7.0/VibeTest.gha` (Rhino 8 Mac)
- `bin/Debug/net7.0-windows/VibeTest.gha` (Rhino 8 Windows)

**Note**: Close Rhino before rebuilding if the plugin is loaded!

## Installation

1. Copy the appropriate `.gha` file for your Rhino version to the Grasshopper libraries folder:
   - **Rhino 6**: `%AppData%\Roaming\Grasshopper\Libraries\`
   - **Rhino 7**: `%AppData%\Roaming\Grasshopper\Libraries\`
   - **Rhino 8**: `%AppData%\Roaming\McNeel\Rhinoceros\8.0\Plug-ins\Grasshopper\`

2. Restart Grasshopper
3. Find the component in the **VibeTest > Display** tab

## Usage Example

```
┌───────────────────────────┐
│   Wire Display Manager    │
├───────────────────────────┤
│ Faint Threshold: 300      │
│ Hidden Threshold: 900     │
│ Refresh: [false] → [true] │ ← Click this!
│ Debug: false             │
├───────────────────────────┤
│ Status: Processed 8      │
│ wires, 3 modified         │
│ (Faint > 300.0px,        │
│ Hidden > 900.0px)          │
├───────────────────────────┤
│ Log:                     │
│ (empty when debug off)   │
└───────────────────────────┘
```

## Workflow

1. **Set up your definition** in Grasshopper
2. **Place Wire Display Manager** component
3. **Configure thresholds** (Faint: 300, Hidden: 900)
4. **Enable Debug mode** (optional) to see what's being processed
5. **Click Refresh** to apply wire display changes
6. **Check Debug Log** to see:
   - Document structure (how many components/parameters)
   - Which parameters are being excluded
   - Details of every wire connection
7. **Move components around** - wires won't update until you click Refresh
8. **Click Refresh again** - all wire displays update!
9. **Adjust thresholds** if needed → Auto-refreshes
8. **Toggle Debug** on to see what's happening

## Advantages of Manual Trigger

✅ **No complex event handling** - simpler and more reliable
✅ **Predictable behavior** - only updates when you want it to
✅ **No performance overhead** - no background monitoring
✅ **Easy to control** - refresh exactly when needed
✅ **Works in all scenarios** - won't miss events or cause issues

## Threshold Guidelines

Recommended threshold values (pixels):
- **Faint**: 200-400 pixels - Good for medium to large-sized definitions (default: 300)
- **Hidden**: 600-1200 pixels - For very long connections that cross large canvas areas (default: 900)
- **Large definitions**: Increase thresholds to 400-800 for faint, 1000-1500 for hidden
- **Compact definitions**: Decrease to 100-200 for faint, 300-500 for hidden

## Tips

- Start with higher thresholds to see the effect, then adjust downward
- Use Debug mode when first setting up to understand wire lengths
- Hidden wires are useful for very long connections that clutter the canvas
- Faint wires reduce visual noise while still showing connectivity
- All display changes are recorded for undo/redo
- Debug logging writes to both output parameter and Rhino command history
- Component doesn't need to stay on canvas for wire changes to persist
- Close Rhino before rebuilding the plugin

## Notes

- Wire length is calculated as straight-line distance between component centers
- Only affects wire display, does not affect component functionality
- **Plugin's own input wires are excluded from processing** to avoid interference
- Hidden wires are completely invisible (not shown even when selected)
- All display changes are recorded for undo/redo
- Debug logging writes to both output parameter and Rhino command history
- Manual trigger mode means wires only update when you click Refresh
- Close Rhino before rebuilding to avoid file lock errors

## License

Part of the VibeTest project.
