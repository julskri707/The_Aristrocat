using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parametric roof generated from a closed wall footprint.
/// Controls:
/// - roofHeightMeters: raise/lower roof
/// - roundness: 0 = boxy/flat-ish, 1 = strongly rounded toward center
/// - overhangMeters: roof base extension outside wall footprint
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofSystem : MonoBehaviour
{
    const string RoofChildName = "__HouseRoof";
    const float TriEps = 1e-5f;

    [Header("Shape")]
    [Min(0.05f)] public float roofHeightMeters = 1.2f;
    [Range(0f, 1f)] public float roundness = 0.45f;
    [Min(0f)] public float overhangMeters = 0.15f;
    [Min(0f)] public float yOffsetAboveWallTop = 0f;

    [Header("Runtime")]
    public bool autoRebuild = true;

    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    int _lastHash;

    public static HouseRoofSystem EnsureOnWall(WallObject wall)
    {
        if (wall == null)
            return null;
        HouseRoofSystem roof = wall.GetComponent<HouseRoofSystem>();
        if (roof == null)
            roof = wall.gameObject.AddComponent<HouseRoofSystem>();
        roof.EnsureComponents();
        roof.RebuildNow();
        return roof;
    }

    void Awake() => EnsureComponents();
    void OnEnable() => EnsureComponents();

    void LateUpdate()
    {
        if (!autoRebuild)
            return;
        int h = ComputeHash();
        if (h == _lastHash)
            return;
        RebuildNow();
    }

    public void RebuildNow()
    {
        EnsureComponents();
        if (_mf == null || _mr == null)
            return;

        WallObject wall = GetComponent<WallObject>();
        WallEditShape edit = GetComponent<WallEditShape>();
        if (wall == null || edit == null || !edit.IsClosedLoopPath)
        {
            ClearMesh();
            return;
        }

        List<Vector3> ring = edit.GetPreviewPathWorld();
        if (!TryPrepareClosedRing(ring, out List<Vector3> prepared))
        {
            ClearMesh();
            return;
        }

        float baseY = edit.shapeY + wall.height + yOffsetAboveWallTop;
        int n = prepared.Count;
        var baseRing = new List<Vector3>(n);
        var topRing = new List<Vector3>(n);

        Vector2 centroid = ComputeCentroidXZ(prepared);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = prepared[i];
            Vector2 dir = new Vector2(p.x - centroid.x, p.z - centroid.y);
            if (dir.sqrMagnitude > 1e-8f)
                dir.Normalize();
            Vector3 b = new Vector3(p.x + dir.x * overhangMeters, baseY, p.z + dir.y * overhangMeters);
            Vector3 c = new Vector3(centroid.x, baseY + Mathf.Max(0.05f, roofHeightMeters), centroid.y);
            Vector3 t = Vector3.Lerp(b + Vector3.up * Mathf.Max(0.05f, roofHeightMeters), c, Mathf.Clamp01(roundness));
            baseRing.Add(b);
            topRing.Add(t);
        }

        var verts = new List<Vector3>(n * 2 + 8);
        var uvs = new List<Vector2>(n * 2 + 8);
        var tris = new List<int>(n * 12);

        for (int i = 0; i < n; i++)
        {
            verts.Add(baseRing[i]);
            verts.Add(topRing[i]);
            uvs.Add(new Vector2(baseRing[i].x * 0.2f, baseRing[i].z * 0.2f));
            uvs.Add(new Vector2(topRing[i].x * 0.2f, topRing[i].z * 0.2f));
        }

        // Side shell.
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int b0 = i * 2;
            int t0 = b0 + 1;
            int b1 = j * 2;
            int t1 = b1 + 1;

            tris.Add(b0); tris.Add(t0); tris.Add(t1);
            tris.Add(b0); tris.Add(t1); tris.Add(b1);
        }

        // Top cap from top ring.
        var top2 = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
            top2.Add(new Vector2(topRing[i].x, topRing[i].z));
        if (TryTriangulateEarClip(top2, out List<int> topTri))
        {
            for (int k = 0; k < topTri.Count; k += 3)
            {
                int i0 = topTri[k] * 2 + 1;
                int i1 = topTri[k + 1] * 2 + 1;
                int i2 = topTri[k + 2] * 2 + 1;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mf.sharedMesh = _mesh;
        _mr.enabled = true;

        if (_mf != null)
            UpdateRoofPickCollider(_mf.gameObject);

        _lastHash = ComputeHash();
    }

    void UpdateRoofPickCollider(GameObject roofGo)
    {
        if (roofGo == null)
            return;
        var bc = roofGo.GetComponent<BoxCollider>();
        if (bc == null)
            bc = roofGo.AddComponent<BoxCollider>();
        if (_mesh == null || _mesh.vertexCount == 0)
        {
            bc.enabled = false;
            return;
        }
        Bounds b = _mesh.bounds;
        bc.center = b.center;
        bc.size = Vector3.Max(b.size, new Vector3(0.08f, 0.08f, 0.08f));
        bc.enabled = true;
    }

    public void AdjustHeight(float delta)
    {
        roofHeightMeters = Mathf.Max(0.05f, roofHeightMeters + delta);
        RebuildNow();
    }

    public void AdjustRoundness(float delta)
    {
        roundness = Mathf.Clamp01(roundness + delta);
        RebuildNow();
    }

    public void AdjustOverhang(float delta)
    {
        overhangMeters = Mathf.Max(0f, overhangMeters + delta);
        RebuildNow();
    }

    bool TryPrepareClosedRing(List<Vector3> path, out List<Vector3> ring)
    {
        ring = null;
        if (path == null || path.Count < 4)
            return false;
        ring = new List<Vector3>(path);
        if (Vector3.Distance(ring[0], ring[ring.Count - 1]) < 0.001f)
            ring.RemoveAt(ring.Count - 1);
        return ring.Count >= 3;
    }

    Vector2 ComputeCentroidXZ(List<Vector3> ring)
    {
        float sx = 0f, sz = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            sx += ring[i].x;
            sz += ring[i].z;
        }
        float inv = 1f / Mathf.Max(1, ring.Count);
        return new Vector2(sx * inv, sz * inv);
    }

    void EnsureComponents()
    {
        Transform child = transform.Find(RoofChildName);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(RoofChildName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = gameObject.layer;
        }
        else
            go = child.gameObject;

        _mf = go.GetComponent<MeshFilter>();
        if (_mf == null) _mf = go.AddComponent<MeshFilter>();
        _mr = go.GetComponent<MeshRenderer>();
        if (_mr == null) _mr = go.AddComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "HouseRoofMesh" };
            _mf.sharedMesh = _mesh;
        }

        if (go.GetComponent<HouseRoofControlPointProvider>() == null)
            go.AddComponent<HouseRoofControlPointProvider>();

        // Reuse wall material by default.
        WallObject wall = GetComponent<WallObject>();
        MeshRenderer wallMr = wall != null ? wall.GetComponent<MeshRenderer>() : null;
        if (wallMr != null && wallMr.sharedMaterial != null)
            _mr.sharedMaterial = wallMr.sharedMaterial;
    }

    void ClearMesh()
    {
        if (_mesh != null)
            _mesh.Clear();
        if (_mr != null)
            _mr.enabled = false;
        Transform child = transform.Find(RoofChildName);
        if (child != null)
        {
            var bc = child.GetComponent<BoxCollider>();
            if (bc != null)
                bc.enabled = false;
        }
    }

    int ComputeHash()
    {
        unchecked
        {
            int h = 17;
            WallObject wall = GetComponent<WallObject>();
            WallEditShape edit = GetComponent<WallEditShape>();
            if (wall != null)
            {
                h = h * 31 + Mathf.RoundToInt(wall.height * 1000f);
                h = h * 31 + Mathf.RoundToInt(wall.thickness * 1000f);
            }
            h = h * 31 + Mathf.RoundToInt(roofHeightMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(overhangMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(roundness * 1000f);
            h = h * 31 + Mathf.RoundToInt(yOffsetAboveWallTop * 1000f);
            List<Vector3> ring = edit != null ? edit.GetPreviewPathWorld() : null;
            if (ring != null)
            {
                int n = ring.Count;
                if (n >= 2 && Vector3.Distance(ring[0], ring[n - 1]) < 0.001f)
                    n--;
                for (int i = 0; i < n; i++)
                {
                    h = h * 31 + Mathf.RoundToInt(ring[i].x * 100f);
                    h = h * 31 + Mathf.RoundToInt(ring[i].z * 100f);
                }
            }
            return h;
        }
    }

    static bool TryTriangulateEarClip(List<Vector2> poly, out List<int> triangles)
    {
        triangles = null;
        int n = poly != null ? poly.Count : 0;
        if (n < 3)
            return false;

        // Ensure CCW winding
        if (SignedArea(poly) < 0f)
            poly.Reverse();

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);
        var tris = new List<int>((n - 2) * 3);
        int guard = 0;
        while (idx.Count > 3 && guard++ < n * n + 8)
        {
            bool clipped = false;
            int m = idx.Count;
            for (int k = 0; k < m; k++)
            {
                int iPrev = idx[(k + m - 1) % m];
                int iCur = idx[k];
                int iNext = idx[(k + 1) % m];
                Vector2 a = poly[iPrev];
                Vector2 b = poly[iCur];
                Vector2 c = poly[iNext];
                if (Cross2(b - a, c - b) <= TriEps)
                    continue;
                bool anyInside = false;
                for (int t = 0; t < m; t++)
                {
                    int iv = idx[t];
                    if (iv == iPrev || iv == iCur || iv == iNext)
                        continue;
                    if (PointInTriangle(poly[iv], a, b, c))
                    {
                        anyInside = true;
                        break;
                    }
                }
                if (anyInside)
                    continue;
                tris.Add(iPrev); tris.Add(iCur); tris.Add(iNext);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped)
                return false;
        }

        if (idx.Count == 3)
        {
            tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            triangles = tris;
            return true;
        }
        return false;
    }

    static float SignedArea(List<Vector2> p)
    {
        double a = 0.0;
        for (int i = 0; i < p.Count; i++)
        {
            int j = (i + 1) % p.Count;
            a += (double)p[i].x * p[j].y - (double)p[j].x * p[i].y;
        }
        return (float)(0.5 * a);
    }

    static float Cross2(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross2(b - a, p - a);
        float c2 = Cross2(c - b, p - b);
        float c3 = Cross2(a - c, p - c);
        bool hasNeg = c1 < -TriEps || c2 < -TriEps || c3 < -TriEps;
        bool hasPos = c1 > TriEps || c2 > TriEps || c3 > TriEps;
        return !(hasNeg && hasPos);
    }
}
