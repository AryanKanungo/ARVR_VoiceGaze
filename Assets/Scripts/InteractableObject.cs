using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractableObject : MonoBehaviour
{
    public enum TargetKind { Object, Destination, Control }
    public enum StudioAction { None, ToggleLighting, CycleMaterial }
    public TargetKind kind;
    public StudioAction controlAction;
    public bool IsUserDuplicate { get; private set; }
    public Color BaseColor => baseColor;
    public Vector3 OriginalScale => originalScale;
    public Bounds SurfaceBounds => surface.bounds;
    public Transform placementAnchor;
    public bool IsSelected { get; private set; }
    public bool IsGazed { get; private set; }
    public bool IsOccupied => occupant != null;
    InteractableObject occupant;
    Renderer surface;
    MaterialPropertyBlock block;
    Color originalColor, baseColor;
    Vector3 originalPosition, originalScale;
    Quaternion originalRotation;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        surface = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        originalColor = surface.sharedMaterial.GetColor(BaseColorId);
        baseColor = originalColor;
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        originalScale = transform.localScale;
    }
    public void SetGazed(bool value) { IsGazed = value; RefreshVisual(); }
    public void SetSelected(bool value) { IsSelected = value; RefreshVisual(); }
    void RefreshVisual()
    {
        if (!surface) return;
        Color color = IsSelected ? Color.Lerp(baseColor, Color.white, .6f) :
            IsGazed ? Color.Lerp(baseColor, Color.white, .3f) : baseColor;
        surface.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        surface.SetPropertyBlock(block);
    }
    public void SetOccupant(InteractableObject value) { occupant = value; }
    public void SetBaseColor(Color value) { baseColor = value; RefreshVisual(); }
    public void InitializeDuplicate(InteractableObject source)
    {
        IsUserDuplicate = true;
        originalScale = source.OriginalScale;
        IsSelected = IsGazed = false;
        SetBaseColor(source.BaseColor);
    }
    public void RestoreTransform()
    {
        transform.SetPositionAndRotation(originalPosition, originalRotation);
        transform.localScale = originalScale;
        occupant = null;
        baseColor = originalColor;
        SetSelected(false);
    }
}
