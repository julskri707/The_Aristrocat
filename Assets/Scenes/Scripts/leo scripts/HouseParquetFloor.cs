using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plancher horizontal sous un mur fermé : rectangle (4 coins) ou polygone simple (L, U, etc.).
/// </summary>
[DisallowMultipleComponent]
public class HouseParquetFloor : MonoBehaviour
{
    const string FloorChildName = "__HouseParquetFloor";
    const string RuntimeFallbackMaterialName = "HouseParquetFloor_Runtime";
    const float TriEps = 1e-5f;

    [Header("Visual")]
    public Material parquetMaterial;
    [Min(0.01f)] public float uvMetersPerTile = 0.45f;
    [Min(0f)] public float yOffsetAboveBase = 0.003f;
    [Min(0.01f)] public float slabThickness = 0.12f;
    [Tooltip("Hauteur d’un étage (m) : doit correspondre à WallBuildController.addFloorHeightMeters. Sert à empiler une dalle par niveau selon WallObject.height.")]
    [Min(0.1f)] public float storeyHeightMeters = 2.5f;

    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    Material _runtimeFallbackMaterial;

    public bool HasFloorMesh => _mesh != null && _mesh.vertexCount > 0;

    /// <summary>
    /// Lot réellement traité comme maison (plancher ou matériau parquet), pas un composant vide sur un mur standard.
    /// Utilisé pour la fusion de lots adjacents.
    /// </summary>
    public bool IsDesignatedHouseLot => parquetMaterial != null || HasFloorMesh;

    public void ApplyOrRefresh(WallObject wall, WallEditShape editShape)
    {
        if (wall == null || editShape == null)
            return;
        if (editShape.shapeKind != WallEditShape.ShapeKind.Rectangle || !editShape.IsClosedLoopPath)
            return;

        // Même pipeline que le contour libre / ellipse : triangulation du contour complet.
        // (L’ancien quad sur les 4 premiers sommets ne couvrait pas les cas où le mesh reste circulaire
        // mais shapeKind est repassé en Rectangle — carré AABB par-dessus l’ovale.)
        ApplyOrRefreshFromClosedPreviewPath(wall, editShape);
    }

    /// <summary>
    /// Boucle fermée quelconque (ex. fusion en L) : remplit l’intérieur par triangulation, pas seulement un quad.
    /// </summary>
    public void ApplyOrRefreshClosedFreeLoop(WallObject wall, WallEditShape editShape)
    {
        if (wall == null || editShape == null || editShape.shapeKind != WallEditShape.ShapeKind.Free)
            return;
        ApplyOrRefreshFromClosedPreviewPath(wall, editShape);
    }

    /// <summary>
    /// Plancher à partir du contour fermé courant (<see cref="WallEditShape.GetPreviewPathWorld"/>), quel que soit le type de forme
    /// (triangle, ellipse, free fermé, etc.). Si le mesh du mur est encore circulaire mais l’éditeur est en Rectangle,
    /// on suit le mesh pour éviter un quad AABB « parfait » au-dessus de l’ovale.
    /// </summary>
    public void ApplyOrRefreshFromClosedPreviewPath(WallObject wall, WallEditShape editShape)
    {
        if (wall == null || editShape == null || !editShape.IsClosedLoopPath)
            return;

        List<Vector3> path = WallEditShape.ResolveClosedLotDisplayRingWorld(wall, editShape);
        if (path == null || path.Count < 3)
            return;

        var ring = new List<Vector3>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0 && i == path.Count - 1 &&
                Vector3.Distance(path[0], path[i]) < 0.0001f)
                continue;
            ring.Add(path[i]);
        }

        if (ring.Count < 3)
            return;

        var poly2 = new List<Vector2>(ring.Count);
        for (int i = 0; i < ring.Count; i++)
            poly2.Add(new Vector2(ring[i].x, ring[i].z));

        RemoveDuplicateClosing(poly2);
        if (poly2.Count < 3)
            return;

        RemoveCollinearVerticesClosedLoop(poly2, TriEps * 10f);
        if (poly2.Count < 3)
            return;

        float area = SignedAreaPoly2(poly2);
        if (Mathf.Abs(area) < TriEps)
            return;

        if (area < 0f)
            poly2.Reverse();

        BuildMultiStoreyExtrudedFloors(wall, poly2, editShape.shapeY + yOffsetAboveBase);
    }

    static void RemoveDuplicateClosing(List<Vector2> poly)
    {
        while (poly.Count >= 2 &&
               (poly[0] - poly[poly.Count - 1]).sqrMagnitude <= TriEps * TriEps)
            poly.RemoveAt(poly.Count - 1);
    }

    /// <summary>
    /// Retire les sommets colinéaires sur le contour fermé (ex. milieux d’arêtes d’un rectangle orthogonaux)
    /// pour que l’ear-clipping ne bloque pas tout en gardant la même enveloppe.
    /// </summary>
    static void RemoveCollinearVerticesClosedLoop(List<Vector2> poly, float colinearEps)
    {
        if (poly == null || poly.Count < 4)
            return;

        bool changed = true;
        int guard = 0;
        int maxGuard = Mathf.Max(8, poly.Count * poly.Count);
        while (changed && poly.Count >= 4 && guard++ < maxGuard)
        {
            changed = false;
            int n = poly.Count;
            for (int i = n - 1; i >= 0; i--)
            {
                int ip = (i - 1 + n) % n;
                int inx = (i + 1) % n;
                Vector2 a = poly[ip];
                Vector2 b = poly[i];
                Vector2 c = poly[inx];
                float cr = Cross2(b - a, c - b);
                if (Mathf.Abs(cr) <= colinearEps)
                {
                    poly.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
    }

    static float SignedAreaPoly2(List<Vector2> p)
    {
        double a = 0.0;
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += (double)p[i].x * p[j].y - (double)p[j].x * p[i].y;
        }

        return (float)(0.5 * a);
    }

    static bool TryTriangulateEarClip(List<Vector2> poly, out List<int> triangles)
    {
        triangles = null;
        int n = poly.Count;
        if (n < 3)
            return false;

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++)
            idx.Add(i);

        var tris = new List<int>(Mathf.Max(6, (n - 2) * 3));
        int guard = 0;
        int maxGuard = n * n + 8;

        while (idx.Count > 3 && guard++ < maxGuard)
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

                if (!IsConvex(a, b, c))
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

                tris.Add(iPrev);
                tris.Add(iCur);
                tris.Add(iNext);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }

            if (!clipped)
                return false;
        }

        if (idx.Count != 3)
            return false;

        tris.Add(idx[0]);
        tris.Add(idx[1]);
        tris.Add(idx[2]);

        if (!VerifyWindingUp(tris, poly))
        {
            for (int i = 0; i < tris.Count; i += 3)
            {
                int t = tris[i + 1];
                tris[i + 1] = tris[i + 2];
                tris[i + 2] = t;
            }
        }

        triangles = tris;
        return true;
    }

    static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross2(b - a, c - b) > TriEps;
    }

    static float Cross2(Vector2 u, Vector2 v)
    {
        return u.x * v.y - u.y * v.x;
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross2(b - a, p - a);
        float c2 = Cross2(c - b, p - b);
        float c3 = Cross2(a - c, p - c);
        bool hasNeg = c1 < -TriEps || c2 < -TriEps || c3 < -TriEps;
        bool hasPos = c1 > TriEps || c2 > TriEps || c3 > TriEps;
        return !(hasNeg && hasPos);
    }

    static bool VerifyWindingUp(List<int> tris, List<Vector2> poly)
    {
        if (tris.Count < 3)
            return true;
        int i0 = tris[0], i1 = tris[1], i2 = tris[2];
        Vector3 v0 = new Vector3(poly[i0].x, 0f, poly[i0].y);
        Vector3 v1 = new Vector3(poly[i1].x, 0f, poly[i1].y);
        Vector3 v2 = new Vector3(poly[i2].x, 0f, poly[i2].y);
        return Vector3.Cross(v1 - v0, v2 - v0).y > 0f;
    }

    void BuildMultiStoreyExtrudedFloors(WallObject wall, List<Vector2> poly2, float firstSlabTopY)
    {
        if (wall == null || poly2 == null || poly2.Count < 3)
            return;

        if (!TryTriangulateEarClip(poly2, out List<int> topIndices) || topIndices == null || topIndices.Count < 3)
        {
            ClearFloor();
            return;
        }

        float story = Mathf.Max(0.1f, storeyHeightMeters);
        int floorCount = Mathf.Max(1, Mathf.RoundToInt(wall.height / story));

        EnsureComponents();

        var verts = new List<Vector3>(poly2.Count * 6 * floorCount);
        var uvs = new List<Vector2>(poly2.Count * 6 * floorCount);
        var tris = new List<int>(topIndices.Count * 2 * floorCount + poly2.Count * 6 * floorCount);

        for (int k = 0; k < floorCount; k++)
        {
            float topY = firstSlabTopY + k * story;
            AppendExtrudedSlabForPolygon(wall, poly2, topIndices, topY, verts, uvs, tris);
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mf.sharedMesh = _mesh;

        Material matToUse = parquetMaterial != null ? parquetMaterial : GetOrCreateRuntimeFallbackMaterial();
        if (matToUse != null)
            _mr.sharedMaterial = matToUse;
        _mr.enabled = true;
    }

    void AppendExtrudedSlabForPolygon(
        WallObject wall,
        List<Vector2> poly2,
        List<int> topIndices,
        float topY,
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris)
    {
        Transform wt = wall.transform;
        int n = poly2.Count;
        float bottomY = topY - Mathf.Max(0.01f, slabThickness);
        float uvDen = Mathf.Max(0.01f, uvMetersPerTile);

        int baseV = verts.Count;

        for (int i = 0; i < n; i++)
        {
            Vector3 w = new Vector3(poly2[i].x, topY, poly2[i].y);
            verts.Add(wt.InverseTransformPoint(w));
            uvs.Add(new Vector2(w.x / uvDen, w.z / uvDen));
        }

        for (int i = 0; i < topIndices.Count; i += 3)
        {
            tris.Add(baseV + topIndices[i]);
            tris.Add(baseV + topIndices[i + 1]);
            tris.Add(baseV + topIndices[i + 2]);
        }

        int bottomStart = verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 w = new Vector3(poly2[i].x, bottomY, poly2[i].y);
            verts.Add(wt.InverseTransformPoint(w));
            uvs.Add(new Vector2(w.x / uvDen, w.z / uvDen));
        }

        for (int i = 0; i < topIndices.Count; i += 3)
        {
            tris.Add(bottomStart + topIndices[i + 2]);
            tris.Add(bottomStart + topIndices[i + 1]);
            tris.Add(bottomStart + topIndices[i]);
        }

        float sideVMax = Mathf.Max(0.01f, slabThickness) / uvDen;
        float sideUAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector2 a2 = poly2[i];
            Vector2 b2 = poly2[j];
            float edgeLen = Vector2.Distance(a2, b2);

            Vector3 ta = new Vector3(a2.x, topY, a2.y);
            Vector3 tb = new Vector3(b2.x, topY, b2.y);
            Vector3 bb = new Vector3(b2.x, bottomY, b2.y);
            Vector3 ba = new Vector3(a2.x, bottomY, a2.y);

            int sideStart = verts.Count;
            verts.Add(wt.InverseTransformPoint(ta));
            verts.Add(wt.InverseTransformPoint(tb));
            verts.Add(wt.InverseTransformPoint(bb));
            verts.Add(wt.InverseTransformPoint(ba));

            float u0 = sideUAcc / uvDen;
            float u1 = (sideUAcc + edgeLen) / uvDen;
            uvs.Add(new Vector2(u0, 0f));
            uvs.Add(new Vector2(u1, 0f));
            uvs.Add(new Vector2(u1, sideVMax));
            uvs.Add(new Vector2(u0, sideVMax));

            tris.Add(sideStart + 0);
            tris.Add(sideStart + 1);
            tris.Add(sideStart + 2);
            tris.Add(sideStart + 0);
            tris.Add(sideStart + 2);
            tris.Add(sideStart + 3);

            sideUAcc += edgeLen;
        }
    }

    void EnsureComponents()
    {
        Transform t = transform;
        Transform child = t.Find(FloorChildName);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(FloorChildName);
            go.transform.SetParent(t, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
        else
            go = child.gameObject;

        _mf = go.GetComponent<MeshFilter>();
        if (_mf == null)
            _mf = go.AddComponent<MeshFilter>();

        _mr = go.GetComponent<MeshRenderer>();
        if (_mr == null)
            _mr = go.AddComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "HouseParquetFloor" };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
        }
    }

    public void ClearFloor()
    {
        if (_mesh != null)
            _mesh.Clear();

        if (_mr != null)
            _mr.enabled = false;
    }

    void OnDestroy()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }

        if (_runtimeFallbackMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeFallbackMaterial);
            else
                DestroyImmediate(_runtimeFallbackMaterial);
            _runtimeFallbackMaterial = null;
        }
    }

    Material GetOrCreateRuntimeFallbackMaterial()
    {
        if (_runtimeFallbackMaterial != null)
            return _runtimeFallbackMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Diffuse");
        if (shader == null)
            return null;

        _runtimeFallbackMaterial = new Material(shader);
        _runtimeFallbackMaterial.name = RuntimeFallbackMaterialName;
        _runtimeFallbackMaterial.color = new Color(0.72f, 0.62f, 0.48f, 1f);
        return _runtimeFallbackMaterial;
    }
}
