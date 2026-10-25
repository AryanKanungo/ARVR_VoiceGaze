# Windows player microphone regression checklist

Run Builds/Windows/IVRAR_VoiceGaze.exe normally (no verification flags). Keep the entire Windows build folder together. The default window is 1280×720 and the normal desktop frame-rate target is 60 FPS.

1. Confirm Voice: Listening and a detected default microphone. If unavailable, enable Windows Settings > Privacy & security > Microphone > Microphone access and Let desktop apps access your microphone. Select an unmuted default Windows input device and ensure compatible English speech language data is installed. No cloud speech service was added.
2. Hold RMB and move the mouse to look; use WASD and Q/E to move. Hold gaze for at least 120 ms. Confirm the named cube highlights.
3. Say **select** while gazing at a cube. Confirm Selected names that cube and its selected feedback persists while gaze moves away.
4. Aim at Left or Right Pad until Gaze names it, then say **place**. Confirm the selected cube moves onto that pad and selection clears.
5. Aim at another cube, say **select**, then **release**. Confirm selection clears without moving the cube.
6. Say **reset**. Confirm all cubes return to their original table positions and both pads become available.
7. Say **place** without selection, and **select** while looking at empty space. Confirm safe rejection and readable reasons.
8. Repeat using keys 1/2/3/4. Confirm the dashboard shows the result and command processing latency.
9. Check the newest CSV under %USERPROFILE%/AppData/LocalLow/DefaultCompany/IVRAR_VoiceGaze/Telemetry. Confirm real microphone rows say input_source=voice, synthetic_input=false, have confidence/timestamps, and correctly distinguish accepted/rejected commands. Rejected commands have no action latency. Keyboard rows leave recognition confidence/latency blank.

These are manual physical-microphone tests. Automated virtual-keyboard/dispatcher tests do not establish spoken-command success in the standalone player. Callback-to-action latency is not acoustic speech latency. Close with Alt+F4 when finished; this stops/disposes recognition and flushes telemetry.
