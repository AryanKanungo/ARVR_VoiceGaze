# MainVRScene verification

Unity 6000.5.7f1; URP 17.5.0; Input System 1.20.0 (new Input System enabled). Existing Meta XR 207.0.1, OpenXR 1.17.1 and XR Management 4.5.4 preserved. No packages installed or upgraded. SampleScene originally contained only Main Camera, Directional Light and Global Volume. No XR Interaction Toolkit package, XR Device Simulator prefab or camera rig was present. OpenXR did create a HeadTrackingOpenXR device during Play Mode. Existing XR configuration was left intact.

MainVRScene uses an explicitly named Desktop Gaze Rig with removable DesktopGazeCamera fallback. GazeTargetResolver accepts a camera reference for future tracked-rig integration. This delivery does not claim XR Device Simulator testing.

## Controls

Focus the Game view in Play Mode. Hold right mouse button and move the mouse to look; WASD to move, Q/E down/up. Hold gaze on a target for 120 milliseconds. 1 selects a cube; 2 places it on a stable destination pad; 3 releases selection; 4 restores all target transforms. Placement completes selection. Occupied pads reject placement.

## Verified

- Project scripts compiled; final Editor isCompiling was false.
- Entered and exited Play Mode through Unity MCP.
- Ten final runtime checks passed: invalid Place; stabilized cube gaze; keyboard Select; pad gaze retaining selection; keyboard Place; keyboard Reset restoring position and occupancy; reselect; keyboard Release; invalid Select; W camera movement.
- Earlier direct check confirmed MaterialPropertyBlock highlight changes renderer color and immediate stale-target clearing.
- All six targets have colliders and use GazeTarget layer 8. Saved controller references, target count, status text and camera reference checked.
- Camera preview inspected; status panel enlarged for readability. Pad label world scale subsequently normalized.
- Latest runtime Console query: zero errors and zero warnings. Existing Unity AI service logs mention NoSubscription at Info level, unrelated to scene operation.

Keyboard verification used temporary Input System virtual keyboard events through runtime frames. Background input routing and Application.runInBackground were temporarily changed for the unfocused automation session and restored. Initial harness attempts failed because unfocused Unity discarded input; history is retained in InteractionTestHistory.txt. RuntimeSmokeTest.cs contains the final Unity MCP RunCommand test source, outside Assets so it is not part of the game assembly.

## Not verified

Physical keyboard/mouse end-to-end use by a human, mouse look automation, hardware XR tracking, XR Device Simulator control, standalone player build, and profiler allocation measurement. The resolver uses a single nonallocating Physics.Raycast and cached state/property blocks by inspection. No speech recognition or Fitts' Law experiments were added.

Scene is saved and Editor is left out of Play Mode. Open Assets/Scenes/MainVRScene.unity, press Play and click the Game view.
