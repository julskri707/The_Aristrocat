using UnityEngine;

public enum StoneModuleSizeClass
{
    Large = 0,
    Medium = 1,
    Small = 2,
}

[CreateAssetMenu(menuName = "TinyGlade/Walls/Cladding/Stone Module", fileName = "WallStoneModule")]
public sealed class WallStoneModuleDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Stone Module";
    public StoneModuleSizeClass sizeClass = StoneModuleSizeClass.Medium;

    [Header("Usage")]
    [Min(0.01f)] public float weight = 1f;
    [Range(0f, 1f)] public float probability = 1f;
    public bool canUseNearCorners = true;
    public bool preferAsGapFiller = false;

    [Header("Width / Height Ratio")]
    [Min(0.40f)] public float minWidthToHeight = 1.10f;
    [Min(0.40f)] public float maxWidthToHeight = 1.90f;

    [Header("Corner Cuts")]
    [Range(0f, 0.40f)] public float minCornerCut = 0.06f;
    [Range(0f, 0.50f)] public float maxCornerCut = 0.18f;

    [Header("Face Shape")]
    [Range(0f, 0.15f)] public float frontRelief = 0.025f;
    [Range(0.5f, 1.5f)] public float depthMultiplier = 1f;
    [Range(0f, 0.25f)] public float verticalEdgeLean = 0.06f;
    [Range(0f, 0.25f)] public float horizontalEdgeLean = 0.04f;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;

        weight = Mathf.Max(0.01f, weight);
        probability = Mathf.Clamp01(probability);

        minWidthToHeight = Mathf.Max(0.40f, minWidthToHeight);
        maxWidthToHeight = Mathf.Max(minWidthToHeight, maxWidthToHeight);

        minCornerCut = Mathf.Clamp(minCornerCut, 0f, 0.40f);
        maxCornerCut = Mathf.Clamp(maxCornerCut, minCornerCut, 0.50f);

        frontRelief = Mathf.Clamp(frontRelief, 0f, 0.15f);
        depthMultiplier = Mathf.Clamp(depthMultiplier, 0.5f, 1.5f);
        verticalEdgeLean = Mathf.Clamp(verticalEdgeLean, 0f, 0.25f);
        horizontalEdgeLean = Mathf.Clamp(horizontalEdgeLean, 0f, 0.25f);
    }
}
