using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poignées toit : faîtage (hauteur), arrondi entre base et faîtage, débord au milieu de chaque arête du
/// <b>contour déjà décalé</b> (même radial que <see cref="HouseRoofSystem"/> — aligné sur le fil gris).
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofControlPointProvider : MonoBehaviour,
    IControlPointProvider,
    IControlPointPathProvider,
    IControlPointDragPlaneProvider
{
    public const int IdxHeight = 0;
    public const int IdxRoundness = 1;
    public const int IdxOverhangFirst = 2;

    const float VerticalHandleInset = 0.03f;

    HouseRoofSystem _roof;

    public WallObject HostWall => _roof != null ? _roof.GetComponent<WallObject>() : null;

    void Awake() => CacheRoof();
    void OnEnable() => CacheRoof();

    void CacheRoof()
    {
        if (_roof == null)
            _roof = GetComponentInParent<HouseRoofSystem>();
    }

    /// <summary>Nombre d’arêtes du contour fermé (pour débord).</summary>
    public int EdgeCount
    {
        get
        {
            if (HostWall == null)
                return 0;
            WallEditShape edit = HostWall.GetComponent<WallEditShape>();
            return TryGetClosedFootprintVerts(edit, out List<Vector3> v) ? v.Count : 0;
        }
    }

    public int ControlPointCount
    {
        get
        {
            int e = EdgeCount;
            return e <= 0 ? 0 : 2 + e;
        }
    }

    public bool IsControlPointEditable(int index)
    {
        return index >= 0 && index < ControlPointCount && _roof != null && HostWall != null;
    }

    public Vector3 GetControlPointWorld(int index)
    {
        if (_roof == null)
            CacheRoof();
        if (!TryGetVerticalBasis(out WallEditShape edit, out float wallTopY, out Vector3 centroidXZ, out float basePlateY, out float apexY))
            return transform.position;

        if (index == IdxHeight)
            return new Vector3(centroidXZ.x, apexY, centroidXZ.z);

        if (index == IdxRoundness)
            return GetRoundnessHandleWorld(basePlateY, apexY, centroidXZ);

        int ei = index - IdxOverhangFirst;
        if (ei >= 0 && ei < EdgeCount &&
            TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
        {
            Vector2 c = new Vector2(centroidXZ.x, centroidXZ.z);
            int n = verts.Count;
            int ej = (ei + 1) % n;
            Vector3 oi = OffsetFootprintCornerWorld(verts[ei], c, _roof.overhangMeters, basePlateY);
            Vector3 oj = OffsetFootprintCornerWorld(verts[ej], c, _roof.overhangMeters, basePlateY);
            Vector3 mid = (oi + oj) * 0.5f;
            // Même Y que les sommets du fil gris (<see cref="GetPreviewPathWorld"/>) — pas de lift : évite le décalage sur la pente du mesh.
            mid.y = basePlateY;
            return mid;
        }

        return transform.position;
    }

    Vector3 GetRoundnessHandleWorld(float basePlateY, float apexY, Vector3 centroidXZ)
    {
        float lo = basePlateY + VerticalHandleInset;
        float hi = apexY - VerticalHandleInset;
        if (hi <= lo)
            hi = lo + 0.05f;
        float y = Mathf.Lerp(lo, hi, Mathf.Clamp01(_roof.roundness));
        return new Vector3(centroidXZ.x, y, centroidXZ.z);
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        if (_roof == null)
            CacheRoof();
        if (_roof == null || HostWall == null)
            return;

        WallEditShape edit = HostWall.GetComponent<WallEditShape>();
        if (edit == null)
            return;

        if (!TryGetVerticalBasis(out _, out float wallTopY, out Vector3 centroidXZ, out float basePlateY, out float apexY))
            return;

        if (index == IdxHeight)
        {
            float h = worldPos.y - wallTopY - _roof.yOffsetAboveWallTop;
            _roof.roofHeightMeters = Mathf.Max(0.05f, h);
            _roof.RebuildNow();
            return;
        }

        if (index == IdxRoundness)
        {
            float lo = basePlateY + VerticalHandleInset;
            float hi = apexY - VerticalHandleInset;
            if (hi <= lo)
                hi = lo + 0.05f;
            _roof.roundness = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, worldPos.y));
            _roof.RebuildNow();
            return;
        }

        int ei = index - IdxOverhangFirst;
        if (ei < 0 || ei >= EdgeCount || !TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return;

        Vector2 c = new Vector2(centroidXZ.x, centroidXZ.z);
        int n = verts.Count;
        int ej = (ei + 1) % n;
        Vector3 vi = verts[ei];
        Vector3 vj = verts[ej];

        Vector2 A = new Vector2((vi.x + vj.x) * 0.5f, (vi.z + vj.z) * 0.5f);
        Vector2 ri = RadialOutXZ(vi, c);
        Vector2 rj = RadialOutXZ(vj, c);
        Vector2 B = (ri + rj) * 0.5f;
        Vector2 W = new Vector2(worldPos.x, worldPos.z);

        float denom = B.sqrMagnitude;
        float t;
        if (denom < 1e-10f)
        {
            Vector2 dir = RadialOutXZ(new Vector3(A.x, 0f, A.y), c);
            t = Vector2.Dot(W - A, dir);
        }
        else
            t = Vector2.Dot(W - A, B) / denom;

        _roof.overhangMeters = Mathf.Max(0f, t);
        _roof.RebuildNow();
    }

    /// <summary>
    /// Fil gris : uniquement le contour extérieur du toit (même logique radiale que <see cref="HouseRoofSystem"/>),
    /// fermé — rectangle vu du dessus pour une base rectangulaire. Pas de lien vers le sommet ni vers l’arrondi.
    /// </summary>
    public List<Vector3> GetPreviewPathWorld()
    {
        var path = new List<Vector3>(16);
        if (_roof == null)
            CacheRoof();
        WallEditShape edit = HostWall != null ? HostWall.GetComponent<WallEditShape>() : null;
        if (edit == null || _roof == null)
            return path;

        if (!TryGetVerticalBasis(out _, out _, out Vector3 centroidXZ, out float basePlateY, out _))
            return path;

        if (!TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return path;

        Vector2 centroid = new Vector2(centroidXZ.x, centroidXZ.z);
        int n = verts.Count;
        for (int i = 0; i < n; i++)
            path.Add(OffsetFootprintCornerWorld(verts[i], centroid, _roof.overhangMeters, basePlateY));

        if (path.Count >= 2)
            path.Add(path[0]);

        return path;
    }

    public bool TryGetDragPlane(int index, Camera cam, Vector3 startWorld, out Plane plane)
    {
        plane = default;
        if (cam == null)
            return false;

        if (index == IdxHeight || index == IdxRoundness)
        {
            Vector3 n = Vector3.Cross(cam.transform.forward, Vector3.up);
            if (n.sqrMagnitude < 1e-6f)
                n = Vector3.right;
            n.Normalize();
            plane = new Plane(n, startWorld);
            return true;
        }

        if (!TryGetVerticalBasis(out _, out _, out _, out float basePlateY, out _))
        {
            return false;
        }

        // Plan horizontal au niveau exact du contour gris (cohérent avec les poignées de débord).
        plane = new Plane(Vector3.up, new Vector3(0f, basePlateY, 0f));
        return true;
    }

    bool TryGetVerticalBasis(out WallEditShape edit, out float wallTopY, out Vector3 centroidXZ, out float basePlateY, out float apexY)
    {
        edit = null;
        wallTopY = 0f;
        centroidXZ = default;
        basePlateY = 0f;
        apexY = 0f;
        if (HostWall == null || _roof == null)
            return false;
        edit = HostWall.GetComponent<WallEditShape>();
        if (edit == null)
            return false;
        if (!TryComputeCentroidXZ(edit, out Vector2 c2))
            return false;

        centroidXZ = new Vector3(c2.x, 0f, c2.y);
        wallTopY = edit.shapeY + HostWall.height;
        basePlateY = wallTopY + _roof.yOffsetAboveWallTop;
        apexY = wallTopY + _roof.yOffsetAboveWallTop + Mathf.Max(0.05f, _roof.roofHeightMeters);
        return true;
    }

    static bool TryComputeCentroidXZ(WallEditShape edit, out Vector2 c)
    {
        c = default;
        if (!TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return false;
        float sx = 0f, sz = 0f;
        int n = verts.Count;
        for (int i = 0; i < n; i++)
        {
            sx += verts[i].x;
            sz += verts[i].z;
        }
        float inv = 1f / Mathf.Max(1, n);
        c = new Vector2(sx * inv, sz * inv);
        return true;
    }

    static bool TryGetClosedFootprintVerts(WallEditShape edit, out List<Vector3> verts)
    {
        verts = null;
        if (edit == null)
            return false;
        var path = edit.GetPreviewPathWorld();
        if (path == null || path.Count < 3)
            return false;
        verts = new List<Vector3>(path);
        int n = verts.Count;
        if (n >= 2 && Vector3.Distance(verts[0], verts[n - 1]) < 0.001f)
            verts.RemoveAt(n - 1);
        return verts.Count >= 3;
    }

    /// <summary>Même décalage radial que <see cref="HouseRoofSystem"/> (sommet du mur → extérieur).</summary>
    static Vector3 OffsetFootprintCornerWorld(Vector3 innerCorner, Vector2 centroidXZ, float overhangMeters, float y)
    {
        Vector2 dir = RadialOutXZ(innerCorner, centroidXZ);
        return new Vector3(
            innerCorner.x + dir.x * overhangMeters,
            y,
            innerCorner.z + dir.y * overhangMeters);
    }

    static Vector2 RadialOutXZ(Vector3 cornerWorld, Vector2 centroidXZ)
    {
        Vector2 d = new Vector2(cornerWorld.x - centroidXZ.x, cornerWorld.z - centroidXZ.y);
        if (d.sqrMagnitude < 1e-10f)
            return Vector2.right;
        return d.normalized;
    }
}
