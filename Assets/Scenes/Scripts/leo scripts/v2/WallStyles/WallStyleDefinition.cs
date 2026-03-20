using UnityEngine;

[CreateAssetMenu(fileName = "WallStyle", menuName = "TinyGlade/Walls/Wall Style")]
public class WallStyleDefinition : ScriptableObject
{
    [Header("Identity")]
    public string styleId = "wall_style";
    public string displayName = "Wall Style";
    public Sprite icon;

    [Header("Rendering")]
    public Material wallMaterial;

    [Header("Geometry")]
    [Min(0.1f)] public float height = 2.5f;
    [Min(0.01f)] public float thickness = 0.25f;

    [Header("UV")]
    [Min(0.01f)] public float uvMetersPerU = 0.5f;
    [Min(0.01f)] public float uvMetersPerV = 2.0f;

    [Header("UI")]
    public Color previewTint = Color.white;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(styleId))
            styleId = name;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;

        height = Mathf.Max(0.1f, height);
        thickness = Mathf.Max(0.01f, thickness);
        uvMetersPerU = Mathf.Max(0.01f, uvMetersPerU);
        uvMetersPerV = Mathf.Max(0.01f, uvMetersPerV);
    }
}