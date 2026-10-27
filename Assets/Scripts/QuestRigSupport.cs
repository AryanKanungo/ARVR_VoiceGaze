using UnityEngine;
using UnityEngine.XR;

// Quest-only rig support: applies the tracked pose and moves the origin, never the physical head.
[DefaultExecutionOrder(-100)]
public sealed class QuestRigSupport : MonoBehaviour
{
    public Camera headCamera;
    public Transform rigOrigin;
    public CommandHandbook handbook;
    public SpatialVoiceIntentController controller;
    Vector3 homePosition;
    Quaternion homeRotation;
    bool menuHeld, primaryHeld, secondaryHeld, aHeld, bHeld;
    void Awake() { homePosition = rigOrigin.position; homeRotation = rigOrigin.rotation; }
    void Update() { ApplyPose(); }
    void OnEnable() { Application.onBeforeRender += ApplyPose; }
    void OnDisable() { Application.onBeforeRender -= ApplyPose; }
    void ApplyPose()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p)) headCamera.transform.localPosition = p;
        if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion q)) headCamera.transform.localRotation = q;
    }
    void LateUpdate()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        bool menu = device.TryGetFeatureValue(CommonUsages.menuButton, out bool v) && v;
        bool primary = device.TryGetFeatureValue(CommonUsages.primaryButton, out v) && v;
        bool secondary = device.TryGetFeatureValue(CommonUsages.secondaryButton, out v) && v;
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool a = right.TryGetFeatureValue(CommonUsages.primaryButton, out v) && v;
        bool b = right.TryGetFeatureValue(CommonUsages.secondaryButton, out v) && v;
        if (controller && menu && !menuHeld) controller.ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System,
            handbook.IsOpen ? "close handbook" : "open handbook"));
        if (controller && handbook.IsOpen && primary && !primaryHeld)
            controller.ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "next page"));
        if (controller && handbook.IsOpen && secondary && !secondaryHeld) Command("previous page");
        if (controller && !handbook.IsOpen)
        {
            if (a && !aHeld) Command("select");
            else if (b && !bHeld) Command("place");
            else if (primary && !primaryHeld) Command("release");
            else if (secondary && !secondaryHeld) Command("reset");
        }
        menuHeld = menu; primaryHeld = primary; secondaryHeld = secondary; aHeld = a; bHeld = b;
    }
    void Command(string value) { controller.ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, value)); }
    public void GoHome()
    {
        // Preserve current tracked head height, pitch and roll; recenter horizontal pose only.
        rigOrigin.rotation = homeRotation;
        float turn = Mathf.DeltaAngle(headCamera.transform.eulerAngles.y, homeRotation.eulerAngles.y);
        rigOrigin.RotateAround(headCamera.transform.position, Vector3.up, turn);
        Vector3 delta = homePosition - headCamera.transform.position; delta.y = homePosition.y - rigOrigin.position.y;
        rigOrigin.position += delta;
    }
}
