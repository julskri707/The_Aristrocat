using UnityEngine;

public enum BrickModuleKind
{
    Full = 0,
    Half = 1,
    Corner = 2,
    End = 3,
}

[CreateAssetMenu(menuName = "TinyGlade/Walls/Cladding/Brick Module", fileName = "BrickModule")]
public sealed class WallBrickModuleDefinition : ScriptableObject
{
    [Header("Prefab")]
    public GameObject prefab;
    public BrickModuleKind kind = BrickModuleKind.Full;
    public WallModulePlacement placement = WallModulePlacement.Side;

    [Header("Local Size")]
    [Min(0.05f)] public float nominalWidth = 0.32f;
    [Min(0.05f)] public float nominalHeight = 0.16f;
    [Min(0.01f)] public float nominalDepth = 0.12f;

    [Header("Usage")]
    [Range(0f, 1f)] public float probability = 1f;
    [Min(0f)] public float weight = 1f;
    public bool allowRandomFlipX = false;
    public bool allowRandomFlipY = false;

    [Header("Random")]
    [Range(0f, 0.2f)] public float sizeJitter = 0.015f;
    [Range(0f, 5f)] public float randomYaw = 0.5f;
    [Range(0f, 0.05f)] public float positionJitter = 0.005f;

    private void OnValidate()
    {
        nominalWidth = Mathf.Max(0.05f, nominalWidth);
        nominalHeight = Mathf.Max(0.05f, nominalHeight);
        nominalDepth = Mathf.Max(0.01f, nominalDepth);
        probability = Mathf.Clamp01(probability);
        weight = Mathf.Max(0f, weight);
        sizeJitter = Mathf.Clamp(sizeJitter, 0f, 0.2f);
        positionJitter = Mathf.Clamp(positionJitter, 0f, 0.05f);
    }
}
