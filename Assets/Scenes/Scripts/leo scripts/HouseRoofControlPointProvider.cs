using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Faîtage · quatre arrondis (milieu de face entre bord décalé et faîtage) · quatre débords (bord extérieur).
/// Fil gris : contour extérieur + cadre intérieur (arrondi).
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofControlPointProvider : MonoBehaviour,
    IControlPointProvider,
    IControlPointPathProvider,
    ISecondaryControlPointPathProvider,
    IControlPointDragPlaneProvider
{
    public const int IdxHeight = 0;
    /// <summary>Premier index des poignées d’arrondi (une par face / arête).</summary>
    public const int IdxRoundnessFirst = 1;
    const float RoundnessHandleRadial01 = 0.55f;

    HouseRoofSystem _roof;

    public WallObject HostWall => _roof != null ? _roof.GetComponent<WallObject>() : null;

    /// <summary>Après les arrondis : poignées de débord (une par arête).</summary>
    public int IdxOverhangFirst => 1 + EdgeCount;

    void Awake() => CacheRoof();
    void OnEnable() => CacheRoof();

    void CacheRoof()
    {
        if (_roof == null)
            _roof = GetComponentInParent<HouseRoofSystem>();
    }

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
            return e <= 0 ? 0 : 1 + e + e;
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
        if (!TryGetVerticalBasis(out WallEditShape edit, out _, out Vector3 centroidXZ, out float basePlateY, out float apexY))
            return transform.position;

        if (index == IdxHeight)
            return new Vector3(centroidXZ.x, apexY, centroidXZ.z);

        int e = EdgeCount;
        if (index >= IdxRoundnessFirst && index < IdxRoundnessFirst + e)
            return GetRoundnessHandleWorldForEdge(edit, centroidXZ, basePlateY, apexY, index - IdxRoundnessFirst);

        int ei = index - IdxOverhangFirst;
        if (ei >= 0 && ei < e && TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
        {
            Vector2 c = new Vector2(centroidXZ.x, centroidXZ.z);
            int n = verts.Count;
            int ej = (ei + 1) % n;
            Vector3 oi = OffsetFootprintCornerWorld(verts[ei], c, _roof.overhangMeters, basePlateY);
            Vector3 oj = OffsetFootprintCornerWorld(verts[ej], c, _roof.overhangMeters, basePlateY);
            Vector3 mid = (oi + oj) * 0.5f;
            mid.y = basePlateY;
            return mid;
        }

        return transform.position;
    }

    bool TryGetRoundnessEdgeOuterMid(
        WallEditShape edit,
        Vector3 centroidXZ,
        float basePlateY,
        float overhangMeters,
        int edgeIndex,
        out Vector3 outerMid,
        out Vector3 centerBase)
    {
        outerMid = default;
        centerBase = default;
        if (!TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return false;
        int n = verts.Count;
        if (edgeIndex < 0 || edgeIndex >= n)
            return false;

        Vector2 c = new Vector2(centroidXZ.x, centroidXZ.z);
        int ej = (edgeIndex + 1) % n;
        Vector3 oi = OffsetFootprintCornerWorld(verts[edgeIndex], c, overhangMeters, basePlateY);
        Vector3 oj = OffsetFootprintCornerWorld(verts[ej], c, overhangMeters, basePlateY);
        outerMid = (oi + oj) * 0.5f;
        outerMid.y = basePlateY;
        centerBase = new Vector3(centroidXZ.x, basePlateY, centroidXZ.z);
        return true;
    }

    /// <summary>Point d'arrondi ancré sur la surface du dôme (rayon intermédiaire fixe).</summary>
    Vector3 GetRoundnessHandleWorldForEdge(
        WallEditShape edit,
        Vector3 centroidXZ,
        float basePlateY,
        float apexY,
        int edgeIndex)
    {
        if (!TryGetRoundnessEdgeOuterMid(edit, centroidXZ, basePlateY, _roof.overhangMeters, edgeIndex, out Vector3 outerMid, out Vector3 centerBase))
            return transform.position;

        float r = Mathf.Clamp01(RoundnessHandleRadial01);
        Vector3 onRadial = Vector3.Lerp(outerMid, centerBase, r);
        float yNorm = HouseRoofSystem.EvaluateDomeProfile(r, Mathf.Clamp01(_roof.roundness));
        onRadial.y = basePlateY + (apexY - basePlateY) * yNorm;
        return onRadial;
    }

    /// <summary>
    /// Position de la poignée pour un roundness donné (sommet inchangé); sert d’origine pour le drag projeté.
    /// </summary>
    Vector3 GetRoundnessHandleWorldForEdgeAtRoundness(
        WallEditShape edit,
        Vector3 centroidXZ,
        float basePlateY,
        float apexY,
        int edgeIndex,
        float roundness01)
    {
        if (!TryGetRoundnessEdgeOuterMid(edit, centroidXZ, basePlateY, _roof.overhangMeters, edgeIndex, out Vector3 outerMid, out Vector3 centerBase))
            return transform.position;

        float r = Mathf.Clamp01(RoundnessHandleRadial01);
        Vector3 onRadial = Vector3.Lerp(outerMid, centerBase, r);
        float yNorm = HouseRoofSystem.EvaluateDomeProfile(r, Mathf.Clamp01(roundness01));
        float roofH = Mathf.Max(HouseRoofSystem.MinRoofHeightMeters, apexY - basePlateY);
        onRadial.y = basePlateY + roofH * yNorm;
        return onRadial;
    }

    static bool TryGetRoundnessDomeAxis(Vector3 outerMid, Vector3 centerBase, out Vector3 horizontalOut, out Vector3 axisOutUp)
    {
        horizontalOut = new Vector3(outerMid.x - centerBase.x, 0f, outerMid.z - centerBase.z);
        if (horizontalOut.sqrMagnitude < 1e-10f)
        {
            horizontalOut = Vector3.forward;
            axisOutUp = (horizontalOut + Vector3.up).normalized;
            return true;
        }

        horizontalOut.Normalize();
        axisOutUp = (horizontalOut + Vector3.up).normalized;
        return true;
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
            float h = worldPos.y - wallTopY - _roof.yOffsetAboveWallTop - HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
            _roof.roofHeightMeters = Mathf.Clamp(h, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);
            _roof.RebuildNow();
            return;
        }

        int e = EdgeCount;
        if (index >= IdxRoundnessFirst && index < IdxRoundnessFirst + e)
        {
            int ei = index - IdxRoundnessFirst;
            if (!TryGetRoundnessEdgeOuterMid(edit, centroidXZ, basePlateY, _roof.overhangMeters, ei, out Vector3 outerMid, out Vector3 centerBase))
                return;

            TryGetRoundnessDomeAxis(outerMid, centerBase, out _, out Vector3 axisOutUp);

            // Neutre à 0.5 : déplacement le long de (horizontalOut + up) ⇒ dôme normal, l’inverse ⇒ dôme inversé.
            Vector3 pNeutral = GetRoundnessHandleWorldForEdgeAtRoundness(edit, centroidXZ, basePlateY, apexY, ei, 0.5f);
            float h = Mathf.Max(HouseRoofSystem.MinRoofHeightMeters, apexY - basePlateY);
            float delta = Vector3.Dot(worldPos - pNeutral, axisOutUp);
            float span = Mathf.Max(0.25f, h * 0.55f);
            float signedHalf = delta / span;
            _roof.roundness = Mathf.Clamp01(0.5f + signedHalf * 0.5f);
            _roof.RebuildNow();
            return;
        }

        int edgeIdx = index - IdxOverhangFirst;
        if (edgeIdx < 0 || edgeIdx >= e || !TryGetClosedFootprintVerts(edit, out List<Vector3> verts2))
            return;

        Vector2 c2 = new Vector2(centroidXZ.x, centroidXZ.z);
        int n2 = verts2.Count;
        int nextEdgeIdx = (edgeIdx + 1) % n2;
        Vector3 vi = verts2[edgeIdx];
        Vector3 vj = verts2[nextEdgeIdx];

        Vector2 A = new Vector2((vi.x + vj.x) * 0.5f, (vi.z + vj.z) * 0.5f);
        Vector2 ri = RadialOutXZ(vi, c2);
        Vector2 rj = RadialOutXZ(vj, c2);
        Vector2 B = (ri + rj) * 0.5f;
        Vector2 W = new Vector2(worldPos.x, worldPos.z);

        float denom = B.sqrMagnitude;
        float tOver;
        if (denom < 1e-10f)
        {
            Vector2 dir = RadialOutXZ(new Vector3(A.x, 0f, A.y), c2);
            tOver = Vector2.Dot(W - A, dir);
        }
        else
            tOver = Vector2.Dot(W - A, B) / denom;

        _roof.overhangMeters = Mathf.Clamp(tOver, HouseRoofSystem.MinOverhangMeters, HouseRoofSystem.MaxOverhangMeters);
        _roof.RebuildNow();
    }

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

    public List<Vector3> GetSecondaryPreviewPathWorld()
    {
        var path = new List<Vector3>(16);
        if (_roof == null)
            CacheRoof();
        WallEditShape edit = HostWall != null ? HostWall.GetComponent<WallEditShape>() : null;
        if (edit == null || _roof == null)
            return path;

        int e = EdgeCount;
        if (e < 3)
            return path;

        if (!TryGetVerticalBasis(out _, out _, out Vector3 centroidXZ, out float basePlateY, out float apexY))
            return path;

        var mids = new List<Vector3>(e);
        for (int i = 0; i < e; i++)
            mids.Add(GetRoundnessHandleWorldForEdge(edit, centroidXZ, basePlateY, apexY, i));

        // Cas toiture 4 côtés : les 4 points d'arrondi restent des milieux de côtés,
        // et le fil gris forme un cadre orthogonal (angles droits) qui passe par ces milieux.
        if (e == 4 && TryBuildOrthogonalInnerFrameFromMidpoints(mids, out List<Vector3> frame))
        {
            path.AddRange(frame);
            path.Add(frame[0]);
            return path;
        }

        path.AddRange(mids);

        if (path.Count >= 2)
            path.Add(path[0]);

        return path;
    }

    public bool TryGetDragPlane(int index, Camera cam, Vector3 startWorld, out Plane plane)
    {
        plane = default;
        if (cam == null)
            return false;

        if (index == IdxHeight)
        {
            Vector3 n = Vector3.Cross(cam.transform.forward, Vector3.up);
            if (n.sqrMagnitude < 1e-6f)
                n = Vector3.right;
            n.Normalize();
            plane = new Plane(n, startWorld);
            return true;
        }

        int e = EdgeCount;
        if (index >= IdxRoundnessFirst && index < IdxRoundnessFirst + e)
        {
            int ei = index - IdxRoundnessFirst;
            if (_roof == null)
                CacheRoof();
            WallEditShape wEdit = HostWall != null ? HostWall.GetComponent<WallEditShape>() : null;
            if (wEdit == null || _roof == null || !TryGetVerticalBasis(out wEdit, out _, out Vector3 centroidXZ, out float basePlateY, out _))
            {
                Vector3 n2 = Vector3.Cross(cam.transform.forward, Vector3.up);
                if (n2.sqrMagnitude < 1e-6f)
                    n2 = Vector3.right;
                plane = new Plane(n2.normalized, startWorld);
                return true;
            }

            if (!TryGetRoundnessEdgeOuterMid(wEdit, centroidXZ, basePlateY, _roof.overhangMeters, ei, out Vector3 outerMid, out Vector3 centerBase))
            {
                Vector3 nfb = Vector3.Cross(cam.transform.forward, Vector3.up);
                if (nfb.sqrMagnitude < 1e-6f)
                    nfb = Vector3.right;
                plane = new Plane(nfb.normalized, startWorld);
                return true;
            }

            TryGetRoundnessDomeAxis(outerMid, centerBase, out Vector3 horizontalOut, out _);
            Vector3 planeNormal = Vector3.Cross(horizontalOut, Vector3.up);
            if (planeNormal.sqrMagnitude < 1e-10f)
            {
                Vector3 nfb = Vector3.Cross(cam.transform.forward, Vector3.up);
                if (nfb.sqrMagnitude < 1e-6f)
                    nfb = Vector3.right;
                plane = new Plane(nfb.normalized, startWorld);
                return true;
            }

            planeNormal.Normalize();
            if (Vector3.Dot(planeNormal, cam.transform.position - startWorld) < 0f)
                planeNormal = -planeNormal;
            plane = new Plane(planeNormal, startWorld);
            return true;
        }

        if (!TryGetVerticalBasis(out _, out _, out _, out float bpY, out _))
            return false;

        plane = new Plane(Vector3.up, new Vector3(0f, bpY, 0f));
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
        float lift = HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        basePlateY = wallTopY + _roof.yOffsetAboveWallTop + lift;
        apexY = basePlateY + Mathf.Clamp(_roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);
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

    static bool TryBuildOrthogonalInnerFrameFromMidpoints(List<Vector3> mids, out List<Vector3> frame)
    {
        frame = null;
        if (mids == null || mids.Count != 4)
            return false;

        Vector3 m0 = mids[0];
        Vector3 m1 = mids[1];
        Vector3 m2 = mids[2];
        Vector3 m3 = mids[3];

        Vector3 center = (m0 + m1 + m2 + m3) * 0.25f;
        float y = center.y;

        Vector3 xDir3 = m1 - m3;
        Vector3 zDir3 = m0 - m2;
        Vector2 xDir = new Vector2(xDir3.x, xDir3.z);
        Vector2 zDir = new Vector2(zDir3.x, zDir3.z);
        if (xDir.sqrMagnitude < 1e-8f || zDir.sqrMagnitude < 1e-8f)
            return false;

        xDir.Normalize();
        // Force l'orthogonalite explicite pour avoir des angles droits.
        Vector2 zPerp = new Vector2(-xDir.y, xDir.x);
        if (Vector2.Dot(zPerp, zDir) < 0f)
            zPerp = -zPerp;
        zDir = zPerp;

        Vector2 c2 = new Vector2(center.x, center.z);
        float halfX = 0.5f * Mathf.Abs(Vector2.Dot(new Vector2(m1.x, m1.z) - new Vector2(m3.x, m3.z), xDir));
        float halfZ = 0.5f * Mathf.Abs(Vector2.Dot(new Vector2(m0.x, m0.z) - new Vector2(m2.x, m2.z), zDir));
        if (halfX < 1e-5f || halfZ < 1e-5f)
            return false;

        Vector2 c0 = c2 + xDir * halfX + zDir * halfZ;
        Vector2 c1 = c2 - xDir * halfX + zDir * halfZ;
        Vector2 c2b = c2 - xDir * halfX - zDir * halfZ;
        Vector2 c3 = c2 + xDir * halfX - zDir * halfZ;

        frame = new List<Vector3>(4)
        {
            new Vector3(c0.x, y, c0.y),
            new Vector3(c1.x, y, c1.y),
            new Vector3(c2b.x, y, c2b.y),
            new Vector3(c3.x, y, c3.y)
        };
        return true;
    }
}
