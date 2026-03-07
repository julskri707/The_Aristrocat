using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class WallObject : MonoBehaviour, IControlPointProvider, IControlPointPathProvider
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

    [Header("Debug")]
    public bool logWarnings = false;
    public bool drawGizmos = false;

    private readonly List<Vector3> _points = new List<Vector3>();

    private MeshFilter _mf;
    private MeshRenderer _mr;
    private MeshCollider _mc;
    private Mesh _mesh;

    public IReadOnlyList<Vector3> Points => _points;

    // =========================
    // IControlPointProvider
    // =========================
    public int ControlPointCount
    {
        get
        {
            if (_points == null) return 0;
            if (_points.Count < 2) return 0;

            // Si loop fermée et dernier point == premier, on ne veut pas 2 handles au même endroit
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

        // Si closedLoop et dernier point du stockage est un duplicate du premier -> on garde la cohérence
        if (closedLoop && _points.Count >= 2)
        {
            if (Vector3.Distance(_points[0], _points[_points.Count - 1]) < 0.001f)
                _points[_points.Count - 1] = _points[0];
        }

        RebuildMesh();
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

        // Preview = points dans l’ordre (pas de X)
        var list = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
            list.Add(_points[i]);

        // Si c'est une boucle fermée, on referme la ligne (LineRenderer.loop peut aussi le faire)
        // Ici on ajoute le premier point à la fin pour une preview "continue".
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

        _mesh = new Mesh();
        _mesh.name = "WallMesh";
        _mf.sharedMesh = _mesh;

        ApplyMaterial();
    }

    // ---------------------------------------------
    // Public API
    // ---------------------------------------------
    public void SetPath(List<Vector3> points)
    {
        _points.Clear();
        if (points != null) _points.AddRange(points);

        // auto-detect closed loop if last is almost first
        if (_points.Count >= 3 && Vector3.Distance(_points[0], _points[_points.Count - 1]) < 0.001f)
            closedLoop = true;

        RebuildMesh();
    }

    public void SetPoint(int index, Vector3 worldPos)
    {
        if (index < 0 || index >= _points.Count) return;
        _points[index] = worldPos;
        RebuildMesh();
    }

    public void SetHeight(float newHeight)
    {
        height = Mathf.Max(0.1f, newHeight);
        RebuildMesh();
    }

    public void SetThickness(float newThickness)
    {
        thickness = Mathf.Max(0.01f, newThickness);
        RebuildMesh();
    }

    // ---------------------------------------------
    // Core generation
    // ---------------------------------------------
    private void RebuildMesh()
    {
        if (_mesh == null) return;

        _mesh.Clear();

        if (_points.Count < 2)
        {
            SyncCollider();
            return;
        }

        // If closed loop, we assume last point duplicates first
        int count = closedLoop ? _points.Count - 1 : _points.Count;
        if (count < 2)
        {
            SyncCollider();
            return;
        }

        int segCount = closedLoop ? count : (count - 1);
        if (segCount < 1)
        {
            SyncCollider();
            return;
        }

        // Decide "outside" for closed loops (CW/CCW in XZ plane)
        float outsideSign = 1f;
        if (closedLoop)
        {
            bool isCCW = ComputeIsCCW_XZ(_points, count);
            // Convention: for CCW loop, outside is RIGHT of segment direction
            outsideSign = isCCW ? 1f : -1f;
        }

        float halfT = thickness * 0.5f;

        // Precompute cumulative distance for UV continuity
        float[] dist = new float[count];
        dist[0] = 0f;
        for (int i = 1; i < count; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(_points[i - 1], _points[i]);

        // Compute per-point mitered offsets (no cracks)
        var outB = new Vector3[count];
        var inB  = new Vector3[count];
        var outT = new Vector3[count];
        var inT  = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 p = _points[i];

            Vector3 dirPrev = GetDirPrev(i, count);
            Vector3 dirNext = GetDirNext(i, count);

            if (!closedLoop)
            {
                if (i == 0) dirPrev = dirNext;
                if (i == count - 1) dirNext = dirPrev;
            }

            // bisector for miter
            Vector3 bis = dirPrev + dirNext;
            bis.y = 0f;
            if (bis.sqrMagnitude < 0.000001f)
                bis = dirNext;

            bis.Normalize();

            Vector3 rightBis  = Vector3.Cross(Vector3.up, bis).normalized;
            Vector3 rightNext = Vector3.Cross(Vector3.up, dirNext).normalized;

            float denom = Mathf.Abs(Vector3.Dot(rightBis, rightNext));
            float miterLen = (denom < 0.2f) ? halfT : (halfT / denom);
            miterLen = Mathf.Min(miterLen, halfT * miterLimit);

            Vector3 outsideOffset = rightBis * (miterLen * outsideSign);
            Vector3 insideOffset  = -outsideOffset;

            outB[i] = p + outsideOffset;
            inB[i]  = p + insideOffset;
            outT[i] = outB[i] + Vector3.up * height;
            inT[i]  = inB[i]  + Vector3.up * height;
        }

        var verts = new List<Vector3>(segCount * 24 + 16);
        var uvs   = new List<Vector2>(segCount * 24 + 16);
        var tris  = new List<int>(segCount * 36 + 24);

        float uAcross = thickness / Mathf.Max(0.01f, uvMetersPerU);

        for (int i = 0; i < segCount; i++)
        {
            int n = (i + 1) % count;

            float v0 = dist[i] / Mathf.Max(0.01f, uvMetersPerV);
            float v1 = dist[n] / Mathf.Max(0.01f, uvMetersPerV);

            // OUTER face
            AddQuad(verts, uvs, tris,
                outB[i], outT[i], outT[n], outB[n],
                0f, v0, uAcross, v1);

            // INNER face (reverse)
            AddQuad(verts, uvs, tris,
                inB[n], inT[n], inT[i], inB[i],
                0f, v1, uAcross, v0);

            // TOP face
            AddQuad(verts, uvs, tris,
                outT[i], inT[i], inT[n], outT[n],
                0f, v0, uAcross, v1);

            // BOTTOM face (optional)
            if (addBottom)
            {
                AddQuad(verts, uvs, tris,
                    outB[n], inB[n], inB[i], outB[i],
                    0f, v1, uAcross, v0);
            }
        }

        // Caps for open walls
        if (addCaps && !closedLoop && count >= 2)
        {
            AddQuad(verts, uvs, tris,
                inB[0], inT[0], outT[0], outB[0],
                0f, 0f, 1f, 1f);

            int last = count - 1;
            AddQuad(verts, uvs, tris,
                outB[last], outT[last], inT[last], inB[last],
                0f, 0f, 1f, 1f);
        }

        if (doubleSided)
            AddBackfaces(tris);

        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);

        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mesh.RecalculateTangents();

        SyncCollider();
        ApplyMaterial();

        if (logWarnings && closedLoop && count >= 3)
        {
            float areaAbs = Mathf.Abs(ComputeSignedAreaXZ(_points, count));
            if (areaAbs < 0.001f)
                Debug.LogWarning("[WallObject] Closed loop area is near zero (loop might be degenerate/self-intersecting).");
        }
    }

    private void SyncCollider()
    {
        if (_mc == null) _mc = GetComponent<MeshCollider>();
        if (_mc == null) return;

        _mc.sharedMesh = null;
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

    private Vector3 GetDirPrev(int i, int count)
    {
        int prev = (i - 1 + count) % count;
        Vector3 d = _points[i] - _points[prev];
        d.y = 0f;
        if (d.sqrMagnitude < 0.000001f) d = Vector3.forward;
        return d.normalized;
    }

    private Vector3 GetDirNext(int i, int count)
    {
        int next = (i + 1) % count;
        Vector3 d = _points[next] - _points[i];
        d.y = 0f;
        if (d.sqrMagnitude < 0.000001f) d = Vector3.forward;
        return d.normalized;
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
        uvs.Add(new Vector2(u0, v0 + 1f));
        uvs.Add(new Vector2(u1, v1 + 1f));
        uvs.Add(new Vector2(u1, v1));

        tris.Add(start + 0);
        tris.Add(start + 1);
        tris.Add(start + 2);

        tris.Add(start + 0);
        tris.Add(start + 2);
        tris.Add(start + 3);
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
