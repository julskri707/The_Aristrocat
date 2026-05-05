using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class WallObject : MonoBehaviour, IControlPointProvider, IControlPointPathProvider, IControlPointWallShapeBinding
{
    [Header("Wall Settings")]
    [Min(0.1f)] public float height = 2.5f;
    [Min(0.01f)] public float thickness = 0.25f;

    [Header("Build Options")]
    [Tooltip("If true, the last point is assumed to be the first point (closed loop).")]
    public bool closedLoop;

    [Tooltip("Add caps at start/end for open walls.")]
    public bool addCaps = true;

    [Tooltip("Generate bottom face (usually not needed).")]
    public bool addBottom = false;

    [Tooltip("If enabled, duplicates triangles reversed (debug / special shaders).")]
    public bool doubleSided = false;

    [Header("Rendering")]
    [Tooltip("If set, WallObject will force-apply this material to MeshRenderer.")]
    public Material wallMaterial;

    [Header("UV")]
    [Tooltip("How many meters correspond to 1 unit in V direction (along the wall). Smaller = more tiling.")]
    [Min(0.01f)] public float uvMetersPerV = 2.0f;

    [Tooltip("How many meters correspond to 1 unit in U direction (across thickness).")]
    [Min(0.01f)] public float uvMetersPerU = 0.5f;

    [Header("Corner Handling")]
    [Tooltip("Max miter length multiplier to avoid extreme spikes on sharp angles.")]
    [Range(1f, 10f)] public float miterLimit = 3.0f;

    [Tooltip("Below this dot threshold, corners use bevel fallback instead of long miters.")]
    [Range(0.05f, 0.95f)] public float sharpCornerThreshold = 0.35f;

    [Tooltip("Chamfrein sur le centreline (hors sommets H/V en angle droit) si la boucle n'est pas 100% Manhattan. Les coins stricte-axes (carré + arc) ne sont pas coupés. Ignoré sur les boucles entièrement H/V (L, rectangle).")]
    [Min(0f)] public float closedLoopCornerBevel = 0.035f;

    [Header("Mesh optimization")]
    [Tooltip("Boucle fermée : rééchantillonage si beaucoup de sommets. Sauté si le contour est entièrement H/V, ou s'il comporte un coin H/V 90° (ex. carré fusionné à un cercle) pour ne pas lisser les droits du carré.")]
    [Range(0, 256)] public int maxClosedLoopMeshVertices = 56;
    [Tooltip("Skip tangent generation on rebuild. Keep OFF if your wall material does not use normal maps.")]
    public bool recalculateTangents = false;

    [Header("Debug")]
    public bool logWarnings = false;
    public bool drawGizmos = false;

    private readonly List<Vector3> _points = new List<Vector3>();

    private MeshFilter _mf;
    private MeshRenderer _mr;
    private MeshCollider _mc;
    private Mesh _mesh;
    private WallCladdingGenerator _claddingGenerator;
    private static float s_NextClosedLoopAreaWarningUnscaledTime = -999f;
    readonly List<WallOpeningEntry> _scratchOpeningsForSegment = new List<WallOpeningEntry>();

    public IReadOnlyList<Vector3> Points => _points;

    public bool ControlPointsBelongToWallShape => true;

    // =========================
    // IControlPointProvider
    // =========================
    public int ControlPointCount
    {
        get
        {
            if (_points == null) return 0;
            if (_points.Count < 2) return 0;

            if (closedLoop && _points.Count >= 3 && Vector3.Distance(_points[0], _points[_points.Count - 1]) < 0.001f)
                return _points.Count - 1;

            return _points.Count;
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        int count = ControlPointCount;
        if (count <= 0) return Vector3.zero;
        index = Mathf.Clamp(index, 0, count - 1);
        return _points[index];
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        int count = ControlPointCount;
        if (count <= 0) return;
        if (index < 0 || index >= count) return;

        _points[index] = worldPos;

        if (closedLoop && _points.Count >= 2)
        {
            if (Vector3.Distance(_points[0], _points[_points.Count - 1]) < 0.001f)
                _points[_points.Count - 1] = _points[0];
        }

        RebuildMesh();
        MarkCladdingDirty();
    }

    /// <summary>
    /// Reconstruit le mesh (et marque le bardage) après ouvertures runtime (<see cref="WallOpeningRegistry"/>).
    /// </summary>
    public void ForceRebuildMesh()
    {
        RebuildMesh();
        MarkCladdingDirty();
    }

    public bool IsControlPointEditable(int index)
    {
        return index >= 0 && index < ControlPointCount;
    }

    // =========================
    // IControlPointPathProvider
    // =========================
    public List<Vector3> GetPreviewPathWorld()
    {
        int count = ControlPointCount;
        if (count < 2) return null;

        var list = new List<Vector3>(count + 1);

        for (int i = 0; i < count; i++)
            list.Add(_points[i]);

        if (closedLoop)
            list.Add(_points[0]);

        return list;
    }

    // ---------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------
    private void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mc = GetComponent<MeshCollider>();
        _claddingGenerator = GetComponent<WallCladdingGenerator>();

        _mesh = new Mesh();
        _mesh.name = "WallMesh";
        _mesh.MarkDynamic();
        _mf.sharedMesh = _mesh;

        ApplyMaterial();
    }

    private void OnDestroy()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }
    }

    // ---------------------------------------------
    // Public API
    // ---------------------------------------------
    public void SetPath(List<Vector3> points)
    {
        _points.Clear();
        if (points != null) _points.AddRange(points);

        // Keep loop state consistent with the actual path payload.
        // This prevents stale "closedLoop=true" on open paths, which can remove end caps.
        closedLoop =
            _points.Count >= 3 &&
            Vector3.Distance(_points[0], _points[_points.Count - 1]) < 0.001f;

        RebuildMesh();
        MarkCladdingDirty();
    }

    public void SetPoint(int index, Vector3 worldPos)
    {
        if (index < 0 || index >= _points.Count) return;
        _points[index] = worldPos;
        RebuildMesh();
        MarkCladdingDirty();
    }

    public void SetHeight(float newHeight)
    {
        height = Mathf.Max(0.1f, newHeight);
        RebuildMesh();
        MarkCladdingDirty();
    }

    public void SetThickness(float newThickness)
    {
        thickness = Mathf.Max(0.01f, newThickness);
        RebuildMesh();
        MarkCladdingDirty();
    }

    void MarkCladdingDirty()
    {
        if (_claddingGenerator == null)
            _claddingGenerator = GetComponent<WallCladdingGenerator>();

        if (_claddingGenerator != null)
            _claddingGenerator.MarkDirty();
    }

    // ---------------------------------------------
    // Core generation
    // ---------------------------------------------
    private void RebuildMesh()
    {
        if (_mesh == null) return;

        _mesh.Clear();

        List<Vector3> points = BuildRenderablePoints(_points, closedLoop);
        bool isClosed = closedLoop && points.Count >= 3;
        if (isClosed && closedLoopCornerBevel > 0.0001f && !IsClosedLoopOrthogonalAxisAlignedXZ(points))
            points = ApplyClosedLoopCornerBevel(points, closedLoopCornerBevel);

        if (isClosed && maxClosedLoopMeshVertices > 0 && points.Count > maxClosedLoopMeshVertices &&
            !IsClosedLoopOrthogonalAxisAlignedXZ(points) &&
            !ClosedLoopHasAnyAxisAlignedHvNinetyCorner(points))
            points = ResampleClosedLoopEvenly(points, maxClosedLoopMeshVertices);

        int count = points.Count;
        int segCount = isClosed ? count : count - 1;
        if (segCount < 1)
        {
            SyncCollider();
            return;
        }

        float outsideSign = 1f;

        float halfT = thickness * 0.5f;
        var segDir = new Vector3[segCount];
        var segRight = new Vector3[segCount];
        var segLen = new float[segCount];

        bool loopIsCCW = isClosed && count >= 3 && ComputeIsCCW_XZ(points, count);

        for (int i = 0; i < segCount; i++)
        {
            int n = i + 1;
            if (isClosed) n %= count;
            Vector3 d = points[n] - points[i];
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.0001f)
                d = (i > 0 ? segDir[i - 1] : Vector3.forward);
            else
                d /= len;
            segDir[i] = d;
            segLen[i] = Mathf.Max(len, 0.0001f);
            Vector3 right = Vector3.Cross(Vector3.up, d).normalized;
            if (isClosed)
            {
                // Extérieur du bâtiment = à droite du sens de parcours si la boucle est CCW (sinon inverse).
                // L’ancien test au centroïde se trompait sur les L (angles rentrants) → mitres / pics.
                segRight[i] = loopIsCCW ? right : -right;
            }
            else
            {
                segRight[i] = right * outsideSign;
            }
        }

        float[] dist = new float[count];
        dist[0] = 0f;
        for (int i = 1; i < count; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(points[i - 1], points[i]);

        var outB = new Vector3[count];
        var inB = new Vector3[count];
        var outT = new Vector3[count];
        var inT = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 p = points[i];

            if (!isClosed && i == 0)
            {
                Vector3 off = segRight[0] * halfT;
                outB[i] = p + off;
                inB[i] = p - off;
            }
            else if (!isClosed && i == count - 1)
            {
                Vector3 off = segRight[segCount - 1] * halfT;
                outB[i] = p + off;
                inB[i] = p - off;
            }
            else
            {
                int prev = isClosed ? (i - 1 + segCount) % segCount : i - 1;
                int next = isClosed ? i % segCount : i;
                bool strictTriangleJoin = isClosed && count == 3;
                float localMiterLimit = strictTriangleJoin ? Mathf.Max(miterLimit, 100f) : miterLimit;

                Vector3 outsideOff;
                Vector3 insideOff;
                if (strictTriangleJoin)
                {
                    outsideOff = ComputeTriangleJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, 1f);
                    insideOff = ComputeTriangleJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, -1f);
                }
                else if (isClosed && count >= 3 && IsReflexCornerXZ(segDir[prev], segDir[next], loopIsCCW))
                {
                    // Angle rentrant (L, U…) : le mitre extérieur diverge → pic ; biseau stable.
                    outsideOff = ComputeBevelOffset(segRight[prev], segRight[next], halfT, 1f);
                    insideOff = ComputeBevelOffset(segRight[prev], segRight[next], halfT, -1f);
                }
                else
                {
                    outsideOff = ComputeJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, 1f, localMiterLimit);
                    insideOff = ComputeJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, -1f, localMiterLimit);
                }

                outB[i] = p + outsideOff;
                inB[i] = p + insideOff;
            }

            outT[i] = outB[i] + Vector3.up * height;
            inT[i] = inB[i] + Vector3.up * height;
        }

        var verts = new List<Vector3>(segCount * 24 + 16);
        var uvs = new List<Vector2>(segCount * 24 + 16);
        var tris = new List<int>(segCount * 36 + 24);

        float uHeight = height / Mathf.Max(0.01f, uvMetersPerU);
        float uThickness = thickness / Mathf.Max(0.01f, uvMetersPerU);

        for (int i = 0; i < segCount; i++)
        {
            int n = isClosed ? (i + 1) % count : i + 1;
            float v0 = dist[i] / Mathf.Max(0.01f, uvMetersPerV);
            float v1 = dist[n] / Mathf.Max(0.01f, uvMetersPerV);
            if (isClosed && n == 0)
                v1 = (dist[count - 1] + Vector3.Distance(points[count - 1], points[0])) / Mathf.Max(0.01f, uvMetersPerV);

            Vector3 expectedOuterNormal = outB[i] - inB[i];
            expectedOuterNormal.y = 0f;
            if (expectedOuterNormal.sqrMagnitude < 0.000001f)
                expectedOuterNormal = segRight[i];
            expectedOuterNormal.Normalize();

            WallOpeningRegistry openingRegistry = GetComponent<WallOpeningRegistry>();
            if (openingRegistry != null)
            {
                openingRegistry.GetOpeningsForSegment(i, _scratchOpeningsForSegment);
                WallOpeningMeshBuilder.AppendSegmentWallFacesWithHoles(
                    verts, uvs, tris,
                    outB[i], outT[i], outT[n], outB[n],
                    inB[i], inT[i], inT[n], inB[n],
                    v0, v1, uHeight, expectedOuterNormal,
                    _scratchOpeningsForSegment.Count > 0 ? _scratchOpeningsForSegment : null);
            }
            else
            {
                // Une seule orientation par face : épaisseur déjà modélisée par out* vs in* (AddQuadTwoSided doublait les tris coplanaires).
                AddQuadOriented(verts, uvs, tris,
                    outB[i], outT[i], outT[n], outB[n],
                    0f, v0, uHeight, v1,
                    expectedOuterNormal);

                AddQuadOriented(verts, uvs, tris,
                    inB[n], inT[n], inT[i], inB[i],
                    0f, v1, uHeight, v0,
                    -expectedOuterNormal);
            }

            AddQuadOriented(verts, uvs, tris,
                outT[i], inT[i], inT[n], outT[n],
                0f, v0, uThickness, v1,
                Vector3.up);

            if (addBottom)
            {
                AddQuadOriented(verts, uvs, tris,
                    outB[n], inB[n], inB[i], outB[i],
                    0f, v1, uThickness, v0,
                    Vector3.down);
            }
        }

        if (addCaps && !isClosed)
        {
            Vector3 startDir = segDir[0];
            AddQuadOriented(verts, uvs, tris,
                inB[0], inT[0], outT[0], outB[0],
                0f, 0f, 1f, 1f,
                -startDir);

            Vector3 endDir = segDir[segCount - 1];
            AddQuadOriented(verts, uvs, tris,
                outB[count - 1], outT[count - 1], inT[count - 1], inB[count - 1],
                0f, 0f, 1f, 1f,
                endDir);
        }

        if (doubleSided)
            AddBackfaces(tris);

        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        if (recalculateTangents)
            _mesh.RecalculateTangents();

        SyncCollider();
        ApplyMaterial();

        if (logWarnings && isClosed && count >= 3 && !ControlPointHandleUI.IsDraggingAnyHandle)
        {
            float areaAbs = Mathf.Abs(ComputeSignedAreaXZ(points, count));
            if (areaAbs < 0.001f && Time.unscaledTime >= s_NextClosedLoopAreaWarningUnscaledTime)
            {
                s_NextClosedLoopAreaWarningUnscaledTime = Time.unscaledTime + 1.25f;
                Debug.LogWarning("[WallObject] Closed loop area is near zero (loop might be degenerate/self-intersecting).");
            }
        }
    }

    private void SyncCollider()
    {
        if (_mc == null) _mc = GetComponent<MeshCollider>();
        if (_mc == null) return;

        // Unity logs an error if a MeshCollider uses a mesh with 0 vertices.
        if (_mesh == null || _mesh.vertexCount == 0)
        {
            _mc.sharedMesh = null;
            return;
        }

        _mc.sharedMesh = _mesh;
    }

    // ---------------------------------------------
    // Helpers: orientation & directions
    // ---------------------------------------------
    private static bool ComputeIsCCW_XZ(List<Vector3> pts, int count)
    {
        return ComputeSignedAreaXZ(pts, count) > 0f;
    }

    private static float ComputeSignedAreaXZ(List<Vector3> pts, int count)
    {
        float area = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % count];
            area += (a.x * b.z - b.x * a.z);
        }
        return area;
    }

    static float Cross2XZ(Vector3 a, Vector3 b)
    {
        return a.x * b.z - a.z * b.x;
    }

    /// <summary>
    /// Sommet rentrant d’un polygone simple (angle intérieur &gt; 180°) en XZ, selon le sens CCW/CW.
    /// </summary>
    /// <summary>
    /// Coin rentrant en XZ (angle intérieur du polygone &gt; 180°, ex. ~270° sur contour orthogonal).
    /// dirPrev / dirNext : directions des arêtes entrante et sortante (même convention que le maillage).
    /// </summary>
    public static bool IsReflexCornerXZ(Vector3 dirPrev, Vector3 dirNext, bool loopIsCCW)
    {
        const float eps = 1e-5f;
        float t = Cross2XZ(dirPrev, dirNext);
        if (Mathf.Abs(t) <= eps)
            return false;
        return loopIsCCW ? t < -eps : t > eps;
    }

    /// <summary>
    /// Tous les côtés alignés axes (tolérance) : pas de chamfrein automatique sur le centreline.
    /// Public pour aligner maillage / cladding (éviter un centreline rééchantillonné vs polyline d’édition).
    /// </summary>
    public static bool IsClosedLoopOrthogonalAxisAlignedXZ(List<Vector3> pts)
    {
        int n = pts.Count;
        if (n < 3)
            return false;

        const float minLen = 1e-4f;
        const float maxDiagFrac = 0.02f;

        for (int i = 0; i < n; i++)
        {
            Vector3 d = pts[(i + 1) % n] - pts[i];
            d.y = 0f;
            float len = d.magnitude;
            if (len < minLen)
                continue;

            float ax = Mathf.Abs(d.x);
            float az = Mathf.Abs(d.z);
            if (ax < minLen && az < minLen)
                return false;
            if (ax >= minLen && az >= minLen && az / Mathf.Max(ax, 1e-6f) > maxDiagFrac && ax / Mathf.Max(az, 1e-6f) > maxDiagFrac)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Sommet dont les deux arêtes incidentes sont l’une parallèle à X, l’autre à Z (angle droit, saillant ou rentrant L).
    /// Sert à ne pas appliquer le chamfrein maillage sur ce coin lorsque le contour global mélange arc et facette rectiligne.
    /// </summary>
    static bool IsVertexAxisAlignedHvNinetyDegreeCornerXZ(List<Vector3> pts, int i, int count)
    {
        const float minLen = 1e-4f;
        const float axisFrac = 0.02f;
        Vector3 p = pts[i];
        Vector3 prev = pts[(i - 1 + count) % count];
        Vector3 next = pts[(i + 1) % count];
        Vector3 toPrev = p - prev;
        Vector3 toNext = next - p;
        toPrev.y = 0f;
        toNext.y = 0f;
        float lenP = toPrev.magnitude;
        float lenN = toNext.magnitude;
        if (lenP < minLen || lenN < minLen)
            return false;

        float axP = Mathf.Abs(toPrev.x) / lenP;
        float azP = Mathf.Abs(toPrev.z) / lenP;
        float axN = Mathf.Abs(toNext.x) / lenN;
        float azN = Mathf.Abs(toNext.z) / lenN;

        bool prevH = axP > 0.01f && azP < axisFrac;
        bool prevV = azP > 0.01f && axP < axisFrac;
        bool nextH = axN > 0.01f && azN < axisFrac;
        bool nextV = azN > 0.01f && axN < axisFrac;

        return (prevH && nextV) || (prevV && nextH);
    }

    static bool ClosedLoopHasAnyAxisAlignedHvNinetyCorner(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 3)
            return false;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            if (IsVertexAxisAlignedHvNinetyDegreeCornerXZ(pts, i, n))
                return true;
        }
        return false;
    }

    private static Vector3 GetDirPrev(List<Vector3> points, int i, int count, bool closed)
    {
        const float eps = 0.000001f;

        if (closed)
        {
            for (int step = 1; step < count; step++)
            {
                int prev = (i - step + count) % count;
                Vector3 d = points[i] - points[prev];
                d.y = 0f;
                if (d.sqrMagnitude >= eps)
                    return d.normalized;
            }
        }

        for (int prev = i - 1; prev >= 0; prev--)
        {
            Vector3 d = points[i] - points[prev];
            d.y = 0f;
            if (d.sqrMagnitude >= eps)
                return d.normalized;
        }

        for (int next = i + 1; next < count; next++)
        {
            Vector3 d = points[next] - points[i];
            d.y = 0f;
            if (d.sqrMagnitude >= eps)
                return d.normalized;
        }

        return Vector3.forward;
    }

    private static Vector3 GetDirNext(List<Vector3> points, int i, int count, bool closed)
    {
        const float eps = 0.000001f;

        if (closed)
        {
            for (int step = 1; step < count; step++)
            {
                int next = (i + step) % count;
                Vector3 d = points[next] - points[i];
                d.y = 0f;
                if (d.sqrMagnitude >= eps)
                    return d.normalized;
            }
        }

        for (int next = i + 1; next < count; next++)
        {
            Vector3 d = points[next] - points[i];
            d.y = 0f;
            if (d.sqrMagnitude >= eps)
                return d.normalized;
        }

        for (int prev = i - 1; prev >= 0; prev--)
        {
            Vector3 d = points[i] - points[prev];
            d.y = 0f;
            if (d.sqrMagnitude >= eps)
                return d.normalized;
        }

        return Vector3.forward;
    }

    /// <summary>
    /// Reduces vertex count on a closed polygon (XZ) while keeping roughly the same outline — lowers triangle count on circles / dense fits.
    /// Ring must be one vertex per corner (no duplicate closing point). Also used by cladding to cap path complexity.
    /// </summary>
    public static List<Vector3> ResampleClosedLoopEvenly(List<Vector3> ring, int targetVerts)
    {
        if (ring == null || ring.Count < 3 || targetVerts < 8)
            return ring;

        targetVerts = Mathf.Clamp(targetVerts, 8, 256);
        if (ring.Count <= targetVerts)
            return ring;

        int n = ring.Count;
        var edgeLen = new float[n];
        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float d = Vector3.Distance(ring[i], ring[j]);
            edgeLen[i] = d;
            total += d;
        }

        if (total < 0.000001f)
            return ring;

        var result = new List<Vector3>(targetVerts);
        for (int k = 0; k < targetVerts; k++)
        {
            float distAlong = (k / (float)targetVerts) * total;
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                float next = acc + edgeLen[i];
                if (distAlong <= next + 0.000001f || i == n - 1)
                {
                    float segT = edgeLen[i] > 0.000001f ? Mathf.Clamp01((distAlong - acc) / edgeLen[i]) : 0f;
                    int b = (i + 1) % n;
                    result.Add(Vector3.Lerp(ring[i], ring[b], segT));
                    break;
                }

                acc = next;
            }
        }

        return result;
    }

    /// <summary>
    /// Same XZ polyline cleaning as <see cref="RebuildMesh"/> (merge consecutive near-duplicate vertices, drop redundant closing point).
    /// Cladding and any system that walks edges should use this so segment counts match the extruded wall mesh.
    /// </summary>
    public static List<Vector3> GetRenderablePolylineXZ(List<Vector3> source, bool closed)
    {
        return BuildRenderablePoints(source, closed);
    }

    private static List<Vector3> BuildRenderablePoints(List<Vector3> source, bool closed)
    {
        var cleaned = new List<Vector3>();
        if (source == null || source.Count == 0)
            return cleaned;

        const float minPointSpacing = 0.001f;
        float minPointSpacingSqr = minPointSpacing * minPointSpacing;

        for (int i = 0; i < source.Count; i++)
        {
            Vector3 p = source[i];
            if (cleaned.Count == 0 || (p - cleaned[cleaned.Count - 1]).sqrMagnitude > minPointSpacingSqr)
                cleaned.Add(p);
        }

        if (closed && cleaned.Count >= 2 && (cleaned[0] - cleaned[cleaned.Count - 1]).sqrMagnitude <= minPointSpacingSqr)
            cleaned.RemoveAt(cleaned.Count - 1);

        return cleaned;
    }

    private static List<Vector3> ApplyClosedLoopCornerBevel(List<Vector3> pts, float bevelDistance)
    {
        var result = new List<Vector3>(pts.Count * 2);
        int count = pts.Count;
        if (count < 3)
            return new List<Vector3>(pts);

        for (int i = 0; i < count; i++)
        {
            Vector3 p = pts[i];
            Vector3 prev = pts[(i - 1 + count) % count];
            Vector3 next = pts[(i + 1) % count];

            Vector3 toPrev = p - prev;
            Vector3 toNext = next - p;
            toPrev.y = 0f;
            toNext.y = 0f;

            float lenPrev = toPrev.magnitude;
            float lenNext = toNext.magnitude;
            if (lenPrev < 0.0001f || lenNext < 0.0001f)
            {
                result.Add(p);
                continue;
            }

            if (IsVertexAxisAlignedHvNinetyDegreeCornerXZ(pts, i, count))
            {
                result.Add(p);
                continue;
            }

            Vector3 dirPrev = toPrev / lenPrev;
            Vector3 dirNext = toNext / lenNext;
            float cut = Mathf.Min(bevelDistance, lenPrev * 0.35f, lenNext * 0.35f);
            if (cut < 0.0001f)
            {
                result.Add(p);
                continue;
            }

            result.Add(p - dirPrev * cut);
            result.Add(p + dirNext * cut);
        }

        return result;
    }

    private Vector3 ComputeJoinOffset(
        Vector3 pivot,
        Vector3 dirPrev,
        Vector3 rightPrev,
        Vector3 dirNext,
        Vector3 rightNext,
        float halfThickness,
        float sideSign,
        float localMiterLimit)
    {
        Vector3 n1 = rightPrev * sideSign;
        Vector3 n2 = rightNext * sideSign;
        n1.y = 0f;
        n2.y = 0f;

        if (n1.sqrMagnitude < 0.000001f) n1 = n2;
        if (n2.sqrMagnitude < 0.000001f) n2 = n1;
        if (n1.sqrMagnitude < 0.000001f)
            return Vector3.zero;

        n1.Normalize();
        n2.Normalize();

        Vector3 bis = n1 + n2;
        bis.y = 0f;
        if (bis.sqrMagnitude < 0.000001f)
            return n2 * halfThickness;
        bis.Normalize();

        float denom = Mathf.Abs(Vector3.Dot(bis, n2));
        float minTurn = Mathf.Clamp01(sharpCornerThreshold) * 0.25f;
        float turnAmount = Vector3.Cross(dirPrev, dirNext).magnitude;
        if (denom < 0.0001f || turnAmount < minTurn)
            return n2 * halfThickness;

        float miterLen = halfThickness / denom;
        float maxMiter = halfThickness * Mathf.Max(1f, localMiterLimit);
        if (miterLen > maxMiter)
            miterLen = maxMiter;

        return bis * miterLen;
    }

    private Vector3 ComputeTriangleJoinOffset(
        Vector3 pivot,
        Vector3 dirPrev,
        Vector3 rightPrev,
        Vector3 dirNext,
        Vector3 rightNext,
        float halfThickness,
        float sideSign)
    {
        Vector3 p1 = pivot + rightPrev * (halfThickness * sideSign);
        Vector3 p2 = pivot + rightNext * (halfThickness * sideSign);
        if (TryLineIntersectionXZ(p1, dirPrev, p2, dirNext, out Vector3 hit))
        {
            Vector3 miter = hit - pivot;
            miter.y = 0f;
            if (miter.sqrMagnitude > 0.000001f)
                return miter;
        }

        return ComputeBevelOffset(rightPrev, rightNext, halfThickness, sideSign);
    }

    private static Vector3 ComputeBevelOffset(Vector3 rightPrev, Vector3 rightNext, float halfThickness, float sideSign)
    {
        Vector3 avg = rightPrev + rightNext;
        avg.y = 0f;
        if (avg.sqrMagnitude < 0.000001f)
            avg = rightNext.sqrMagnitude > 0.000001f ? rightNext : rightPrev;
        avg.Normalize();
        return avg * (halfThickness * sideSign);
    }

    private static bool TryLineIntersectionXZ(Vector3 p1, Vector3 d1, Vector3 p2, Vector3 d2, out Vector3 intersection)
    {
        intersection = p1;
        Vector2 a1 = new Vector2(p1.x, p1.z);
        Vector2 v1 = new Vector2(d1.x, d1.z);
        Vector2 a2 = new Vector2(p2.x, p2.z);
        Vector2 v2 = new Vector2(d2.x, d2.z);

        float denom = Cross2(v1, v2);
        if (Mathf.Abs(denom) < 0.00001f)
            return false;

        Vector2 delta = a2 - a1;
        float t = Cross2(delta, v2) / denom;
        Vector2 hit = a1 + v1 * t;
        intersection = new Vector3(hit.x, p1.y, hit.y);
        return true;
    }

    private static float Cross2(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    // ---------------------------------------------
    // Helpers: quad building
    // ---------------------------------------------
    private void AddQuad(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        float u0, float v0, float u1, float v1)
    {
        int start = verts.Count;

        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u0, v1));

        tris.Add(start + 0);
        tris.Add(start + 1);
        tris.Add(start + 2);

        tris.Add(start + 0);
        tris.Add(start + 2);
        tris.Add(start + 3);
    }

    private void AddQuadOriented(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        float u0, float v0, float u1, float v1,
        Vector3 expectedNormal)
    {
        int start = verts.Count;

        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u0, v1));

        Vector3 triNormal = Vector3.Cross(b - a, c - a);
        bool sameDirection =
            expectedNormal.sqrMagnitude < 0.000001f ||
            triNormal.sqrMagnitude < 0.000001f ||
            Vector3.Dot(triNormal, expectedNormal) >= 0f;

        if (sameDirection)
        {
            tris.Add(start + 0);
            tris.Add(start + 1);
            tris.Add(start + 2);

            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }
        else
        {
            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);

            tris.Add(start + 0);
            tris.Add(start + 3);
            tris.Add(start + 2);
        }
    }

    private void AddBackfaces(List<int> tris)
    {
        int original = tris.Count;
        for (int i = 0; i < original; i += 3)
        {
            int a = tris[i];
            int b = tris[i + 1];
            int c = tris[i + 2];
            tris.Add(c);
            tris.Add(b);
            tris.Add(a);
        }
    }

    // ---------------------------------------------
    // Rendering
    // ---------------------------------------------
    private void ApplyMaterial()
    {
        if (_mr != null && wallMaterial != null)
            _mr.sharedMaterial = wallMaterial;
    }

    // ---------------------------------------------
    // Debug gizmos
    // ---------------------------------------------
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (_points == null || _points.Count < 2) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < _points.Count - 1; i++)
            Gizmos.DrawLine(_points[i], _points[i + 1]);
    }
}