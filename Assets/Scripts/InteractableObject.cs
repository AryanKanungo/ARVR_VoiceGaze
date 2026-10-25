using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractableObject : MonoBehaviour
{
    public enum TargetKind { Object, Destination }
    public TargetKind kind;
    public Transform placementAnchor;
    public bool IsSelected { get; private set; }
    public bool IsGazed { get; private set; }
    public bool IsOccupied => occupant != null;
    InteractableObject occupant;
    Renderer surface;
    MaterialPropertyBlock block;
    Color originalColor;
    Vector3 originalPosition, originalScale;
    Quaternion originalRotation;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        surface = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        originalColor = surface.sharedMaterial.GetColor(BaseColor);
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        originalScale = transform.localScale;
    }
    public void SetGazed(bool value) { IsGazed = value; RefreshVisual(); }
    public void SetSelected(bool value) { IsSelected = value; RefreshVisual(); }
    void RefreshVisual()
    {
        if (!surface) return;
        Color color = IsSelected ? Color.Lerp(originalColor, Color.white, .6f) :
            IsGazed ? Color.Lerp(originalColor, Color.white, .3f) : originalColor;
        surface.GetPropertyBlock(block);
        block.SetColor(BaseColor, color);
        surface.SetPropertyBlock(block);
    }
    public void SetOccupant(InteractableObject value) { occupant = value; }
    public void RestoreTransform()
    {
        transform.SetPositionAndRotation(originalPosition, originalRotation);
        transform.localScale = originalScale;
        occupant = null;
        SetSelected(false);
    }
}
