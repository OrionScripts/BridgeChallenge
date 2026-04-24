# Bridge Builder Tool by Ryan Amos

This project contains a Unity bridge placement prototype with both editor authoring and runtime Play Mode placement.

Designers can in edit mode:
- Click and drag to place a new bridge
- Preview bridge geometry with a dedicated preview material during placement
- Place multiple authored bridges that persist into Play Mode
- Adjust the latest authored bridge by moving the start and end handles in the scene view

Players/testers can in Play Mode:
- Left-click and drag on the ground plane to preview a bridge
- Release to commit the bridge
- Place multiple bridges by starting another left-click drag after committing one
- See a green `Bridge Placed!` toast and hear a procedural confirmation tone when a bridge placement is finalized
- Hear a separate confirmation tone when an endpoint edit is finalized
- Click `Edit Bridges` to show endpoint handles for all placed bridges
- Click `Clear Bridges` to delete all editable bridges in Play Mode and return to placement mode
- Drag any visible green or orange endpoint handle to adjust that bridge
- Pan the isometric camera with `WASD`, arrow keys, or middle-mouse drag
- Rotate the isometric camera with `Q`/`E` or `Alt + left-mouse drag`
- Zoom the isometric camera with the mouse wheel
- Endpoint handles keep a consistent screen size while zooming
- Right-click or press `Escape` to cancel the current placement
- Press `R` to clear all editable bridges in Play Mode

## How to Use

### Runtime Placement

1. Open the project in Unity.
2. Open `Assets/Scenes/BridgeChallenge.unity`.
3. Press Play.
4. Left-click and drag in the Game view to preview a bridge.
5. Drag until the guide turns cyan, then release to commit the bridge.
6. Repeat the same drag gesture anywhere else to place additional bridges.

The HUD shows the current editable bridge count, including editor-authored bridges that persist into Play Mode. Successful placements show a brief green `Bridge Placed!` toast and play a small synthesized confirmation sound.

### Runtime Editing

1. Click `Edit Bridges` at the bottom of the Game view.
2. Green and orange endpoint handles appear for editor-authored bridges and Play Mode-created bridges.
3. Drag either endpoint handle to reshape that bridge.
4. Release to commit the edit.
5. Click `Exit Edit Bridges` to hide handles and return to placement mode.

Use `Clear Bridges` or press `R` to remove all editable Play Mode bridges and handles, including bridges authored in the editor before pressing Play. Clearing also exits edit mode.

If a bridge or edit is too short for the modular start/end pieces, the guide turns orange and the bridge will not commit.

### Runtime Camera

- Pan with `WASD`, arrow keys, or middle-mouse drag.
- Rotate with `Q`/`E` or `Alt + left-mouse drag`.
- Zoom with the mouse wheel.
- Endpoint handles maintain a consistent screen size while zooming.

### Editor Authoring

1. Open the project in Unity.
2. Open `Assets/Scenes/BridgeChallenge.unity`.
3. Select `Editor Bridge Placement` in the hierarchy.
4. Click `Begin Drag Placement` in the inspector.
5. Click and drag in the scene view to place the bridge.
6. Continue click-dragging to add more authored bridges without leaving placement mode.
7. Click `Exit Drag Placement` when finished placing bridges.
8. Move the start/end handles to edit the latest authored bridge after placement.
9. Click `Clear Editor Bridges` to remove editor-authored bridges.

## Segment Setup

Each bridge segment prefab uses `BridgeSegment` to define:
- Segment type
- Authored length

If the art changes, select the prefab root and run:

`Measure And Apply Length`

## Technical Notes

- `BridgeBuilder` handles bridge layout, segment reuse, preview/final visual state, and hierarchy management.
- Editor-authored bridges are grouped as separate children under the builder content root so multiple authored bridges can persist into Play Mode.
- `RuntimeBridgePlacement` handles Play Mode placement, adoption of editor-authored bridges for runtime editing, runtime bridge records, edit-mode endpoint handles, the bottom HUD buttons, the bridge counter/toasts, and procedural confirmation audio.
- `RuntimeIsometricCamera` frames the Play Mode scene from an ARPG-style isometric angle and handles pan/zoom/rotation controls.
- `BridgeBuilderAuthoring` handles authoring workflow, start/end points, placement state, and recentering of the authoring object after commit.
- `BridgeBuilderAuthoringEditor` handles inspector UI and scene-view interaction.

## Performance Notes

- Existing bridge edits reuse live segment instances instead of destroying and rebuilding the entire bridge on every handle movement.
- Removed segments are pooled by segment type and reused when possible during subsequent edits.
- Preview/final material swapping uses cached renderer/material state per segment root.
- Placement planning reuses scratch data to reduce drag-time allocations in both edit mode and Play Mode.

## Known Limitations

- Placement currently assumes a flat plane at `y = 0`.
- There is currently no terrain conformance or snapping system.

## Time Invested

Estimated editor implementation time: 2.5 hours.
Estimated runtime implementation time: 1.5 hours.
