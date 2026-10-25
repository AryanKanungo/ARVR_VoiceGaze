using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(20)]
public sealed class SpatialVoiceIntentController : MonoBehaviour
{
    public enum InteractionState { Idle, Selected }
    public GazeTargetResolver resolver;
    public Text statusText;
    public InteractableObject[] targets;
    public InteractionState State { get; private set; }
    public InteractableObject SelectedObject { get; private set; }
    public string LastMessage { get; private set; } = "Aim at a cube. 1 Select | 2 Place | 3 Release | 4 Reset";
    InteractableObject lastGaze;
    string voiceStatus;
    string lastHeard;
    public void SetVoiceFeedback(string status, string heard)
    {
        voiceStatus = status;
        lastHeard = heard;
        RefreshStatus();
    }
    void Start() { RefreshStatus(); }
    // Resolve this frame's gaze before accepting commands.
    void LateUpdate()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) Select();
            else if (keyboard.digit2Key.wasPressedThisFrame) Place();
            else if (keyboard.digit3Key.wasPressedThisFrame) Release();
            else if (keyboard.digit4Key.wasPressedThisFrame) ResetObjects();
        }
        if (lastGaze != resolver.CurrentStableTarget) RefreshStatus();
    }
    public void Select()
    {
        var target = resolver.CurrentStableTarget;
        if (!target || target.kind != InteractableObject.TargetKind.Object) { Report("Select rejected: aim at a cube."); return; }
        if (State == InteractionState.Selected) { Report("Release your selection before selecting another cube."); return; }
        SelectedObject = target;
        target.SetSelected(true);
        State = InteractionState.Selected;
        Report("Selected " + target.name + ". Aim at a pad, then press 2.");
    }
    public void Place()
    {
        var pad = resolver.CurrentStableTarget;
        if (State != InteractionState.Selected || !SelectedObject) { Report("Place rejected: select a cube first."); return; }
        if (!pad || pad.kind != InteractableObject.TargetKind.Destination || !pad.placementAnchor) { Report("Place rejected: aim at a destination pad."); return; }
        if (pad.IsOccupied) { Report("Place rejected: pad is occupied."); return; }
        foreach (var target in targets)
            if (target.kind == InteractableObject.TargetKind.Destination) target.SetOccupant(null);
        // Reconstruct occupancy from object positions after moving, allowing independent pads.
        SelectedObject.transform.SetPositionAndRotation(pad.placementAnchor.position, pad.placementAnchor.rotation);
        foreach (var destination in targets)
            if (destination.kind == InteractableObject.TargetKind.Destination)
                foreach (var item in targets)
                    if (item.kind == InteractableObject.TargetKind.Object &&
                        Vector3.SqrMagnitude(item.transform.position - destination.placementAnchor.position) < .001f)
                        destination.SetOccupant(item);
        string placed = SelectedObject.name;
        ClearSelection();
        Report("Placed " + placed + " on " + pad.name + ".");
    }
    public void Release() { ClearSelection(); Report("Selection released."); }
    public void ResetObjects()
    {
        if (targets == null || resolver == null) return;
        ClearSelection();
        foreach (var target in targets) if (target) target.RestoreTransform();
        Report("All cubes reset.");
    }
    void ClearSelection()
    {
        if (SelectedObject) SelectedObject.SetSelected(false);
        SelectedObject = null;
        State = InteractionState.Idle;
    }
    void Report(string message) { LastMessage = message; RefreshStatus(); }
    void RefreshStatus()
    {
        lastGaze = resolver.CurrentStableTarget;
        if (statusText) statusText.text = "VOICE + GAZE / GROUP 05\n" + LastMessage +
            "\nGaze: " + (lastGaze ? lastGaze.name : "—") +
            "\n1 Select   2 Place   3 Release   4 Reset\nRMB + mouse: look   WASD: move   Q/E: down/up" +
            (string.IsNullOrEmpty(voiceStatus) ? "" : "\n" + voiceStatus + "\n" + lastHeard);
    }
    void OnDisable() { ClearSelection(); }
}
