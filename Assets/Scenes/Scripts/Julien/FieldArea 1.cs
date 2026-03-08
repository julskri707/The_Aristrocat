// FieldArea.cs
// Unity 2022+
//
// FULL FIELD SCRIPT (all functions):
// - Stores original polygon points (worldPoints) for logic (ContainsWorldPoint / GetAreaWorldXZ)
// - Fill: internal quad-grid aligned on X/Z (tileWorldSize), boundary cells clipped & triangulated
// - Boundary clipping: Sutherland–Hodgman clip of FIELD polygon against CELL rect (convex clip) -> triangulate
// - Triangulation: PolygonTriangulator.Triangulate(Vector2[])
// - Terrain conform: per-vertex RaycastAll down, accept first hit with GroundMarker in parent (no LayerMasks)
// - UVs: per-cell 0..1 mapping (stable tiles), then uvScale/uvOffset applied on top
// - Z-fighting elimination: heightOffset + per-instance material renderQueue (fillRenderQueue)
// - Outline: Subdivide -> Chaikin smoothing -> merge close points -> optional terrain conform -> offsets
// - Outline "Pro" LineRenderer settings: corners, caps, alignment, textureMode, sortingOrder, width
// - Selection highlight: MaterialPropertyBlock tint/emission; fallback selectedMaterialOverride if shader lacks properties
// - Selection trigger: thin BoxCollider trigger above ground
// - MeshCollider safety: only assign if meshCollider is on SAME GameObject
//
// Dependencies (existing in your project):
// - GroundMarker : MonoBehaviour
// - PolygonTriangulator : static class with int[] Triangulate(Vector2[] polygon)
//
// NO LayerMasks.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Transform))]
public class FieldArea : MonoBehaviour
{
    [Header("Data (original click points)")]
    public List<Vector3> worldPoints = new List<Vector3>();

    [Header("Outline")]
    public LineRenderer outline;

    [Header("Fill Mesh")]
    public MeshFilter meshFilter;
    public MeshRenderer meshRenderer;

    [Header("Selection / Click Trigger")]
    public BoxCollider selectionBoxTrigger;

    [Header("Optional MeshCollider (must be on same GameObject)")]
    public MeshCollider meshCollider;

    [Header("Materials")]
    public List<Material> fillMaterials = new List<Material>();
    public Material fallbackMaterial;

    [Tooltip("Fallback if shader has no _BaseColor/_Color and MPB highlight is not possible.")]
    public Material selectedMaterialOverride;

    [Header("Z-Fighting / Rendering")]
    [Tooltip("Lift the fill mesh this much above the ground in WORLD space to avoid z-fighting.")]
    public float heightOffset = 0.03f;

    [Tooltip("Outline is lifted above the fill by this amount (WORLD space).")]
    public float outlineExtraOffset = 0.01f;

    [Tooltip("Force Fill renderQueue to this value (URP Lit usually works). 2451 = Geometry+1.")]
    public int fillRenderQueue = 2451;

    [Header("UV / Tiling")]
    [Tooltip("Applied on top of per-cell 0..1 UVs: uv = uv*uvScale + uvOffset.")]
    public float uvScale = 1f;
    public Vector2 uvOffset = Vector2.zero;

    [Tooltip("Kept for compatibility. In Quad-Grid mode UVs are per-cell 0..1; lockUVToWorld is ignored.")]
    public bool lockUVToWorld = true;

    [Header("Quad Grid Fill")]
    public float tileWorldSize = 1f;
    public int maxGridCells = 5000;

    [Header("Smooth Subdivision (Outline input)")]
    public bool subdivideEdges = true;
    public float maxEdgeLength = 0.5f;
    public int maxSubdivisionsPerEdge = 64;

    [Header("Outline Smoothing (Chaikin)")]
    public bool smoothOutline = true;
    [Range(0, 4)] public int outlineSmoothIterations = 2;
    [Tooltip("Prevents point explosion by merging points closer than this in XZ.")]
    public float outlineMinSegmentLength = 0.05f;

    [Header("Outline Pro Settings")]
    public int outlineCornerVerts = 6;
    public int outlineCapVerts = 6;

    public enum OutlineAlignmentMode
    {
        View,
        TransformZ
    }

    public OutlineAlignmentMode outlineAlignmentMode = OutlineAlignmentMode.View;
    public float outlineWidth = 0.08f;

    [Header("Terrain Conform (per vertex)")]
    public bool conformToTerrain = true;
    public float conformRayHeight = 50f;

    [Range(0f, 1f)]
    public float terrainFollowStrength = 1f;

    [Tooltip("Simple clamp: prevents extreme steep steps by limiting slope across triangle edges.")]
    public float maxSlopeAngle = 35f;

    [Header("Selection Highlight (MPB)")]
    [Range(0f, 1f)] public float highlightTintStrength = 0.25f;
    [Range(0f, 5f)] public float emissionBoost = 1.2f;
    public Color highlightTargetColor = new Color(1.0f, 0.92f, 0.45f, 1f);

    [Header("Debug")]
    public bool debugLogs = false;

    public event Action<FieldArea> OnClicked;

    private Mesh _mesh;
    private bool _isSelected;

    private MaterialPropertyBlock _mpb;
    private Material[] _originalRendererMaterials;

    // Per-instance material cache for renderQueue control (avoid touching sharedMaterial globally)
    private Material[] _instanceQueueMats;
    private bool _usingOverrideMaterialFallback;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // -------------------------
    // Unity lifecycle
    // -------------------------

    private void Awake()
    {
        EnsureOutlineConfigured();
    }

    private void OnEnable()
    {
        EnsureOutlineConfigured();
    }

    private void OnValidate()
    {
        outlineCornerVerts = Mathf.Clamp(outlineCornerVerts, 0, 32);
        outlineCapVerts = Mathf.Clamp(outlineCapVerts, 0, 32);
        outlineSmoothIterations = Mathf.Clamp(outlineSmoothIterations, 0, 4);
        outlineMinSegmentLength = Mathf.Max(0.001f, outlineMinSegmentLength);
        outlineWidth = Mathf.Max(0.0001f, outlineWidth);

        maxSubdivisionsPerEdge = Mathf.Clamp(maxSubdivisionsPerEdge, 0, 512);
        maxEdgeLength = Mathf.Max(0.001f, maxEdgeLength);

        tileWorldSize = Mathf.Max(0.01f, tileWorldSize);
        maxGridCells = Mathf.Max(1, maxGridCells);

        conformRayHeight = Mathf.Max(1f, conformRayHeight);
        maxSlopeAngle = Mathf.Max(0f, maxSlopeAngle);

        EnsureOutlineConfigured();
    }

    private void OnDestroy()
    {
        CleanupInstanceMaterials();
    }

    private void EnsureOutlineConfigured()
    {
        if (outline == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[FieldArea] '{name}' Outline config skipped: LineRenderer 'outline' is null.");
            return;
        }

        outline.useWorldSpace = true;
        outline.loop = true;

        outline.numCornerVertices = outlineCornerVerts;
        outline.numCapVertices = outlineCapVerts;

        outline.alignment = (outlineAlignmentMode == OutlineAlignmentMode.TransformZ)
            ? LineAlignment.TransformZ
            : LineAlignment.View;

        outline.textureMode = LineTextureMode.Stretch;

        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;

        outline.sortingOrder = 10;
        outline.widthMultiplier = outlineWidth;
    }

    // -------------------------
    // Public API
    // -------------------------

    public void SetPoints(List<Vector3> pointsWorld)
    {
        if (pointsWorld == null)
        {
            Debug.LogWarning($"[FieldArea] SetPoints called with null on '{name}'.");
            worldPoints.Clear();
            Rebuild();
            return;
        }

        worldPoints = new List<Vector3>(pointsWorld);
        Rebuild();
    }

    public void Rebuild()
    {
        EnsureOutlineConfigured();

        if (worldPoints == null || worldPoints.Count < 3)
        {
            Debug.LogWarning($"[FieldArea] Rebuild skipped on '{name}' (need >= 3 points, have {(worldPoints == null ? 0 : worldPoints.Count)}).");
            ClearVisuals();
            SetupSelectionTrigger();
            return;
        }

        RemoveNearDuplicatePoints(worldPoints, 0.001f);
        if (worldPoints.Count < 3)
        {
            Debug.LogWarning($"[FieldArea] Rebuild skipped on '{name}' after duplicate cleanup (need >= 3 points, have {worldPoints.Count}).");
            ClearVisuals();
            SetupSelectionTrigger();
            return;
        }

        EnsureOriginalMaterialsCached();
        ApplyBaseMaterials();
        TryApplyFillRenderQueue();

        if (lockUVToWorld && debugLogs)
            Debug.LogWarning($"[FieldArea] '{name}' Quad-Grid UVs are per-cell 0..1. lockUVToWorld is ignored in this mode.");

        // Fill
        if (!BuildFillMesh_QuadGrid(worldPoints))
        {
            ClearVisuals();
            SetupSelectionTrigger();
            return;
        }

        // Outline
        BuildOutline_Smooth(worldPoints);

        SetupSelectionTrigger();
        ApplyMeshColliderIfValid();

        // Ensure selection tint state persists after rebuild
        SetSelected(_isSelected);
    }

    // -------------------------
    // Fill: Quad Grid + Clipped boundary cells
    // -------------------------

    private bool BuildFillMesh_QuadGrid(List<Vector3> polyWorld)
    {
        if (meshFilter == null)
        {
            Debug.LogWarning($"[FieldArea] Missing MeshFilter 'meshFilter' on '{name}'. Fill mesh will not be built.");
            return false;
        }

        if (meshRenderer == null)
        {
            Debug.LogWarning($"[FieldArea] Missing MeshRenderer 'meshRenderer' on '{name}'. Fill mesh may not be visible.");
        }

        if (polyWorld == null || polyWorld.Count < 3)
        {
            Debug.LogWarning($"[FieldArea] '{name}' Quad-Grid build skipped (polygon has < 3 points).");
            return false;
        }

        float tile = tileWorldSize;
        if (tile <= 0f)
        {
            Debug.LogWarning($"[FieldArea] '{name}' tileWorldSize <= 0. Set tileWorldSize > 0.");
            return false;
        }

        int cellCap = Mathf.Max(1, maxGridCells);

        // Polygon in XZ (world)
        List<Vector2> polyXZ = new List<Vector2>(polyWorld.Count);
        for (int i = 0; i < polyWorld.Count; i++)
            polyXZ.Add(new Vector2(polyWorld[i].x, polyWorld[i].z));

        // AABB in XZ
        GetWorldXZBounds(polyWorld, out Vector2 min, out Vector2 max);

        float width = Mathf.Max(0.0001f, max.x - min.x);
        float height = Mathf.Max(0.0001f, max.y - min.y);

        int cols = Mathf.CeilToInt(width / tile);
        int rows = Mathf.CeilToInt(height / tile);
        int totalCells = cols * rows;

        if (totalCells > cellCap)
            Debug.LogWarning($"[FieldArea] '{name}' Grid cells would be {totalCells}, clamped to maxGridCells={cellCap}. Increase tileWorldSize or maxGridCells.");

        var vertsWorld = new List<Vector3>(Mathf.Min(cellCap, totalCells) * 6);
        var uvs = new List<Vector2>(Mathf.Min(cellCap, totalCells) * 6);
        var tris = new List<int>(Mathf.Min(cellCap, totalCells) * 12);

        float flatY = AverageY(polyWorld);

        int usedCells = 0;
        int clippedCells = 0;

        for (int r = 0; r < rows; r++)
        {
            float z0 = min.y + r * tile;
            float z1 = z0 + tile;

            for (int c = 0; c < cols; c++)
            {
                if (usedCells >= cellCap)
                    goto DONE_GRID;

                float x0 = min.x + c * tile;
                float x1 = x0 + tile;

                // quick inclusion checks
                Vector3 center = new Vector3((x0 + x1) * 0.5f, flatY, (z0 + z1) * 0.5f);
                bool centerInside = ContainsWorldPoint(center);

                if (!centerInside)
                {
                    // if no corner inside, skip (perf)
                    bool anyCornerInside =
                        ContainsWorldPoint(new Vector3(x0, flatY, z0)) ||
                        ContainsWorldPoint(new Vector3(x1, flatY, z0)) ||
                        ContainsWorldPoint(new Vector3(x1, flatY, z1)) ||
                        ContainsWorldPoint(new Vector3(x0, flatY, z1));

                    if (!anyCornerInside)
                        continue;
                }

                bool fullInside =
                    ContainsWorldPoint(new Vector3(x0, flatY, z0)) &&
                    ContainsWorldPoint(new Vector3(x1, flatY, z0)) &&
                    ContainsWorldPoint(new Vector3(x1, flatY, z1)) &&
                    ContainsWorldPoint(new Vector3(x0, flatY, z1));

                if (fullInside)
                {
                    AddFullQuad(x0, x1, z0, z1, flatY, vertsWorld, uvs, tris);
                    usedCells++;
                    continue;
                }

                // boundary cell: clip field polygon to cell rect and triangulate
                Rect rect = Rect.MinMaxRect(x0, z0, x1, z1);
                List<Vector2> clipped = ClipPolygonToRect(polyXZ, rect);
                if (clipped.Count < 3)
                    continue;

                int baseIndex = vertsWorld.Count;
                for (int i = 0; i < clipped.Count; i++)
                {
                    Vector2 q = clipped[i];
                    vertsWorld.Add(new Vector3(q.x, flatY, q.y));
                    uvs.Add(CellUV(q, rect));
                }

                // triangulate in LOCAL XZ
                Vector2[] localPoly = new Vector2[clipped.Count];
                for (int i = 0; i < clipped.Count; i++)
                {
                    Vector3 local = transform.InverseTransformPoint(new Vector3(clipped[i].x, flatY, clipped[i].y));
                    localPoly[i] = new Vector2(local.x, local.z);
                }

                int[] triArr = PolygonTriangulator.Triangulate(localPoly);
                if (triArr == null || triArr.Length < 3)
                {
                    if (debugLogs)
                        Debug.LogWarning($"[FieldArea] '{name}' Triangulation failed for clipped cell polygon (cell {c},{r}). Skipping cell.");
                    continue;
                }

                for (int i = 0; i < triArr.Length; i += 3)
                {
                    tris.Add(baseIndex + triArr[i]);
                    tris.Add(baseIndex + triArr[i + 1]);
                    tris.Add(baseIndex + triArr[i + 2]);
                }

                usedCells++;
                clippedCells++;
            }
        }

    DONE_GRID:

        if (vertsWorld.Count < 3 || tris.Count < 3)
        {
            Debug.LogWarning($"[FieldArea] '{name}' Quad-Grid produced no geometry (0 verts/tris). Check tileWorldSize or polygon validity.");
            return false;
        }

        // apply uvScale/uvOffset on top of per-cell 0..1 UVs
        float s = (Mathf.Abs(uvScale) < 1e-6f) ? 1f : uvScale;
        for (int i = 0; i < uvs.Count; i++)
            uvs[i] = (uvs[i] * s) + uvOffset;

        // terrain conform (Y only)
        int hitsOk = 0;
        if (conformToTerrain && terrainFollowStrength > 0f)
            ConformVerticesToTerrain(vertsWorld, flatY, ref hitsOk);

        // optional slope clamp
        if (conformToTerrain && maxSlopeAngle > 0f)
        {
            bool clamped = ApplySlopeClampOnMesh(vertsWorld, tris, flatY, Mathf.Max(1f, maxSlopeAngle), Mathf.Clamp01(terrainFollowStrength));
            if (clamped && debugLogs)
                Debug.LogWarning($"[FieldArea] '{name}' SlopeClamp applied (maxSlopeAngle={maxSlopeAngle:0.##}).");
        }

        // height offset
        float lift = Mathf.Max(0f, heightOffset);
        if (lift > 0f)
        {
            for (int i = 0; i < vertsWorld.Count; i++)
            {
                Vector3 p = vertsWorld[i];
                p.y += lift;
                vertsWorld[i] = p;
            }
        }

        // build mesh
        if (_mesh == null)
            _mesh = new Mesh { name = $"{name}_FieldAreaMesh" };
        else
            _mesh.Clear();

        int n = vertsWorld.Count;
        Vector3[] vertsLocal = new Vector3[n];
        for (int i = 0; i < n; i++)
            vertsLocal[i] = transform.InverseTransformPoint(vertsWorld[i]);

        _mesh.vertices = vertsLocal;
        _mesh.triangles = tris.ToArray();
        _mesh.uv = uvs.ToArray();

        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        if (IsMeshFacingDown(_mesh))
        {
            int[] triFix = _mesh.triangles;
            FlipTrianglesInPlace(triFix);
            _mesh.triangles = triFix;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        meshFilter.sharedMesh = _mesh;

        if (debugLogs)
            Debug.Log($"[FieldArea] '{name}' Quad-Grid: usedCells={usedCells}/{Mathf.Min(totalCells, cellCap)}, clippedCells={clippedCells}, verts={vertsWorld.Count}, trisIdx={tris.Count}, terrainHits={hitsOk}.");

        return true;
    }

    private static void AddFullQuad(float x0, float x1, float z0, float z1, float y,
        List<Vector3> vertsWorld, List<Vector2> uvs, List<int> tris)
    {
        int baseIndex = vertsWorld.Count;

        // p00, p10, p11, p01
        vertsWorld.Add(new Vector3(x0, y, z0));
        vertsWorld.Add(new Vector3(x1, y, z0));
        vertsWorld.Add(new Vector3(x1, y, z1));
        vertsWorld.Add(new Vector3(x0, y, z1));

        // per-cell 0..1
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        // triangles
        tris.Add(baseIndex + 0);
        tris.Add(baseIndex + 1);
        tris.Add(baseIndex + 2);

        tris.Add(baseIndex + 0);
        tris.Add(baseIndex + 2);
        tris.Add(baseIndex + 3);
    }

    private static Vector2 CellUV(Vector2 worldXZ, Rect cellRect)
    {
        float u = (worldXZ.x - cellRect.xMin) / Mathf.Max(0.0001f, cellRect.width);
        float v = (worldXZ.y - cellRect.yMin) / Mathf.Max(0.0001f, cellRect.height);
        return new Vector2(u, v);
    }

    // -------------------------
    // Outline: Subdivide -> Chaikin -> Merge -> TerrainConform -> Offset -> LineRenderer
    // -------------------------

    private void BuildOutline_Smooth(List<Vector3> basePoly)
    {
        if (outline == null)
        {
            Debug.LogWarning($"[FieldArea] Missing LineRenderer 'outline' on '{name}'. Outline will not be drawn.");
            return;
        }

        if (basePoly == null || basePoly.Count < 3)
        {
            outline.positionCount = 0;
            return;
        }

        // 1) subdivide input edges
        List<Vector3> pts = basePoly;
        if (subdivideEdges)
        {
            if (maxEdgeLength <= 0f)
            {
                Debug.LogWarning($"[FieldArea] '{name}' maxEdgeLength <= 0 -> outline subdivision disabled.");
            }
            else
            {
                pts = GetSubdividedWorldPoints(basePoly, maxEdgeLength, maxSubdivisionsPerEdge);
            }
        }

        int beforeSmooth = pts.Count;

        // 2) chaikin smoothing in XZ
        if (smoothOutline && outlineSmoothIterations > 0)
        {
            pts = SmoothOutlineChaikinXZ(pts, outlineSmoothIterations, outlineMinSegmentLength);
            if (debugLogs)
                Debug.Log($"[FieldArea] '{name}' Outline smoothing: {beforeSmooth} -> {pts.Count} points (iters={outlineSmoothIterations}, minSeg={outlineMinSegmentLength:0.###}).");
        }

        // 3) optional terrain conform for outline points (Y)
        float flatY = AverageY(worldPoints);
        float strength = Mathf.Clamp01(terrainFollowStrength);
        bool doConform = conformToTerrain && strength > 0f;

        float liftFill = Mathf.Max(0f, heightOffset);
        float liftOutline = Mathf.Max(0f, outlineExtraOffset);

        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            float y = flatY;

            if (doConform)
            {
                float rayUp = Mathf.Max(1f, conformRayHeight);
                if (TrySampleGroundY(p.x, p.z, flatY + rayUp, rayUp * 2f, out float hitY))
                    y = Mathf.Lerp(flatY, hitY, strength);
                else if (debugLogs)
                    Debug.LogWarning($"[FieldArea] '{name}' Outline conform: no GroundMarker under point #{i} XZ({p.x:0.##},{p.z:0.##}). Using flatY.");
            }

            // always above fill to avoid flimmern
            p.y = y + liftFill + liftOutline;
            pts[i] = p;
        }

        // 4) assign to LineRenderer (loop already true in EnsureOutlineConfigured)
        outline.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++)
            outline.SetPosition(i, pts[i]);
    }

    private List<Vector3> SmoothOutlineChaikinXZ(List<Vector3> input, int iterations, float minSegLen)
    {
        if (input == null || input.Count < 3 || iterations <= 0)
            return input;

        float minSqr = Mathf.Max(0.001f, minSegLen) * Mathf.Max(0.001f, minSegLen);
        List<Vector3> pts = new List<Vector3>(input);

        for (int it = 0; it < iterations; it++)
        {
            var next = new List<Vector3>(pts.Count * 2);
            int n = pts.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = pts[i];
                Vector3 p1 = pts[(i + 1) % n];

                Vector3 q = Vector3.Lerp(p0, p1, 0.25f);
                Vector3 r = Vector3.Lerp(p0, p1, 0.75f);

                q.y = 0f;
                r.y = 0f;

                next.Add(q);
                next.Add(r);
            }

            // merge/skip close points (XZ)
            var merged = new List<Vector3>(next.Count);
            for (int i = 0; i < next.Count; i++)
            {
                Vector3 p = next[i];
                if (merged.Count == 0)
                {
                    merged.Add(p);
                    continue;
                }

                Vector3 prev = merged[merged.Count - 1];
                float dx = p.x - prev.x;
                float dz = p.z - prev.z;
                if ((dx * dx + dz * dz) >= minSqr)
                    merged.Add(p);
            }

            // ensure last not too close to first
            if (merged.Count >= 3)
            {
                Vector3 first = merged[0];
                Vector3 last = merged[merged.Count - 1];
                float dx = first.x - last.x;
                float dz = first.z - last.z;
                if ((dx * dx + dz * dz) < minSqr)
                    merged.RemoveAt(merged.Count - 1);
            }

            pts = merged;
            if (pts.Count < 3)
                break;
        }

        return pts;
    }

    // -------------------------
    // Clipping: Sutherland–Hodgman (clip polygon against convex rect)
    // -------------------------

    private static List<Vector2> ClipPolygonToRect(List<Vector2> subject, Rect rect)
    {
        List<Vector2> output = new List<Vector2>(subject);

        output = ClipAgainstEdge(output, p => p.x >= rect.xMin, (a, b) => IntersectVertical(a, b, rect.xMin));
        output = ClipAgainstEdge(output, p => p.x <= rect.xMax, (a, b) => IntersectVertical(a, b, rect.xMax));
        output = ClipAgainstEdge(output, p => p.y >= rect.yMin, (a, b) => IntersectHorizontal(a, b, rect.yMin));
        output = ClipAgainstEdge(output, p => p.y <= rect.yMax, (a, b) => IntersectHorizontal(a, b, rect.yMax));

        RemoveNearDuplicatePoints2D(output, 1e-6f);
        return output;
    }

    private static List<Vector2> ClipAgainstEdge(List<Vector2> input, Func<Vector2, bool> inside, Func<Vector2, Vector2, Vector2> intersect)
    {
        List<Vector2> output = new List<Vector2>();
        if (input == null || input.Count == 0) return output;

        Vector2 S = input[input.Count - 1];
        bool S_in = inside(S);

        for (int i = 0; i < input.Count; i++)
        {
            Vector2 E = input[i];
            bool E_in = inside(E);

            if (E_in)
            {
                if (!S_in) output.Add(intersect(S, E));
                output.Add(E);
            }
            else
            {
                if (S_in) output.Add(intersect(S, E));
            }

            S = E;
            S_in = E_in;
        }

        return output;
    }

    private static Vector2 IntersectVertical(Vector2 a, Vector2 b, float xEdge)
    {
        float dx = b.x - a.x;
        if (Mathf.Abs(dx) < 1e-8f) return new Vector2(xEdge, a.y);
        float t = (xEdge - a.x) / dx;
        return new Vector2(xEdge, a.y + (b.y - a.y) * t);
    }

    private static Vector2 IntersectHorizontal(Vector2 a, Vector2 b, float yEdge)
    {
        float dy = b.y - a.y;
        if (Mathf.Abs(dy) < 1e-8f) return new Vector2(a.x, yEdge);
        float t = (yEdge - a.y) / dy;
        return new Vector2(a.x + (b.x - a.x) * t, yEdge);
    }

    private static void RemoveNearDuplicatePoints2D(List<Vector2> pts, float eps)
    {
        if (pts == null || pts.Count < 2) return;
        float epsSqr = eps * eps;

        for (int i = pts.Count - 1; i > 0; i--)
            if ((pts[i] - pts[i - 1]).sqrMagnitude <= epsSqr)
                pts.RemoveAt(i);

        if (pts.Count >= 2 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude <= epsSqr)
            pts.RemoveAt(pts.Count - 1);
    }

    // -------------------------
    // Terrain conform + slope clamp (no LayerMasks)
    // -------------------------

    private void ConformVerticesToTerrain(List<Vector3> vertsWorld, float flatY, ref int hitCount)
    {
        float strength = Mathf.Clamp01(terrainFollowStrength);
        float rayUp = Mathf.Max(1f, conformRayHeight);

        for (int i = 0; i < vertsWorld.Count; i++)
        {
            Vector3 p = vertsWorld[i];

            if (TrySampleGroundY(p.x, p.z, flatY + rayUp, rayUp * 2f, out float hitY))
            {
                hitCount++;
                p.y = Mathf.Lerp(flatY, hitY, strength);
            }
            else if (debugLogs)
            {
                Debug.LogWarning($"[FieldArea] '{name}' Conform: no GroundMarker under vertex #{i} XZ({p.x:0.##},{p.z:0.##}). Keeping Y={p.y:0.###}.");
            }

            vertsWorld[i] = p;
        }
    }

    private bool TrySampleGroundY(float x, float z, float originY, float dist, out float y)
    {
        y = originY;

        Vector3 origin = new Vector3(x, originY, z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            if (h.collider.GetComponentInParent<GroundMarker>() == null) continue;

            y = h.point.y;
            return true;
        }

        return false;
    }

    private bool ApplySlopeClampOnMesh(List<Vector3> vertsWorld, List<int> tris, float flatY, float maxAngleDeg, float strength)
    {
        if (vertsWorld == null || vertsWorld.Count < 2 || tris == null || tris.Count < 3)
            return false;

        float maxTan = Mathf.Tan(maxAngleDeg * Mathf.Deg2Rad);
        bool any = false;

        for (int i = 0; i < tris.Count; i += 3)
        {
            any |= ClampEdge(tris[i], tris[i + 1]);
            any |= ClampEdge(tris[i + 1], tris[i + 2]);
            any |= ClampEdge(tris[i + 2], tris[i]);
        }

        return any;

        bool ClampEdge(int ia, int ib)
        {
            Vector3 a = vertsWorld[ia];
            Vector3 b = vertsWorld[ib];

            float horiz = DistXZ(a, b);
            if (horiz <= 1e-4f) return false;

            float dy = b.y - a.y;
            float angle = Mathf.Atan2(Mathf.Abs(dy), horiz) * Mathf.Rad2Deg;

            if (angle <= maxAngleDeg)
                return false;

            float maxDeltaY = maxTan * horiz;
            float clampedDy = Mathf.Clamp(dy, -maxDeltaY, maxDeltaY);

            b.y = a.y + clampedDy;

            float flatten = 1f - strength;
            b.y = Mathf.Lerp(b.y, flatY, flatten * 0.75f);

            vertsWorld[ib] = b;
            return true;
        }
    }

    private static float DistXZ(Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // -------------------------
    // Outline: Edge subdivision helper
    // -------------------------

    public List<Vector3> GetSubdividedWorldPoints(List<Vector3> basePoints, float maxLen, int maxInsertsPerEdge)
    {
        var result = new List<Vector3>();
        if (basePoints == null || basePoints.Count < 3)
            return result;

        if (maxLen <= 0f)
        {
            Debug.LogWarning($"[FieldArea] GetSubdividedWorldPoints called with maxLen <= 0 on '{name}'. Returning base points.");
            result.AddRange(basePoints);
            return result;
        }

        int n = basePoints.Count;
        result.Capacity = Mathf.Max(n, n * 2);

        for (int i = 0; i < n; i++)
        {
            Vector3 a = basePoints[i];
            Vector3 b = basePoints[(i + 1) % n];

            result.Add(a);

            float dx = b.x - a.x;
            float dz = b.z - a.z;
            float distXZ = Mathf.Sqrt(dx * dx + dz * dz);

            if (distXZ <= maxLen)
                continue;

            int segments = Mathf.CeilToInt(distXZ / maxLen);
            segments = Mathf.Max(1, segments);

            int inserts = Mathf.Clamp(segments - 1, 0, Mathf.Max(0, maxInsertsPerEdge));
            segments = inserts + 1;

            for (int s = 1; s <= inserts; s++)
            {
                float t = (float)s / segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                result.Add(p);
            }
        }

        return result;
    }

    // -------------------------
    // Z-Fighting: per-instance renderQueue (no sharedMaterial global change)
    // -------------------------

    private void TryApplyFillRenderQueue()
    {
        if (meshRenderer == null)
        {
            Debug.LogWarning($"[FieldArea] Cannot apply renderQueue on '{name}': meshRenderer is null.");
            return;
        }

        int targetQueue = fillRenderQueue;
        if (targetQueue <= 0)
        {
            if (debugLogs)
                Debug.LogWarning($"[FieldArea] '{name}' fillRenderQueue <= 0; skipping renderQueue enforcement. Using heightOffset only.");
            return;
        }

        Material[] src = meshRenderer.sharedMaterials;
        if (src == null || src.Length == 0)
        {
            Debug.LogWarning($"[FieldArea] Cannot apply renderQueue on '{name}': MeshRenderer has no materials. Using heightOffset only.");
            return;
        }

        bool needRebuild = (_instanceQueueMats == null) || (_instanceQueueMats.Length != src.Length);
        if (needRebuild)
        {
            CleanupInstanceMaterials();

            _instanceQueueMats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == null)
                {
                    Debug.LogWarning($"[FieldArea] '{name}' material slot {i} is null; cannot set renderQueue for that slot.");
                    _instanceQueueMats[i] = null;
                    continue;
                }

                _instanceQueueMats[i] = new Material(src[i]);
                _instanceQueueMats[i].name = $"{src[i].name} (FieldArea Instance {name})";
            }
        }

        try
        {
            bool anySet = false;
            for (int i = 0; i < _instanceQueueMats.Length; i++)
            {
                var m = _instanceQueueMats[i];
                if (m == null) continue;
                m.renderQueue = targetQueue;
                anySet = true;
            }

            if (!anySet)
            {
                Debug.LogWarning($"[FieldArea] '{name}' renderQueue not applied (no valid materials). Using heightOffset only.");
                return;
            }

            meshRenderer.sharedMaterials = _instanceQueueMats;

            if (debugLogs)
                Debug.Log($"[FieldArea] '{name}' Fill renderQueue enforced to {targetQueue} using per-instance materials.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FieldArea] '{name}' failed to set renderQueue: {e.Message}. Falling back to heightOffset only.");
        }
    }

    private void CleanupInstanceMaterials()
    {
        if (_instanceQueueMats == null) return;

        for (int i = 0; i < _instanceQueueMats.Length; i++)
        {
            if (_instanceQueueMats[i] != null)
            {
                Destroy(_instanceQueueMats[i]);
                _instanceQueueMats[i] = null;
            }
        }

        _instanceQueueMats = null;
    }

    // -------------------------
    // Materials / Highlight
    // -------------------------

    private void EnsureOriginalMaterialsCached()
    {
        if (meshRenderer == null) return;
        if (_originalRendererMaterials != null && _originalRendererMaterials.Length > 0) return;
        _originalRendererMaterials = meshRenderer.sharedMaterials;
    }

    private void RestoreOriginalMaterials()
    {
        if (meshRenderer == null) return;
        if (_originalRendererMaterials == null) return;
        meshRenderer.sharedMaterials = _originalRendererMaterials;
    }

    private void ApplyBaseMaterials()
    {
        if (meshRenderer == null)
        {
            Debug.LogWarning($"[FieldArea] Missing MeshRenderer 'meshRenderer' on '{name}'. Materials cannot be applied.");
            return;
        }

        if (_usingOverrideMaterialFallback)
        {
            RestoreOriginalMaterials();
            _usingOverrideMaterialFallback = false;
        }

        Material[] matsToUse = null;

        bool hasFillList = fillMaterials != null && fillMaterials.Count > 0 && fillMaterials[0] != null;
        if (hasFillList)
            matsToUse = fillMaterials.ToArray();
        else if (fallbackMaterial != null)
            matsToUse = new[] { fallbackMaterial };
        else
            matsToUse = meshRenderer.sharedMaterials;

        if (matsToUse == null || matsToUse.Length == 0)
            Debug.LogWarning($"[FieldArea] '{name}' has no fillMaterials/fallbackMaterial and MeshRenderer has no materials.");

        meshRenderer.sharedMaterials = matsToUse;
        EnsureOriginalMaterialsCached();
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;

        if (meshRenderer == null)
        {
            Debug.LogWarning($"[FieldArea] SetSelected({selected}) but meshRenderer is null on '{name}'.");
            return;
        }

        EnsureOriginalMaterialsCached();

        if (!selected)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.Clear();
            meshRenderer.SetPropertyBlock(_mpb);

            if (_usingOverrideMaterialFallback)
            {
                RestoreOriginalMaterials();
                _usingOverrideMaterialFallback = false;
            }
            return;
        }

        if (TryApplyHighlightViaMPB())
        {
            if (_usingOverrideMaterialFallback)
            {
                RestoreOriginalMaterials();
                _usingOverrideMaterialFallback = false;
            }
            return;
        }

        if (selectedMaterialOverride != null)
        {
            meshRenderer.sharedMaterials = new[] { selectedMaterialOverride };
            _usingOverrideMaterialFallback = true;

            if (debugLogs)
                Debug.LogWarning($"[FieldArea] '{name}' shader had no _BaseColor/_Color for MPB highlight. Using selectedMaterialOverride fallback.");
        }
        else
        {
            Debug.LogWarning($"[FieldArea] '{name}' cannot highlight: shader has no _BaseColor/_Color and selectedMaterialOverride is null.");
        }
    }

    private bool TryApplyHighlightViaMPB()
    {
        if (meshRenderer == null) return false;

        var mats = meshRenderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
        {
            Debug.LogWarning($"[FieldArea] '{name}' cannot highlight: meshRenderer has no materials.");
            return false;
        }

        bool hasBaseProp = false;
        bool hasEmissionProp = false;
        Material refMatForBase = null;
        Material refMatForEmission = null;

        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) continue;

            if (!hasBaseProp && (m.HasProperty(BaseColorId) || m.HasProperty(ColorId)))
            {
                hasBaseProp = true;
                refMatForBase = m;
            }

            if (!hasEmissionProp && m.HasProperty(EmissionColorId))
            {
                hasEmissionProp = true;
                refMatForEmission = m;
            }

            if (hasBaseProp && hasEmissionProp)
                break;
        }

        if (!hasBaseProp)
        {
            if (debugLogs)
                Debug.LogWarning($"[FieldArea] '{name}' highlight MPB failed: shader has neither _BaseColor nor _Color.");
            return false;
        }

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(_mpb);

        Color baseCol = Color.white;
        if (refMatForBase != null)
        {
            if (refMatForBase.HasProperty(BaseColorId))
                baseCol = refMatForBase.GetColor(BaseColorId);
            else if (refMatForBase.HasProperty(ColorId))
                baseCol = refMatForBase.GetColor(ColorId);
        }

        Color tinted = Color.Lerp(baseCol, highlightTargetColor, Mathf.Clamp01(highlightTintStrength));
        _mpb.SetColor(BaseColorId, tinted);
        _mpb.SetColor(ColorId, tinted);

        if (hasEmissionProp && refMatForEmission != null)
        {
            Color em = refMatForEmission.GetColor(EmissionColorId);
            if (em.maxColorComponent <= 0.001f)
                em = highlightTargetColor * 0.25f;

            _mpb.SetColor(EmissionColorId, em * Mathf.Max(0f, emissionBoost));
        }

        meshRenderer.SetPropertyBlock(_mpb);
        return true;
    }

    // -------------------------
    // Selection trigger + mesh collider
    // -------------------------

    private void SetupSelectionTrigger()
    {
        if (selectionBoxTrigger == null)
        {
            Debug.LogWarning($"[FieldArea] Missing BoxCollider 'selectionBoxTrigger' on '{name}'. Click/trigger selection will not work.");
            return;
        }

        if (worldPoints == null || worldPoints.Count < 3)
        {
            selectionBoxTrigger.enabled = false;
            return;
        }

        selectionBoxTrigger.enabled = true;
        selectionBoxTrigger.isTrigger = true;

        GetWorldXZBounds(worldPoints, out var min, out var max);
        float avgY = AverageY(worldPoints);

        Vector3 worldCenter = new Vector3((min.x + max.x) * 0.5f, avgY + 0.02f, (min.y + max.y) * 0.5f);
        Vector3 worldSize = new Vector3(Mathf.Max(0.01f, max.x - min.x), 0.05f, Mathf.Max(0.01f, max.y - min.y));

        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
        Vector3 localSize = transform.InverseTransformVector(worldSize);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

        selectionBoxTrigger.center = localCenter;
        selectionBoxTrigger.size = localSize;
    }

    private void ApplyMeshColliderIfValid()
    {
        if (meshCollider == null) return;

        if (meshCollider.gameObject != gameObject)
        {
            Debug.LogWarning($"[FieldArea] meshCollider reference is NOT on this FieldArea ('{name}'). It is on '{meshCollider.gameObject.name}'. Skipping meshCollider assignment.");
            return;
        }

        if (_mesh == null || _mesh.vertexCount == 0)
        {
            Debug.LogWarning($"[FieldArea] '{name}' meshCollider exists but mesh is empty. Setting sharedMesh=null.");
            meshCollider.sharedMesh = null;
            return;
        }

        meshCollider.sharedMesh = _mesh;
    }

    // -------------------------
    // Visual cleanup
    // -------------------------

    private void ClearVisuals()
    {
        if (outline != null)
            outline.positionCount = 0;

        if (meshFilter != null)
        {
            if (_mesh != null)
                _mesh.Clear();
            meshFilter.sharedMesh = _mesh;
        }

        if (meshCollider != null && meshCollider.gameObject == gameObject)
            meshCollider.sharedMesh = null;
    }

    // -------------------------
    // Polygon logic (original worldPoints)
    // -------------------------

    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        if (worldPoints == null || worldPoints.Count < 3)
            return false;

        float px = worldPoint.x;
        float pz = worldPoint.z;

        bool inside = false;
        int n = worldPoints.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float ix = worldPoints[i].x;
            float iz = worldPoints[i].z;
            float jx = worldPoints[j].x;
            float jz = worldPoints[j].z;

            bool intersect = ((iz > pz) != (jz > pz)) &&
                             (px < (jx - ix) * (pz - iz) / ((jz - iz) == 0f ? 1e-6f : (jz - iz)) + ix);

            if (intersect)
                inside = !inside;
        }

        return inside;
    }

    public float GetAreaWorldXZ()
    {
        if (worldPoints == null || worldPoints.Count < 3)
            return 0f;

        double sum = 0.0;
        int n = worldPoints.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = worldPoints[i].x;
            double zi = worldPoints[i].z;
            double xj = worldPoints[j].x;
            double zj = worldPoints[j].z;
            sum += (xj * zi) - (xi * zj);
        }

        return (float)(Math.Abs(sum) * 0.5);
    }

    // -------------------------
    // Mesh orientation helpers
    // -------------------------

    private static bool IsMeshFacingDown(Mesh m)
    {
        if (m == null) return false;
        var tri = m.triangles;
        var v = m.vertices;
        if (tri == null || tri.Length < 3 || v == null || v.Length < 3) return false;

        Vector3 a = v[tri[0]];
        Vector3 b = v[tri[1]];
        Vector3 c = v[tri[2]];
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;

        return n.y < 0f;
    }

    private static void FlipTrianglesInPlace(int[] triangles)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int tmp = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = tmp;
        }
    }

    // -------------------------
    // Misc helpers
    // -------------------------

    private static void RemoveNearDuplicatePoints(List<Vector3> pts, float epsilon)
    {
        if (pts == null || pts.Count < 2) return;

        float epsSqr = epsilon * epsilon;

        for (int i = pts.Count - 1; i > 0; i--)
            if ((pts[i] - pts[i - 1]).sqrMagnitude <= epsSqr)
                pts.RemoveAt(i);

        if (pts.Count >= 2 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude <= epsSqr)
            pts.RemoveAt(pts.Count - 1);
    }

    private static void GetWorldXZBounds(List<Vector3> pts, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            min.x = Mathf.Min(min.x, p.x);
            min.y = Mathf.Min(min.y, p.z);
            max.x = Mathf.Max(max.x, p.x);
            max.y = Mathf.Max(max.y, p.z);
        }
    }

    private static float AverageY(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < pts.Count; i++) sum += pts[i].y;
        return sum / pts.Count;
    }

    // -------------------------
    // Click (optional)
    // -------------------------

    private void OnMouseDown()
    {
        OnClicked?.Invoke(this);
        if (debugLogs) Debug.Log($"[FieldArea] Clicked '{name}'.");
    }
}