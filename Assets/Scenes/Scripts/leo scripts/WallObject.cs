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

    [Tooltip("Below this dot threshold, corners use bevel fallback instead of long miters.")]
    [Range(0.05f, 0.95f)] public float sharpCornerThreshold = 0.35f;

    [Tooltip("Cuts closed-loop corner tips on centerline before extrusion (meters).")]
    [Min(0f)] public float closedLoopCornerBevel = 0.035f;

    [Header("Debug")]
    public bool logWarnings = false;
    public bool drawGizmos = false;

    private readonly List<Vector3> _points = new List<Vector3>();

    private MeshFilter _mf;
    private MeshRenderer _mr;
    private MeshCollider _mc;
    private Mesh _mesh;
    private WallCladdingGenerator _claddingGenerator;

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
        if (isClosed && closedLoopCornerBevel > 0.0001f)
            points = ApplyClosedLoopCornerBevel(points, closedLoopCornerBevel);
        int count = points.Count;
        int segCount = isClosed ? count : count - 1;
        if (segCount < 1)
        {
            SyncCollider();
            return;
        }

        float outsideSign = 1f;
        Vector3 loopCentroid = Vector3.zero;
        if (isClosed)
        {
            for (int i = 0; i < count; i++)
                loopCentroid += points[i];
            loopCentroid /= Mathf.Max(1, count);
        }

        float halfT = thickness * 0.5f;
        var segDir = new Vector3[segCount];
        var segRight = new Vector3[segCount];
        var segLen = new float[segCount];

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
                // Choose the normal that truly points outward from loop centroid.
                Vector3 mid = (points[i] + points[n]) * 0.5f;
                Vector3 toOutsideR = (mid + right * 0.05f) - loopCentroid;
                Vector3 toOutsideL = (mid - right * 0.05f) - loopCentroid;
                segRight[i] = toOutsideR.sqrMagnitude >= toOutsideL.sqrMagnitude ? right : -right;
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

                Vector3 outsideOff = strictTriangleJoin
                    ? ComputeTriangleJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, 1f)
                    : ComputeJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, 1f, localMiterLimit);
                Vector3 insideOff = strictTriangleJoin
                    ? ComputeTriangleJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, -1f)
                    : ComputeJoinOffset(p, segDir[prev], segRight[prev], segDir[next], segRight[next], halfT, -1f, localMiterLimit);

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

            AddQuadTwoSided(verts, uvs, tris,
                outB[i], outT[i], outT[n], outB[n],
                0f, v0, uHeight, v1,
                expectedOuterNormal);

            AddQuadTwoSided(verts, uvs, tris,
                inB[n], inT[n], inT[i], inB[i],
                0f, v1, uHeight, v0,
                -expectedOuterNormal);

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
            AddQuadTwoSided(verts, uvs, tris,
                inB[0], inT[0], outT[0], outB[0],
                0f, 0f, 1f, 1f,
                -startDir);

            Vector3 endDir = segDir[segCount - 1];
            AddQuadTwoSided(verts, uvs, tris,
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
        _mesh.RecalculateTangents();

        SyncCollider();
        ApplyMaterial();

        if (logWarnings && isClosed && count >= 3)
        {
            float areaAbs = Mathf.Abs(ComputeSignedAreaXZ(points, count));
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

    private void AddQuadTwoSided(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        float u0, float v0, float u1, float v1,
        Vector3 expectedNormal)
    {
        AddQuadOriented(verts, uvs, tris, a, b, c, d, u0, v0, u1, v1, expectedNormal);
        AddQuadOriented(verts, uvs, tris, a, b, c, d, u0, v0, u1, v1, -expectedNormal);
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