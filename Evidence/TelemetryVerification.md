# VoiceGaze telemetry and reliability verification

## Files changed
- Assets/Scripts/InteractionTelemetry.cs (new): shared command context/result records, event subscription, background CSV writer, numeric frame sampling and 1 Hz performance dashboard.
- Assets/Scripts/SpatialVoiceIntentController.cs: existing state machine routed through one result-producing entry point; keyboard and public Select/Place/Release/ResetObjects APIs retained. Invalid command reasons and target/destination snapshots are recorded before selection clears. Same-frame duplicate actions are rejected. Reset no longer depends on the gaze resolver.
- Assets/Scripts/VoiceCommandRecognizer.cs: native callbacks retain the existing KeywordRecognizer engine, confidence filter, 0.75 s cooldown, 2 s expiry and lifecycle/session guard. Callback receipt captures UTC and monotonic ticks before preparing the event. Dispatch records accepted/rejected controller results; suppressed callbacks emit rejected results. Callback overflow is counted.
- Assets/Scenes/MainVRScene.unity: telemetry attached to Interaction Systems with cached references; high-contrast world panel enlarged and text fitted to show gaze, selected object, voice status, last phrase/result, command processing/event-to-action latency, mean frame time/FPS and writer health.

GazeTargetResolver.cs, InteractableObject.cs and DesktopGazeCamera.cs are byte-identical to the pre-change snapshots in Evidence/PreTelemetry. Gaze stabilization is 0.12 s. Materials, colliders, scene objects, camera controls, Windows speech engine and installed packages are preserved. UI panel layout changed intentionally.

## CSV semantics and measurement boundaries
CSV files are created under Application.persistentDataPath/Telemetry with a UTC timestamp and unique session GUID in each filename. A new telemetry enable starts a new session/file. The scene defaults to session_label=development, trial_id=unassigned and automated_test_session=false; no implicit research trials or participant data are generated. Set the trial ID explicitly via BeginTrial or the Inspector for an actual protocol. Reset restores the scene; it does not invent or advance a trial.

Schema v1 has 35 columns, with session_start, command, measurement_reset, performance, trial_begin and session_end record types. event_id is monotonically increasing within a session and includes metadata rows. Input sources are voice, keyboard and system (direct public API calls). The CSV contains UTF-8 text, invariant-culture numbers, quoted/escaped fields and ISO-8601 UTC timestamps. Confidence is an ordinal native label, not a numerical probability.

- input_received_utc is callback receipt for voice and keyboard polling time for keyboard. Hardware key-down timestamp is not measured.
- result_completed_utc is captured immediately after the scene/state operation, before dashboard formatting, CSV formatting or event-subscriber work. For metadata rows it is the metadata snapshot timestamp.
- input_event_to_result_ms uses Stopwatch monotonic ticks from receipt to completion and is present for accepted and rejected command results.
- recognition_event_to_action_ms contains the same interval only for accepted voice commands. It is blank for keyboard/system input and for rejected commands because no action occurred.
- command_processing_ms starts at controller entry and ends when the operation/rejection decision completes. It excludes queue waiting and logger/UI work.
- Action completion means logical Unity scene/state mutation. Rendering, presentation, microphone audio onset and input-to-photon latency are not measured.
- phrase_started_utc and phrase_duration_ms preserve native recognition metadata. No acoustic onset-to-recognition/action latency is claimed; the phrase timestamp is not used as a proxy for that metric.
- target_object and destination_object are context snapshots taken before the command clears selection: Select captures its gaze candidate, other commands capture the selected object, and destination_object captures a stable gazed pad if present. A pad in a Reset/Release row is gaze context, not a claim that those actions place anything. Interpret an actual placement destination only for accepted Place rows.
- UTC values are wall-clock audit timestamps, not the latency clock. Their textual precision does not imply matching clock accuracy/resolution. Compute reported intervals from the monotonic latency columns, not UTC subtraction.
- frame_time_ms is the command frame's Time.unscaledDeltaTime interval, not GPU time or command duration.
- avg_frame_time_ms = 1000 * sum(included unscaled frame intervals) / sampled_frames.
- avg_fps = sampled_frames / sum(included unscaled frame intervals). It is not the arithmetic mean of instantaneous FPS.
- Each measurement ID starts after a configurable two-second warm-up. The first interval crossing the warm-up boundary is excluded. Genuine later stalls are retained. There is no outlier trimming. measurement_reset records identify new numeric windows; sampled_duration_s and max_frame_time_ms provide context.
- application_focused is the state at record creation, not a claim about the entire interval. Metadata and performance rows leave command-only fields blank.
- dropped_rows reports CSV queue/write failures; voice_callback_events_dropped reports the speech component's cumulative callback-overflow count. Nonzero loss invalidates completeness assumptions.

Commands raise CommandCompleted. The logger formats/enqueues immutable snapshots in response. Its Update only accumulates numeric frame statistics; formatting/UI refresh happens at 1 Hz and performance rows normally at 5 s. Directory creation, file creation, writes and flushes run on a dedicated background thread. Disable/quit requests a final row and a bounded flush/join. Queue capacity is 4096; errors/loss are visible on the dashboard. Abrupt process termination/power loss may still lose pending records; this is not a transactional research data store.

## Automated regression and actual measurements
Evidence/TelemetrySmokeTest.cs is the reproducible Unity MCP RunCommand source, stored outside Assets so it is not part of the player. All 18 checks passed:
1. 120 ms stabilization setting unchanged.
2. Invalid keyboard Place rejected with the correct source/reason.
3. Keyboard Select accepted with target recorded.
4. Locked selection persists while gaze moves.
5. Place moves the object and records both object and pad before clearing selection.
6. Occupied-pad rejection retains selection.
7. Invalid destination rejection is safe.
8. Keyboard Release.
9. Keyboard Reset restores position and occupancy.
10. Invalid Select rejection.
11. Same-frame duplicate suppression (explicitly synthetic voice contexts).
12. Reset with the gaze resolver temporarily absent.
13. Rejected/filter-result event logging (explicitly synthetic).
14. Rejected results have blank action latency.
15. Keyboard camera movement.
16. Frame sampling.
17. FPS/mean-frame formula consistency.
18. Background CSV flush, with zero dropped rows and no writer error.

The test stopped real speech recognition while injecting a virtual keyboard, temporarily enabled background input for automation, and restored those settings afterward. It created a separate automated_test_session=true session with a quoted/comma-containing label and trial_id=AUTOMATED-001. Its three voice-source records have synthetic_input=true. They are software-path tests, not microphone events or human data.

Automated CSV: C:/Users/aryan/AppData/LocalLow/DefaultCompany/IVRAR_VoiceGaze/Telemetry/interaction_20261025_091450_9021819b1c0748d8ba23b2bdc13b5ef1.csv
Copy: Evidence/TelemetryAutomated.csv
Independent validator: Evidence/VerifyTelemetryCsv.py
Validation summary: Evidence/TelemetryCsvValidation.json
Validated 35 columns, 22 records, 15 command rows (10 keyboard, 3 synthetic voice, 2 system), 9 accepted commands and 6 rejected commands with reasons. Quoting, unique IDs, UTC timestamps, numeric fields, blank inapplicable latency/confidence fields and performance formulas passed. Zero CSV drops/callback overflows. The Python 3.10 validator normalizes timestamp fractional digits for parsing only; original seven-digit CSV timestamps are preserved.

Measured window: 2026-10-25T09:14:54.4621009Z through 09:15:06.4880203Z; two-second warm-up, measurement ID 2.
- Included sample count: 232 frames.
- Included interval duration: 9.9851832129 s.
- Mean frame interval: 43.0395828143 ms.
- Mean FPS: 23.2344259543.
- Maximum included frame interval: 60.2793991566 ms.
- Keyboard controller processing, including accepted/rejected commands: N=10, mean 1.65458 ms, maximum 12.9717 ms. This small cold/active automated command set is not a representative user-performance estimate.
Context: Unity 6000.5.7f1 Windows Editor, Vulkan, vSyncCount=0, targetFrameRate=-1, focused at the final snapshot, existing Meta/OpenXR simulator environment active. This is a short Editor observation, not a standalone/headset benchmark, an instrumentation-overhead comparison, or human-trial results. The 23 FPS observation is below typical VR refresh targets; CPU/GPU causes have not been profiled. Evidence/TelemetryPerformance.json contains the exact measured values.

The initial Editor closed during stop/recompile and its old named pipe disappeared. It was reopened without changing packages. The reopened Editor selected Vulkan rather than the earlier D3D11, so no before/after performance comparison is claimed. Initial startup-stall CSVs were not used for the reported steady measurement. The existing simulator's update reminder was dismissed without installing an update.

## Allocation/material review
The new numeric sampler Update contains no managed allocation, I/O, component lookup or scene search by source inspection. Controller/recognizer component references are cached/assigned at enable or scene setup. Gaze retains its original single GetComponentInParent on a ray hit; its algorithm was not changed for speculative optimization. InteractableObject caches its Renderer/MaterialPropertyBlock, reads sharedMaterial in Awake and never accesses Renderer.material. No new materials/material instances are introduced. CSV strings and UI strings allocate on events/low-frequency refresh; native speech queue/history allocations occur on events. Whole-scene/SDK GC allocation and CPU/GPU cost have not been measured with a profiler, and zero allocation for the entire frame is not claimed.

## Live-session status and limits
The logger started a separate development_live session with trial_id=unassigned and automated_test_session=false, then restarted the real recognizer successfully (Listening / native Running). The user performed a real microphone test. Three native voice commands were accepted and independently validated in CSV: Select (Low confidence, Blue Cube, 09:21:08.8887499Z); Place (Medium confidence, Blue Cube to Left Pad, 09:21:15.3613806Z); Reset (Low confidence, 09:21:17.6559314Z). Each has synthetic_input=false and native phrase metadata. Reset restored the scene. Spoken Release has not yet appeared in this session; Release is covered by the keyboard and explicitly synthetic dispatcher tests. Previous-phase speech tests are not substituted for current telemetry results.

For these three real development commands only, callback-receipt-to-action intervals were 6.4499, 1.0479 and 1.6314 ms (mean 3.0430667 ms, range 1.0479–6.4499 ms). These are descriptive software-path observations from a tiny development check, not acoustic speech latency, a recognition accuracy estimate, human-trial results or a general performance claim. Evidence/TelemetryLive.csv and TelemetryLiveCsvValidation.json contain the checked live snapshot. The active CSV continues growing after that snapshot.

Current live CSV: C:/Users/aryan/AppData/LocalLow/DefaultCompany/IVRAR_VoiceGaze/Telemetry/interaction_20261025_091507_0e6375257fc8446a8d7f3f026eb7402e.csv
The CSV path changes on each new session. The scene remains in Play Mode with the microphone listening so manual testing can continue.

Console check: zero errors, eight existing Meta/OpenXR warnings (audio-output fallback and unsupported extension functions). Actual Game view was inspected on desktop; Evidence/TelemetryDashboard.png is an independently rendered camera preview. The Vulkan/XR ScreenCapture mirror returned a black image despite the actual desktop Game view rendering correctly, so it was not used as visual evidence.

No formal participants, acoustic latency trials, standalone executable tests, hardware VR tests, network-disconnected tests, native error injection, or speech-backlog saturation tests were performed. Native confidence/cooldown/expiry guards remain implemented; the automated filter-result check tests downstream rejected-result logging rather than falsely claiming to test the native recognizer's acoustic confidence.

