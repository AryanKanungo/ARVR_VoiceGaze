# VOICEVERSE — The Design Atelier

IVRAR Group 05's architectural studio prototype for accessible voice and simulated head-gaze interaction. The current desktop scene uses a Windows PC, keyboard, mouse and microphone; no physical VR headset is required.

## Project requirements

- **Unity:** Unity 6.5, Editor **6000.5.7f1**. Use this exact version.
- **Rendering:** Universal Render Pipeline **17.5.0**.
- **Input:** Unity Input System **1.20.0**.
- **Main scene:** `Assets/Scenes/MainVRScene.unity`.
- **Voice:** Windows `UnityEngine.Windows.Speech.KeywordRecognizer`, with 31 registered phrases.
- **Reference development machine:** Intel Core i5 13th Gen, 16 GB RAM, Intel Iris Xe. This is not a validated minimum specification or FPS guarantee.

## What is done

- A studio interior with a central workbench, materials library, display gallery, lighting corner and precision display, using Unity primitives and a small URP material palette.
- Seven original interactables: Red, Blue, Green and Yellow Cubes, Study Sphere, Study Cylinder and Pavilion Model.
- Four working destinations: Left Pad, Right Pad, Gallery A and Gallery B.
- Desktop camera controls, centre-screen gaze targeting, highlights and persistent selection. Gaze stabilization is **120 ms**, with an **8 m** maximum distance.
- Voice selection, placement, movement, rotation, color changes, bounded resizing, duplication, protected deletion and single-edit Undo. Core keyboard fallback is preserved.
- Labeled, targetable lighting and materials controls. The materials control changes the selected object's color.
- A seven-page camera-anchored handbook, hidden initially, with manipulation blocked while it is open.
- A live dashboard and CSV logging for command outcomes, task attempts and runtime performance.

The precision wall remains a display, not a validated Fitts' Law experiment. Existing XR packages/settings are retained; the desktop workflow does not require setting up a headset or simulator.

## Set up on another PC

1. Install **Git**, **Unity Hub**, and Unity Editor **6000.5.7f1**. Complete Unity's license activation. Make Git available on `PATH` before opening Unity: the project contains a Git-based package dependency.
2. Clone this repository to a local folder. Confirm it contains `Assets`, `Packages`, `ProjectSettings` and Unity `.meta` files. Do not create a fresh project and copy only the scripts.
3. In Unity Hub, choose **Add project from disk** and select the repository root. Open it with **6000.5.7f1** rather than upgrading it.
4. Let the first import finish. Unity restores dependencies from `Packages/manifest.json` and `Packages/packages-lock.json` and regenerates `Library`. **Initial dependency restoration requires internet access.** Keep the checked-in package versions; do not remove XR packages or upgrade dependencies just to run the demo.
5. Open `Assets/Scenes/MainVRScene.unity`. Preserve the checked-in graphics and Input System settings, including Vulkan support. Do not force a graphics API for one GPU as a default setup step.
6. Set up the microphone below. Enter **Play Mode** and click the **Game** view. Test keyboard interaction first, then check the dashboard's recognizer status before trying speech.
7. Say **Help** or press **H** for the handbook. Close it before manipulating objects. Play Mode changes are temporary.

Unity MCP is an optional development integration; running the scene does not require connecting an AI client. Windows build-support modules are only needed for a later standalone build, not for these Editor setup steps.

### Microphone setup

- Use **Windows** for voice. The recognizer code targets Windows Editor/standalone; macOS/Linux voice support is not implemented or validated.
- Select an unmuted, working default microphone in Windows sound settings.
- In Windows microphone privacy settings, enable **Microphone access** and **Let desktop apps access your microphone**. Labels can vary by Windows version.
- Install compatible **English speech recognition language components** and use the English phrases below. Installing components may require internet; the project uses the local Windows recognizer rather than a cloud speech service or API key.
- The scene uses **Low** recognition confidence and a **0.75 s** repeat cooldown. Speak separate commands rather than rapidly repeating the same phrase.
- If initialization fails, check microphone selection, permissions and speech language. On `Interaction Systems`, the `VoiceCommandRecognizer` component has **Start / Retry Voice Recognition** and **Stop Voice Recognition** context-menu actions. Keyboard fallback remains usable.

## Controls

### Keyboard and mouse

| Input | Action |
| --- | --- |
| Hold right mouse + move mouse | Look / aim simulated head-gaze |
| W / A / S / D | Move camera |
| Q / E | Move camera down / up |
| 1 / 2 / 3 / 4 | Select / Place / Release / Reset |
| H | Open / close handbook |
| Left / right arrow, with handbook open | Previous / next page |

Additional editing/studio commands use voice; they do not each have a keyboard binding.

### Exact voice phrases

| Phrase(s) | Action |
| --- | --- |
| **Select** | Lock the stable gazed-at object. |
| **Place** | Put the selection on a stable gazed-at, unoccupied destination; clear selection. |
| **Release** | Clear selection without moving it. |
| **Reset** | Restore original object transforms/colors, remove copies, restore normal lighting and clear Undo. |
| **Rotate left**, **Rotate right** | Rotate selection −45° / +45° around local Y. |
| **Rotate 45 degrees**, **Rotate 90 degrees** | Rotate selection +45° / +90° around local Y. |
| **Flip** | Rotate selection 180° around local X. |
| **Move up**, **Move down** | Move selection +0.25 / −0.25 m along world Y. |
| **Move left**, **Move right** | Move selection −0.25 / +0.25 m along world X. |
| **Move forward**, **Move backward** | Move selection +0.25 / −0.25 m along world Z, independent of camera facing. |
| **Change color** | Cycle chalk, olive, terracotta, blue and ochre on the selection. |
| **Make bigger**, **Make smaller** | Multiply scale by 1.25 / 0.75, bounded to 50–200% of original scale. |
| **Duplicate** | Create one selectable nearby copy, up to five live copies. Reject if no clear nearby spot exists. |
| **Delete** | Remove only a selected user-created copy. Original objects are protected. |
| **Undo** | Reverse one supported object edit: movement, rotation, color, scale, placement, duplication or deletion. |
| **Activate** | Operate the stable gazed-at lighting/material control. Material changes require a selection. |
| **Turn lights on**, **Turn lights off** | Use normal lighting / readable daylight safety mode. |
| **Go home** | Return the camera to its starting pose. |
| **What am I looking at** | Display the stable gaze target's name in the status UI. |
| **Help**, **Open handbook** | Open the handbook. |
| **Close handbook** | Close the handbook and resume editing. |
| **Next page**, **Previous page** | Navigate the open handbook. |

While the handbook is open, navigation, Help and “What am I looking at” remain available; edits are rejected and logged. Undo stores one edit with no redo; selection, lighting, camera movement and Reset are not undoable. Increasing then decreasing scale by 25% does not restore the starting size—use Undo or Reset.

## First-run check before handing over work

1. Close the handbook. Aim at Red Cube until its name/highlight is stable, then press **1**. Look at Left Pad and press **2**. Confirm placement; press **4** to restore it.
2. Try spoken **Select → Place**, then **Select → Release → Reset**. Check the dashboard's heard-command/result fields. A “Listening” status alone does not prove the microphone works on that PC.
3. Select an object and try **Change color**, **Make bigger** and **Undo**. Create a copy with **Duplicate**, gaze at/select the copy, and **Delete** it. Reset must remove copies and restore originals.
4. Gaze at the labeled lighting control and say **Activate**. For materials, select an object first, approach the library and Activate its labeled control. Move closer when targets are beyond the 8 m gaze range.
5. Repeatedly open/close the handbook with **H**, test arrow navigation, and confirm manipulation is blocked while open.
6. Check the Console for errors and the printed CSV path. Confirm a session CSV exists after leaving Play Mode.

## Telemetry

CSV files are saved in `Application.persistentDataPath/Telemetry`. With the current Windows project identity:

```text
%USERPROFILE%\AppData\LocalLow\DefaultCompany\IVRAR_VoiceGaze\Telemetry
```

The Console prints the actual session path at startup. This folder is outside the repository and is local to each collaborator's PC.

- The existing 50-column CSV distinguishes command, task, performance and session records using `record_type`.
- Commands record input source, objects, accepted/rejected outcome, rejection reason, timestamps, and recognition confidence when supplied.
- A task starts on successful **Select** and succeeds on successful **Place**. Release, Reset or abandonment before Place ends it unsuccessfully. Rejections increment the active task's rejection count; each rejection is not a separate failed task.
- The dashboard reports gaze/selection, recognizer status, last command/result, processing latency, command/task statistics and average FPS from runtime data.
- Callback-to-action timing is **not acoustic speech latency**. Geometric gaze distance/width/index-of-difficulty records do not establish a validated experiment. Acquisition time and throughput remain N/A without an explicit acquisition trial.
- Automated/synthetic records are not human participant results. Keep local test CSVs separate from any intentionally collected study dataset.

## Git collaboration

- Pull before starting and use a branch for your work. Agree who edits `MainVRScene.unity` to avoid simultaneous scene conflicts.
- Commit `Assets`, `Packages`, `ProjectSettings` and corresponding **`.meta` files** together. Preserve both package manifest/lockfiles. Move/rename assets in Unity so metadata/GUIDs stay intact.
- Preserve text-serialized assets. Review scene, Inspector-reference and package/settings changes before committing; avoid incidental local setup changes.
- Do not commit generated `Library`, `Temp`, `Logs`, `Obj`, `Builds`, `UserSettings` or IDE files; the root `.gitignore` excludes them. Share a clean repository rather than the whole generated working folder.
- Before handing over a branch, perform the check above and report Console errors, platform issues and whether speech was actually tested using a microphone.

## Verification status and limitations

The latest implementation passed compilation, **123 new-command checks**, **10 existing keyboard/gaze regressions**, and **28 focused input/telemetry checks**. These covered protected originals, copy limits, Undo, Reset, resized placement, both controls, handbook navigation, camera look synchronization, recognizer restart and CSV validity. The final Console check reported zero errors.

Select/Place/Release/Reset were previously tested with real speech. The latest added phrases were checked through automated routing and recognizer initialization, **not physical microphone tests**. Repeat spoken checks on each collaborator's PC. Stable 60 FPS, standalone build readiness, cross-platform voice support and a validated human experiment are not claimed.

## Quest 3 scene and development profile

The Windows scene remains `Assets/Scenes/MainVRScene.unity`. The separate Quest scene is `Assets/Scenes/MainQuestScene.unity`; its Android profile is `Assets/Settings/Build Profiles/VOICEVERSE Quest 3.asset`. Windows automatic XR startup is disabled. Android uses OpenXR, IL2CPP and ARM64. No additional XR or Voice packages were installed for this setup.

Quest uses head-gaze, not eye tracking. The existing 120 ms gaze stabilization, interaction controller and CSV logger are shared. The scene contains one active tracked camera/audio listener. Headset comfort, tracking and microphone operation still require physical Quest 3 testing.

### Existing Wit.ai configuration

Use the existing `voice_verse` app and `Assets/wit_voice_verse.asset`; do not create another app or duplicate configuration. The configuration and local Wit settings are excluded from Git because they may contain access tokens. Never paste tokens into scripts, commit them, or send them in chat. On another PC, link the same app locally and run **VOICEVERSE > Quest > Create or Validate Quest Scene** to assign that local configuration.

Quest voice uses final Meta Voice SDK/Wit.ai transcription and maps the same 31 command phrases listed above, without requiring trained Wit.ai intents. Case, surrounding punctuation and repeated whitespace are normalized; spoken forty-five/ninety-degree forms map to the existing numeric command strings. Unknown phrases are rejected through the existing telemetry. Partial transcripts never execute commands, and duplicate SDK notifications produce only one outcome per request. Transcription confidence is left unavailable rather than substituting an intent score. Optional `select_object` intent fallback remains available but is disabled by default; its separate confidence threshold applies only to that fallback. Conduit/attribute dispatch stays disabled because the bridge handles final transcription directly. These routes are implemented, not physically verified on Quest.

### Quest activation and fallback controls

- **Right trigger:** start one voice request. Allow microphone permission when prompted; speech processing requires Wi-Fi/internet. This is controller-triggered activation, not continuous hands-free listening.
- **A / B:** Select / Place.
- **X / Y:** Release / Reset while the handbook is closed.
- **Left menu button:** open/close the handbook.
- **X / Y with handbook open:** next/previous page. Object manipulation remains blocked while open.

Controller fallback actions currently use the existing `system` input-source label in CSV; recognized speech uses `voice`. Microphone denial, missing configuration, unsupported/low-confidence intents, request timeout and network failure have visible status messages. No headset recognition success is claimed until spoken commands are tested on a physical device. Only physical-room movement is configured; artificial locomotion has not been added.

### Building and testing later

1. Save your work and select the **VOICEVERSE Quest 3** profile in Unity Build Profiles. It includes only `MainQuestScene` and preserves the global Windows scene list.
2. Build a development APK. This project's first Android build can spend substantial time compiling the preinstalled SDKs' shaders; a progress window alone does not establish a failure.
3. When the headset is available, enable Developer Mode, connect a USB data cable, approve USB debugging, and install the built APK using Meta Quest Developer Hub or Unity's Build And Run.
4. In the app, aim at an object, press the right trigger, grant microphone access and say an existing exact command such as **Select**, **Place** or **Help**. Verify the actual action and voice CSV record. Test permission denial/retry, network loss and pause/resume as well.
5. Use the controller fallback to check Place, Release, Reset and handbook navigation. CSV remains under the device's `Application.persistentDataPath/Telemetry`; its actual path is logged at runtime.
6. Select the Windows platform/profile again to return to normal desktop development. Use `MainVRScene` for Windows and the Quest profile for headset builds.

### Understanding Viewer diagnostics

Use **VOICEVERSE > Quest > Diagnose Wit Text Request (No Microphone)** for a bounded text-only request through the same SDK factory as the Edit-mode Understanding Viewer. It reuses the existing configuration and its timeout. The Console and `Builds/QuestVerification/wit-text-diagnostic.json` record only HTTP status, a whitelisted error category and timing—never tokens, request headers/URLs or response bodies. This tests the Viewer text/network path separately from headset audio capture. A successful text request does not prove microphone recognition. Runtime Quest errors expose the same safe status/category diagnostics, and timeout, microphone denial, missing configuration and network loss remain visible in the status panel.
