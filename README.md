# Bridge Builder Tool by Ryan Amos

This project contains a Unity editor tool for placing and editing modular bridges.

Designers can:
- Click and drag to place a new bridge
- Preview bridge geometry with a dedicated preview material during placement
- Adjust an existing bridge by moving the start and end handles in the scene view

## How to Use

1. Open the project in Unity.
2. Open `Assets/Scenes/BridgeChallenge.unity`.
3. Select `BridgeBuilderAuthoring - Start Here` in the hierarchy.
4. Click `Begin Drag Placement` in the inspector.
5. Click and drag in the scene view to place the bridge.
6. Move the start/end handles to edit the bridge after placement.

## Segment Setup

Each bridge segment prefab uses `BridgeSegment` to define:
- Segment type
- Authored length

If the art changes, select the prefab root and run:

`Measure And Apply Length`

## Technical Notes

- `BridgeBuilder` handles bridge layout, segment reuse, preview/final visual state, and hierarchy management.
- `BridgeBuilderAuthoring` handles authoring workflow, start/end points, placement state, and recentering of the authoring object after commit.
- `BridgeBuilderAuthoringEditor` handles inspector UI and scene-view interaction.

## Performance Notes

- Existing bridge edits reuse live segment instances instead of destroying and rebuilding the entire bridge on every handle movement.
- Removed segments are pooled by segment type and reused when possible during subsequent edits.
- Preview/final material swapping uses cached renderer/material state per segment root.
- Placement planning reuses scratch data to reduce editor-time allocations.

## Known Limitations

- Placement currently assumes a flat plane at `y = 0`.
- There is currently no terrain conformance or snapping system.

## Time Invested

Estimated implementation time: 2.5 hours.
