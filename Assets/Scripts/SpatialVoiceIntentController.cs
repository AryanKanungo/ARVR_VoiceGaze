using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(20)]
public sealed class SpatialVoiceIntentController : MonoBehaviour
{
    public enum InteractionState { Idle, Selected }
    public GazeTargetResolver resolver;
    public CommandHandbook handbook;
    public StudioInteractionCommands studio;
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
    bool statusDirty = true;
    float nextStatusRefresh;
    readonly List<InteractableObject> occupancyObjects = new List<InteractableObject>(24);
    string taskStatus = "Recognized: 0 | Command success: N/A\nCompleted tasks: 0 | Failed tasks: 0 | Task failure: N/A\nAvg completed task: N/A";

    public void SetTaskFeedback(string value) { taskStatus = value; RefreshStatus(); }

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
            if (keyboard.hKey.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, handbook && handbook.IsOpen ? "close handbook" : "open handbook"));
            else if (handbook && handbook.IsOpen && keyboard.rightArrowKey.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "next page"));
            else if (handbook && handbook.IsOpen && keyboard.leftArrowKey.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "previous page"));
            else if (keyboard.digit1Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "select"));
            else if (keyboard.digit2Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "place"));
            else if (keyboard.digit3Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "release"));
            else if (keyboard.digit4Key.wasPressedThisFrame) ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.Keyboard, "reset"));
        }
        if (lastGaze != (resolver ? resolver.CurrentStableTarget : null)) RefreshStatus();
        if (statusDirty && Time.unscaledTime >= nextStatusRefresh)
        { nextStatusRefresh = Time.unscaledTime + .2f; statusDirty = false; RenderStatus(); }
    }
    // Existing API remains available. Direct API calls are honestly labeled system, not keyboard.
    public void Select() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "select")); }
    public void Place() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "place")); }
    public void Release() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "release")); }
    public void ResetObjects() { ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "reset")); }

    public InteractionCommandResult ExecuteCommand(InteractionCommandContext context)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        string selectedBefore = ObjectName(SelectedObject);
        var gaze = resolver ? resolver.CurrentStableTarget : null;
        string target = context.Command == "select" || context.Command == "activate" || context.Command == "what am i looking at" ? ObjectName(gaze) : ObjectName(SelectedObject);
        if (studio && context.Command == "undo") target = studio.UndoTargetName;
        else if (studio && (context.Command == "turn lights on" || context.Command == "turn lights off")) target = ObjectName(studio.lightingControl);
        else if (studio && context.Command == "go home") target = studio.studioCamera ? studio.studioCamera.name : "";
        string destination = gaze && gaze.kind == InteractableObject.TargetKind.Destination ? gaze.name : "";
        bool accepted = false;
        string reason = "", message;
        if (!isActiveAndEnabled) { reason = "controller_unavailable"; message = "Controller is disabled."; }
        else if (handbook && handbook.IsOpen && context.Command != "help" && context.Command != "what am i looking at" && context.Command != "open handbook" && context.Command != "close handbook" && context.Command != "next page" && context.Command != "previous page")
        { reason = "handbook_open"; message = "Close the handbook to manipulate objects."; }
        else if (lastAcceptedFrame == Time.frameCount && lastAcceptedCommand == context.Command)
        { reason = "duplicate_same_frame"; message = "Duplicate command ignored."; }
        else
        {
            switch (context.Command)
            {
                case "open handbook":
                case "help":
                case "close handbook":
                case "next page":
                case "previous page":
                    if (!handbook) { reason = "handbook_unconfigured"; message = "Handbook unavailable."; }
                    else { accepted = handbook.Navigate(context.Command == "help" ? "open handbook" : context.Command, out message); if (!accepted) reason = "handbook_unavailable_or_closed"; }
                    break;
                case "what am i looking at":
                    accepted = gaze != null;
                    reason = accepted ? "" : "no_stable_target";
                    message = accepted ? "You are looking at " + gaze.name + "." : "No stable gaze target.";
                    break;
                case "activate":
                case "turn lights on":
                case "turn lights off":
                case "go home":
                case "undo":
                    if (!studio) { reason = "studio_unconfigured"; message = "Studio commands unavailable."; }
                    else
                    {
                        if (context.Command == "activate") accepted = studio.Activate(gaze, SelectedObject, out reason, out message);
                        else if (context.Command == "go home") accepted = studio.GoHome(out reason, out message);
                        else if (context.Command == "undo")
                        {
                            accepted = studio.UndoEdit(out reason, out message);
                            if (State == InteractionState.Selected && (!SelectedObject || !SelectedObject.gameObject.activeSelf)) ClearSelection();
                        }
                        else accepted = studio.SetLighting(context.Command == "turn lights on", out reason, out message);
                        if (accepted) RebuildOccupancy();
                    }
                    break;
                case "flip":
                case "move forward":
                case "move backward":
                case "change color":
                case "make bigger":
                case "make smaller":
                case "duplicate":
                case "delete":
                    if (!studio) { reason = "studio_unconfigured"; message = "Studio commands unavailable."; }
                    else
                    {
                        accepted = studio.Edit(context.Command, SelectedObject, out reason, out message);
                        if (accepted && context.Command == "delete") ClearSelection();
                        if (accepted) RebuildOccupancy();
                    }
                    break;
                case "select":
                    if (!gaze || gaze.kind != InteractableObject.TargetKind.Object)
                    { reason = "no_stable_object"; message = "Select rejected: aim at an object."; }
                    else if (State == InteractionState.Selected)
                    { reason = "already_selected"; message = "Release your selection before selecting another object."; }
                    else
                    {
                        SelectedObject = gaze; gaze.SetSelected(true); State = InteractionState.Selected;
                        accepted = true; message = "Selected " + gaze.name + ". Aim at a pad; say Place or press 2.";
                    }
                    break;
                case "place":
                    if (State != InteractionState.Selected || !SelectedObject)
                    { reason = "no_selection"; message = "Place rejected: select an object first."; }
                    else if (!gaze || gaze.kind != InteractableObject.TargetKind.Destination || !gaze.placementAnchor)
                    { reason = "no_stable_destination"; message = "Place rejected: aim at a destination pad."; }
                    else if (gaze.IsOccupied)
                    { reason = "destination_occupied"; message = "Place rejected: pad is occupied."; }
                    else
                    {
                        if (studio) studio.CaptureEdit(SelectedObject);
                        SelectedObject.transform.rotation = gaze.placementAnchor.rotation;
                        SelectedObject.transform.position = PlacementPosition(gaze, SelectedObject);
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
                        if (studio) studio.ResetStudio();
                        accepted = true; message = "All objects reset.";
                    }
                    break;
                case "rotate left":
                case "rotate right":
                case "rotate 45 degrees":
                case "rotate 90 degrees":
                case "move up":
                case "move down":
                case "move left":
                case "move right":
                    if (State != InteractionState.Selected || !SelectedObject)
                    { reason = "no_selection"; message = context.Command + " rejected: select an object first."; }
                    else
                    {
                        if (studio) studio.CaptureEdit(SelectedObject);
                        var selected = SelectedObject.transform;
                        switch (context.Command)
                        {
                            case "rotate left": selected.Rotate(0f, -45f, 0f, Space.Self); break;
                            case "rotate right":
                            case "rotate 45 degrees": selected.Rotate(0f, 45f, 0f, Space.Self); break;
                            case "rotate 90 degrees": selected.Rotate(0f, 90f, 0f, Space.Self); break;
                            case "move up": selected.position += Vector3.up * .25f; break;
                            case "move down": selected.position += Vector3.down * .25f; break;
                            case "move left": selected.position += Vector3.left * .25f; break;
                            case "move right": selected.position += Vector3.right * .25f; break;
                        }
                        RebuildOccupancy(); accepted = true;
                        message = context.Command + ": " + SelectedObject.name + ".";
                    }
                    break;
                default: reason = "unknown_command"; message = "Unknown command rejected."; break;
            }
        }
        if (accepted) { lastAcceptedCommand = context.Command; lastAcceptedFrame = Time.frameCount; }
        return Complete(context, started, accepted, reason, target, destination, message,
            accepted && context.Command == "select" ? target : selectedBefore);
    }
    public InteractionCommandResult RecordRejectedCommand(InteractionCommandContext context, string reason)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var gaze = resolver ? resolver.CurrentStableTarget : null;
        return Complete(context, started, false, reason,
            context.Command == "select" ? ObjectName(gaze) : ObjectName(SelectedObject),
            gaze && gaze.kind == InteractableObject.TargetKind.Destination ? gaze.name : "",
            context.Command + " rejected: " + reason + ".", ObjectName(SelectedObject));
    }
    InteractionCommandResult Complete(InteractionCommandContext context, long started, bool accepted,
        string reason, string target, string destination, string message, string selectedObject)
    {
        // End the timing boundary before UI formatting, event logging, or disk work.
        long completed = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = new InteractionCommandResult(context, accepted, reason, target, destination,
            DateTime.UtcNow, completed, started, Time.unscaledDeltaTime * 1000f, selectedObject);
        LastResult = result; HasCommandResult = true; LastMessage = message;
        CommandCompleted?.Invoke(result);
        RefreshStatus();
        return result;
    }
    void RebuildOccupancy()
    {
        if (targets == null) return;
        occupancyObjects.Clear();
        occupancyObjects.AddRange(targets);
        if (studio) studio.AppendDuplicates(occupancyObjects);
        foreach (var pad in targets)
        {
            if (!pad || pad.kind != InteractableObject.TargetKind.Destination) continue;
            pad.SetOccupant(null);
            if (!pad.placementAnchor) continue;
            foreach (var item in occupancyObjects)
                if (item && item.kind == InteractableObject.TargetKind.Object &&
                    item.gameObject.activeSelf && Vector3.SqrMagnitude(item.transform.position - PlacementPosition(pad, item)) < .001f)
                    pad.SetOccupant(item);
        }
    }
    static Vector3 PlacementPosition(InteractableObject pad, InteractableObject item)
    {
        // Original 0.4 m objects keep their existing anchor pose; resized forms rest on the pad.
        var bounds = item.SurfaceBounds;
        return pad.placementAnchor.position + Vector3.up * (bounds.extents.y + item.transform.position.y - bounds.center.y - .2f);
    }
    static string ObjectName(InteractableObject item) { return item ? item.name : ""; }
    void ClearSelection()
    {
        if (SelectedObject) SelectedObject.SetSelected(false);
        SelectedObject = null; State = InteractionState.Idle;
    }
    void RefreshStatus()
    { statusDirty = true; }
    void RenderStatus()
    {
        lastGaze = resolver ? resolver.CurrentStableTarget : null;
        if (!statusText) return;
        string latency = !HasCommandResult ? "Processing: — | Voice event-to-action: —" :
            "Processing: " + LastResult.ProcessingMs.ToString("F3") + " ms | Voice event-to-action: " +
            (LastResult.RecognitionEventToActionMs.HasValue ? LastResult.RecognitionEventToActionMs.Value.ToString("F2") + " ms" : "n/a");
        statusText.text = "VOICEVERSE / STUDIO LIVE\n" + (voiceStatus ?? "Voice: starting") +
            "\n" + (lastHeard ?? "Heard: None") +
            "\nGaze: " + (lastGaze ? lastGaze.name : "—") + " | Selected: " + (SelectedObject ? SelectedObject.name : "—") +
            "\nResult: " + LastMessage + "\n" + latency + "\n" + taskStatus + "\n" + performanceStatus + "\n" + loggingStatus +
            "\n1 Select  2 Place  3 Release  4 Reset  |  H Handbook\nRMB + mouse: look | WASD: move | Q/E: down/up";
    }
    void OnDisable() { ClearSelection(); }
}
