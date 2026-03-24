using UnityEngine;

[CreateAssetMenu(
    fileName = "BrickModule",
    menuName = "TinyGlade/Walls/Cladding/Brick Module"
)]
public class WallBrickModuleDefinition : ScriptableObject
{
    [Header("Optional Prefab")]
    public GameObject prefab;

    [Header("Nominal Size")]
    [Min(0.01f)] public float nominalWidth = 0.50f;
    [Min(0.01f)] public float nominalHeight = 0.20f;
    [Min(0.01f)] public float nominalDepth = 0.12f;

    [Header("Placement / Variation")]
    [Range(0f, 1f)] public float probability = 1f;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Range(0f, 10f)] public float widthJitter = 0.02f;
    [Range(0f, 10f)] public float heightJitter = 0.02f;
    [Range(0f, 10f)] public float depthJitter = 0.02f;

    private void OnValidate()
    {
        nominalWidth = Mathf.Max(0.01f, nominalWidth);
        nominalHeight = Mathf.Max(0.01f, nominalHeight);
        nominalDepth = Mathf.Max(0.01f, nominalDepth);
        probability = Mathf.Clamp01(probability);

        widthJitter = Mathf.Max(0f, widthJitter);
        heightJitter = Mathf.Max(0f, heightJitter);
        depthJitter = Mathf.Max(0f, depthJitter);
    }
}