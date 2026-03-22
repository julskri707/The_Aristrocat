using UnityEngine;

public enum StoneModuleSizeClass
{
    Large = 0,
    Medium = 1,
    Small = 2,
}

[CreateAssetMenu(menuName = "TinyGlade/Walls/Cladding/Stone Module", fileName = "StoneModule")]
public sealed class WallStoneModuleDefinition : ScriptableObject
{
    [Header("Prefab")]
    public GameObject prefab;
    public StoneModuleSizeClass sizeClass = StoneModuleSizeClass.Medium;
    public WallModulePlacement placement = WallModulePlacement.Side;

    [Header("Local Size")]
    [Min(0.05f)] public float nominalWidth = 0.5f;
    [Min(0.05f)] public float nominalHeight = 0.35f;
    [Min(0.01f)] public float nominalDepth = 0.15f;

    [Header("Random")]
    [Range(0f, 1f)] public float probability = 1f;
    [Min(0f)] public float weight = 1f;
    [Range(0f, 0.5f)] public float scaleJitter = 0.15f;
    [Range(0f, 20f)] public float randomYaw = 8f;
    [Range(0f, 20f)] public float randomPitch = 2f;
    [Range(0f, 20f)] public float randomRoll = 4f;

    [Header("Fitting")]
    public bool canUseNearCorners = true;
    public bool preferAsGapFiller = false;
    [Min(0f)] public float extraEdgeInset = 0f;

    private void OnValidate()
    {
        nominalWidth = Mathf.Max(0.05f, nominalWidth);
        nominalHeight = Mathf.Max(0.05f, nominalHeight);
        nominalDepth = Mathf.Max(0.01f, nominalDepth);
        probability = Mathf.Clamp01(probability);
        weight = Mathf.Max(0f, weight);
        scaleJitter = Mathf.Clamp(scaleJitter, 0f, 0.5f);
        extraEdgeInset = Mathf.Max(0f, extraEdgeInset);
    }
}
