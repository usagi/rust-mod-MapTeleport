# MapTeleport

Teleport utility plugin for Rust (Carbon/uMod).

MapTeleport provides quick player teleport and camera-target utilities with map-marker trigger support.

## Features

- Teleport to:
  - look target
  - coordinates(eg., 100 200)
  - grid reference(eg., s10)
  - Map marker trigger with selectable multi-click modes
- Teleport debugcamera to:
  - look target
  - coordinates
  - grid reference
- Map-marker trigger teleport with selectable mode:
  - Single: Teleport on marker creation once
  - Double: Teleport on marker creation twice within time window and position tolerance (like triple-click)
  - Triple: Teleport on marker creation three times within time window and position tolerance (like penta-click)
- Configurable time window and position tolerance for multi-click marker triggers
- Optional marker removal after marker-trigger teleport
- Permission-based access with admin implicit access

## Commands

### Chat Commands

- `/mtp`
  - Teleport to look target
- `/mtp <x> <z>`
  - Teleport to world coordinates
- `/mtp <grid>`
  - Teleport to grid center (example: `/mtp s10`)

- `/mtpc`
  - Camera workflow to look target
- `/mtpc <x> <z>`
  - Camera workflow to world coordinates
- `/mtpc <grid>`
  - Camera workflow to grid center

### Console Commands

- `mapteleport.tp`
- `mapteleport.tpc`

These accept the same argument patterns as chat commands.

## Permission

- `mapteleport.use`

Admins can use the plugin by default.

Grant example:

```bash
o.grant group admin mapteleport.use
```

## Installation

1. Place `MapTeleport.cs` in your server `plugins` directory.
2. Reload plugin:

```bash
o.reload MapTeleport
```

3. Adjust config at `configs/MapTeleport.json`.

## Configuration

Current keys:

- `Teleport Height Offset`
- `Max Raycast Distance`
- `Show Teleport Success Message`
- `Enable Map Marker Trigger Teleport`
- `Map Marker Trigger Mode (Single/Double/Triple)`
- `Map Marker Multi-Click Time Window Seconds`
- `Map Marker Multi-Click Position Tolerance Meters`
- `Remove Trigger Marker After Marker Teleport`
- `Camera Teleport Return Delay Seconds`

## Notes

- Grid teleport uses map-grid parsing and center targeting.
- Camera command uses a temporary move + debugcamera workflow.

## Author

- [USAGI/USAGI.NETWORK](https://usagi.network)
