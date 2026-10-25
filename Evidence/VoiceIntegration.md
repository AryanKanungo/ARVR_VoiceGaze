# Windows microphone command integration

## Changed files

- Assets/Scripts/VoiceCommandRecognizer.cs: Windows KeywordRecognizer lifecycle, asynchronous callback queue, confidence filtering, duplicate/cooldown suppression, timestamps and bounded event history.
- Assets/Scripts/SpatialVoiceIntentController.cs: status text extension only; selection, placement, release, reset, keyboard routing and the interaction state machine remain unchanged.
- Assets/Scenes/MainVRScene.unity: recognizer attached to Interaction Systems, controller reference assigned, status Text best-fit enabled (20–30) to fit recognition feedback. Scene geometry, camera controls, materials and gaze algorithm preserved. A verified black Game-view bug was corrected using the desktop camera's URP allowXRRendering=false flag; XR packages, runtime and project settings remain intact.
- ProjectSettings/EditorBuildSettings.asset: MainVRScene placed first in the desktop build scene list; previous scene entries retained.

## Configuration and use

Windows Editor / Windows standalone only, with a guarded keyboard-only fallback on other platforms. Verified installed Unity 6000.5.7f1, WindowsEditor host, StandaloneWindows64 target, KeywordRecognizer in UnityEngine.CoreModule, PhraseRecognitionSystem.isSupported true, and Realtek microphone enumeration.

Press Play and say select, place, release or reset. The tested scene configuration uses Low minimum confidence (the script's initial default is Medium), same-command cooldown .75 seconds, event expiry 2 seconds, and recent history 32 events. Inspector changes to native confidence require stopping/restarting the recognizer. Component context menu offers Start / Retry Voice Recognition and Stop Voice Recognition during Play Mode. Keyboard 1–4 remains available.

Speech callbacks do not access Unity GameObjects. They enqueue session-tagged events in a bounded queue; LateUpdate order 30 processes at most eight events after gaze order 10 and keyboard order 20. Dispatch calls the existing controller methods. Duplicate phrase timestamps, repeat cooldowns, low confidence, stale events and unavailable controllers are rejected. Each delivered recognition event records received UTC time, phrase-start UTC time, duration, confidence and disposition; diagnostic logs and the in-memory RecognitionHistory expose these. Sent means dispatched to the existing state machine; its own feedback reports invalid interaction commands.

Initialization and health checks are guarded. Native errors stop/dispose this recognizer, update the panel and leave keyboard control intact. Disable/destroy/application quit unsubscribe and dispose native resources. The shared PhraseRecognitionSystem is deliberately not shut down because other recognizers may use it. No audio is recorded or saved by this component.

## Verification so far

- Compilation succeeded, no C# errors.
- Play Mode initialization: component Listening, native PhraseRecognitionSystem Running, default microphone detected.
- All ten existing Phase 1 runtime checks passed with voice attached: invalid Place, gaze stabilization, keyboard Select, persistent selection while gazing at pad, keyboard Place, keyboard Reset, reselect, keyboard Release, invalid Select and W camera movement.
- The same ten checks passed again with recognition explicitly stopped (IVRAR VOICE-STOPPED PASS), verifying keyboard fallback independently. Recognition was then restarted successfully at Low confidence and native status Running. Original background input settings were restored by the harness.
- Recognition status panel visually inspected in Evidence/VoiceStatusPanel.png.
- Live microphone Select verified: native High-confidence select received at 2026-10-25T08:31:38.7093243Z, phrase start 08:31:37.2531183Z, duration .72 seconds; existing controller selected Blue Cube. No synthetic speech event was injected. Evidence/LiveVoiceEvents.txt contains the received event metadata.
- User reported Place was initially not heard; no native Place event was recorded during that attempt. The recognizer was stopped and restarted at Low minimum confidence. Subsequent real Select (08:35:34.2836613Z) and Place (08:35:41.1799957Z) both returned High confidence. The controller reported Placed Blue Cube on Left Pad; verified Blue position (-1.05, .35, 1.35), Left Pad occupied, interaction state Idle. Lowering the threshold was a test configuration, not proof of the cause of the earlier missed utterance.
- Recognizer StopListening and StartListening exercised successfully; native system returned Running.
- Black-screen reproduction: actual Unity Game view showed black with XR active and URP XR camera rendering allowed. Setting only allowXRRendering=false restored the Game view; scene was saved and a fresh Play Mode run visibly rendered correctly. No camera-control or gaze script changes were required.
- No runtime errors reported. Eight Meta/OpenXR startup warnings (audio output fallback and unavailable runtime extension functions) were observed. Unity AI also emits unrelated NoSubscription Info messages. XR settings were preserved.
- Latest Console query reported nine Meta/OpenXR warnings (two audio-output fallback messages and seven unsupported-extension messages), zero errors. A temporary Camera.stereoTargetEye diagnostic produced one URP warning in an earlier session; that built-in-renderer setting was not persisted. Only the URP camera allowXRRendering flag is saved for the desktop rendering correction.
- Final real microphone sequence verified all four commands: Select at 08:41:56.9142106Z (Medium), Place at 08:42:01.6737288Z (Medium), Reset at 08:42:04.7531511Z (Medium), Select at 08:42:11.1037777Z (High), and Release at 08:42:13.4775051Z (Medium). All were native recognizer callbacks, dispatched without synthetic speech injection. User confirmed Place moved the cube and both Release and Reset worked. Final runtime snapshot confirms all cube positions restored, no selected objects, both pads unoccupied, state Idle, feedback Selection released. These observations supersede the earlier pending Release/Reset status.

## Final test limits and handoff

Real spoken Select, Place, Release and Reset are verified, including actual cube movement, selection clearing, reset positions and pad occupancy. Duplicate/confidence rejection and native error recovery are implemented but were not fault-injected or stress-tested. Network-disconnected operation and a standalone executable remain untested.

Scene settings saved: confidence Low, startOnEnable true, desktop camera URP XR rendering false. The Editor is left in Play Mode with Voice Listening / native Running so manual testing can continue. Stop Play Mode to stop/dispose recognition. Spoken-event evidence is in LiveVoiceEvents.txt and FinalLiveVoiceEvents.txt; actual Game-view evidence is VoiceGameView.png. Initial attempts with Medium confidence recognized Select but did not produce a Place event; a later Low-threshold session recognized both words at High confidence. This is a successful live test, not a guarantee that every utterance will be recognized.

## Microphone permissions / prerequisites

In Windows Settings > Privacy & security > Microphone, enable Microphone access and Let desktop apps access your microphone. Ensure the chosen default Windows recording device works and is unmuted. The recognizer uses Windows' configured speech language; install a compatible English speech language component if needed. Keyword recognition uses a fixed local grammar with no cloud SDK, credentials or network calls in this integration. Offline operation with the network disconnected has not been tested; installing Windows speech language components may initially require a download.

This is Windows desktop integration, not headset/Android voice input. A standalone executable has not been built or tested. MainVRScene is first in the build scene list; SampleScene remains available.

Sources: https://docs.unity3d.com/6000.5/Documentation/ScriptReference/Windows.Speech.KeywordRecognizer.html ; https://learn.microsoft.com/en-us/windows/apps/develop/input/speech-recognition ; https://support.microsoft.com/en-us/windows/privacy/turn-on-app-permissions-for-your-microphone-in-windows
