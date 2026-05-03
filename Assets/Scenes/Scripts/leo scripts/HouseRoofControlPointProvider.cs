using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poignees du toit simplifiees :
/// - sommet central (hauteur)
/// - arrondi (roundness) inchangé
/// - debord (overhang)
/// - clic droit sur la poignee hauteur : cycle 4 points latéraux sur la boucle guide (ordre fixe monde XZ : droite, gauche, devant, derrière),
///   puis retrait LIFO ; le curseur ne choisit plus la direction a l’ajout.
/// - debord (overhang)
/// Le systeme de deplacement horizontal du faitage est desactive.
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofControlPointProvider : MonoBehaviour,
    IControlPointProvider,
    IControlPointPathProvider,
    ISecondaryControlPointPathProvider,
    IControlPointDragPlaneProvider
{
    public const int IdxHeight = 0;
    public const int IdxRoundnessFirst = 1;
    const float RoundnessHandleRadial01 = 0.55f;

    /// <summary>Ordre fixe des clics (ajout) : droite +X, gauche −X, devant +Z, derrière −Z. Le retrait inverse la liste (LIFO).</summary>
    static readonly Vector2[] RoofLateralCardinalProbeDirsXZ =
    {
        Vector2.right,
        Vector2.left,
        new Vector2(0f, 1f),
        new Vector2(0f, -1f),
    };

    HouseRoofSystem _roof;
    public WallObject HostWall => _roof != null ? _roof.GetComponent<WallObject>() : null;

    public int IdxOverhangFirst => 1 + EdgeCount;

    // Reutilise les ids legacy pour le nouveau point jaune secondaire (une seule poignee).
    public int IdxHorizontalApexMove => 1 + EdgeCount + EdgeCount;
    public int IdxHorizontalApexMove2 => IdxHorizontalApexMove + 1;
    public bool IsHorizontalApexHandleEnabled => _roof != null && _roof.LateralApexOffsetCount > 0;
    public bool UsesSecondHorizontalSummit => _roof != null && _roof.LateralApexOffsetCount >= 2;
    public bool IsMeshAnchorNearlyCoincidentWithCentroid =>
        _roof == null || _roof.IsPrimaryLateralNearlyCoincidentWithCentroid;

    /// <summary>Nombre de poignées latérales (≤ <see cref="HouseRoofSystem.MaxLateralApexPoints"/>).</summary>
    public int LateralApexControlCount => _roof != null ? _roof.LateralApexOffsetCount : 0;

    public bool TryComputeLateralSnapOffsetFromWorld(Vector3 world, out Vector2 offsetXZ)
    {
        offsetXZ = default;
        CacheRoof();
        if (_roof == null || HostWall == null)
            return false;
        if (!TryGetVerticalBasis(out _, out _, out Vector3 centroidXZ, out _, out float apexY))
            return false;
        Vector2 center = new Vector2(centroidXZ.x, centroidXZ.z);
        Vector2 drag = new Vector2(world.x, world.z);
        if (!TryResolveGuideGridOffsetAtWorld(drag, center, out offsetXZ))
            return false;
        ApplyLateralOffsetAfterCornerPolicy(drag, center, apexY, ref offsetXZ);
        return true;
    }

    /// <summary>
    /// Clic droit sur la poignée hauteur : pas la position du curseur — le Nième ajout va au point guide le plus proche
    /// dans la direction cardinale (0=droite +X, 1=gauche, 2=devant +Z, 3=derrière −Z).
    /// </summary>
    public bool TryComputeLateralSnapOffsetForCycleSlot(int cycleSlotIndex, out Vector2 offsetXZ)
    {
        offsetXZ = default;
        CacheRoof();
        if (_roof == null || HostWall == null)
            return false;
        if (cycleSlotIndex < 0 || cycleSlotIndex >= RoofLateralCardinalProbeDirsXZ.Length)
            return false;
        if (!TryGetVerticalBasis(out WallEditShape edit, out _, out Vector3 centroidXZ3, out _, out float apexY))
            return false;

        Vector2 center = new Vector2(centroidXZ3.x, centroidXZ3.z);
        float probeDist = 80f;
        if (edit != null &&
            TryGetClosedFootprintVerts(edit, out List<Vector3> fv) &&
            fv != null &&
            fv.Count >= 3 &&
            _roof != null)
        {
            float r = ComputeOverhangRingMaxRadius(fv, center, _roof.overhangMeters);
            probeDist = Mathf.Clamp(r * 5f, 25f, 500f);
        }

        Vector2 dir = RoofLateralCardinalProbeDirsXZ[cycleSlotIndex];
        Vector2 probe = center + dir * probeDist;

        if (!TryResolveGuideGridOffsetAtWorld(probe, center, out offsetXZ))
            return false;

        ApplyLateralOffsetAfterCornerPolicy(probe, center, apexY, ref offsetXZ);
        return true;
    }

    public bool TryEnableHorizontalApexHandle()
    {
        CacheRoof();
        if (_roof == null || HostWall == null)
            return false;
        if (!TryGetVerticalBasis(out _, out _, out Vector3 centroidXZ, out _, out float apexY))
            return false;
        Vector2 center = new Vector2(centroidXZ.x, centroidXZ.z);
        if (!TryResolveGuideGridOffsetAtWorld(center + Vector2.right, center, out Vector2 off))
            off = Vector2.zero;
        ApplyLateralOffsetAfterCornerPolicy(center + Vector2.right, center, apexY, ref off);
        if (!_roof.TryAddLateralOffsetXZ(off))
            return false;
        _roof.RebuildNow();
        return true;
    }

    public bool TryEnableHorizontalApexHandleAtWorld(Vector3 world)
    {
        if (!TryComputeLateralSnapOffsetFromWorld(world, out Vector2 off))
            return false;
        CacheRoof();
        if (_roof == null || !_roof.TryAddLateralOffsetXZ(off))
            return false;
        _roof.RebuildNow();
        return true;
    }

    public bool TryEnsureMeshAnchorNonCoincidentWithApex()
    {
        CacheRoof();
        if (_roof == null || _roof.LateralApexOffsetCount < 1)
            return false;
        if (!_roof.IsPrimaryLateralNearlyCoincidentWithCentroid)
            return false;
        return TryEnableHorizontalApexHandle();
    }

    public bool TryEnableSecondHorizontalSummit()
    {
        CacheRoof();
        if (_roof == null || HostWall == null || _roof.LateralApexOffsetCount < 1)
            return false;
        if (!TryGetVerticalBasis(out _, out _, out Vector3 centroidXZ, out _, out float apexY))
            return false;

        Vector2 center = new Vector2(centroidXZ.x, centroidXZ.z);
        Vector2 primary = _roof.GetLateralApexOffsetSnapshot(0);
        Vector2 desired = center - primary;
        if (primary.sqrMagnitude <= 1e-8f)
            desired = center + Vector2.left;

        if (!TryResolveGuideGridOffsetAtWorld(desired, center, out Vector2 off2))
        {
            if (!TryResolveSecondSummitOffsetFromFootprintLoop(center, out off2))
                return false;
        }

        if ((off2 - primary).sqrMagnitude <= 1e-8f)
        {
            if (TryResolveGuideGridOffsetAtWorld(center + Vector2.up, center, out Vector2 alt) &&
                (alt - primary).sqrMagnitude > 1e-8f)
                off2 = alt;
            else if (TryResolveGuideGridOffsetAtWorld(center + Vector2.down, center, out alt) &&
                     (alt - primary).sqrMagnitude > 1e-8f)
                off2 = alt;
            else if (TryResolveSecondSummitOffsetFromFootprintLoop(center, out alt) &&
                     (alt - primary).sqrMagnitude > 1e-8f)
                off2 = alt;
            else
                return false;
        }

        Vector2 dragSecond = center + off2;
        ApplyLateralOffsetAfterCornerPolicy(dragSecond, center, apexY, ref off2);

        if (!_roof.TryAddLateralOffsetXZ(off2))
            return false;
        _roof.RebuildNow();
        return true;
    }

    public bool TryRefreshSecondLateralApexAtWorld(Vector3 world)
    {
        if (!TryComputeLateralSnapOffsetFromWorld(world, out Vector2 off2))
            return false;
        CacheRoof();
        if (_roof == null || _roof.LateralApexOffsetCount < 2)
            return false;
        _roof.SetLateralApexOffsetAtIndex(1, off2);
        _roof.RebuildNow();
        return true;
    }

    public bool TryDisableSecondHorizontalSummit()
    {
        CacheRoof();
        if (_roof == null || _roof.LateralApexOffsetCount < 2)
            return false;
        if (!_roof.TryRemoveLateralAtIndex(1))
            return false;
        _roof.RebuildNow();
        return true;
    }

    public bool TryDisableHorizontalApexHandle()
    {
        CacheRoof();
        if (_roof == null || _roof.LateralApexOffsetCount < 1)
            return false;
        _roof.ClearLateralApexOffsets();
        _roof.RebuildNow();
        return true;
    }

    public bool ToggleHorizontalApexHandle()
    {
        if (IsHorizontalApexHandleEnabled)
            return !TryDisableHorizontalApexHandle();
        return TryEnableHorizontalApexHandle();
    }

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
            if (e <= 0)
                return 0;
            int count = 1 + e + e + LateralApexControlCount;
            return count;
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

        int lateralSlot = index - IdxHorizontalApexMove;
        if (lateralSlot >= 0 && lateralSlot < LateralApexControlCount)
        {
            if (_roof.TryGetLateralApexWorldAtIndex(lateralSlot, out Vector3 w))
                return w;
            Vector2 off = _roof.GetLateralApexOffsetSnapshot(lateralSlot);
            return new Vector3(centroidXZ.x + off.x, apexY, centroidXZ.z + off.y);
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
        float yNorm = _roof.useDomeProfile
            ? HouseRoofSystem.EvaluateDomeProfile(r, Mathf.Clamp01(_roof.roundness))
            : Mathf.Clamp01(r);
        onRadial.y = basePlateY + (apexY - basePlateY) * yNorm;
        return onRadial;
    }

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
        float yNorm = _roof.useDomeProfile
            ? HouseRoofSystem.EvaluateDomeProfile(r, Mathf.Clamp01(roundness01))
            : Mathf.Clamp01(r);
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
            Vector3 pNeutral = GetRoundnessHandleWorldForEdgeAtRoundness(edit, centroidXZ, basePlateY, apexY, ei, 0.5f);
            float h = Mathf.Max(HouseRoofSystem.MinRoofHeightMeters, apexY - basePlateY);
            float delta = Vector3.Dot(worldPos - pNeutral, axisOutUp);
            float span = Mathf.Max(0.25f, h * 0.55f);
            float signedHalf = delta / span;
            _roof.roundness = Mathf.Clamp01(0.5f + signedHalf * 0.5f);
            _roof.RebuildNow();
            return;
        }

        int lateralSlot = index - IdxHorizontalApexMove;
        if (lateralSlot >= 0 && lateralSlot < LateralApexControlCount)
        {
            Vector2 center = new Vector2(centroidXZ.x, centroidXZ.z);
            Vector2 drag = new Vector2(worldPos.x, worldPos.z);
            if (TryResolveGuideGridOffsetAtWorld(drag, center, out Vector2 off))
            {
                ApplyLateralOffsetAfterCornerPolicy(drag, center, apexY, ref off);
                _roof.SetLateralApexOffsetAtIndex(lateralSlot, off);
            }

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

        int lateralSlotDrag = index - IdxHorizontalApexMove;
        if (lateralSlotDrag >= 0 && lateralSlotDrag < LateralApexControlCount)
        {
            plane = new Plane(Vector3.up, new Vector3(0f, startWorld.y, 0f));
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
        wallTopY = edit.shapeY + HostWall.height;
        float lift = HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        basePlateY = wallTopY + _roof.yOffsetAboveWallTop + lift;
        apexY = basePlateY + Mathf.Clamp(_roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);

        // Même centroïde XZ que <see cref="HouseRoofSystem.TryComputeFootprintBaseCornersWorld"/> (repère des offsets latéraux / mesh).
        if (_roof.TryComputeFootprintBaseCornersWorld(out _, out Vector2 roofCentroid, out _, out _))
        {
            centroidXZ = new Vector3(roofCentroid.x, 0f, roofCentroid.y);
            return true;
        }

        if (!TryComputeCentroidXZ(edit, out Vector2 c2))
            return false;
        centroidXZ = new Vector3(c2.x, 0f, c2.y);
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

    static float ComputeOverhangRingMaxRadius(List<Vector3> footprintVerts, Vector2 centroidXZ, float overhangMeters)
    {
        if (footprintVerts == null || footprintVerts.Count == 0)
            return 0.1f;

        float maxR = 0.1f;
        for (int i = 0; i < footprintVerts.Count; i++)
        {
            Vector3 c = footprintVerts[i];
            Vector2 d = RadialOutXZ(c, centroidXZ);
            Vector3 outer = new Vector3(c.x + d.x * overhangMeters, 0f, c.z + d.y * overhangMeters);
            float r = Vector2.Distance(new Vector2(outer.x, outer.z), centroidXZ);
            if (r > maxR)
                maxR = r;
        }

        return maxR;
    }

    /// <summary>
    /// Repli si la résolution grille échoue pour la cible « symétrique » (coins filtrés, etc.) :
    /// teste chaque sommet de la boucle guide jusqu'à un offset distinct du premier jaune.
    /// </summary>
    bool TryResolveSecondSummitOffsetFromFootprintLoop(Vector2 centroidXZ, out Vector2 offsetXZ)
    {
        offsetXZ = default;
        CacheRoof();
        if (_roof == null || HostWall == null)
            return false;
        WallEditShape edit = HostWall.GetComponent<WallEditShape>();
        if (edit == null)
            return false;

        float baseY = edit.shapeY + HostWall.height + _roof.yOffsetAboveWallTop + HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        float y = baseY + 0.04f;
        if (!_roof.TryGetFootprintGuideLoopWorld(y, out Vector3[] loop) || loop == null || loop.Length < 3)
            return false;

        Vector2 primary = _roof.lateralApexOffsetXZ;
        for (int i = 0; i < loop.Length; i++)
        {
            Vector2 probe = new Vector2(loop[i].x, loop[i].z);
            if (!TryResolveGuideGridOffsetAtWorld(probe, centroidXZ, out Vector2 candidate))
                continue;
            if ((candidate - primary).sqrMagnitude <= 1e-8f)
                continue;
            offsetXZ = candidate;
            return true;
        }

        return false;
    }

    bool TryResolveGuideGridOffsetAtWorld(Vector2 worldXZ, Vector2 centroidXZ, out Vector2 offset)
    {
        offset = Vector2.zero;
        if (_roof == null || HostWall == null)
            return false;
        WallEditShape edit = HostWall.GetComponent<WallEditShape>();
        if (edit == null)
            return false;

        float baseY = edit.shapeY + HostWall.height + _roof.yOffsetAboveWallTop + HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        float y = baseY + 0.04f;
        if (!_roof.TryGetFootprintGuideLoopWorld(y, out Vector3[] loop))
            return false;
        if (loop == null || loop.Length < 3)
            return false;

        List<Vector3> outerCorners = null;
        bool filterFootprintCorners =
            _roof.DisableRoofCornerAnchorsTemporary &&
            _roof.TryComputeFootprintBaseCornersWorld(out _, out _, out outerCorners, out _) &&
            outerCorners != null && outerCorners.Count >= 3;

        float cornerRejectEpsSq = 0f;
        if (filterFootprintCorners)
        {
            float minEdge = MinFootprintEdgeLengthXZ(outerCorners);
            float eps = Mathf.Max(0.04f, minEdge * 0.09f);
            cornerRejectEpsSq = eps * eps;
        }

        if (!TryPickNearestLoopVertexXZ(worldXZ, centroidXZ, loop, filterFootprintCorners ? outerCorners : null, cornerRejectEpsSq, out Vector2 best))
        {
            if (!TryPickNearestLoopVertexXZ(worldXZ, centroidXZ, loop, null, 0f, out best))
                return false;
        }

        offset = best - centroidXZ;
        return true;
    }

    /// <summary>
    /// Sommet de la boucle guide le plus proche en XZ. Si <paramref name="rejectNearTheseCorners"/> est fourni,
    /// ignore les sommets qui coïncident avec un coin d’empreinte (même base que le blocage corner anchor temporaire).
    /// </summary>
    static bool TryPickNearestLoopVertexXZ(
        Vector2 worldXZ,
        Vector2 centroidXZ,
        Vector3[] loop,
        List<Vector3> rejectNearTheseCorners,
        float cornerRejectEpsSq,
        out Vector2 best)
    {
        best = centroidXZ;
        if (loop == null || loop.Length < 3)
            return false;

        bool reject = rejectNearTheseCorners != null && rejectNearTheseCorners.Count >= 3 && cornerRejectEpsSq > 0f;
        float bestSq = float.MaxValue;
        int hits = 0;

        for (int i = 0; i < loop.Length; i++)
        {
            Vector2 p = new Vector2(loop[i].x, loop[i].z);
            if (reject && IsPointNearFootprintCornerXZ(p, rejectNearTheseCorners, cornerRejectEpsSq))
                continue;
            hits++;
            float d = (p - worldXZ).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = p;
            }
        }

        return hits > 0 && bestSq < float.MaxValue * 0.5f;
    }

    static bool IsPointNearFootprintCornerXZ(Vector2 pxz, List<Vector3> corners, float epsSq)
    {
        if (corners == null)
            return false;
        for (int i = 0; i < corners.Count; i++)
        {
            Vector2 c = new Vector2(corners[i].x, corners[i].z);
            if ((pxz - c).sqrMagnitude <= epsSq)
                return true;
        }

        return false;
    }

    void ApplyLateralOffsetAfterCornerPolicy(Vector2 dragWorldXZ, Vector2 footprintCentroidXZ, float apexWorldY, ref Vector2 lateralOffsetXZ)
    {
        if (_roof == null)
            return;
        if (_roof.DisableRoofCornerAnchorsTemporary)
        {
            _roof.TryPushLateralOffsetAwayFromFootprintCornersTemporary(dragWorldXZ, footprintCentroidXZ, apexWorldY, ref lateralOffsetXZ);
            Vector2 apex = footprintCentroidXZ + lateralOffsetXZ;
            if (TryResolveGuideGridOffsetAtWorld(apex, footprintCentroidXZ, out Vector2 snapped))
                lateralOffsetXZ = snapped;
        }
        else
            MaybeSnapLateralOffsetToExactFootprintCorner(dragWorldXZ, footprintCentroidXZ, ref lateralOffsetXZ);
    }

    /// <summary>
    /// Si le curseur est sur un coin d’empreinte (même base que le maillage / grille 8 pts),
    /// force l’offset stocké à <c>coinXZ - centroid</c> pour éviter tout léger décalage numérique vs le clamp interne du toit.
    /// </summary>
    void MaybeSnapLateralOffsetToExactFootprintCorner(Vector2 dragXZ, Vector2 centroidXZ, ref Vector2 offsetXZ)
    {
        if (_roof == null || HostWall == null)
            return;
        if (!_roof.TryComputeFootprintBaseCornersWorld(out _, out _, out List<Vector3> corners, out _))
            return;
        if (corners == null || corners.Count < 3)
            return;

        float minEdge = MinFootprintEdgeLengthXZ(corners);
        float eps = Mathf.Max(0.03f, minEdge * 0.09f);

        for (int i = 0; i < corners.Count; i++)
        {
            Vector2 c = new Vector2(corners[i].x, corners[i].z);
            if ((dragXZ - c).sqrMagnitude > eps * eps)
                continue;
            offsetXZ = new Vector2(c.x - centroidXZ.x, c.y - centroidXZ.y);
            return;
        }
    }

    static float MinFootprintEdgeLengthXZ(List<Vector3> corners)
    {
        if (corners == null || corners.Count < 2)
            return 0.5f;
        float best = float.MaxValue;
        int n = corners.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[(i + 1) % n];
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < best)
                best = len;
        }

        return best >= float.MaxValue * 0.5f ? 0.5f : best;
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
