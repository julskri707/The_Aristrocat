using UnityEngine;

[CreateAssetMenu(fileName = "HierarchicalGridSettings", menuName = "Grid/Hierarchical Grid Settings")]
public class HierarchicalGridSettings : ScriptableObject
{
    public enum FocusMode
    {
        Camera,
        TargetTransform,
        ManualPoint
    }

    [Header("Hierarchy")]
    [Min(8f)] public float rootCellSize = 256f;
    [Range(1, 8)] public int maxDepth = 5;
    [Min(0.25f)] public float minCellSize = 0.5f;
    public bool uniformSubdivision = true;
    [Min(0.25f)] public float subdivisionDistanceFactor = 2.4f;
    [Range(1.1f, 4f)] public float perLevelDistanceMultiplier = 1.7f;
    [Min(1024)] public int maxLeafNodes = 70000;

    [Header("Focus")]
    public FocusMode focusMode = FocusMode.Camera;
    public bool recenterRootOnFocus = false;
    [Min(0.01f)] public float rebuildMoveThreshold = 0.1f;
    [Min(0.01f)] public float rebuildHeightThreshold = 0.2f;
    [Min(0f)] public float minRebuildInterval = 0.015f;
    public Vector3 manualFocusPoint = Vector3.zero;
    public float gridPlaneY = 0f;

    [Header("Map alignment")]
    [Tooltip("World XZ of the map center. The root grid cell is centered on this point (symmetric around it).")]
    public Vector2 gridWorldCenterXZ = Vector2.zero;

    [Header("World offset")]
    [Tooltip("Added to the resolved focus point (camera / target / manual) so the whole grid shifts in world space. X = world +X; Y component maps to world +Z.")]
    public Vector2 gridFocusOffsetXZ = Vector2.zero;

    [Header("Render")]
    [Min(0.0005f)] public float baseLineThickness = 0.05f;
    [Range(0.05f, 2f)] public float deepLevelThicknessFactor = 0.35f;
    [Range(0f, 1f)] public float globalOpacity = 0.8f;
    [Range(0f, 0.3f)] public float levelFadeRange = 0.07f;
    [Min(16)] public int maxLinesPerAxisPerLevel = 320;
    [Range(-0.05f, 0.25f)] public float surfaceYOffset = 0.01f;
    public bool castShadows = false;
    public bool receiveShadows = false;

    [Header("Style")]
    public Gradient levelColorGradient = DefaultGradient();
    public Material lineMaterial;

    [Header("Optional")]
    public bool highlightCellUnderMouse = true;
    public Color highlightColor = new Color(0.95f, 0.55f, 0.2f, 0.85f);
    public bool enableDebugLogs = false;

    static Gradient DefaultGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 0f),
                new GradientColorKey(new Color(0.82f, 0.82f, 0.82f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.35f, 1f)
            });
        return g;
    }
}

