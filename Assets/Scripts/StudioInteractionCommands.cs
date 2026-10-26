using System.Collections.Generic;
using UnityEngine;

// Bounded object-edit support. The existing controller still owns commands and telemetry.
[DisallowMultipleComponent]
public sealed class StudioInteractionCommands : MonoBehaviour
{
    public Camera studioCamera;
    public QuestRigSupport questRig;
    public Light studioLight;
    public InteractableObject lightingControl;
    public InteractableObject materialControl;
    [Range(1, 10)] public int duplicateLimit = 5;
    readonly List<InteractableObject> duplicates = new List<InteractableObject>(5);
    static readonly Color[] Palette = { new Color(.83f,.8f,.73f), new Color(.32f,.38f,.25f),
        new Color(.57f,.28f,.19f), new Color(.22f,.35f,.48f), new Color(.72f,.59f,.26f) };
    Vector3 homePosition; Quaternion homeRotation;
    float originalLightIntensity; Color originalAmbient;
    public bool LightsOn { get; private set; } = true;
    public int DuplicateCount => duplicates.Count;
    public string UndoTargetName => undoObject ? undoObject.name : "";
    enum UndoKind { None, Object, Created, Deleted }
    UndoKind undoKind;
    InteractableObject undoObject;
    Vector3 undoPosition, undoScale;
    Quaternion undoRotation;
    Color undoColor;
    int duplicateSerial;

    void Awake()
    {
        if (studioCamera) { homePosition = studioCamera.transform.position; homeRotation = studioCamera.transform.rotation; }
        if (studioLight) originalLightIntensity = studioLight.intensity;
        originalAmbient = RenderSettings.ambientLight;
    }
    void Start() { UpdateLightingIndicator(); }
    void ClearUndo()
    {
        // Deleted duplicates remain inactive for one undo only; never accumulate hidden copies.
        if (undoKind == UndoKind.Deleted && undoObject && !undoObject.gameObject.activeSelf)
        { duplicates.Remove(undoObject); Destroy(undoObject.gameObject); }
        undoKind = UndoKind.None; undoObject = null;
    }
    public void CaptureEdit(InteractableObject item)
    {
        ClearUndo(); undoKind = UndoKind.Object; undoObject = item;
        undoPosition = item.transform.position; undoRotation = item.transform.rotation;
        undoScale = item.transform.localScale; undoColor = item.BaseColor;
    }
    public bool Edit(string command, InteractableObject selected, out string reason, out string message)
    {
        reason = ""; message = "";
        if (!selected || selected.kind != InteractableObject.TargetKind.Object)
        { reason = "no_selection"; message = "Select an object first."; return false; }
        if (command == "delete" && !selected.IsUserDuplicate)
        { reason = "original_protected"; message = "Original studio objects cannot be deleted."; return false; }
        Vector3 nextScale = selected.transform.localScale;
        if (command == "make bigger" || command == "make smaller")
        {
            float ratio = nextScale.x / selected.OriginalScale.x;
            float bounded = Mathf.Clamp(ratio * (command == "make bigger" ? 1.25f : .75f), .5f, 2f);
            if (Mathf.Approximately(ratio, bounded))
            { reason = "scale_limit"; message = "Scale limit reached (50–200% of original)."; return false; }
            nextScale *= bounded / ratio;
        }
        if (command == "duplicate")
        {
            int live = 0; foreach (var item in duplicates) if (item && item.gameObject.activeSelf) live++;
            if (live >= duplicateLimit) { reason = "duplicate_limit"; message = "Duplicate limit reached."; return false; }
            if (!FindCopyPosition(selected, out Vector3 position))
            { reason = "no_copy_space"; message = "Move the object to a clear area before duplicating."; return false; }
            ClearUndo();
            var copy = Instantiate(selected, selected.transform.parent);
            copy.name = "Studio Copy " + (++duplicateSerial);
            copy.transform.position = position;
            copy.InitializeDuplicate(selected); duplicates.Add(copy);
            undoObject = copy; undoKind = UndoKind.Created;
            message = "Created " + copy.name + ". Gaze and Select to edit it."; return true;
        }
        CaptureEdit(selected);
        switch (command)
        {
            case "flip": selected.transform.Rotate(180, 0, 0, Space.Self); break;
            case "move forward": selected.transform.position += Vector3.forward * .25f; break;
            case "move backward": selected.transform.position += Vector3.back * .25f; break;
            case "change color":
                int current = -1;
                for (int i = 0; i < Palette.Length; i++) if ((Vector4)(selected.BaseColor - Palette[i]) == Vector4.zero) { current = i; break; }
                selected.SetBaseColor(Palette[(current + 1) % Palette.Length]); break;
            case "make bigger": case "make smaller": selected.transform.localScale = nextScale; break;
            case "delete": undoKind = UndoKind.Deleted; selected.gameObject.SetActive(false); break;
            default: ClearUndo(); reason = "unsupported_edit"; message = "Unsupported edit."; return false;
        }
        message = command + ": " + selected.name + "."; return true;
    }
    static bool FindCopyPosition(InteractableObject source, out Vector3 position)
    {
        Physics.SyncTransforms();
        var bounds = source.SurfaceBounds;
        float step = Mathf.Max(bounds.size.x, bounds.size.z) + .15f;
        for (int ring = 1; ring <= 3; ring++)
            for (int direction = 0; direction < 8; direction++)
            {
                float angle = direction * Mathf.PI / 4;
                var offset = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * (step * ring);
                if (!Physics.CheckBox(bounds.center + offset, bounds.extents * .95f,
                    Quaternion.identity, 1 << source.gameObject.layer, QueryTriggerInteraction.Ignore))
                { position = source.transform.position + offset; return true; }
            }
        position = source.transform.position; return false;
    }
    public bool UndoEdit(out string reason, out string message)
    {
        reason = "";
        if (undoKind == UndoKind.None || !undoObject)
        { message = "No supported edit to undo."; reason = "nothing_to_undo"; return false; }
        if (undoKind == UndoKind.Created)
        { undoObject.gameObject.SetActive(false); duplicates.Remove(undoObject); Destroy(undoObject.gameObject); }
        else if (undoKind == UndoKind.Deleted) undoObject.gameObject.SetActive(true);
        else
        {
            undoObject.transform.SetPositionAndRotation(undoPosition, undoRotation);
            undoObject.transform.localScale = undoScale; undoObject.SetBaseColor(undoColor);
        }
        undoKind = UndoKind.None; undoObject = null;
        message = "Undid the last supported object edit."; return true;
    }
    public bool Activate(InteractableObject gaze, InteractableObject selected, out string reason, out string message)
    {
        reason = "";
        if (!gaze || gaze.kind != InteractableObject.TargetKind.Control)
        { reason = "no_stable_control"; message = "Gaze at a labeled studio control."; return false; }
        if (gaze.controlAction == InteractableObject.StudioAction.ToggleLighting)
            return SetLighting(!LightsOn, out reason, out message);
        if (gaze.controlAction == InteractableObject.StudioAction.CycleMaterial)
            return Edit("change color", selected, out reason, out message);
        reason = "control_unconfigured"; message = "Control unavailable."; return false;
    }
    public bool SetLighting(bool enabled, out string reason, out string message)
    {
        reason = "";
        if (!studioLight) { reason = "lighting_unconfigured"; message = "Lighting unavailable."; return false; }
        LightsOn = enabled;
        studioLight.intensity = enabled ? originalLightIntensity : .15f;
        RenderSettings.ambientLight = enabled ? originalAmbient : originalAmbient * .65f;
        UpdateLightingIndicator();
        message = enabled ? "Studio lights on." : "Studio lights off / daylight safety mode."; return true;
    }
    void UpdateLightingIndicator()
    { if (lightingControl) lightingControl.SetBaseColor(LightsOn ? Palette[1] : Palette[2]); }
    public bool GoHome(out string reason, out string message)
    {
        reason = "";
        if (questRig) { questRig.GoHome(); message = "Returned XR origin home."; return true; }
        if (!studioCamera) { reason = "camera_unconfigured"; message = "Camera unavailable."; return false; }
        studioCamera.transform.SetPositionAndRotation(homePosition, homeRotation);
        var movement = studioCamera.GetComponent<DesktopGazeCamera>();
        if (movement) movement.SynchronizeLook();
        message = "Returned to the studio entrance."; return true;
    }
    public void ResetStudio()
    {
        undoKind = UndoKind.None; undoObject = null;
        foreach (var item in duplicates) if (item) { item.gameObject.SetActive(false); Destroy(item.gameObject); }
        duplicates.Clear();
        SetLighting(true, out _, out _);
    }
    public void AppendDuplicates(List<InteractableObject> list)
    { foreach (var item in duplicates) if (item && item.gameObject.activeSelf) list.Add(item); }
}
