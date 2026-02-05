# VibeTest - Wire Display Manager for Grasshopper

A Grasshopper plugin that automatically sets wires longer than a specified threshold to "Faint" display mode, with full undo/redo support and real-time monitoring.

## Features

- **Automatic Wire Monitoring**: Automatically detects when wires are added, removed, or modified
- **Length-Based Display**: Sets wires exceeding the threshold to Faint display mode
- **Real-Time Updates**: Monitors canvas changes and updates wire display instantly
- **Full Undo/Redo Support**: All wire display changes can be undone and redone
- **Auto-Restore**: Automatically restores original display when wires are shortened below threshold or removed
- **Toggle On/Off**: Can be enabled/disabled without losing settings

## Component

### Wire Display Manager
- **Category**: VibeTest > Display
- **Inputs**:
  - `Length Threshold` (Number): Wire length threshold in pixels (default: 100)
  - `Active` (Number): Enable/disable monitoring (1 = active, 0 = inactive, default: 1)
- **Outputs**:
  - `Status` (Text): Current status message

## How It Works

1. Place the **Wire Display Manager** component on your Grasshopper canvas
2. Set the **Length Threshold** to your desired maximum wire length
3. Ensure **Active** is set to 1 (or any number > 0)
4. The component will:
   - Monitor all wire connections in the document
   - Calculate straight-line distance between connected component centers
   - Set wires exceeding the threshold to "Faint" display
   - Automatically restore display when wires are shortened or removed
5. Toggle **Active** to 0 to disable monitoring and restore all wires to original display

## Technical Details

- **Wire Length Calculation**: Straight-line distance between parameter centers (pixels)
- **Display Modes**: Uses `GH_ParamWireDisplay.faint` for long wires
- **Event Monitoring**: Listens to `ObjectsAdded`, `ObjectsDeleted`, and `SettingsChanged` events
- **Undo/Redo**: Uses built-in `GH_WireDisplayAction` for proper undo support
- **Performance**: Optimized to minimize canvas lag on large definitions

## Building

```bash
dotnet build
```

The build will produce `.gha` files for each target framework:
- `bin/Debug/net48/VibeTest.gha`
- `bin/Debug/net7.0-windows/VibeTest.gha`
- `bin/Debug/net7.0/VibeTest.gha`

## Installation

1. Copy the appropriate `.gha` file for your Rhino version to Grasshopper libraries folder
   - **Rhino 6**: `AppData\Roaming\Grasshopper\Libraries\`
   - **Rhino 7**: `AppData\Roaming\Grasshopper\Libraries\`
   - **Rhino 8**: `AppData\Roaming\McNeel\Rhinoceros\8.0\Plug-ins\Grasshopper\`

2. Restart Grasshopper
3. Find the component in the **VibeTest > Display** tab

## Usage Example

```
┌─────────────────────────┐
│ Wire Display Manager │
├─────────────────────────┤
│ Threshold: 150       │
│ Active: 1            │
├─────────────────────────┤
│ Status: Active -       │
│ Monitoring wires >      │
│ 150.0 pixels         │
└─────────────────────────┘
```

## Notes

- Wire length is calculated as straight-line distance between component centers
- Only affects wire display, does not affect component functionality
- All display changes are recorded for undo/redo
- Component must be active on the canvas for monitoring to work
- Settings persist as long as the component remains on the canvas

## License

Part of the VibeTest project.
