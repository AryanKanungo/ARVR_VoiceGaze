# IVRAR VoiceGaze

Accessible voice + simulated head-gaze spatial interaction prototype.

- **Unity:** 6.5, Editor **6000.5.7f1**, Universal Render Pipeline.
- **Main scene:** `Assets/Scenes/MainVRScene.unity`.
- **Desktop voice support:** Windows, using `UnityEngine.Windows.Speech.KeywordRecognizer`.

## Setup

1. Clone the repository and add its root folder in Unity Hub.
2. Open with Unity **6000.5.7f1**. Let Unity restore the dependencies recorded in `Packages/manifest.json` and `Packages/packages-lock.json`.
3. Open `Assets/Scenes/MainVRScene.unity`, enter Play Mode, and focus the Game view. The desktop controls support testing without a physical headset.
4. For voice input, use an unmuted default Windows microphone with compatible English speech-language data installed. Enable **Microphone access** and **Let desktop apps access your microphone** in Windows Settings → Privacy & security → Microphone. Check the in-scene recognizer status; keyboard commands remain available.

## Controls

- **Look:** hold right mouse button and move the mouse.
- **Move camera:** WASD; Q/E move down/up.
- **Keyboard:** 1 Select, 2 Place, 3 Release, 4 Reset.
- **Voice:** “Select”, “Place”, “Release”, “Reset”. Hold gaze on the intended cube/pad until its name appears; gaze stabilization is 120 ms.
- **Selected-object rotation:** “Rotate left” (−45°), “Rotate right” (+45°), “Rotate 45 degrees” (+45°), “Rotate 90 degrees” (+90°), around local Y.
- **Selected-object movement:** “Move up”, “Move down”, “Move left”, “Move right”, in 0.25 m world-space steps.

Select a cube, then gaze at a destination pad and use Place. Release clears selection; Reset restores the scene. Rotation/movement requires a selection.

## Telemetry

CSV files are created in `Application.persistentDataPath/Telemetry`. With the current Windows project identity, this is:

`%USERPROFILE%\AppData\LocalLow\DefaultCompany\IVRAR_VoiceGaze\Telemetry`

The Unity Console prints the session's CSV path. Command and task records are distinguished by record type. Callback-to-action latency is not acoustic speech latency. Fitts acquisition time and throughput are N/A without an explicit acquisition trial.

## Collaboration

Commit `Assets`, `Packages`, `ProjectSettings`, and all Unity `.meta` files together. Preserve existing metadata/GUIDs when moving or renaming assets. Generated caches, logs, IDE files, and build outputs are excluded by `.gitignore`.
