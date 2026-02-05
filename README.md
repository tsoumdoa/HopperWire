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
  - `Faint Threshold` (Number): Wire length threshold for faint display in pixels (default: 100)
  - `Hidden Threshold` (Number): Wire length threshold for hidden display in pixels (default: 200)
  - `Refresh` (Boolean): **Click this button to refresh wire displays!** (toggle to refresh)
  - `Debug` (Boolean): Enable debug logging (default: false)
- **Outputs**:
  - `Status` (Text): Current status message
  - `Log` (Text): Debug log messages

## How It Works

1. Place **Wire Display Manager** component on your Grasshopper canvas
2. Set **Faint Threshold** for when wires should become faint
3. Set **Hidden Threshold** for when wires should be completely hidden
4. Optional: Set **Debug** to true for detailed logging
5. **Click the Refresh button** (toggle false → true) to process all wires!
6. The component will:
   - Scan all wire connections in document
   - Calculate straight-line distance between connected component centers
   - Set wires exceeding hidden threshold to "Hidden" display
   - Set wires exceeding faint threshold (but not hidden) to "Faint" display
   - Record all changes for undo/redo
7. Move components, add new ones, then **click Refresh again** to update!

## Display Modes

- **Default** (length ≤ faint threshold): Normal wire display
- **Faint** (faint threshold < length ≤ hidden threshold): Thin, transparent wires
- **Hidden** (length > hidden threshold): Wires completely invisible

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
[14:32:15.124]   Faint Threshold: 100.0 pixels
[14:32:15.125]   Hidden Threshold: 200.0 pixels
[14:32:15.126]   Debug Mode: True
[14:32:15.789] Processed 8 wires
[14:32:15.790]   Modified: 3 wires
[14:32:15.791]   Wire Number -> Curve: 250.5px > 200.0px = HIDDEN
[14:32:15.792]   Wire Point -> List: 150.3px > 100.0px = FAINT
[14:32:15.793]   Wire List -> Panel: 180.7px > 100.0px = FAINT
```

## Technical Details

- **Wire Length Calculation**: Straight-line distance between parameter centers (pixels)
- **Display Modes**: Uses `GH_ParamWireDisplay.faint` and `GH_ParamWireDisplay.hidden`
- **Manual Trigger**: No event listening - just click Refresh to process!
- **Undo/Redo**: Uses built-in `GH_WireDisplayAction` for proper undo support
- **Performance**: Process on-demand only - no background monitoring overhead

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
│ Faint Threshold: 100      │
│ Hidden Threshold: 200     │
│ Refresh: [false] → [true] │ ← Click this!
│ Debug: false             │
├───────────────────────────┤
│ Status: Processed 8      │
│ wires, 3 modified         │
│ (Faint > 100.0px,        │
│ Hidden > 200.0px)          │
├───────────────────────────┤
│ Log:                     │
│ (empty when debug off)   │
└───────────────────────────┘
```

## Workflow

1. **Set up your definition** in Grasshopper
2. **Place the Wire Display Manager** component
3. **Configure thresholds** (Faint and Hidden)
4. **Click Refresh** to apply wire display changes
5. **Move components around** - wires won't update yet
6. **Click Refresh again** - wires update to new positions!
7. **Adjust thresholds** if needed → Auto-refreshes
8. **Toggle Debug** on to see what's happening

## Advantages of Manual Trigger

✅ **No complex event handling** - simpler and more reliable
✅ **Predictable behavior** - only updates when you want it to
✅ **No performance overhead** - no background monitoring
✅ **Easy to control** - refresh exactly when needed
✅ **Works in all scenarios** - won't miss events or cause issues

## Threshold Guidelines

Recommended threshold values (pixels):
- **Faint**: 100-150 pixels - Good for medium-sized definitions
- **Hidden**: 200-300 pixels - For very long connections that cross large canvas areas
- **Large definitions**: Increase thresholds to 200-400 pixels
- **Compact definitions**: Decrease to 50-100 pixels

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
- Hidden wires are completely invisible (not shown even when selected)
- All display changes are recorded for undo/redo
- Debug logging writes to both output parameter and Rhino command history
- Manual trigger mode means wires only update when you click Refresh
- Close Rhino before rebuilding to avoid file lock errors

## License

Part of the VibeTest project.
