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
    bool menuHeld, primaryHeld;
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
        if (controller && menu && !menuHeld) controller.ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System,
            handbook.IsOpen ? "close handbook" : "open handbook"));
        if (controller && handbook.IsOpen && primary && !primaryHeld)
            controller.ExecuteCommand(InteractionCommandContext.Capture(InteractionInputSource.System, "next page"));
        menuHeld = menu; primaryHeld = primary;
    }
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
