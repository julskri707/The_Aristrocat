using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Habillage type « tuiles » sur le shell du toit (<see cref="HouseRoofSystem"/> submesh 0), même esprit que <see cref="WallCladdingGenerator"/> mais surface triangulée.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HouseRoofSystem))]
[RequireComponent(typeof(RoofCladdingRuntime))]
[DefaultExecutionOrder(3300)]
public sealed class RoofCladdingGenerator : MonoBehaviour
{
    const int RoofShellSubMeshIndex = 0;

    static readonly Color TerracottaTileBase = new Color(0.65f, 0.28f, 0.16f, 1f);

    readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        readonly int a;
        readonly int b;

        public int A => a;
        public int B => b;

        public EdgeKey(int i0, int i1)
        {
            if (i0 < i1)
            {
                a = i0;
                b = i1;
            }
            else
            {
                a = i1;
                b = i0;
            }
        }

        public bool Equals(EdgeKey other) => a == other.a && b == other.b;
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => (a * 397) ^ b;
    }

    struct CreaseEdgeInfo
    {
        public Vector3 worldA;
        public Vector3 worldB;
        public Vector3 normalA;
        public Vector3 normalB;
        public int vertexA;
        public int vertexB;
        public bool hasSecondNormal;
    }

    [Header("Profile")]
    [SerializeField] private RoofCladdingProfile defaultProfile;
    [SerializeField] private bool autoRegenerate = true;

    /// <summary>Lecture pour logs / <see cref="HouseRoofSystem"/> (application du défaut inspecteur).</summary>
    public RoofCladdingProfile SerializedDefaultCladdingProfile => defaultProfile;

    /// <returns><c>true</c> si le profil par défaut du générateur a été assigné.</returns>
    public bool EnsureDefaultCladdingProfileIfEmpty(RoofCladdingProfile profile)
    {
        if (defaultProfile != null || profile == null)
            return false;
        defaultProfile = profile;
        return true;
    }

    [Header("Base shell")]
    [Tooltip("Si activé : désactive le MeshRenderer du maillage toit d’origine (shell + gouttière sur le même renderer).")]
    [SerializeField] private bool hideBaseRoofMeshRenderer;

    [Header("Fallback sans profil")]
    [SerializeField, Min(0.08f)] private float fallbackTileWidthMeters = 0.42f;
    [SerializeField, Min(0.06f)] private float fallbackTileHeightMeters = 0.28f;
    [SerializeField, Min(0f)] private float fallbackTileOverlapMeters = 0.035f;
    [SerializeField, Min(0.002f)] private float fallbackTileThicknessMeters = 0.022f;
    [SerializeField, Min(0f)] private float fallbackTileEmbedDepthMeters = 0.02f;
    [SerializeField, Min(0f)] private float fallbackNormalSurfaceOffsetMeters = 0.006f;
    [SerializeField, Range(0f, 1f)] private float fallbackRowStaggerFraction = 0.5f;
    [SerializeField, Range(0f, 0.12f)] private float fallbackUniformScaleJitter = 0.04f;
    [SerializeField, Min(16)] private int fallbackMaxGeneratedTiles = 4000;

    [Header("Espacement tuiles (shell)")]
    [Tooltip("S’ajoute au pas U et V (horizontal entre tuiles et vertical entre rangées). Sans changer l’orientation.")]
    [SerializeField, Min(0f)] private float extraTileGapMeters = 0.01f;
    [SerializeField] private bool scaleExtraTileGapWithBuildingScale = true;
    [Tooltip("Logs [RoofTileGap] une fois par rebuild.")]
    [SerializeField] private bool logRoofTileGap;

    [Header("Décalage normale (shell, anti z-fighting)")]
    [Tooltip("S’ajoute au normalSurfaceOffset du profil (après scale bâtiment si actif), le long de la normale du pan. Défaut 0,01 m (supplément anti z-fighting).")]
    [SerializeField, Min(0f)] private float extraTileNormalOffsetMeters = 0.01f;
    [SerializeField] private bool scaleExtraTileNormalOffsetWithBuildingScale = true;
    [Tooltip("Logs [RoofTileZFightFix] une fois par rebuild.")]
    [SerializeField] private bool logRoofTileZFightFix;

    [Header("Finitions d'arêtes")]
    [SerializeField] private bool generateCreaseCaps = true;
    [SerializeField, Range(2f, 45f)] private float creaseCapMinAngleDeg = 8f;
    [SerializeField, Range(0.90f, 0.999f)] private float capCoplanarDotThreshold = 0.98f;
    [SerializeField, Min(0.02f)] private float creaseCapWidthMeters = 0.18f;
    [SerializeField, Min(0.002f)] private float creaseCapSurfaceProtrusionMeters = 0.035f;
    [SerializeField, Min(0f)] private float creaseCapEmbedDepthMeters = 0.02f;
    [SerializeField] private bool generatePerimeterCaps = true;
    [SerializeField, Min(0.02f)] private float perimeterCapWidthMeters = 0.16f;
    [SerializeField, Min(0.002f)] private float perimeterCapSurfaceProtrusionMeters = 0.03f;
    [SerializeField, Min(0f)] private float perimeterCapEmbedDepthMeters = 0.02f;

    [SerializeField] private bool logRebuildWarnings = true;

    [Header("Debug — orientation tuiles (shell)")]
    [Tooltip("Logs [RoofTileOrientationStable] pour quelques triangles par rebuild (ne pas laisser en prod).")]
    [SerializeField] private bool logStableTileOrientation;

    [Tooltip("Logs [RoofTileGroupOrientation] : groupe pan / référence / petite face alignée.")]
    [SerializeField] private bool logRoofTileGroupOrientation;

    HouseRoofSystem _roof;
    RoofCladdingRuntime _runtime;
    Material _fallbackTileMaterial;
    int _lastConfigHash = int.MinValue;
    /// <summary>Budget de logs <see cref="logStableTileOrientation"/> par passage de <see cref="RebuildInternal"/>.</summary>
    int _roofTileOrientationLogRemaining;

    /// <summary>Budget de logs <see cref="logRoofTileGroupOrientation"/> par rebuild.</summary>
    int _roofTileGroupOrientationLogRemaining;

    void Awake()
    {
        _roof = GetComponent<HouseRoofSystem>();
        _runtime = GetComponent<RoofCladdingRuntime>();
    }

    void OnEnable()
    {
        if (_runtime == null) _runtime = GetComponent<RoofCladdingRuntime>();
        if (_roof == null) _roof = GetComponent<HouseRoofSystem>();
        _runtime?.MarkDirty();
    }

    void LateUpdate()
    {
        if (!autoRegenerate || _roof == null || _runtime == null)
            return;

        RoofCladdingProfile profile = _runtime.CurrentProfile != null ? _runtime.CurrentProfile : defaultProfile;
        int configHash = _roof.GetRoofConfigurationHash();
        configHash = configHash * 31 + Mathf.RoundToInt(GetEffectiveBuildingScale() * 1000f);
        configHash = configHash * 31 + Mathf.RoundToInt(extraTileGapMeters * 10000f);
        configHash = configHash * 31 + (scaleExtraTileGapWithBuildingScale ? 1 : 0);
        configHash = configHash * 31 + Mathf.RoundToInt(extraTileNormalOffsetMeters * 100000f);
        configHash = configHash * 31 + (scaleExtraTileNormalOffsetWithBuildingScale ? 7 : 11);
        bool needRebuild = _runtime.IsDirty || configHash != _lastConfigHash;
        if (!needRebuild)
            return;

        RebuildInternal(profile, configHash);
    }

    /// <summary>Forcer un rebuild (ex. après undo ou changement de profil en code).</summary>
    public void RequestRebuild()
    {
        _runtime?.MarkDirty();
    }

    float GetEffectiveBuildingScale()
    {
        WallBuildController controller = FindFirstObjectByType<WallBuildController>(FindObjectsInactive.Include);
        return controller != null ? Mathf.Max(0.01f, controller.GetEffectiveBuildingScale()) : 1f;
    }

    void RebuildInternal(RoofCladdingProfile profile, int configHash)
    {
        _runtime.ClearRoot();

        ApplyBaseRoofRendererVisible(!hideBaseRoofMeshRenderer);

        Mesh mesh = _roof.GetRoofSharedMesh();
        MeshFilter roofMf = _roof.GetRoofMeshFilter();
        if (mesh == null || roofMf == null || mesh.subMeshCount <= RoofShellSubMeshIndex)
        {
            _runtime.MarkDirty();
            return;
        }

        int[] tris = mesh.GetTriangles(RoofShellSubMeshIndex);
        Vector3[] verts = mesh.vertices;
        if (tris == null || tris.Length < 3 || verts == null || verts.Length == 0)
        {
            _runtime.MarkDirty();
            return;
        }

        int exteriorTriIndexCount = _roof.GetRoofExteriorShellTriangleCount() * 3;
        if (exteriorTriIndexCount <= 0)
        {
            _runtime.MarkDirty();
            return;
        }
        exteriorTriIndexCount = Mathf.Min(exteriorTriIndexCount, tris.Length);

        System.Random rng = new System.Random(_runtime.CurrentSeed ^ configHash);
        Transform root = _runtime.GetOrCreateRoot();
        // Positions des tuiles en espace monde → local du parent « GeneratedRoofCladding » uniquement.
        Matrix4x4 worldToCladdingLocal = root.worldToLocalMatrix;

        Material mat = ResolveTileMaterial(profile, roofMf, out bool useExplicitAssignedTileMaterial);
        if (mat == null)
        {
            if (logRebuildWarnings)
                Debug.LogWarning("[RoofCladdingGenerator] Aucun matériau résolu (profil, toit ou mur).", this);
            _lastConfigHash = configHash;
            _runtime.MarkClean();
            return;
        }

        float tileWidth = profile != null ? profile.tileWidthMeters : fallbackTileWidthMeters;
        float tileHeight = profile != null ? profile.tileHeightMeters : fallbackTileHeightMeters;
        float tileOverlap = profile != null ? profile.tileOverlapMeters : fallbackTileOverlapMeters;
        float tileThickness = profile != null ? profile.tileThicknessMeters : fallbackTileThicknessMeters;
        float tileEmbedDepth = profile != null ? profile.tileEmbedDepthMeters : fallbackTileEmbedDepthMeters;
        float normalOffset = profile != null ? profile.normalSurfaceOffsetMeters : fallbackNormalSurfaceOffsetMeters;
        float buildingScale = GetEffectiveBuildingScale();
        if (!Mathf.Approximately(buildingScale, 1f))
        {
            tileWidth *= buildingScale;
            tileHeight *= buildingScale;
            tileOverlap *= buildingScale;
            tileThickness *= buildingScale;
            tileEmbedDepth *= buildingScale;
            normalOffset *= buildingScale;
            Debug.Log($"[BuildingScale] roof cladding will use scale={buildingScale:F3}", this);
        }

        float profileNormalOffsetScaled = normalOffset;
        float effectiveExtraTileNormalOffset = extraTileNormalOffsetMeters;
        if (scaleExtraTileNormalOffsetWithBuildingScale)
            effectiveExtraTileNormalOffset *= buildingScale;
        normalOffset += effectiveExtraTileNormalOffset;

        if (logRoofTileZFightFix)
        {
            Debug.Log($"[RoofTileZFightFix] profileNormalOffset={profileNormalOffsetScaled:F5}", this);
            Debug.Log($"[RoofTileZFightFix] extraTileNormalOffset={effectiveExtraTileNormalOffset:F5}", this);
            Debug.Log($"[RoofTileZFightFix] effectiveNormalOffset={normalOffset:F5}", this);
        }

        float effectiveExtraTileGap = extraTileGapMeters;
        if (scaleExtraTileGapWithBuildingScale)
            effectiveExtraTileGap *= buildingScale;

        float rowStagger = profile != null ? profile.rowStaggerFraction : fallbackRowStaggerFraction;
        float scaleJitter = profile != null ? profile.uniformScaleJitter : fallbackUniformScaleJitter;
        bool usingFallbackVisuals = profile == null;
        bool vertexColors = profile == null || profile.enablePerTileVertexColor;
        bool applyHueVarToTiles = vertexColors && !useExplicitAssignedTileMaterial;
        float hueJitter = profile != null ? profile.hueJitter : 0.022f;
        float saturationJitter = profile != null ? profile.saturationJitter : 0.055f;
        float valueJitter = profile != null ? profile.valueJitter : 0.09f;
        int maxTiles = Mathf.Max(16, profile != null ? profile.maxGeneratedTiles : fallbackMaxGeneratedTiles);

        float stepU = Mathf.Max(0.02f, tileWidth - tileOverlap + effectiveExtraTileGap);
        float stepV = Mathf.Max(0.02f, tileHeight - tileOverlap + effectiveExtraTileGap);
        if (logRoofTileGap)
        {
            Debug.Log($"[RoofTileGap] extraTileGapMeters={extraTileGapMeters:F5}", this);
            Debug.Log($"[RoofTileGap] effectiveExtraTileGap={effectiveExtraTileGap:F5}", this);
            Debug.Log($"[RoofTileGap] stepU={stepU:F5}", this);
            Debug.Log($"[RoofTileGap] stepV={stepV:F5}", this);
        }

        _roofTileOrientationLogRemaining = logStableTileOrientation ? 24 : 0;
        _roofTileGroupOrientationLogRemaining = logRoofTileGroupOrientation ? 64 : 0;

        int tilesBuilt = 0;

        var outVerts = new List<Vector3>(maxTiles * 12);
        var outUv = new List<Vector2>(maxTiles * 12);
        var outCol = new List<Color>(maxTiles * 12);
        var outTris = new List<int>(maxTiles * 18);
        var edgeMap = generateCreaseCaps ? new Dictionary<EdgeKey, CreaseEdgeInfo>(exteriorTriIndexCount) : null;
        var creaseEdges = generateCreaseCaps ? new List<CreaseEdgeInfo>(64) : null;

        int triCount = exteriorTriIndexCount / 3;
        var triValid = new bool[triCount];
        var triV0 = new Vector3[triCount];
        var triE1 = new Vector3[triCount];
        var triE2 = new Vector3[triCount];
        var triN = new Vector3[triCount];
        var triAxisU = new Vector3[triCount];
        var triAxisV = new Vector3[triCount];
        var triArea = new float[triCount];

        // --- Phase 1 : normale + TryComputeRoofTileAxes pour tout le shell ---
        for (int t = 0; t + 2 < exteriorTriIndexCount; t += 3)
        {
            int slot = t / 3;
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
                continue;

            Vector3 v0w = roofMf.transform.TransformPoint(verts[i0]);
            Vector3 v1w = roofMf.transform.TransformPoint(verts[i1]);
            Vector3 v2w = roofMf.transform.TransformPoint(verts[i2]);
            Vector3 e1 = v1w - v0w;
            Vector3 e2 = v2w - v0w;
            Vector3 n = Vector3.Cross(e1, e2);
            float area2 = n.sqrMagnitude;
            if (area2 < 1e-12f)
                continue;
            float areaTri = 0.5f * Mathf.Sqrt(area2);
            n = n / Mathf.Sqrt(area2);
            if (n.y < 0f)
                n = -n;

            RegisterCreaseEdge(edgeMap, creaseEdges, i0, i1, v0w, v1w, n);
            RegisterCreaseEdge(edgeMap, creaseEdges, i1, i2, v1w, v2w, n);
            RegisterCreaseEdge(edgeMap, creaseEdges, i2, i0, v2w, v0w, n);

            if (!TryComputeRoofTileAxes(v0w, v1w, v2w, n, out Vector3 axisU, out Vector3 axisV))
                continue;
            if (axisV.sqrMagnitude < 1e-10f)
                continue;

            triValid[slot] = true;
            triV0[slot] = v0w;
            triE1[slot] = e1;
            triE2[slot] = e2;
            triN[slot] = n;
            triAxisU[slot] = axisU;
            triAxisV[slot] = axisV;
            triArea[slot] = areaTri;
        }

        ApplyRoofSideGroupTileOrientation(triAxisU, triAxisV, triN, triArea, triValid);

        // --- Phase 2 : même grille qu’avant, axes du groupe pour les petites faces du même pan ---
        for (int slot = 0; slot < triCount && tilesBuilt < maxTiles; slot++)
        {
            if (!triValid[slot])
                continue;

            Vector3 v0w = triV0[slot];
            Vector3 e1 = triE1[slot];
            Vector3 e2 = triE2[slot];
            Vector3 n = triN[slot];
            Vector3 axisU = triAxisU[slot];
            Vector3 axisV = triAxisV[slot];

            float u0 = 0f, u1 = Vector3.Dot(e1, axisU), u2 = Vector3.Dot(e2, axisU);
            float vv0 = 0f, vv1 = Vector3.Dot(e1, axisV), vv2 = Vector3.Dot(e2, axisV);
            float minU = Mathf.Min(u0, u1, u2);
            float maxU = Mathf.Max(u0, u1, u2);
            float minV = Mathf.Min(vv0, vv1, vv2);
            float maxV = Mathf.Max(vv0, vv1, vv2);

            int row = 0;
            for (float fv = minV; fv <= maxV + 1e-4f && tilesBuilt < maxTiles; fv += stepV, row++)
            {
                float stagger = (row & 1) == 1 ? stepU * Mathf.Clamp01(rowStagger) : 0f;
                for (float fu = minU + stagger; fu <= maxU + 1e-4f && tilesBuilt < maxTiles; fu += stepU)
                {
                    float sj = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * scaleJitter;
                    float w = tileWidth * sj * (usingFallbackVisuals ? 0.88f : 1f);
                    float h = tileHeight * sj * (usingFallbackVisuals ? 0.82f : 1f);
                    float thick = tileThickness;

                    Color baseTint = useExplicitAssignedTileMaterial
                        ? Color.white
                        : (profile != null ? profile.baseTileColor : TerracottaTileBase);
                    Color tint = baseTint;
                    if (applyHueVarToTiles)
                    {
                        Color.RGBToHSV(tint, out float hue, out float sat, out float val);
                        hue = Mathf.Repeat(hue + (float)(rng.NextDouble() * 2.0 - 1.0) * hueJitter, 1f);
                        sat = Mathf.Clamp01(sat + (float)(rng.NextDouble() * 2.0 - 1.0) * saturationJitter);
                        val = Mathf.Clamp01(val + (float)(rng.NextDouble() * 2.0 - 1.0) * valueJitter);
                        tint = Color.HSVToRGB(hue, sat, val);
                    }

                    if (AppendClippedTileFaceLocal(
                            outVerts,
                            outUv,
                            outCol,
                            outTris,
                            worldToCladdingLocal,
                            v0w,
                            axisU,
                            axisV,
                            n,
                            new Vector2(u0, vv0),
                            new Vector2(u1, vv1),
                            new Vector2(u2, vv2),
                            fu,
                            fv,
                            w,
                            h,
                            normalOffset,
                            thick,
                            tileEmbedDepth,
                            tint))
                    {
                        tilesBuilt++;
                    }
                }
            }
        }

        if (generateCreaseCaps && creaseEdges != null && creaseEdges.Count > 0)
        {
            Color capTint = new Color(0.48f, 0.20f, 0.12f, 1f);
            for (int i = 0; i < creaseEdges.Count; i++)
            {
                CreaseEdgeInfo edge = creaseEdges[i];
                if (!edge.hasSecondNormal)
                    continue;

                float normalDot = Vector3.Dot(edge.normalA.normalized, edge.normalB.normalized);
                if (normalDot > capCoplanarDotThreshold)
                {
                    if (logRebuildWarnings)
                        Debug.Log($"[RoofCladding] cap skipped: smooth/coplanar edge dot={normalDot:0.###}", this);
                    continue;
                }

                AppendCreaseCapLocal(
                    outVerts,
                    outUv,
                    outCol,
                    outTris,
                    worldToCladdingLocal,
                    edge.worldA,
                    edge.worldB,
                    edge.normalA,
                    edge.normalB,
                    creaseCapWidthMeters * buildingScale,
                    creaseCapSurfaceProtrusionMeters * buildingScale,
                    creaseCapEmbedDepthMeters * buildingScale,
                    capTint);
                if (logRebuildWarnings)
                    Debug.Log($"[RoofCladding] cap generated: real roof break dot={normalDot:0.###}", this);
            }
        }

        if (generatePerimeterCaps && edgeMap != null)
        {
            Color perimeterTint = new Color(0.42f, 0.17f, 0.11f, 1f);
            foreach (CreaseEdgeInfo edge in edgeMap.Values)
            {
                if (edge.hasSecondNormal)
                    continue;
                AppendCreaseCapLocal(
                    outVerts,
                    outUv,
                    outCol,
                    outTris,
                    worldToCladdingLocal,
                    edge.worldA,
                    edge.worldB,
                    edge.normalA,
                    edge.normalA,
                    perimeterCapWidthMeters * buildingScale,
                    perimeterCapSurfaceProtrusionMeters * buildingScale,
                    perimeterCapEmbedDepthMeters * buildingScale,
                    perimeterTint);
                if (logRebuildWarnings)
                    Debug.Log("[RoofCladding] perimeter cap generated", this);
            }
        }

        if (outVerts.Count == 0)
        {
            _lastConfigHash = configHash;
            _runtime.MarkClean();
            return;
        }

        var combined = new Mesh { name = "GeneratedRoofCladdingMesh" };
        combined.SetVertices(outVerts);
        combined.SetUVs(0, outUv);
        if (outCol.Count == outVerts.Count && (vertexColors || useExplicitAssignedTileMaterial))
            combined.SetColors(outCol);
        combined.SetTriangles(outTris, 0);
        combined.RecalculateNormals();
        combined.RecalculateBounds();

        MeshFilter mf = root.GetComponent<MeshFilter>();
        if (mf == null) mf = root.gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = root.GetComponent<MeshRenderer>();
        if (mr == null) mr = root.gameObject.AddComponent<MeshRenderer>();
        mf.sharedMesh = combined;
        mr.sharedMaterial = mat;
        mr.enabled = true;
        mr.SetPropertyBlock(null);

        LogRoofCladdingMaterialDiagnostics(profile, mat, mr);

        _lastConfigHash = configHash;
        _runtime.MarkClean();
    }

    void LogRoofCladdingMaterialDiagnostics(RoofCladdingProfile profile, Material selected, MeshRenderer mr)
    {
        string activeProfile = profile != null ? profile.name : "(null)";
        string claddingMat = profile != null && profile.claddingMaterial != null ? profile.claddingMaterial.name : "(null)";
        string activeTileMat = profile != null && profile.tileMaterial != null ? profile.tileMaterial.name : "(null)";
        string selectedName = selected != null ? selected.name : "(null)";
        Debug.Log($"[RoofCladdingMaterial] active profile = {activeProfile}", this);
        Debug.Log($"[RoofCladdingMaterial] active profile claddingMaterial = {claddingMat}", this);
        Debug.Log($"[RoofCladdingMaterial] active profile tileMaterial = {activeTileMat}", this);
        Debug.Log($"[RoofCladdingMaterial] selected material = {selectedName}", this);
        if (mr != null)
            Debug.Log($"[RoofCladdingMaterial] renderer sharedMaterial after assign = {mr.sharedMaterial?.name ?? "(null)"}", this);
    }

    Material ResolveTileMaterial(RoofCladdingProfile profile, MeshFilter roofMf, out bool useExplicitAssignedTileMaterial)
    {
        useExplicitAssignedTileMaterial = false;

        if (profile != null && profile.claddingMaterial != null)
        {
            useExplicitAssignedTileMaterial = true;
            return profile.claddingMaterial;
        }

        if (profile != null && profile.tileMaterial != null)
        {
            useExplicitAssignedTileMaterial = true;
            return profile.tileMaterial;
        }

        if (defaultProfile != null && defaultProfile.claddingMaterial != null)
        {
            useExplicitAssignedTileMaterial = true;
            return defaultProfile.claddingMaterial;
        }

        if (defaultProfile != null && defaultProfile.tileMaterial != null)
        {
            useExplicitAssignedTileMaterial = true;
            return defaultProfile.tileMaterial;
        }

        MeshRenderer roofRenderer = roofMf != null ? roofMf.GetComponent<MeshRenderer>() : null;
        Debug.Log("[RoofCladdingMaterial] Using fallback hardcoded material (no claddingMaterial / tileMaterial on active or default profile)", this);
        return EnsureFallbackTileMaterial(roofRenderer != null ? roofRenderer.sharedMaterial : null);
    }

    Material EnsureFallbackTileMaterial(Material source)
    {
        if (_fallbackTileMaterial != null)
            return _fallbackTileMaterial;

        Shader shader = source != null && source.shader != null
            ? source.shader
            : Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Hidden/InternalErrorShader");

        _fallbackTileMaterial = new Material(shader)
        {
            name = "Roof Cladding Fallback Tiles",
            hideFlags = HideFlags.DontSave,
            color = TerracottaTileBase
        };
        if (_fallbackTileMaterial.HasProperty("_BaseColor"))
            _fallbackTileMaterial.SetColor("_BaseColor", TerracottaTileBase);
        if (_fallbackTileMaterial.HasProperty("_Color"))
            _fallbackTileMaterial.SetColor("_Color", TerracottaTileBase);
        if (_fallbackTileMaterial.HasProperty("_Smoothness"))
            _fallbackTileMaterial.SetFloat("_Smoothness", 0.14f);
        if (_fallbackTileMaterial.HasProperty("_Metallic"))
            _fallbackTileMaterial.SetFloat("_Metallic", 0f);
        return _fallbackTileMaterial;
    }

    void ApplyBaseRoofRendererVisible(bool visible)
    {
        MeshFilter roofMf = _roof.GetRoofMeshFilter();
        if (roofMf == null)
            return;
        MeshRenderer rm = roofMf.GetComponent<MeshRenderer>();
        if (rm != null)
            rm.enabled = visible;
    }

    static void RegisterCreaseEdge(
        Dictionary<EdgeKey, CreaseEdgeInfo> edgeMap,
        List<CreaseEdgeInfo> creaseEdges,
        int ia,
        int ib,
        Vector3 worldA,
        Vector3 worldB,
        Vector3 normal)
    {
        if (edgeMap == null || creaseEdges == null)
            return;

        var key = new EdgeKey(ia, ib);
        if (!edgeMap.TryGetValue(key, out CreaseEdgeInfo existing))
        {
            edgeMap[key] = new CreaseEdgeInfo
            {
                worldA = worldA,
                worldB = worldB,
                normalA = normal,
                normalB = normal,
                vertexA = key.A,
                vertexB = key.B,
                hasSecondNormal = false
            };
            return;
        }

        if (existing.hasSecondNormal)
            return;

        existing.normalB = normal;
        existing.hasSecondNormal = true;
        edgeMap[key] = existing;
        creaseEdges.Add(existing);
    }

    enum RoofPanSideGroup
    {
        Front,
        Back,
        Left,
        Right
    }

    /// <summary>
    /// Classe le pan via la normale projetée sur XZ : composante dominante |x| vs |z| (world X / world Z).
    /// Front = +Z, Back = −Z, Right = +X, Left = −X (normale horizontale dominante).
    /// </summary>
    static RoofPanSideGroup GetRoofPanSideGroupFromNormal(Vector3 normal)
    {
        Vector2 nxz = new Vector2(normal.x, normal.z);
        if (nxz.sqrMagnitude < 1e-12f)
            return RoofPanSideGroup.Front;

        nxz.Normalize();
        if (Mathf.Abs(nxz.x) >= Mathf.Abs(nxz.y))
            return nxz.x < 0f ? RoofPanSideGroup.Left : RoofPanSideGroup.Right;
        return nxz.y < 0f ? RoofPanSideGroup.Back : RoofPanSideGroup.Front;
    }

    static string RoofPanSideGroupToString(RoofPanSideGroup g)
    {
        return g switch
        {
            RoofPanSideGroup.Front => "Front",
            RoofPanSideGroup.Back => "Back",
            RoofPanSideGroup.Left => "Left",
            RoofPanSideGroup.Right => "Right",
            _ => g.ToString()
        };
    }

    /// <summary>
    /// Après <see cref="TryComputeRoofTileAxes"/> sur tout le shell : regroupe les triangles par « côté » (normal XZ).
    /// Dans chaque groupe, le triangle d’aire maximale garde son orientation (référence du pan).
    /// Les autres triangles du groupe avec aire &lt; 75 % de la référence héritent de <c>axisU</c> de la référence (projection sur le plan local), puis <c>axisV</c> et correction pente comme <see cref="TryComputeRoofTileAxes"/>.
    /// Les grands triangles du groupe (≥ 75 % de l’aire de référence) ne sont pas modifiés.
    /// </summary>
    void ApplyRoofSideGroupTileOrientation(
        Vector3[] axisU,
        Vector3[] axisV,
        Vector3[] normals,
        float[] areas,
        bool[] triValid)
    {
        if (axisU == null || axisV == null || normals == null || areas == null || triValid == null)
            return;

        int triCount = axisU.Length;
        var byGroup = new Dictionary<RoofPanSideGroup, List<int>>();
        for (int slot = 0; slot < triCount; slot++)
        {
            if (!triValid[slot])
                continue;
            RoofPanSideGroup g = GetRoofPanSideGroupFromNormal(normals[slot]);
            if (!byGroup.TryGetValue(g, out List<int> list))
            {
                list = new List<int>(32);
                byGroup[g] = list;
            }

            list.Add(slot);
        }

        var inv = CultureInfo.InvariantCulture;
        const float smallFaceAreaRatio = 0.75f;

        foreach (KeyValuePair<RoofPanSideGroup, List<int>> kv in byGroup)
        {
            RoofPanSideGroup group = kv.Key;
            List<int> members = kv.Value;
            if (members == null || members.Count == 0)
                continue;

            int refSlot = members[0];
            float refArea = areas[refSlot];
            for (int i = 1; i < members.Count; i++)
            {
                int s = members[i];
                if (areas[s] > refArea)
                {
                    refArea = areas[s];
                    refSlot = s;
                }
            }

            Vector3 refAxisU = axisU[refSlot];
            if (logRoofTileGroupOrientation)
                Debug.Log(
                    $"[RoofTileGroupOrientation] group={RoofPanSideGroupToString(group)} referenceArea={refArea.ToString("F6", inv)}",
                    this);

            foreach (int slot in members)
            {
                if (slot == refSlot)
                    continue;
                if (areas[slot] >= areas[refSlot] * smallFaceAreaRatio)
                    continue;

                Vector3 n = normals[slot];
                Vector3 oldAxisU = axisU[slot];
                Vector3 oldAxisV = axisV[slot];

                Vector3 projectedU = Vector3.ProjectOnPlane(refAxisU, n);
                if (projectedU.sqrMagnitude <= 1e-4f)
                    continue;

                projectedU.Normalize();
                axisU[slot] = projectedU;
                axisV[slot] = Vector3.Cross(n, projectedU);
                if (axisV[slot].sqrMagnitude < 1e-14f)
                {
                    axisU[slot] = oldAxisU;
                    axisV[slot] = oldAxisV;
                    continue;
                }

                axisV[slot].Normalize();
                if (axisV[slot].y < -1e-5f)
                {
                    axisU[slot] = -axisU[slot];
                    axisV[slot] = Vector3.Cross(n, axisU[slot]).normalized;
                }

                if (axisV[slot].sqrMagnitude < 1e-14f || axisV[slot].y < -1e-5f)
                {
                    axisU[slot] = oldAxisU;
                    axisV[slot] = oldAxisV;
                    continue;
                }

                if (logRoofTileGroupOrientation && _roofTileGroupOrientationLogRemaining > 0)
                {
                    _roofTileGroupOrientationLogRemaining--;
                    Debug.Log("[RoofTileGroupOrientation] applied group orientation to small/addition face", this);
                    Debug.Log($"[RoofTileGroupOrientation] oldAxisU={oldAxisU.ToString("F5", inv)}", this);
                    Debug.Log($"[RoofTileGroupOrientation] newAxisU={axisU[slot].ToString("F5", inv)}", this);
                }
            }
        }
    }

    static HashSet<int> BuildPerimeterVertexSet(Dictionary<EdgeKey, CreaseEdgeInfo> edgeMap)
    {
        if (edgeMap == null || edgeMap.Count == 0)
            return null;

        var vertices = new HashSet<int>();
        foreach (KeyValuePair<EdgeKey, CreaseEdgeInfo> pair in edgeMap)
        {
            if (pair.Value.hasSecondNormal)
                continue;
            vertices.Add(pair.Key.A);
            vertices.Add(pair.Key.B);
        }

        return vertices.Count > 0 ? vertices : null;
    }

    bool ShouldSkipCreaseCap(CreaseEdgeInfo edge, HashSet<int> perimeterVertices, float averageTileWidth)
    {
        bool edgeTouchesPerimeter =
            perimeterVertices != null &&
            (perimeterVertices.Contains(edge.vertexA) || perimeterVertices.Contains(edge.vertexB));
        if (edgeTouchesPerimeter)
            return true;

        Vector3 delta = edge.worldB - edge.worldA;
        float edgeLength = delta.magnitude;
        if (edgeLength < 1e-5f)
            return true;

        float heightDifference = Mathf.Abs(delta.y);
        if (heightDifference > 0.05f)
            return true;

        float tileReference = Mathf.Max(0.08f, averageTileWidth);
        if (edgeLength > tileReference * 3f && heightDifference > 0.02f)
            return true;

        Vector2 xz = new Vector2(delta.x, delta.z);
        float xzLength = xz.magnitude;
        if (xzLength > 1e-5f)
        {
            Vector2 dir = xz / xzLength;
            float axisAlignment = Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            bool stronglyDiagonal = axisAlignment < 0.86f;
            if (stronglyDiagonal && edgeLength > tileReference * 2f)
                return true;
        }

        return false;
    }

    static bool IsProbablyInternalOpenEdge(
        CreaseEdgeInfo edge,
        Dictionary<EdgeKey, CreaseEdgeInfo> edgeMap,
        Bounds roofBounds,
        float averageTileWidth)
    {
        Vector3 delta = edge.worldB - edge.worldA;
        float edgeLength = delta.magnitude;
        if (edgeLength < 1e-5f)
            return true;

        float tileReference = Mathf.Max(0.08f, averageTileWidth);
        float heightDifference = Mathf.Abs(delta.y);
        if (heightDifference > 0.05f)
            return true;
        if (edgeLength > tileReference * 3f && heightDifference > 0.02f)
            return true;

        if (HasNearlyDuplicateOpenEdge(edge, edgeMap, Mathf.Max(0.015f, tileReference * 0.08f)))
            return true;

        const float boundsEps = 0.045f;
        bool aOnBounds = IsOnRoofBoundsXZ(edge.worldA, roofBounds, boundsEps);
        bool bOnBounds = IsOnRoofBoundsXZ(edge.worldB, roofBounds, boundsEps);
        if (!aOnBounds && !bOnBounds)
            return true;

        Vector2 xz = new Vector2(delta.x, delta.z);
        float xzLength = xz.magnitude;
        if (xzLength > 1e-5f)
        {
            Vector2 dir = xz / xzLength;
            float axisAlignment = Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            bool stronglyDiagonal = axisAlignment < 0.86f;
            if (stronglyDiagonal && edgeLength > tileReference * 2f && (!aOnBounds || !bOnBounds))
                return true;
        }

        return false;
    }

    static bool HasNearlyDuplicateOpenEdge(
        CreaseEdgeInfo edge,
        Dictionary<EdgeKey, CreaseEdgeInfo> edgeMap,
        float positionEpsilon)
    {
        if (edgeMap == null)
            return false;

        float epsSq = positionEpsilon * positionEpsilon;
        foreach (CreaseEdgeInfo other in edgeMap.Values)
        {
            if (other.vertexA == edge.vertexA && other.vertexB == edge.vertexB)
                continue;
            if (other.hasSecondNormal)
                continue;

            bool same =
                (other.worldA - edge.worldA).sqrMagnitude <= epsSq &&
                (other.worldB - edge.worldB).sqrMagnitude <= epsSq;
            bool reversed =
                (other.worldA - edge.worldB).sqrMagnitude <= epsSq &&
                (other.worldB - edge.worldA).sqrMagnitude <= epsSq;
            if (same || reversed)
                return true;
        }

        return false;
    }

    static bool IsOnRoofBoundsXZ(Vector3 p, Bounds bounds, float eps)
    {
        return
            Mathf.Abs(p.x - bounds.min.x) <= eps ||
            Mathf.Abs(p.x - bounds.max.x) <= eps ||
            Mathf.Abs(p.z - bounds.min.z) <= eps ||
            Mathf.Abs(p.z - bounds.max.z) <= eps;
    }

    static bool TryComputeEdgeMapBounds(Dictionary<EdgeKey, CreaseEdgeInfo> edgeMap, out Bounds bounds)
    {
        bounds = default;
        if (edgeMap == null || edgeMap.Count == 0)
            return false;

        bool initialized = false;
        foreach (CreaseEdgeInfo edge in edgeMap.Values)
        {
            if (!initialized)
            {
                bounds = new Bounds(edge.worldA, Vector3.zero);
                initialized = true;
            }
            bounds.Encapsulate(edge.worldA);
            bounds.Encapsulate(edge.worldB);
        }

        return initialized;
    }

    static void AppendCreaseCapLocal(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<Color> cols,
        List<int> tris,
        Matrix4x4 worldToRootLocal,
        Vector3 a,
        Vector3 b,
        Vector3 normalA,
        Vector3 normalB,
        float width,
        float protrusion,
        float embedDepth,
        Color tint)
    {
        Vector3 along = b - a;
        float length = along.magnitude;
        if (length < 0.04f)
            return;
        along /= length;

        Vector3 outward = normalA + normalB;
        if (outward.sqrMagnitude < 1e-8f)
            outward = Vector3.up;
        outward.Normalize();
        if (outward.y < 0f)
            outward = -outward;

        Vector3 across = Vector3.Cross(outward, along);
        if (across.sqrMagnitude < 1e-8f)
            across = Vector3.Cross(Vector3.up, along);
        if (across.sqrMagnitude < 1e-8f)
            return;
        across.Normalize();

        float halfWidth = width * 0.5f;
        Vector3 frontOffset = outward * Mathf.Max(0.002f, protrusion);
        Vector3 backOffset = -outward * Mathf.Max(0f, embedDepth);

        Vector3 f0 = a - across * halfWidth + frontOffset;
        Vector3 f1 = b - across * halfWidth + frontOffset;
        Vector3 f2 = b + across * halfWidth + frontOffset;
        Vector3 f3 = a + across * halfWidth + frontOffset;

        Vector3 b0 = a - across * halfWidth + backOffset;
        Vector3 b1 = b - across * halfWidth + backOffset;
        Vector3 b2 = b + across * halfWidth + backOffset;
        Vector3 b3 = a + across * halfWidth + backOffset;

        int first = verts.Count;
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, f0, new Vector2(0f, 0f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, f1, new Vector2(length, 0f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, f2, new Vector2(length, 1f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, f3, new Vector2(0f, 1f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, b0, new Vector2(0f, 0f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, b1, new Vector2(length, 0f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, b2, new Vector2(length, 1f), tint);
        AddCreaseCapVertex(verts, uvs, cols, worldToRootLocal, b3, new Vector2(0f, 1f), tint);

        AddQuad(tris, first + 0, first + 1, first + 2, first + 3);
        AddQuad(tris, first + 4, first + 7, first + 6, first + 5);
        AddQuad(tris, first + 0, first + 4, first + 5, first + 1);
        AddQuad(tris, first + 1, first + 5, first + 6, first + 2);
        AddQuad(tris, first + 2, first + 6, first + 7, first + 3);
        AddQuad(tris, first + 3, first + 7, first + 4, first + 0);
    }

    static void AddCreaseCapVertex(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<Color> cols,
        Matrix4x4 worldToRootLocal,
        Vector3 world,
        Vector2 uv,
        Color tint)
    {
        verts.Add(worldToRootLocal.MultiplyPoint3x4(world));
        uvs.Add(uv);
        cols.Add(tint);
    }

    static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    /// <summary>
    /// <para><b>Oriente les tuiles sur ce triangle.</b> Il n’y a pas de « tileForward » / « tileRight » nommés :
    /// <c>axisU</c> ≈ direction des rangées (souvent le long de l’égout / arête basse du pan),
    /// <c>axisV</c> ≈ Cross(normal, axisU), direction « montée » sur le pan pour enchaîner les rangées vers le faîtage.</para>
    /// <para><b>Règle qui rend cette version stable :</b> on ne prend pas l’arête la plus longue ni une normale arbitraire seule.
    /// On choisit parmi les trois arêtes du triangle celle qui minimise <see cref="EdgeTileAxisScore"/> :
    /// priorité aux arêtes basses (hauteur moyenne des extrémités) et peu « couchées » horizontalement (pénalité verticalité),
    /// puis on projette cette arête sur le plan du toit. Les petits triangles (extensions) choisissent ainsi la même « arête de référence »
    /// que le pan principal quand elle est la plus « basse », au lieu d’une arête latérale qui ferait pivoter la grille.</para>
    /// <para><b>À ne pas casser sans tests :</b> la combinaison EdgeTileAxisScore + projection sur le plan + inversion de <c>axisU</c> si <c>axisV.y &lt; 0</c>
    /// (alignement du sens de rangée avec la pente vers le haut). Les UV dans <see cref="AppendClippedTileFaceLocal"/> utilisent ce repère ;
    /// modifier seulement stepU/stepV (espacement) ne doit pas toucher ce bloc.</para>
    /// </summary>
    bool TryComputeRoofTileAxes(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, out Vector3 axisU, out Vector3 axisV)
    {
        axisU = Vector3.zero;
        axisV = Vector3.zero;

        Vector3 edgeA = b - a;
        Vector3 edgeB = c - b;
        Vector3 edgeC = a - c;

        float scoreA = EdgeTileAxisScore(a, b, edgeA);
        float scoreB = EdgeTileAxisScore(b, c, edgeB);
        float scoreC = EdgeTileAxisScore(c, a, edgeC);

        Vector3 edge = edgeA;
        float best = scoreA;
        string selectedEdgeLabel = "v0-v1";
        if (scoreB < best)
        {
            best = scoreB;
            edge = edgeB;
            selectedEdgeLabel = "v1-v2";
        }

        if (scoreC < best)
        {
            edge = edgeC;
            selectedEdgeLabel = "v2-v0";
        }

        edge = Vector3.ProjectOnPlane(edge, normal);
        if (edge.sqrMagnitude < 1e-10f)
            return false;

        axisU = edge.normalized;
        axisV = Vector3.Cross(normal, axisU);
        if (axisV.sqrMagnitude < 1e-10f)
            return false;
        axisV.Normalize();

        bool flippedSlopeAlignment = false;
        if (axisV.y < -1e-5f)
        {
            flippedSlopeAlignment = true;
            axisU = -axisU;
            axisV = Vector3.Cross(normal, axisU).normalized;
        }

        if (logStableTileOrientation && _roofTileOrientationLogRemaining > 0)
        {
            _roofTileOrientationLogRemaining--;
            Debug.Log($"[RoofTileOrientationStable] face normal={normal}", this);
            Debug.Log($"[RoofTileOrientationStable] axisU={axisU}", this);
            Debug.Log($"[RoofTileOrientationStable] axisV={axisV}", this);
            Debug.Log($"[RoofTileOrientationStable] selected edge={selectedEdgeLabel}", this);
            Debug.Log($"[RoofTileOrientationStable] flipped={flippedSlopeAlignment}", this);
        }

        return axisV.y >= -1e-5f;
    }

    /// <summary>
    /// Score « plus bas = meilleur » pour choisir quelle arête du triangle sert de direction U après projection.
    /// Bas : arête dont les deux sommets ont une altitude moyenne faible (égout). Verticité : évite les arêtes trop horizontales seules.
    /// </summary>
    static float EdgeTileAxisScore(Vector3 a, Vector3 b, Vector3 edge)
    {
        float avgY = (a.y + b.y) * 0.5f;
        float verticality = Mathf.Abs(edge.y) / Mathf.Max(0.001f, edge.magnitude);
        return avgY + verticality * 2f;
    }

    /// <summary>
    /// Tuile axis-aligned dans le plan (axisU, axisV) : quad centré sur (centerU, centerV), découpé au triangle en UV puis extrudé en épaisseur.
    /// Les UV : U suit <c>p.x</c> le long de axisU ; V utilise <c>1 - InverseLerp(...)</c> pour l’orientation texture « haut du pan » — couplé au repère axisU/axisV fixé plus haut.
    /// Si <see cref="SignedArea"/> du polygone découpé est négatif, <see cref="polygon.Reverse"/> corrige le winding pour les triangles.
    /// </summary>
    static bool AppendClippedTileFaceLocal(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<Color> cols,
        List<int> tris,
        Matrix4x4 worldToRootLocal,
        Vector3 originW,
        Vector3 axisU,
        Vector3 axisV,
        Vector3 n,
        Vector2 triA,
        Vector2 triB,
        Vector2 triC,
        float centerU,
        float centerV,
        float width,
        float height,
        float normalOffset,
        float surfaceProtrusion,
        float embedDepth,
        Color tint)
    {
        float hu = width * 0.5f;
        float hv = height * 0.5f;
        var polygon = new List<Vector2>(8)
        {
            new Vector2(centerU - hu, centerV - hv),
            new Vector2(centerU + hu, centerV - hv),
            new Vector2(centerU + hu, centerV + hv),
            new Vector2(centerU - hu, centerV + hv)
        };

        ClipPolygonAgainstEdge(polygon, triA, triB, triC);
        ClipPolygonAgainstEdge(polygon, triB, triC, triA);
        ClipPolygonAgainstEdge(polygon, triC, triA, triB);

        if (polygon.Count < 3 || Mathf.Abs(SignedArea(polygon)) < 0.0005f)
            return false;

        if (SignedArea(polygon) < 0f)
            polygon.Reverse();

        int frontFirst = verts.Count;
        int count = polygon.Count;
        Vector3 frontOff = n * (normalOffset + Mathf.Max(0.002f, surfaceProtrusion));
        Vector3 backOff = n * -Mathf.Max(0f, embedDepth);
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 p = polygon[i];
            Vector3 world = originW + axisU * p.x + axisV * p.y + frontOff;
            verts.Add(worldToRootLocal.MultiplyPoint3x4(world));
            uvs.Add(new Vector2(
                width > 1e-6f ? Mathf.InverseLerp(centerU - hu, centerU + hu, p.x) : 0f,
                height > 1e-6f ? 1f - Mathf.InverseLerp(centerV - hv, centerV + hv, p.y) : 0f));
            cols.Add(tint);
        }

        int backFirst = verts.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 p = polygon[i];
            Vector3 world = originW + axisU * p.x + axisV * p.y + backOff;
            verts.Add(worldToRootLocal.MultiplyPoint3x4(world));
            uvs.Add(new Vector2(
                width > 1e-6f ? Mathf.InverseLerp(centerU - hu, centerU + hu, p.x) : 0f,
                height > 1e-6f ? 1f - Mathf.InverseLerp(centerV - hv, centerV + hv, p.y) : 0f));
            cols.Add(tint);
        }

        for (int i = 1; i + 1 < count; i++)
        {
            tris.Add(frontFirst);
            tris.Add(frontFirst + i);
            tris.Add(frontFirst + i + 1);
        }

        for (int i = 1; i + 1 < count; i++)
        {
            tris.Add(backFirst);
            tris.Add(backFirst + i + 1);
            tris.Add(backFirst + i);
        }

        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            int f0 = frontFirst + i;
            int f1 = frontFirst + j;
            int b0 = backFirst + i;
            int b1 = backFirst + j;

            tris.Add(f0); tris.Add(b0); tris.Add(b1);
            tris.Add(f0); tris.Add(b1); tris.Add(f1);
        }

        return true;
    }

    static void ClipPolygonAgainstEdge(List<Vector2> polygon, Vector2 edgeA, Vector2 edgeB, Vector2 insideReference)
    {
        if (polygon.Count == 0)
            return;

        float orientation = SignedArea(edgeA, edgeB, insideReference);
        if (Mathf.Abs(orientation) < 1e-8f)
            orientation = 1f;

        bool Inside(Vector2 p)
        {
            float s = SignedArea(edgeA, edgeB, p);
            return orientation >= 0f ? s >= -1e-5f : s <= 1e-5f;
        }

        Vector2 Intersect(Vector2 from, Vector2 to)
        {
            Vector2 dir = to - from;
            Vector2 edge = edgeB - edgeA;
            float denom = Cross(edge, dir);
            if (Mathf.Abs(denom) < 1e-8f)
                return from;
            float t = Cross(edge, edgeA - from) / denom;
            return from + dir * Mathf.Clamp01(t);
        }

        var input = new List<Vector2>(polygon);
        polygon.Clear();

        Vector2 previous = input[input.Count - 1];
        bool previousInside = Inside(previous);
        for (int i = 0; i < input.Count; i++)
        {
            Vector2 current = input[i];
            bool currentInside = Inside(current);

            if (currentInside)
            {
                if (!previousInside)
                    polygon.Add(Intersect(previous, current));
                polygon.Add(current);
            }
            else if (previousInside)
            {
                polygon.Add(Intersect(previous, current));
            }

            previous = current;
            previousInside = currentInside;
        }
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static float SignedArea(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - a);

    static float SignedArea(List<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area * 0.5f;
    }
}
