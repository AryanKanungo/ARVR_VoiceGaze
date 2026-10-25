using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(20)]
public sealed class SpatialVoiceIntentController : MonoBehaviour
{
    public enum InteractionState { Idle, Selected }
    public GazeTargetResolver resolver;
    public UnityEngine.UI.Text statusText;
    public InteractableObject[] targets;
    public InteractionState State { get; private set; }
    public InteractableObject SelectedObject { get; private set; }
    public string LastMessage { get; private set; } = "Aim at a cube. 1 Select | 2 Place | 3 Release | 4 Reset";
    public InteractionCommandResult LastResult { get; private set; }
    public bool HasCommandResult { get; private set; }
    public event Action<InteractionCommandResult> CommandCompleted;
    InteractableObject lastGaze;
    string voiceStatus, lastHeard, performanceStatus = "Performance: warming up", loggingStatus = "CSV: starting";
    string lastAcceptedCommand;
    int lastAcceptedFrame = -1;

    public void SetVoiceFeedback(string status, string heard)
    {
        if (voiceStatus == status && lastHeard == heard) return;
        voiceStatus = status; lastHeard = heard; RefreshStatus();
    }
    public void SetPerformanceFeedback(string performance, string logging)
    {
        performanceStatus = performance; loggingStatus = logging; RefreshStatus();
    }
    void Start() { RefreshStatus(); }
    void LateUpdate()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "select"));
            else if (keyboard.digit2Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "place"));
            else if (keyboard.digit3Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "release"));
            else if (keyboard.digit4Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "reset"));
        }
        if (lastGaze != (resolver ? resolver.CurrentStableTarget : null)) RefreshStatus();
    }
    // Existing API remains available. Direct API calls are honestly labeled system, not keyboard.
    public void Select() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "select")); }
    public void Place() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "place")); }
    public void Release() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "release")); }
    public void ResetObjects() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "reset")); }

    public InteractionCommandResult ExecuteCommand(InteractionCommandContext context)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var gaze = resolver ? resolver.CurrentStableTarget : null;
        string target = context.Command == "select" ? ObjectName(gaze) : ObjectName(SelectedObject);
        string destination = gaze && gaze.kind == InteractableObject.TargetKind.Destination ? gaze.name : "";
        bool accepted = false;
        string reason = "", message;
        if (!isActiveAndEnabled) { reason = "controller_unavailable"; message = "Controller is disabled."; }
        else if (lastAcceptedFrame == Time.frameCount && lastAcceptedCommand == context.Command)
        { reason = "duplicate_same_frame"; message = "Duplicate command ignored."; }
        else
        {
            switch (context.Command)
            {
                case "select":
                    if (!gaze || gaze.kind != InteractableObject.TargetKind.Object)
                    { reason = "no_stable_object"; message = "Select rejected: aim at a cube."; }
                    else if (State == InteractionState.Selected)
                    { reason = "already_selected"; message = "Release your selection before selecting another cube."; }
                    else
                    {
                        SelectedObject = gaze; gaze.SetSelected(true); State = InteractionState.Selected;
                        accepted = true; message = "Selected " + gaze.name + ". Aim at a pad; say Place or press 2.";
                    }
                    break;
                case "place":
                    if (State != InteractionState.Selected || !SelectedObject)
                    { reason = "no_selection"; message = "Place rejected: select a cube first."; }
                    else if (!gaze || gaze.kind != InteractableObject.TargetKind.Destination || !gaze.placementAnchor)
                    { reason = "no_stable_destination"; message = "Place rejected: aim at a destination pad."; }
                    else if (gaze.IsOccupied)
                    { reason = "destination_occupied"; message = "Place rejected: pad is occupied."; }
                    else
                    {
                        SelectedObject.transform.SetPositionAndRotation(gaze.placementAnchor.position, gaze.placementAnchor.rotation);
                        RebuildOccupancy();
                        message = "Placed " + SelectedObject.name + " on " + gaze.name + ".";
                        ClearSelection(); accepted = true;
                    }
                    break;
                case "release":
                    ClearSelection(); accepted = true; message = "Selection released."; break;
                case "reset":
                    // Reset must not depend on an active gaze/camera or resolver.
                    if (targets == null) { reason = "targets_unconfigured"; message = "Reset rejected: targets are not configured."; }
                    else
                    {
                        ClearSelection();
                        foreach (var item in targets) if (item) item.RestoreTransform();
                        accepted = true; message = "All cubes reset.";
                    }
                    break;
                default: reason = "unknown_command"; message = "Unknown command rejected."; break;
            }
        }
        if (accepted) { lastAcceptedCommand = context.Command; lastAcceptedFrame = Time.frameCount; }
        return Complete(context, started, accepted, reason, target, destination, message);
    }
    public InteractionCommandResult RecordRejectedCommand(InteractionCommandContext context, string reason)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var gaze = resolver ? resolver.CurrentStableTarget : null;
        return Complete(context, started, false, reason,
            context.Command == "select" ? ObjectName(gaze) : ObjectName(SelectedObject),
            gaze && gaze.kind == InteractableObject.TargetKind.Destination ? gaze.name : "",
            context.Command + " rejected: " + reason + ".");
    }
    InteractionCommandResult Complete(InteractionCommandContext context, long started, bool accepted,
        string reason, string target, string destination, string message)
    {
        // End the timing boundary before UI formatting, event logging, or disk work.
        long completed = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = new InteractionCommandResult(context, accepted, reason, target, destination,
            DateTime.UtcNow, completed, started, Time.unscaledDeltaTime * 1000f);
        LastResult = result; HasCommandResult = true; LastMessage = message;
        CommandCompleted?.Invoke(result);
        RefreshStatus();
        return result;
    }
    void RebuildOccupancy()
    {
        if (targets == null) return;
        foreach (var pad in targets)
        {
            if (!pad || pad.kind != InteractableObject.TargetKind.Destination) continue;
            pad.SetOccupant(null);
            if (!pad.placementAnchor) continue;
            foreach (var item in targets)
                if (item && item.kind == InteractableObject.TargetKind.Object &&
                    Vector3.SqrMagnitude(item.transform.position - pad.placementAnchor.position) < .001f)
                    pad.SetOccupant(item);
        }
    }
    static string ObjectName(InteractableObject item) { return item ? item.name : ""; }
    void ClearSelection()
    {
        if (SelectedObject) SelectedObject.SetSelected(false);
        SelectedObject = null; State = InteractionState.Idle;
    }
    void RefreshStatus()
    {
        lastGaze = resolver ? resolver.CurrentStableTarget : null;
        if (!statusText) return;
        string latency = !HasCommandResult ? "Processing: — | Voice event-to-action: —" :
            "Processing: " + LastResult.ProcessingMs.ToString("F3") + " ms | Voice event-to-action: " +
            (LastResult.RecognitionEventToActionMs.HasValue ? LastResult.RecognitionEventToActionMs.Value.ToString("F2") + " ms" : "n/a");
        statusText.text = "VOICE + GAZE / GROUP 05\n" + (voiceStatus ?? "Voice: starting") +
            "\n" + (lastHeard ?? "Heard: None") +
            "\nGaze: " + (lastGaze ? lastGaze.name : "—") + " | Selected: " + (SelectedObject ? SelectedObject.name : "—") +
            "\nResult: " + LastMessage + "\n" + latency + "\n" + performanceStatus + "\n" + loggingStatus +
            "\n1 Select  2 Place  3 Release  4 Reset\nRMB + mouse: look | WASD: move | Q/E: down/up";
    }
    void OnDisable() { ClearSelection(); }
}
