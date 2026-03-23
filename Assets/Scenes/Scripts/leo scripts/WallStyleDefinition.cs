using UnityEngine;

[CreateAssetMenu(fileName = "WallStyle", menuName = "TinyGlade/Walls/Wall Style")]
public class WallStyleDefinition : ScriptableObject
{
    [Header("Identity")]
    public string styleId = "wall_style";
    public string displayName = "Wall Style";
    public Sprite icon;

    [Header("Materials")]
    public Material sideMaterial;
    public Material topMaterial;
    public Material capMaterial;

    [Header("Geometry")]
    [Min(0.1f)] public float height = 2.5f;
    [Min(0.01f)] public float thickness = 0.25f;

    [Header("UV")]
    [Min(0.01f)] public float sideUvMetersPerU = 0.5f;
    [Min(0.01f)] public float sideUvMetersPerV = 1.0f;
    [Min(0.01f)] public float topUvMetersPerU = 0.5f;
    [Min(0.01f)] public float topUvMetersPerV = 0.5f;
    [Min(0.01f)] public float capUvMetersPerU = 0.5f;
    [Min(0.01f)] public float capUvMetersPerV = 1.0f;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(styleId))
            styleId = name;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;

        height = Mathf.Max(0.1f, height);
        thickness = Mathf.Max(0.01f, thickness);

        sideUvMetersPerU = Mathf.Max(0.01f, sideUvMetersPerU);
        sideUvMetersPerV = Mathf.Max(0.01f, sideUvMetersPerV);

        topUvMetersPerU = Mathf.Max(0.01f, topUvMetersPerU);
        topUvMetersPerV = Mathf.Max(0.01f, topUvMetersPerV);

        capUvMetersPerU = Mathf.Max(0.01f, capUvMetersPerU);
        capUvMetersPerV = Mathf.Max(0.01f, capUvMetersPerV);
    }
}