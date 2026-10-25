using UnityEngine;
using UnityEngine.InputSystem;

// Laptop fallback only. Disable when a tracked XR camera rig is added.
[DefaultExecutionOrder(-10)]
public sealed class DesktopGazeCamera : MonoBehaviour
{
    public float moveSpeed = 1.5f;
    public float lookSensitivity = .12f;
    float yaw, pitch;
    void OnEnable() { yaw = transform.eulerAngles.y; pitch = transform.eulerAngles.x; }
    void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -75f, 75f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        if (keyboard == null) return;
        Vector3 move = new Vector3((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
            (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
            (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
        transform.position += (Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(move, 1)) * (moveSpeed * Time.unscaledDeltaTime);
    }
}
