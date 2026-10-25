using UnityEngine;

[DefaultExecutionOrder(10)]
public sealed class GazeTargetResolver : MonoBehaviour
{
    public Camera gazeCamera;
    public LayerMask targetLayers;
    [Min(.1f)] public float maxDistance = 8f;
    [Min(0f)] public float stabilizationSeconds = .12f;
    public InteractableObject CurrentStableTarget { get; private set; }
    InteractableObject candidate;
    float candidateSince;
    void LateUpdate()
    {
        if (!gazeCamera || !gazeCamera.isActiveAndEnabled) gazeCamera = Camera.main;
        InteractableObject hitTarget = null;
        if (gazeCamera && Physics.Raycast(gazeCamera.transform.position, gazeCamera.transform.forward,
            out RaycastHit hit, maxDistance, targetLayers, QueryTriggerInteraction.Ignore))
            hitTarget = hit.collider.GetComponentInParent<InteractableObject>();
        if (candidate != hitTarget)
        {
            candidate = hitTarget;
            candidateSince = Time.unscaledTime;
            // Clear stale focus immediately so commands cannot act on a previous target.
            SetStable(null);
        }
        if (candidate && Time.unscaledTime - candidateSince >= stabilizationSeconds)
            SetStable(candidate);
    }
    void SetStable(InteractableObject value)
    {
        if (CurrentStableTarget == value) return;
        if (CurrentStableTarget) CurrentStableTarget.SetGazed(false);
        CurrentStableTarget = value;
        if (value) value.SetGazed(true);
    }
    void OnDisable() { SetStable(null); candidate = null; }
}
