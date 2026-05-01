using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Points éditables du toit (composant sur l’enfant <c>__HouseRoof</c>). Le mesh est généré dans <see cref="HouseRoofSystem.RebuildNow"/>.
///
/// <para><b>Indices (<see cref="ControlPointCount"/> = 1 + pics_or + e + e, avec e = <see cref="EdgeCount"/>)</b></para>
/// <list type="bullet">
/// <item><b>0</b> — <see cref="IdxHeight"/> : au-dessus du hub (jaune), règle <see cref="HouseRoofSystem.roofHeightMeters"/>.</item>
/// <item><b>1 … pics_or</b> — <see cref="IdxExtraRidgePeakFirst"/>+ : pics additionnels (or), <see cref="HouseRoofSystem.extraRidgePeakOffsetsXZ"/> si faîtage secondaire activé.</item>
/// <item><b><see cref="IdxRoundnessFirst"/> … +e−1</b> : une poignée d’« arrondi » par arête du footprint (<see cref="HouseRoofSystem.roundness"/>).</item>
/// <item><b><see cref="IdxOverhangFirst"/> … +e−1</b> : milieu du débord par arête (<see cref="HouseRoofSystem.overhangMeters"/>).</item>
/// </list>
///
/// <para><b>Chemins de prévisualisation (pas des indices séparés)</b></para>
/// <list type="bullet">
/// <item><see cref="GetPreviewPathWorld"/> : contour extérieur au débord.</item>
/// <item><see cref="GetSecondaryPreviewPathWorld"/> : fil gris / cadre via les milieux d’arrondi (rectangle → <see cref="TryBuildOrthogonalInnerFrameThroughHandles"/>).</item>
/// </list>
///
/// <para><b>Superposition</b> : plusieurs pics (or) peuvent partager la même position XZ, y compris avec le hub — ce n’est pas une erreur ; le snap inclut explicitement le hub comme cible.</para>
///
/// Entrées principales : <see cref="GetControlPointWorld"/>, <see cref="SetControlPointWorld"/>, <see cref="TryGetDragPlane"/>.
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofControlPointProvider : MonoBehaviour,
    IControlPointProvider,
    IControlPointPathProvider,
    ISecondaryControlPointPathProvider,
    IControlPointDragPlaneProvider
{
    public const int IdxHeight = 0;

    /// <summary>Premier sommet de faîtage additionnel (or). Indice 2, 3… pour les suivants (clic droit répété sur le jaune).</summary>
    public const int IdxExtraRidgePeakFirst = 1;

    /// <summary>Rétrocompat : premier sommet or = indice 1.</summary>
    public const int IdxSecondaryRidgePeak = IdxExtraRidgePeakFirst;

    /// <summary>Nombre de sommets or (hors jaune centroïde).</summary>
    public int ExtraRidgePeakCount =>
        _roof != null && _roof.secondaryRidgePeakEnabled ? _roof.GetExtraRidgePeakCount() : 0;

    /// <summary>Premier index des poignées d’arrondi (une par face / arête).</summary>
    public int IdxRoundnessFirst => 1 + ExtraRidgePeakCount;

    const float RoundnessHandleRadial01 = 0.55f;

    HouseRoofSystem _roof;

    public WallObject HostWall => _roof != null ? _roof.GetComponent<WallObject>() : null;

    /// <summary>Après les arrondis : poignées de débord (une par arête).</summary>
    public int IdxOverhangFirst => IdxRoundnessFirst + EdgeCount;

    public bool IsExtraRidgePeakIndex(int index) =>
        _roof != null &&
        _roof.secondaryRidgePeakEnabled &&
        index >= IdxExtraRidgePeakFirst &&
        index < IdxExtraRidgePeakFirst + ExtraRidgePeakCount;

    /// <summary>Rétrocompat.</summary>
    public bool IsSecondaryRidgePeakIndex(int index) => IsExtraRidgePeakIndex(index);

    /// <summary>Clic droit sur le sommet jaune : ajoute un sommet de faîtage or déplaçable (répétable).</summary>
    public bool TryAddSecondaryRidgePeakFromContextMenu()
    {
        CacheRoof();
        if (_roof == null || HostWall == null)
            return false;
        WallEditShape edit = HostWall.GetComponent<WallEditShape>();
        if (edit == null || !TryComputeCentroidXZ(edit, out Vector2 c))
            return false;
        if (!TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return false;

        float maxD = 0f;
        for (int i = 0; i < verts.Count; i++)
        {
            Vector2 d = new Vector2(verts[i].x - c.x, verts[i].z - c.y);
            maxD = Mathf.Max(maxD, d.magnitude);
        }

        float suggested = Mathf.Clamp(maxD * 0.45f, 0.2f, 2.5f);
        _roof.MigrateLegacyRidgePeaks();
        if (_roof.GetExtraRidgePeakCount() == 0)
            _roof.EnableSecondaryRidgePeak(suggested);
        else
            _roof.AppendExtraRidgePeak();

        if (TryGetVerticalBasis(out WallEditShape edSnap, out _, out Vector3 centroidSnap, out float basePlateSnap, out _))
        {
            _roof.MigrateLegacyRidgePeaks();
            var list = _roof.extraRidgePeakOffsetsXZ;
            if (list == null || list.Count == 0)
                return true;
            Vector2 last = list[list.Count - 1];
            Vector2 probe = new Vector2(centroidSnap.x + last.x, centroidSnap.z + last.y);
            list[list.Count - 1] = ComputeSecondaryRidgeSnapOffsetXZ(
                edSnap, centroidSnap, basePlateSnap, _roof.overhangMeters, probe);
            _roof.secondaryPeakHeightMeters = _roof.roofHeightMeters;
            _roof.SyncLegacySecondaryOffsetFromList();
            _roof.RebuildNow();
        }

        return true;
    }

    const int SecondaryRidgeEdgeSnapDivisions = 4;
    /// <summary>
    /// Rayons le long de hub→débord : pas de 0.5 (milieu) pour limiter les snaps accidentels ; le hub seul est ajouté pour la superposition volontaire.
    /// </summary>
    static readonly float[] SecondaryRidgeRadialSnapFractions = { 0.82f, 0.94f, 1f };

    /// <summary>
    /// Grille du second faîte : périmètre décalé + <b>hub</b> (offset nul possible → superposition avec le jaune / entre pics).
    /// </summary>
    Vector2 ComputeSecondaryRidgeSnapOffsetXZ(
        WallEditShape edit,
        Vector3 centroidXZ,
        float basePlateY,
        float overhangMeters,
        Vector2 worldXZProbe)
    {
        if (!TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return Vector2.zero;

        Vector2 cent = new Vector2(centroidXZ.x, centroidXZ.z);
        int n = verts.Count;

        // Carré / formes symétriques : sans zone de tolérance au hub, le curseur rarement tombe exactement sur le centre
        // et la grille discrète peut « gagner » avant le hub. On autorise la superposition comme pour les autres empreintes.
        float footprintSpan = 0f;
        for (int vi = 0; vi < n; vi++)
        {
            Vector2 vx = new Vector2(verts[vi].x, verts[vi].z);
            footprintSpan = Mathf.Max(footprintSpan, (vx - cent).magnitude);
        }

        float stickyHubRad = Mathf.Max(0.02f, footprintSpan * 0.055f);
        if ((worldXZProbe - cent).sqrMagnitude <= stickyHubRad * stickyHubRad)
            return Vector2.zero;

        var candidates = new List<Vector2>(n * SecondaryRidgeEdgeSnapDivisions * SecondaryRidgeRadialSnapFractions.Length);
        for (int i = 0; i < n; i++)
        {
            Vector3 oi = OffsetFootprintCornerWorld(verts[i], cent, overhangMeters, basePlateY);
            int j = (i + 1) % n;
            Vector3 oj = OffsetFootprintCornerWorld(verts[j], cent, overhangMeters, basePlateY);
            for (int step = 0; step < SecondaryRidgeEdgeSnapDivisions; step++)
            {
                float edgeT = step / (float)SecondaryRidgeEdgeSnapDivisions;
                Vector3 edgePoint = Vector3.Lerp(oi, oj, edgeT);
                Vector2 edgeXZ = new Vector2(edgePoint.x, edgePoint.z);
                for (int r = 0; r < SecondaryRidgeRadialSnapFractions.Length; r++)
                {
                    float radialT = SecondaryRidgeRadialSnapFractions[r];
                    candidates.Add(Vector2.Lerp(cent, edgeXZ, radialT));
                }
            }
        }

        // Règle produit : superposition autorisée — le hub est une cible de snap explicite (offset retourné peut être ~0).
        candidates.Add(cent);

        float tieDsqEps = Mathf.Max(1e-12f, 1e-7f * footprintSpan * footprintSpan);
        float hubCandEpsSq = Mathf.Max(1e-14f, (footprintSpan * 1e-9f) * (footprintSpan * 1e-9f));

        Vector2 best = candidates[0];
        float bestSq = (best - worldXZProbe).sqrMagnitude;
        for (int i = 1; i < candidates.Count; i++)
        {
            Vector2 cand = candidates[i];
            float dsq = (cand - worldXZProbe).sqrMagnitude;
            bool candAtHub = (cand - cent).sqrMagnitude <= hubCandEpsSq;
            bool bestAtHub = (best - cent).sqrMagnitude <= hubCandEpsSq;
            if (dsq < bestSq - tieDsqEps)
            {
                bestSq = dsq;
                best = cand;
            }
            else if (Mathf.Abs(dsq - bestSq) <= tieDsqEps && candAtHub && !bestAtHub)
                best = cand;
        }

        Vector2 off = best - cent;
        if (off.sqrMagnitude <= hubCandEpsSq)
            return Vector2.zero;
        return off;
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
            int extraPeak = _roof != null && _roof.secondaryRidgePeakEnabled ? _roof.GetExtraRidgePeakCount() : 0;
            return 1 + extraPeak + e + e;
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
        {
            float hp = Mathf.Clamp(_roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);
            return new Vector3(centroidXZ.x, basePlateY + hp, centroidXZ.z);
        }

        if (_roof.secondaryRidgePeakEnabled && IsExtraRidgePeakIndex(index))
        {
            int ei = index - IdxExtraRidgePeakFirst;
            var offs = _roof.extraRidgePeakOffsetsXZ;
            if (offs != null && ei >= 0 && ei < offs.Count)
            {
                Vector2 off = offs[ei];
                float hp = Mathf.Clamp(_roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);
                return new Vector3(
                    centroidXZ.x + off.x,
                    basePlateY + hp,
                    centroidXZ.z + off.y);
            }
        }

        int e = EdgeCount;
        int rf = IdxRoundnessFirst;
        if (index >= rf && index < rf + e)
            return GetRoundnessHandleWorldForEdge(edit, centroidXZ, basePlateY, apexY, index - rf);

        int overhangIdx = index - IdxOverhangFirst;
        if (overhangIdx >= 0 && overhangIdx < e && TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
        {
            Vector2 c = new Vector2(centroidXZ.x, centroidXZ.z);
            int n = verts.Count;
            int ej = (overhangIdx + 1) % n;
            Vector3 oi = OffsetFootprintCornerWorld(verts[overhangIdx], c, _roof.overhangMeters, basePlateY);
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

        // Aligné sur HouseRoofSystem : rayons centroïde → chaque pic (hub central), pas une polyligne qui peut relier deux pics entre eux.
        Vector2 innerXZ = c;
        if (_roof != null && _roof.secondaryRidgePeakEnabled && _roof.GetExtraRidgePeakCount() > 0)
        {
            innerXZ = HouseRoofSystem.RidgeTargetXZThroughCentralHub(
                new Vector2(outerMid.x, outerMid.z),
                c,
                _roof.extraRidgePeakOffsetsXZ);
        }

        centerBase = new Vector3(innerXZ.x, basePlateY, innerXZ.y);
        return true;
    }

    static Vector2 ClosestPointOnSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-12f)
            return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return a + ab * t;
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
        float yNorm = _roof.RoofProfileYNormalized(r);
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
        float yNorm = _roof.RoofProfileYNormalized(r, roundness01);
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

        if (_roof.secondaryRidgePeakEnabled && IsExtraRidgePeakIndex(index))
        {
            int ei = index - IdxExtraRidgePeakFirst;
            var offs = _roof.extraRidgePeakOffsetsXZ;
            if (offs != null && ei >= 0 && ei < offs.Count)
            {
                Vector2 probe = new Vector2(worldPos.x, worldPos.z);
                offs[ei] = ComputeSecondaryRidgeSnapOffsetXZ(
                    edit, centroidXZ, basePlateY, _roof.overhangMeters, probe);
                #region agent log
                DebugLog(
                    "post-fix",
                    "H4",
                    "HouseRoofControlPointProvider.SetControlPointWorld:363",
                    "Extra ridge point moved",
                    "{"
                    + "\"index\":" + index + ","
                    + "\"extraIndex\":" + ei + ","
                    + "\"extraCount\":" + offs.Count + ","
                    + "\"secondaryRidgePeakEnabled\":" + BoolJson(_roof.secondaryRidgePeakEnabled) + ","
                    + "\"planarHipRoof\":" + BoolJson(_roof.planarHipRoof) + ","
                    + "\"newOffsetX\":" + FloatJson(offs[ei].x) + ","
                    + "\"newOffsetZ\":" + FloatJson(offs[ei].y)
                    + "}");
                #endregion
                _roof.secondaryPeakHeightMeters = _roof.roofHeightMeters;
                _roof.SyncLegacySecondaryOffsetFromList();
                _roof.RebuildNow();
            }
            return;
        }

        int e = EdgeCount;
        int rf = IdxRoundnessFirst;
        if (index >= rf && index < rf + e)
        {
            int ei = index - rf;
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

        if (e == 4 &&
            TryGetClosedFootprintVerts(edit, out List<Vector3> footprint) &&
            footprint.Count == 4 &&
            TryBuildOrthogonalInnerFrameThroughHandles(footprint, centroidXZ, basePlateY, mids, out List<Vector3> frame))
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
        if (_roof == null)
            CacheRoof();

        if (index == IdxHeight)
        {
            Vector3 n = Vector3.Cross(cam.transform.forward, Vector3.up);
            if (n.sqrMagnitude < 1e-6f)
                n = Vector3.right;
            n.Normalize();
            plane = new Plane(n, startWorld);
            return true;
        }

        if (_roof != null && _roof.secondaryRidgePeakEnabled && IsExtraRidgePeakIndex(index))
        {
            plane = new Plane(Vector3.up, startWorld);
            return true;
        }

        int e = EdgeCount;
        int rf = IdxRoundnessFirst;
        if (index >= rf && index < rf + e)
        {
            int ei = index - rf;
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
        float hp = Mathf.Clamp(_roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);
        float hs = _roof.secondaryRidgePeakEnabled
            ? Mathf.Clamp(_roof.secondaryPeakHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters)
            : hp;
        apexY = basePlateY + Mathf.Max(hp, hs);
        return true;
    }

    static bool TryComputeCentroidXZ(WallEditShape edit, out Vector2 c)
    {
        c = default;
        if (!TryGetClosedFootprintVerts(edit, out List<Vector3> verts))
            return false;
        var xz = new List<Vector2>(verts.Count);
        for (int i = 0; i < verts.Count; i++)
            xz.Add(new Vector2(verts[i].x, verts[i].z));
        c = HouseRoofSystem.ComputeFootprintHubXZ(xz);
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

    /// <summary>
    /// Cadre rectangulaire en plan dont chaque cote passe par la poignee d'arrondi correspondante.
    /// Le chemin insere les poignees entre les coins (coin -> poignee -> coin), ce qui garde l'aspect
    /// orthogonal sans faire flotter le fil hors des points reels sur le dome.
    /// </summary>
    bool TryBuildOrthogonalInnerFrameThroughHandles(
        List<Vector3> footprint,
        Vector3 centroidXZ,
        float basePlateY,
        List<Vector3> mids,
        out List<Vector3> frame)
    {
        frame = null;
        if (footprint == null || footprint.Count != 4 || mids == null || mids.Count != 4 || _roof == null)
            return false;

        Vector2 centroid = new Vector2(centroidXZ.x, centroidXZ.z);
        Vector3 o0 = OffsetFootprintCornerWorld(footprint[0], centroid, _roof.overhangMeters, basePlateY);
        Vector3 o1 = OffsetFootprintCornerWorld(footprint[1], centroid, _roof.overhangMeters, basePlateY);
        Vector2 u = new Vector2(o1.x - o0.x, o1.z - o0.z);
        if (u.sqrMagnitude < 1e-8f)
            return false;
        u.Normalize();

        Vector2 v = new Vector2(-u.y, u.x);
        Vector2 footCent = Vector2.zero;
        for (int i = 0; i < 4; i++)
        {
            Vector3 oi = OffsetFootprintCornerWorld(footprint[i], centroid, _roof.overhangMeters, basePlateY);
            footCent += new Vector2(oi.x, oi.z);
        }
        footCent *= 0.25f;

        Vector2 edgeMid01 = new Vector2((o0.x + o1.x) * 0.5f, (o0.z + o1.z) * 0.5f);
        Vector2 inward = footCent - edgeMid01;
        if (inward.sqrMagnitude > 1e-10f && Vector2.Dot(v, inward) < 0f)
            v = -v;

        float[] sideU = new float[4];
        float[] sideV = new float[4];
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = new Vector2(mids[i].x, mids[i].z);
            sideU[i] = Vector2.Dot(p, u);
            sideV[i] = Vector2.Dot(p, v);
        }

        Vector3 c30 = FromUv(sideU[3], sideV[0], u, v, (mids[3].y + mids[0].y) * 0.5f);
        Vector3 c01 = FromUv(sideU[1], sideV[0], u, v, (mids[0].y + mids[1].y) * 0.5f);
        Vector3 c12 = FromUv(sideU[1], sideV[2], u, v, (mids[1].y + mids[2].y) * 0.5f);
        Vector3 c23 = FromUv(sideU[3], sideV[2], u, v, (mids[2].y + mids[3].y) * 0.5f);

        frame = new List<Vector3>(8)
        {
            c30, mids[0],
            c01, mids[1],
            c12, mids[2],
            c23, mids[3]
        };
        return true;
    }

    static Vector3 FromUv(float uCoord, float vCoord, Vector2 u, Vector2 v, float y)
    {
        Vector2 xz = u * uCoord + v * vCoord;
        return new Vector3(xz.x, y, xz.y);
    }

    #region agent log
    static void DebugLog(string runId, string hypothesisId, string location, string message, string dataJson)
    {
        try
        {
            string line =
                "{"
                + "\"sessionId\":\"243ebf\","
                + "\"runId\":\"" + JsonEscape(runId) + "\","
                + "\"hypothesisId\":\"" + JsonEscape(hypothesisId) + "\","
                + "\"location\":\"" + JsonEscape(location) + "\","
                + "\"message\":\"" + JsonEscape(message) + "\","
                + "\"data\":" + dataJson + ","
                + "\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                + "}";
            File.AppendAllText("debug-243ebf.log", line + System.Environment.NewLine);
        }
        catch
        {
            // Debug logging must never break control handles.
        }
    }

    static string FloatJson(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return "0";
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    static string BoolJson(bool value) => value ? "true" : "false";

    static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
    #endregion
}
