using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Clipper2Lib;
using UnityEngine;

/// <summary>
/// Parametric roof generated from a closed wall footprint.
/// Controls:
/// - roofHeightMeters: raise/lower roof
/// - planarHipRoof: straight slope from eave to ridge (default); no dome bending from Roundness
/// - Multi-ridge + hip plan: mesh « pic » en 2 nappes (débord → crête), sans anneaux intermédiaires
/// - roundness: dome curvature when planar hip is off
/// - overhangMeters: roof base extension outside wall footprint
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofSystem : MonoBehaviour
{
    const string RoofChildName = "__HouseRoof";
    const float TriEps = 1e-5f;
    /// <summary>
    /// Incrémenter quand la génération du mesh change (triangulation, nombre d’anneaux, etc.).
    /// Sinon <see cref="ComputeHash"/> reste identique et <see cref="LateUpdate"/> ne régénère pas le mesh.
    /// </summary>
    const int RoofMeshGenerationVersion = 13;
    /// <summary>Échelle monde→entiers Clipper (offset du toit sur empreintes L / concaves).</summary>
    const double RoofClipperScale = 100000.0;
    /// <summary>Nombre de couronnes verticales du loft : plus élevé sur L pour des plis réguliers.</summary>
    const int RoofConcaveRadialBands = 14;
    const int RoofConvexRadialBands = 10;
    /// <summary>Empreinte convexe + pics multiples + hip plan : uniquement débord puis ligne de crête (faces planes).</summary>
    const int PeakFacetRingCount = 2;
    /// <summary>Répétition UV le long du périmètre (U), V = hauteur normalisée.</summary>
    const float RoofUvPerimeterScale = 4f;
    public const float MinRoofHeightMeters = 0.25f;
    public const float MaxRoofHeightMeters = 10f;
    public const float MinOverhangMeters = 0.4f;
    public const float MaxOverhangMeters = 1f;

    /// <summary>Décalage vertical fixe de la semelle du toit au-dessus du mur.</summary>
    public const float RoofBuiltInVerticalLiftMeters = 0.15f;

    /// <summary>
    /// Raccord rentré dans le mur : distance perpendiculaire aux façades (plan XZ).
    /// À 0, la ligne du connecteur mur/toit suit les coins du contour du footprint ; l’ancienne valeur 0,2 m rentrait le joint vers l’intérieur et décalait visuellement le toit par rapport au mur.
    /// </summary>
    public const float EaveInsetPerpendicularToWallMeters = 0f;

    [Header("Shape")]
    [Range(MinRoofHeightMeters, MaxRoofHeightMeters)] public float roofHeightMeters = 1.2f;

    [Tooltip("Deuxième sommet : faîtage entre le centroïde et ce point (toit type deux pans). Clic droit sur le sommet jaune pour l’activer.")]
    public bool secondaryRidgePeakEnabled;

    /// <summary>Toujours alignée sur <see cref="roofHeightMeters"/> ; réservé mesh / sérialisation.</summary>
    [HideInInspector] public float secondaryPeakHeightMeters = 1.2f;

    /// <summary>Décalage XZ du second sommet par rapport au centroïde du footprint (compat sérialisation ; synchro avec le 1er entrée de <see cref="extraRidgePeakOffsetsXZ"/>).</summary>
    public Vector2 secondaryRidgePeakOffsetXZ;

    /// <summary>Sommets de faîtage additionnels (or, hors jaune centroïde). Plusieurs clic droits sur le jaune ajoutent une entrée. Les offsets peuvent coïncider (superposition hub ↔ pic ou pic ↔ pic).</summary>
    public List<Vector2> extraRidgePeakOffsetsXZ = new List<Vector2>();

    [Tooltip("Si activé, la hauteur suit une ligne droite du débord au faîtage (faces planes, hip classique). Sinon le profil courbe utilise Roundness.")]
    public bool planarHipRoof = true;

    [Tooltip("Courbure du profil vertical (dôme). Ignoré tant que « Hip plan » est activé.")]
    [Range(0f, 1f)] public float roundness = 0.45f;
    [Range(MinOverhangMeters, MaxOverhangMeters)] public float overhangMeters = MinOverhangMeters;
    [Min(0.02f)] public float roofThicknessMeters = 0.16f;
    [Min(0f)] public float yOffsetAboveWallTop = 0f;

    [Header("Runtime")]
    public bool autoRebuild = true;

    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    Material _connectorMaterial;
    Material _roofFallbackSkinMaterial;
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

        float baseY = edit.shapeY + wall.height + yOffsetAboveWallTop + RoofBuiltInVerticalLiftMeters;
        roofHeightMeters = Mathf.Clamp(roofHeightMeters, MinRoofHeightMeters, MaxRoofHeightMeters);
        if (secondaryRidgePeakEnabled)
            secondaryPeakHeightMeters = roofHeightMeters;
        overhangMeters = Mathf.Clamp(overhangMeters, MinOverhangMeters, MaxOverhangMeters);

        float h = roofHeightMeters;
        var wallCorners = new List<Vector3>(prepared.Count);
        var baseCorners = new List<Vector3>(prepared.Count);

        var footprintXz = new List<Vector2>(prepared.Count);
        for (int i = 0; i < prepared.Count; i++)
            footprintXz.Add(new Vector2(prepared[i].x, prepared[i].z));

        // Forme en L / concave : la moyenne des sommets peut tomber hors du polygone → dôme incohérent ; utiliser un hub intérieur.
        Vector2 centroid = ComputeFootprintHubXZ(footprintXz);

        if (!TryInsetPolygonXZPerpendicular(footprintXz, EaveInsetPerpendicularToWallMeters, out List<Vector2> wallFootprintXz))
            wallFootprintXz = footprintXz;

        for (int i = 0; i < prepared.Count; i++)
        {
            Vector3 p = prepared[i];
            Vector2 wc = wallFootprintXz[i];
            wallCorners.Add(new Vector3(wc.x, baseY, wc.y));
            Vector2 dir = new Vector2(p.x - centroid.x, p.z - centroid.y);
            if (dir.sqrMagnitude > 1e-8f)
                dir.Normalize();
            baseCorners.Add(new Vector3(p.x + dir.x * overhangMeters, baseY, p.z + dir.y * overhangMeters));
        }

        const int edgeSubdivisions = 8;
        List<Vector3> baseRing = BuildSubdividedClosedRing(baseCorners, edgeSubdivisions);
        List<Vector3> wallRing = BuildSubdividedClosedRing(wallCorners, edgeSubdivisions);
        int n = baseRing.Count;

        MigrateLegacyRidgePeaks();
        bool dualRidge =
            secondaryRidgePeakEnabled &&
            extraRidgePeakOffsetsXZ != null &&
            extraRidgePeakOffsetsXZ.Count > 0;
        bool concaveFootprint =
            IsClosedPolygonConcaveXZ(prepared) ||
            IsClosedPolygonConcaveXZ(baseCorners) ||
            IsClosedPolygonConcaveXZ(baseRing);
        // Faîtage multiple : la géométrie suit les offsets des pics (sinon les poignées or ne déforment rien).
        // « Hip plan » ne coupe plus cette branche : il ne fait que linéariser la hauteur (profileY vs xzT).
        bool applyRidgePeaks = dualRidge && !concaveFootprint;

        int ringCount = concaveFootprint ? RoofConcaveRadialBands : RoofConvexRadialBands;

        var uAlongPerimeter = new float[n];
        ComputeArcLengthU01AlongClosedRingXZ(baseRing, uAlongPerimeter);

        // Empreinte concave (L / U) : loft par offset Clipper successifs.
        // La même progression t ∈ [0,1] sert à l’inset ET à la hauteur (profil dôme) — avant elles étaient désynchronisées,
        // ce qui vrillait la géométrie et les textures.
        Vector2[][] insetRingsXZ = null;
        bool useInsetRings = concaveFootprint &&
            TryBuildInsetRingTableXZ(baseRing, n, ringCount, out insetRingsXZ);

        bool explicitPeakFacetStrip =
            applyRidgePeaks &&
            planarHipRoof &&
            !concaveFootprint &&
            !useInsetRings;

        if (explicitPeakFacetStrip)
            ringCount = PeakFacetRingCount;

        bool concaveFallbackNoInset = concaveFootprint && !useInsetRings && !applyRidgePeaks;

        int ringVertexCount = ringCount * n;

        var verts = new List<Vector3>(ringVertexCount);
        var uvs = new List<Vector2>(ringVertexCount);
        var roofTris = new List<int>(n * (ringCount * 12 + 6));
        var connectorTris = new List<int>(n * 24);

        #region agent log
        DebugLog(
            "initial",
            "H1",
            "HouseRoofSystem.RebuildNow:205",
            "Rebuild flags",
            "{"
            + "\"planarHipRoof\":" + BoolJson(planarHipRoof) + ","
            + "\"roundness\":" + FloatJson(roundness) + ","
            + "\"concaveFootprint\":" + BoolJson(concaveFootprint) + ","
            + "\"useInsetRings\":" + BoolJson(useInsetRings) + ","
            + "\"concaveFallbackNoInset\":" + BoolJson(concaveFallbackNoInset) + ","
            + "\"explicitPeakFacetStrip\":" + BoolJson(explicitPeakFacetStrip) + ","
            + "\"dualRidge\":" + BoolJson(dualRidge) + ","
            + "\"extraRidgeCount\":" + (extraRidgePeakOffsetsXZ != null ? extraRidgePeakOffsetsXZ.Count : 0) + ","
            + "\"applyRidgePeaks\":" + BoolJson(applyRidgePeaks) + ","
            + "\"ringCount\":" + ringCount
            + "}");
        #endregion

        for (int r = 0; r < ringCount; r++)
        {
            float t = RoofRingParam01(r, ringCount);
            float xzT = t;
            if (concaveFootprint && !useInsetRings && !applyRidgePeaks)
                xzT = Mathf.Min(xzT, 0.94f);
            // Hip plan : hauteur = même progression que le Lerp XZ (faces planes). Dôme : ancienne courbe sur le paramètre d’anneau t (peut différer de xzT en concave sans Clipper).
            float profileY = planarHipRoof
                ? Mathf.Clamp01(xzT)
                : EvaluateDomeProfile(t, Mathf.Clamp01(roundness));
            for (int i = 0; i < n; i++)
            {
                Vector3 b = baseRing[i];
                float x;
                float z;
                float y;

                if (useInsetRings && insetRingsXZ != null)
                {
                    Vector2 xz = insetRingsXZ[r][i];
                    x = xz.x;
                    z = xz.y;
                    y = baseY + h * profileY;
                }
                else if (!applyRidgePeaks)
                {
                    x = Mathf.Lerp(b.x, centroid.x, xzT);
                    z = Mathf.Lerp(b.z, centroid.y, xzT);
                    y = baseY + h * profileY;
                }
                else
                {
                    Vector2 bxz = new Vector2(b.x, b.z);
                    Vector2 rc = RidgeTargetXZThroughCentralHub(bxz, centroid, extraRidgePeakOffsetsXZ);
                    x = Mathf.Lerp(b.x, rc.x, xzT);
                    z = Mathf.Lerp(b.z, rc.y, xzT);
                    y = baseY + h * profileY;
                }

                verts.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(uAlongPerimeter[i] * RoofUvPerimeterScale, profileY));
            }
        }

        if (applyRidgePeaks)
        {
            int ridgeRow = (ringCount - 1) * n;
            ForceRidgeRowToReturnThroughCentralHub(
                verts,
                uvs,
                ridgeRow,
                n,
                baseRing,
                centroid,
                extraRidgePeakOffsetsXZ);
        }

        // Connect rings (normale vers le haut : évite les zones noires sur L concave / quads pliés).
        for (int r = 0; r < ringCount - 1; r++)
        {
            int row0 = r * n;
            int row1 = (r + 1) * n;
            int collapsedInnerEdgeCount = 0;
            int diagA0B1Count = 0;
            int diagA1B0Count = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 v00dbg = verts[row0 + i];
                Vector3 v01dbg = verts[row0 + j];
                Vector3 v10dbg = verts[row1 + i];
                Vector3 v11dbg = verts[row1 + j];
                if ((v10dbg - v11dbg).sqrMagnitude < 1e-12f)
                {
                    collapsedInnerEdgeCount++;
                }
                else
                {
                    Vector2 h00 = new Vector2(v00dbg.x, v00dbg.z);
                    Vector2 h01 = new Vector2(v01dbg.x, v01dbg.z);
                    Vector2 h10 = new Vector2(v10dbg.x, v10dbg.z);
                    Vector2 h11 = new Vector2(v11dbg.x, v11dbg.z);
                    Vector2 outerMid = (h00 + h01) * 0.5f;
                    Vector2 hubToCorner = outerMid - centroid;
                    if (hubToCorner.sqrMagnitude < 1e-12f)
                    {
                        diagA0B1Count++;
                    }
                    else
                    {
                        float sA = Vector2.Dot(h11 - h00, hubToCorner);
                        float sB = Vector2.Dot(h10 - h01, hubToCorner);
                        if (sA >= sB) diagA0B1Count++;
                        else diagA1B0Count++;
                    }
                }
                AddBetweenRingQuadUpFacing(verts, roofTris, row0 + i, row0 + j, row1 + i, row1 + j, centroid);
            }

            if (r == ringCount - 2)
            {
                #region agent log
                DebugLog(
                    "initial",
                    "H2",
                    "HouseRoofSystem.RebuildNow:248",
                    "Top connector ring diagnostics",
                    "{"
                    + "\"row\":" + r + ","
                    + "\"collapsedInnerEdgeCount\":" + collapsedInnerEdgeCount + ","
                    + "\"diagA0B1Count\":" + diagA0B1Count + ","
                    + "\"diagA1B0Count\":" + diagA1B0Count + ","
                    + "\"n\":" + n
                    + "}");
                #endregion
            }
        }

        float topRingMinY = float.MaxValue;
        float topRingMaxY = float.MinValue;
        float topRingMaxHubDist = 0f;
        if (ringCount > 0)
        {
            int topStart = (ringCount - 1) * n;
            for (int i = 0; i < n; i++)
            {
                Vector3 tv = verts[topStart + i];
                topRingMinY = Mathf.Min(topRingMinY, tv.y);
                topRingMaxY = Mathf.Max(topRingMaxY, tv.y);
                float d = Vector2.Distance(new Vector2(tv.x, tv.z), centroid);
                if (d > topRingMaxHubDist) topRingMaxHubDist = d;
            }
        }

        #region agent log
        DebugLog(
            "initial",
            "H3",
            "HouseRoofSystem.RebuildNow:282",
            "Top ring spread",
            "{"
            + "\"topRingMinY\":" + FloatJson(topRingMinY) + ","
            + "\"topRingMaxY\":" + FloatJson(topRingMaxY) + ","
            + "\"topRingDeltaY\":" + FloatJson(topRingMaxY - topRingMinY) + ","
            + "\"topRingMaxHubDist\":" + FloatJson(topRingMaxHubDist) + ","
            + "\"ringCount\":" + ringCount + ","
            + "\"vertexCount\":" + verts.Count
            + "}");
        #endregion

        AddThickInteriorAndEaveConnector(verts, uvs, roofTris, connectorTris, baseRing, wallRing, Mathf.Max(0.02f, roofThicknessMeters));

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.subMeshCount = 2;
        _mesh.SetTriangles(roofTris, 0);
        _mesh.SetTriangles(connectorTris, 1);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mf.sharedMesh = _mesh;
        _mr.enabled = true;

        if (_mf != null)
            UpdateRoofPickCollider(_mf.gameObject);

        _lastHash = ComputeHash();
    }

    /// <summary>Détecte un angle rentrant (forme L, U, etc.) sur la projection XZ du contour déjà subdivisé.</summary>
    static bool IsClosedPolygonConcaveXZ(List<Vector3> ring)
    {
        int n = ring.Count;
        if (n < 4)
            return false;

        double a = 0.0;
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = new Vector2(ring[i].x, ring[i].z);
            Vector2 p1 = new Vector2(ring[(i + 1) % n].x, ring[(i + 1) % n].z);
            a += (double)p0.x * p1.y - (double)p1.x * p0.y;
        }

        bool ccw = a >= 0.0;
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = new Vector2(ring[(i + n - 1) % n].x, ring[(i + n - 1) % n].z);
            Vector2 p1 = new Vector2(ring[i].x, ring[i].z);
            Vector2 p2 = new Vector2(ring[(i + 1) % n].x, ring[(i + 1) % n].z);
            float c = (p1.x - p0.x) * (p2.y - p1.y) - (p1.y - p0.y) * (p2.x - p1.x);
            if (ccw && c < -1e-4f)
                return true;
            if (!ccw && c > 1e-4f)
                return true;
        }

        return false;
    }

    static void AddBetweenRingQuadUpFacing(
        List<Vector3> verts,
        List<int> roofTris,
        int a0,
        int a1,
        int b0,
        int b1,
        Vector2 hubXZ)
    {
        // a0,a1 = anneau extérieur (bord), b0,b1 = anneau intérieur (vers le hub).
        // Pli « sommet → coin » : diagonale la plus alignée avec (milieu du bord extérieur − hub XZ).
        Vector3 v00 = verts[a0];
        Vector3 v01 = verts[a1];
        Vector3 v10 = verts[b0];
        Vector3 v11 = verts[b1];

        Vector2 h00 = new Vector2(v00.x, v00.z);
        Vector2 h01 = new Vector2(v01.x, v01.z);
        Vector2 h10 = new Vector2(v10.x, v10.z);
        Vector2 h11 = new Vector2(v11.x, v11.z);

        Vector2 outerMid = (h00 + h01) * 0.5f;
        // Du « sommet » (hub intérieur) vers le coin / bord extérieur de ce morceau de toit.
        Vector2 hubToCorner = outerMid - hubXZ;

        Vector2 diagA0B1 = h11 - h00;
        Vector2 diagA1B0 = h10 - h01;

        bool useDiagA0B1;
        if (hubToCorner.sqrMagnitude < 1e-12f)
            useDiagA0B1 = true;
        else
        {
            float sA = Vector2.Dot(diagA0B1, hubToCorner);
            float sB = Vector2.Dot(diagA1B0, hubToCorner);
            useDiagA0B1 = sA >= sB;
        }

        Vector3 quadRef = Vector3.Cross(v01 - v00, v10 - v00);
        if (quadRef.sqrMagnitude < 1e-14f)
            quadRef = Vector3.up;
        else if (quadRef.y < 0f)
            quadRef = -quadRef;

        // Dernier anneau = tous les sommets au hub (lerp t=1) : b0 et b1 coïncident.
        // Ne pas découper en deux triangles dont un dégénéré → normales fausses, trous, « cap » qui flotte.
        if ((v10 - v11).sqrMagnitude < 1e-12f)
        {
            AddTriangleMatchingQuadReference(verts, roofTris, a0, b0, a1, quadRef);
            return;
        }

        if (useDiagA0B1)
        {
            AddTriangleMatchingQuadReference(verts, roofTris, a0, b0, b1, quadRef);
            AddTriangleMatchingQuadReference(verts, roofTris, a0, b1, a1, quadRef);
        }
        else
        {
            AddTriangleMatchingQuadReference(verts, roofTris, a0, b0, a1, quadRef);
            AddTriangleMatchingQuadReference(verts, roofTris, a1, b0, b1, quadRef);
        }
    }

    static void AddTriangleMatchingQuadReference(
        List<Vector3> verts,
        List<int> tris,
        int i0,
        int i1,
        int i2,
        Vector3 quadReferenceNormal)
    {
        Vector3 a = verts[i0];
        Vector3 b = verts[i1];
        Vector3 c = verts[i2];
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, quadReferenceNormal) >= 0f)
        {
            tris.Add(i0); tris.Add(i1); tris.Add(i2);
        }
        else
        {
            tris.Add(i0); tris.Add(i2); tris.Add(i1);
        }
    }

    /// <summary>Paramètre vertical commun aux couronnes : 0 = débord, 1 = ligne de faîtage.</summary>
    static float RoofRingParam01(int ringIndex, int ringCount)
    {
        if (ringCount <= 1)
            return 0f;
        return ringIndex / (float)(ringCount - 1);
    }

    /// <summary>U ∈ [0,1) : distance normalisée le long du périmètre (XZ) jusqu’à chaque sommet du contour fermé.</summary>
    static void ComputeArcLengthU01AlongClosedRingXZ(List<Vector3> ringYUp, float[] uOut)
    {
        int n = ringYUp != null ? ringYUp.Count : 0;
        if (n < 2 || uOut == null || uOut.Length < n)
            return;

        float total = 0f;
        var edgeLen = new float[n];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            edgeLen[i] = Vector2.Distance(
                new Vector2(ringYUp[i].x, ringYUp[i].z),
                new Vector2(ringYUp[j].x, ringYUp[j].z));
            total += edgeLen[i];
        }

        if (total < 1e-8f)
        {
            for (int i = 0; i < n; i++)
                uOut[i] = i / (float)Mathf.Max(1, n - 1);
            return;
        }

        float acc = 0f;
        for (int i = 0; i < n; i++)
        {
            uOut[i] = acc / total;
            acc += edgeLen[i];
        }
    }

    /// <summary>
    /// En faîtage multiple, interdit une arête directe entre deux pics :
    /// dès que l'ordre du dernier anneau passerait d'un rayon centre→pic A à centre→pic B,
    /// les deux vertices de transition sont remis au hub jaune.
    /// Résultat topologique : pic A → centre → pic B, jamais pic A → pic B.
    /// </summary>
    static void ForceRidgeRowToReturnThroughCentralHub(
        List<Vector3> verts,
        List<Vector2> uvs,
        int rowStart,
        int n,
        List<Vector3> baseRing,
        Vector2 hubXZ,
        List<Vector2> peakOffsets)
    {
        if (verts == null || uvs == null || baseRing == null || peakOffsets == null)
            return;
        if (n < 3 || peakOffsets.Count < 2 || rowStart < 0 || rowStart + n > verts.Count || baseRing.Count != n)
            return;

        var spokeByVertex = new int[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 bxz = new Vector2(baseRing[i].x, baseRing[i].z);
            ClosestPointOnCentroidSpokesXZ(bxz, hubXZ, peakOffsets, out spokeByVertex[i]);
        }

        var forceHub = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int si = spokeByVertex[i];
            int sj = spokeByVertex[j];
            if (si < 0 || sj < 0 || si == sj)
                continue;

            forceHub[i] = true;
            forceHub[j] = true;
        }

        for (int i = 0; i < n; i++)
        {
            if (!forceHub[i])
                continue;

            int vi = rowStart + i;
            Vector3 v = verts[vi];
            v.x = hubXZ.x;
            v.z = hubXZ.y;
            verts[vi] = v;
            uvs[vi] = new Vector2(v.x * 0.2f, v.z * 0.2f);
        }
    }

    /// <summary>
    /// Anneaux horizontaux par offset polygonal (Clipper) : sur un L, un simple lerp vers le hub croise les arêtes → quads retournés et caps cassées.
    /// </summary>
    static bool TryBuildInsetRingTableXZ(
        List<Vector3> baseRing,
        int n,
        int ringCount,
        out Vector2[][] ringsXZ)
    {
        ringsXZ = null;
        if (baseRing == null || n < 3 || baseRing.Count != n)
            return false;

        float maxInset = EstimateSafeMaxInsetMetersXZ(baseRing);
        if (maxInset < 0.03f)
            return false;

        Path64 basePath = BuildClipperPath64FromBaseRingXZ(baseRing, RoofClipperScale);
        if (basePath == null || basePath.Count < 3)
            return false;

        var fracAtVertex = new float[n];
        ComputeVertexAlongPerimeterFractionsXZ(baseRing, fracAtVertex);

        ringsXZ = new Vector2[ringCount][];
        for (int r = 0; r < ringCount; r++)
            ringsXZ[r] = new Vector2[n];

        for (int i = 0; i < n; i++)
            ringsXZ[0][i] = new Vector2(baseRing[i].x, baseRing[i].z);

        double invScale = 1.0 / RoofClipperScale;

        for (int r = 1; r < ringCount; r++)
        {
            // Même t que <see cref="RoofRingParam01"/> + hauteur du dôme pour que XZ et Y restent cohérents.
            float t = RoofRingParam01(r, ringCount);
            float insetM = t * maxInset;
            double deltaClipper = -insetM * RoofClipperScale;

            if (Math.Abs(deltaClipper) < 0.5)
            {
                Array.Copy(ringsXZ[r - 1], ringsXZ[r], n);
                continue;
            }

            if (!TryClipperOffsetInward(basePath, deltaClipper, out Path64 outPath))
            {
                ringsXZ = null;
                return false;
            }

            if (!ResampleClosedPathAtFractionsXZ(outPath, invScale, fracAtVertex, ringsXZ[r]))
            {
                ringsXZ = null;
                return false;
            }
        }

        return true;
    }

    static float EstimateSafeMaxInsetMetersXZ(List<Vector3> baseRing)
    {
        Path64 basePath = BuildClipperPath64FromBaseRingXZ(baseRing, RoofClipperScale);
        if (basePath == null || basePath.Count < 3)
            return 0f;

        double baseAreaMetersSq = Math.Abs(Clipper.Area(basePath)) / (RoofClipperScale * RoofClipperScale);
        if (baseAreaMetersSq < 1e-6)
            return 0f;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        for (int i = 0; i < baseRing.Count; i++)
        {
            Vector3 v = baseRing[i];
            minX = Mathf.Min(minX, v.x);
            maxX = Mathf.Max(maxX, v.x);
            minZ = Mathf.Min(minZ, v.z);
            maxZ = Mathf.Max(maxZ, v.z);
        }

        float diagonal = new Vector2(maxX - minX, maxZ - minZ).magnitude;
        if (diagonal < 1e-4f)
            return 0f;

        float lo = 0f;
        float hi = Mathf.Max(0.1f, diagonal);
        for (int iter = 0; iter < 28; iter++)
        {
            float mid = (lo + hi) * 0.5f;
            if (ClipperInsetStillUsable(basePath, mid, baseAreaMetersSq))
                lo = mid;
            else
                hi = mid;
        }

        return Mathf.Max(0f, lo * 0.985f);
    }

    static bool ClipperInsetStillUsable(Path64 basePath, float insetMeters, double baseAreaMetersSq)
    {
        if (insetMeters <= 1e-5f)
            return true;

        double deltaClipper = -insetMeters * RoofClipperScale;
        if (!TryClipperOffsetInward(basePath, deltaClipper, out Path64 outPath))
            return false;

        double areaMetersSq = Math.Abs(Clipper.Area(outPath)) / (RoofClipperScale * RoofClipperScale);
        double minArea = Math.Max(0.0025, baseAreaMetersSq * 0.00035);
        return areaMetersSq > minArea;
    }

    static Path64 BuildClipperPath64FromBaseRingXZ(List<Vector3> baseRing, double scale)
    {
        var path = new Path64(baseRing.Count);
        foreach (Vector3 v in baseRing)
        {
            long x = (long)Math.Round(v.x * scale, MidpointRounding.AwayFromZero);
            long yy = (long)Math.Round(v.z * scale, MidpointRounding.AwayFromZero);
            path.Add(new Point64(x, yy));
        }

        if (path.Count < 3)
            return path;

        if (Clipper.Area(path) < 0.0)
            path.Reverse();

        return path;
    }

    static bool TryClipperOffsetInward(Path64 basePath, double deltaClipper, out Path64 bestPath)
    {
        bestPath = null;
        var co = new ClipperOffset();
        co.AddPath(basePath, JoinType.Miter, EndType.Polygon);
        var solution = new Paths64();
        co.Execute(deltaClipper, solution);
        if (solution == null || solution.Count == 0)
            return false;

        double bestAbsArea = 0.0;
        foreach (Path64 p in solution)
        {
            if (p == null || p.Count < 3)
                continue;
            double a = Math.Abs(Clipper.Area(p));
            if (a > bestAbsArea)
            {
                bestAbsArea = a;
                bestPath = p;
            }
        }

        return bestPath != null && bestPath.Count >= 3;
    }

    static void ComputeVertexAlongPerimeterFractionsXZ(List<Vector3> baseRing, float[] fracAtVertex)
    {
        int n = baseRing.Count;
        float total = 0f;
        var edgeLen = new float[n];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            edgeLen[i] = Vector2.Distance(
                new Vector2(baseRing[i].x, baseRing[i].z),
                new Vector2(baseRing[j].x, baseRing[j].z));
            total += edgeLen[i];
        }

        if (total < 1e-8f)
        {
            float inv = 1f / Mathf.Max(1, n);
            for (int i = 0; i < n; i++)
                fracAtVertex[i] = i * inv;
            return;
        }

        float cum = 0f;
        for (int i = 0; i < n; i++)
        {
            fracAtVertex[i] = cum / total;
            cum += edgeLen[i];
        }
    }

    static bool ResampleClosedPathAtFractionsXZ(Path64 path, double invScale, float[] fracAtVertex, Vector2[] outRingXZ)
    {
        int n = fracAtVertex.Length;
        var world = new List<Vector2>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            float x = (float)(path[i].X * invScale);
            float z = (float)(path[i].Y * invScale);
            world.Add(new Vector2(x, z));
        }

        int m = world.Count;
        if (m < 3)
            return false;

        float total = 0f;
        var edgeLen = new float[m];
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            edgeLen[i] = Vector2.Distance(world[i], world[j]);
            total += edgeLen[i];
        }

        if (total < 1e-8f)
            return false;

        for (int vi = 0; vi < n; vi++)
        {
            float targetFrac = Mathf.Repeat(fracAtVertex[vi], 1f - 1e-6f);
            float walk = targetFrac * total;
            float acc = 0f;
            bool placed = false;
            for (int e = 0; e < m; e++)
            {
                float el = edgeLen[e];
                if (acc + el >= walk - 1e-5f)
                {
                    float t = el > 1e-6f ? Mathf.Clamp01((walk - acc) / el) : 0f;
                    outRingXZ[vi] = Vector2.Lerp(world[e], world[(e + 1) % m], t);
                    placed = true;
                    break;
                }

                acc += el;
            }

            if (!placed)
                outRingXZ[vi] = world[0];
        }

        return true;
    }

    static void AddThickInteriorAndEaveConnector(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        List<int> connectorTris,
        List<Vector3> outerBaseRing,
        List<Vector3> wallRing,
        float thickness)
    {
        int frontVertexCount = verts.Count;
        int frontTriangleIndexCount = roofTris.Count;
        Vector3 down = Vector3.down * thickness;

        for (int i = 0; i < frontVertexCount; i++)
        {
            verts.Add(verts[i] + down);
            uvs.Add(uvs[i]);
        }

        for (int i = 0; i < frontTriangleIndexCount; i += 3)
        {
            roofTris.Add(frontVertexCount + roofTris[i + 2]);
            roofTris.Add(frontVertexCount + roofTris[i + 1]);
            roofTris.Add(frontVertexCount + roofTris[i]);
        }

        int n = outerBaseRing != null ? outerBaseRing.Count : 0;
        if (n < 3 || wallRing == null || wallRing.Count != n)
            return;

        int lowerOuterStart = frontVertexCount; // first original ring starts at vertex 0.
        int wallTopStart = verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 wallTop = wallRing[i];
            Vector3 wallBottom = wallTop + down;
            verts.Add(wallTop);
            uvs.Add(new Vector2(wallTop.x * 0.2f, wallTop.z * 0.2f));
            verts.Add(wallBottom);
            uvs.Add(new Vector2(wallBottom.x * 0.2f, wallBottom.z * 0.2f));
        }

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int topOuterI = i;
            int topOuterJ = j;
            int bottomOuterI = lowerOuterStart + i;
            int bottomOuterJ = lowerOuterStart + j;
            int wallTopI = wallTopStart + i * 2;
            int wallTopJ = wallTopStart + j * 2;
            int wallBottomI = wallTopI + 1;
            int wallBottomJ = wallTopJ + 1;

            // Visible roof thickness around the exterior lip: same material as the roof.
            AddQuad(roofTris, topOuterI, topOuterJ, bottomOuterJ, bottomOuterI);

            // Plateau horizontal marron entre mur et débord (visible dessus et depuis l'intérieur sous la face).
            AddQuadTwoSidedDupVerts(verts, uvs, connectorTris,
                verts[wallTopI], verts[wallTopJ], verts[topOuterJ], verts[topOuterI]);

            // Soffit + cloison verticale au mur : géométrie dupliquée pour deux faces sans normales incohérentes.
            AddQuadTwoSidedDupVerts(verts, uvs, connectorTris,
                verts[bottomOuterI], verts[bottomOuterJ], verts[wallBottomJ], verts[wallBottomI]);

            AddQuadTwoSidedDupVerts(verts, uvs, connectorTris,
                verts[wallBottomI], verts[wallBottomJ], verts[wallTopJ], verts[wallTopI]);
        }
    }

    static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    static Vector2 UvXZ(Vector3 v) => new Vector2(v.x * 0.2f, v.z * 0.2f);

    /// <summary>Double face avec sommets dupliqués : évite les artefacts de normales liés aux triangles opposés sur les mêmes indices.</summary>
    static void AddQuadTwoSidedDupVerts(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        int i0 = verts.Count;
        verts.Add(a);
        uvs.Add(UvXZ(a));
        int i1 = verts.Count;
        verts.Add(b);
        uvs.Add(UvXZ(b));
        int i2 = verts.Count;
        verts.Add(c);
        uvs.Add(UvXZ(c));
        int i3 = verts.Count;
        verts.Add(d);
        uvs.Add(UvXZ(d));
        AddQuad(tris, i0, i1, i2, i3);

        int j0 = verts.Count;
        verts.Add(a);
        uvs.Add(UvXZ(a));
        int j1 = verts.Count;
        verts.Add(b);
        uvs.Add(UvXZ(b));
        int j2 = verts.Count;
        verts.Add(c);
        uvs.Add(UvXZ(c));
        int j3 = verts.Count;
        verts.Add(d);
        uvs.Add(UvXZ(d));
        AddQuad(tris, j0, j3, j2, j1);
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
        roofHeightMeters = Mathf.Clamp(roofHeightMeters + delta, MinRoofHeightMeters, MaxRoofHeightMeters);
        RebuildNow();
    }

    public void AdjustRoundness(float delta)
    {
        roundness = Mathf.Clamp01(roundness + delta);
        RebuildNow();
    }

    public void AdjustOverhang(float delta)
    {
        overhangMeters = Mathf.Clamp(overhangMeters + delta, MinOverhangMeters, MaxOverhangMeters);
        RebuildNow();
    }

    /// <summary>
    /// Active le second sommet (faîtage). <paramref name="defaultHalfFootprintOffset"/> = demi-envergure typique pour placer le second faîte sur +X local du centroïde.
    /// </summary>
    public void EnableSecondaryRidgePeak(float defaultHalfFootprintOffset)
    {
        MigrateLegacyRidgePeaks();
        secondaryRidgePeakEnabled = true;
        secondaryPeakHeightMeters = Mathf.Clamp(roofHeightMeters, MinRoofHeightMeters, MaxRoofHeightMeters);
        if (extraRidgePeakOffsetsXZ == null)
            extraRidgePeakOffsetsXZ = new List<Vector2>();
        extraRidgePeakOffsetsXZ.Clear();
        float d = Mathf.Clamp(defaultHalfFootprintOffset, 0.12f, 4f);
        extraRidgePeakOffsetsXZ.Add(new Vector2(d, 0f));
        SyncLegacySecondaryOffsetFromList();
        RebuildNow();
    }

    /// <summary>Ajoute un sommet de faîtage après le premier (clic droit répété sur le jaune).</summary>
    /// <remarks>
    /// Le 2ᵉ pic ne doit pas être placé « vers le centre sur le même rayon » que le 1ᵉʳ (ancien first*0.5) :
    /// la grille de snap le ramenait vers le jaune. On propose l’opposé du 1ᵉʳ, puis des directions perpendiculaires.
    /// </remarks>
    public void AppendExtraRidgePeak()
    {
        MigrateLegacyRidgePeaks();
        if (extraRidgePeakOffsetsXZ == null)
            extraRidgePeakOffsetsXZ = new List<Vector2>();
        secondaryRidgePeakEnabled = true;
        secondaryPeakHeightMeters = Mathf.Clamp(roofHeightMeters, MinRoofHeightMeters, MaxRoofHeightMeters);

        Vector2 proposed;
        if (extraRidgePeakOffsetsXZ.Count == 0)
        {
            proposed = new Vector2(0.5f, 0f);
        }
        else if (extraRidgePeakOffsetsXZ.Count == 1)
        {
            Vector2 first = extraRidgePeakOffsetsXZ[0];
            proposed = first.sqrMagnitude > 1e-10f ? -first : new Vector2(-0.5f, 0f);
        }
        else
        {
            Vector2 lastOff = extraRidgePeakOffsetsXZ[extraRidgePeakOffsetsXZ.Count - 1];
            Vector2 firstOff = extraRidgePeakOffsetsXZ[0];
            float mag = Mathf.Max(0.15f, lastOff.magnitude, firstOff.magnitude * 0.35f);
            Vector2 perp = new Vector2(-lastOff.y, lastOff.x);
            if (perp.sqrMagnitude < 1e-10f)
                perp = new Vector2(-firstOff.y, firstOff.x);
            if (perp.sqrMagnitude < 1e-10f)
                perp = new Vector2(1f, 0f);
            proposed = perp.normalized * mag;
        }

        extraRidgePeakOffsetsXZ.Add(proposed);
        SyncLegacySecondaryOffsetFromList();
        RebuildNow();
    }

    public int GetExtraRidgePeakCount()
    {
        MigrateLegacyRidgePeaks();
        if (!secondaryRidgePeakEnabled)
            return 0;
        return extraRidgePeakOffsetsXZ != null ? extraRidgePeakOffsetsXZ.Count : 0;
    }

    public void MigrateLegacyRidgePeaks()
    {
        if (extraRidgePeakOffsetsXZ == null)
            extraRidgePeakOffsetsXZ = new List<Vector2>();
        if (secondaryRidgePeakEnabled && extraRidgePeakOffsetsXZ.Count == 0 && secondaryRidgePeakOffsetXZ.sqrMagnitude > 1e-8f)
            extraRidgePeakOffsetsXZ.Add(secondaryRidgePeakOffsetXZ);
        SyncLegacySecondaryOffsetFromList();
    }

    public void SyncLegacySecondaryOffsetFromList()
    {
        if (extraRidgePeakOffsetsXZ != null && extraRidgePeakOffsetsXZ.Count > 0)
            secondaryRidgePeakOffsetXZ = extraRidgePeakOffsetsXZ[0];
        else
            secondaryRidgePeakOffsetXZ = Vector2.zero;
    }

    public void DisableSecondaryRidgePeak()
    {
        if (!secondaryRidgePeakEnabled && (extraRidgePeakOffsetsXZ == null || extraRidgePeakOffsetsXZ.Count == 0))
            return;
        secondaryRidgePeakEnabled = false;
        if (extraRidgePeakOffsetsXZ != null)
            extraRidgePeakOffsetsXZ.Clear();
        secondaryRidgePeakOffsetXZ = Vector2.zero;
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

    /// <summary>
    /// Point « hub » pour apex jaune, rayons du toit et débords : à l’intérieur du contour pour les L et autres concaves,
    /// où la simple moyenne des sommets peut être hors polygone.
    /// </summary>
    public static Vector2 ComputeFootprintHubXZ(List<Vector2> footprintRingOpen)
    {
        var ring = footprintRingOpen;
        int n = ring != null ? ring.Count : 0;
        if (n < 3)
            return n > 0 ? ring[0] : Vector2.zero;

        Vector2 areaCentroid = ComputeAreaWeightedCentroidXZ(ring);
        if (PointInPolygonXZ(areaCentroid, ring))
            return areaCentroid;

        Vector2 vertexAvg = ComputeVertexAverageXZ(ring);
        if (PointInPolygonXZ(vertexAvg, ring))
            return vertexAvg;

        Vector2 bboxCenter = ComputeBBoxCenterXZ(ring);
        if (PointInPolygonXZ(bboxCenter, ring))
            return bboxCenter;

        for (int step = 1; step <= 32; step++)
        {
            Vector2 p = Vector2.Lerp(areaCentroid, bboxCenter, step / 32f);
            if (PointInPolygonXZ(p, ring))
                return p;
        }

        for (int i = 0; i < n; i++)
        {
            Vector2 mid = (ring[i] + ring[(i + 1) % n]) * 0.5f;
            if (PointInPolygonXZ(mid, ring))
                return mid;
        }

        return areaCentroid;
    }

    static Vector2 ComputeVertexAverageXZ(List<Vector2> ring)
    {
        Vector2 s = Vector2.zero;
        for (int i = 0; i < ring.Count; i++)
            s += ring[i];
        return s / Mathf.Max(1, ring.Count);
    }

    static Vector2 ComputeBBoxCenterXZ(List<Vector2> ring)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < ring.Count; i++)
        {
            Vector2 v = ring[i];
            minX = Mathf.Min(minX, v.x);
            minZ = Mathf.Min(minZ, v.y);
            maxX = Mathf.Max(maxX, v.x);
            maxZ = Mathf.Max(maxZ, v.y);
        }

        return new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
    }

    static Vector2 ComputeAreaWeightedCentroidXZ(List<Vector2> poly)
    {
        int n = poly.Count;
        double cx = 0.0, cy = 0.0;
        double signedDoubleArea = 0.0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double cross = (double)poly[i].x * poly[j].y - (double)poly[j].x * poly[i].y;
            signedDoubleArea += cross;
            cx += ((double)poly[i].x + poly[j].x) * cross;
            cy += ((double)poly[i].y + poly[j].y) * cross;
        }

        if (Mathf.Abs((float)signedDoubleArea) < 1e-12f)
            return ComputeVertexAverageXZ(poly);
        double inv = 1.0 / (3.0 * signedDoubleArea);
        return new Vector2((float)(cx * inv), (float)(cy * inv));
    }

    /// <summary>Polygone simple en XZ ; contour fermé sans vertex dupliqué au dernier index.</summary>
    static bool PointInPolygonXZ(Vector2 p, List<Vector2> poly)
    {
        int n = poly.Count;
        if (n < 3)
            return false;

        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];
            if ((pi.y > p.y) != (pj.y > p.y))
            {
                float xInt = (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 1e-30f) + pi.x;
                if (p.x < xInt)
                    inside = !inside;
            }
        }

        return inside;
    }

    static List<Vector3> BuildSubdividedClosedRing(List<Vector3> corners, int subdivisionsPerEdge)
    {
        int count = corners != null ? corners.Count : 0;
        var ring = new List<Vector3>(Mathf.Max(0, count * Mathf.Max(1, subdivisionsPerEdge)));
        if (count < 3)
            return ring;

        int steps = Mathf.Max(1, subdivisionsPerEdge);
        for (int i = 0; i < count; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[(i + 1) % count];
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                ring.Add(Vector3.Lerp(a, b, t));
            }
        }

        return ring;
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

    /// <summary>
    /// Plus proche point sur la réunion des segments centroïde → chaque sommet de faîtage additionnel.
    /// Une polyligne chaînée P1→P2 faisait court-circuiter le centroïde (trous / maille incorrecte).
    /// </summary>
    public static Vector2 ClosestPointOnCentroidSpokesXZ(Vector2 p, Vector2 centroid, List<Vector2> offsetsFromCentroid)
    {
        return ClosestPointOnCentroidSpokesXZ(p, centroid, offsetsFromCentroid, out _);
    }

    /// <summary>
    /// Cible XZ du faîtage pour une colonne du mesh : uniquement sur les segments « sommet jaune → pic additionnel » (rayons).
    /// Ne suit pas une chaîne P→Q entre deux pics ; après chaque pic la géométrie repasse par le centre avant un autre bras — évite cordes et vides.
    /// </summary>
    public static Vector2 RidgeTargetXZThroughCentralHub(Vector2 footprintXZ, Vector2 centroid, List<Vector2> peakOffsetsFromCentroid)
    {
        if (peakOffsetsFromCentroid == null || peakOffsetsFromCentroid.Count == 0)
            return centroid;
        return ClosestPointOnCentroidSpokesXZ(footprintXZ, centroid, peakOffsetsFromCentroid);
    }

    /// <summary>
    /// Chemin de faîtage compatible avec le mesh en anneaux : les points situés de part et d'autre du centre
    /// sont ordonnés comme A -> C -> B, au lieu d'être chaînés directement A -> B.
    /// </summary>
    public static List<Vector2> BuildRidgePathThroughCentroidXZ(Vector2 centroid, List<Vector2> offsetsFromCentroid)
    {
        if (offsetsFromCentroid == null || offsetsFromCentroid.Count == 0)
            return null;

        Vector2 axis = Vector2.zero;
        float bestLenSq = 0f;
        for (int i = 0; i < offsetsFromCentroid.Count; i++)
        {
            float sq = offsetsFromCentroid[i].sqrMagnitude;
            if (sq > bestLenSq)
            {
                bestLenSq = sq;
                axis = offsetsFromCentroid[i];
            }
        }

        if (bestLenSq < 1e-12f)
            axis = Vector2.right;
        else
            axis.Normalize();

        var negative = new List<Vector2>();
        var positive = new List<Vector2>();
        for (int i = 0; i < offsetsFromCentroid.Count; i++)
        {
            Vector2 off = offsetsFromCentroid[i];
            if (off.sqrMagnitude < 1e-12f)
                continue;

            if (Vector2.Dot(off, axis) < 0f)
                negative.Add(off);
            else
                positive.Add(off);
        }

        negative.Sort((a, b) => Vector2.Dot(a, axis).CompareTo(Vector2.Dot(b, axis)));
        positive.Sort((a, b) => Vector2.Dot(a, axis).CompareTo(Vector2.Dot(b, axis)));

        var path = new List<Vector2>(negative.Count + positive.Count + 1);
        for (int i = 0; i < negative.Count; i++)
            path.Add(centroid + negative[i]);
        path.Add(centroid);
        for (int i = 0; i < positive.Count; i++)
            path.Add(centroid + positive[i]);

        if (path.Count == 1)
            path.Add(centroid + axis * 0.001f);
        return path;
    }

    public static Vector2 ClosestPointOnPolylineXZ(Vector2 p, List<Vector2> path)
    {
        if (path == null || path.Count == 0)
            return p;
        if (path.Count == 1)
            return path[0];

        Vector2 best = path[0];
        float bestSq = float.MaxValue;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 q = ClosestPointOnSegmentXZ(p, path[i], path[i + 1]);
            float dsq = (p - q).sqrMagnitude;
            if (dsq < bestSq)
            {
                bestSq = dsq;
                best = q;
            }
        }

        return best;
    }

    public static Vector2 ClosestPointOnCentroidSpokesXZ(
        Vector2 p,
        Vector2 centroid,
        List<Vector2> offsetsFromCentroid,
        out int spokeIndex)
    {
        spokeIndex = -1;
        if (offsetsFromCentroid == null || offsetsFromCentroid.Count == 0)
            return centroid;

        Vector2 best = centroid;
        float bestSq = float.MaxValue;

        for (int i = 0; i < offsetsFromCentroid.Count; i++)
        {
            Vector2 end = centroid + offsetsFromCentroid[i];
            Vector2 q = ClosestPointOnSegmentXZ(p, centroid, end);
            float dsq = (p - q).sqrMagnitude;
            if (dsq < bestSq)
            {
                bestSq = dsq;
                best = q;
                spokeIndex = i;
            }
        }

        return best;
    }

    static float SignedAreaXZPoly(List<Vector2> poly)
    {
        double a = 0.0;
        int n = poly != null ? poly.Count : 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += (double)poly[i].x * poly[j].y - (double)poly[j].x * poly[i].y;
        }
        return (float)(0.5 * a);
    }

    static bool LineLineIntersectXZ(Vector2 origin0, Vector2 dir0, Vector2 origin1, Vector2 dir1, out Vector2 hit)
    {
        hit = default;
        float cross = dir0.x * dir1.y - dir0.y * dir1.x;
        if (Mathf.Abs(cross) < 1e-7f)
            return false;
        Vector2 d = origin1 - origin0;
        float t = (d.x * dir1.y - d.y * dir1.x) / cross;
        hit = origin0 + dir0 * t;
        return true;
    }

    /// <summary>
    /// Décale le polygone du footprint vers l'intérieur du plan, perpendiculairement à chaque façade (comme un offset « dans le mur »).
    /// Polygone CCW en XZ ; formes très concaves peuvent échouer — repli sans inset.
    /// </summary>
    static bool TryInsetPolygonXZPerpendicular(List<Vector2> poly, float inset, out List<Vector2> result)
    {
        result = null;
        int n = poly != null ? poly.Count : 0;
        if (n < 3)
            return false;
        if (inset <= 1e-6f)
        {
            result = new List<Vector2>(poly);
            return true;
        }

        var work = new List<Vector2>(poly);
        if (SignedAreaXZPoly(work) < 0f)
            work.Reverse();

        result = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = work[(i + n - 1) % n];
            Vector2 cur = work[i];
            Vector2 next = work[(i + 1) % n];

            Vector2 e0 = cur - prev;
            Vector2 e1 = next - cur;
            float l0 = e0.magnitude;
            float l1 = e1.magnitude;
            if (l0 < 1e-7f || l1 < 1e-7f)
                return false;

            Vector2 n0 = new Vector2(-e0.y, e0.x) / l0;
            Vector2 n1 = new Vector2(-e1.y, e1.x) / l1;

            Vector2 o0 = prev + n0 * inset;
            Vector2 o1 = cur + n1 * inset;
            Vector2 d0 = e0 / l0;
            Vector2 d1 = e1 / l1;

            if (!LineLineIntersectXZ(o0, d0, o1, d1, out Vector2 hit))
                return false;

            result.Add(hit);
        }

        return true;
    }

    /// <summary>Hauteur normalisée [0,1] pour une progression radiale (bord → hub), alignée sur le mesh.</summary>
    public float RoofProfileYNormalized(float radial01)
    {
        radial01 = Mathf.Clamp01(radial01);
        if (planarHipRoof)
            return radial01;
        return EvaluateDomeProfile(radial01, Mathf.Clamp01(roundness));
    }

    /// <inheritdoc cref="RoofProfileYNormalized(float)"/>
    /// <param name="roundness01">Roundness hypothétique (poignées / prévisualisation).</param>
    public float RoofProfileYNormalized(float radial01, float roundness01)
    {
        radial01 = Mathf.Clamp01(radial01);
        if (planarHipRoof)
            return radial01;
        return EvaluateDomeProfile(radial01, Mathf.Clamp01(roundness01));
    }

    /// <summary>
    /// Dome profile from edge (0) to center (1).
    /// roundness in [0..1]:
    /// - &lt; 0.5 : inverted dome family
    /// - = 0.5 : near-linear cone-like profile
    /// - &gt; 0.5 : normal dome family
    /// </summary>
    public static float EvaluateDomeProfile(float radial01, float roundness01)
    {
        radial01 = Mathf.Clamp01(radial01);
        roundness01 = Mathf.Clamp01(roundness01);
        float s = roundness01 * 2f - 1f; // [-1..1]
        if (s >= 0f)
        {
            // Normal dome: above the neutral cone, with a flattened tangent at the summit.
            float exponent = Mathf.Lerp(1.0f, 3.4f, s);
            return 1f - Mathf.Pow(Mathf.Max(1e-4f, 1f - radial01), exponent);
        }

        // Inverted dome: below the neutral cone between edge and center.
        float inv = -s;
        float exponentInv = Mathf.Lerp(1.0f, 3.2f, inv);
        return Mathf.Pow(Mathf.Max(1e-4f, radial01), exponentInv);
    }

    /// <summary>
    /// Inverse helper used by roof controls:
    /// given radial position (0 edge -> 1 center) and normalized height,
    /// estimate the roundness parameter that best matches the dome profile.
    /// </summary>
    public static float EstimateRoundnessFromSample(float radial01, float yNorm)
    {
        radial01 = Mathf.Clamp(radial01, 1e-4f, 0.9999f);
        yNorm = Mathf.Clamp(yNorm, 1e-4f, 0.9999f);
        float linear = radial01;
        float curve = yNorm - linear;
        // curve > 0 => normal dome side ; curve < 0 => inverted side
        return Mathf.Clamp01(0.5f + curve * 1.2f);
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

        // Reuse wall material by default (never assign null — sinon tout le maillage perd ses textures).
        WallObject wall = GetComponent<WallObject>();
        MeshRenderer wallMr = wall != null ? wall.GetComponent<MeshRenderer>() : null;
        Material roofMaterial = wallMr != null && wallMr.sharedMaterial != null
            ? wallMr.sharedMaterial
            : (_mr.sharedMaterial != null ? _mr.sharedMaterial : null);
        if (roofMaterial == null)
            roofMaterial = EnsureFallbackRoofSkinMaterial();

        Material connectorMaterial = EnsureConnectorMaterial();
        _mr.sharedMaterials = new[] { roofMaterial, connectorMaterial };
    }

    Material EnsureFallbackRoofSkinMaterial()
    {
        if (_roofFallbackSkinMaterial != null)
            return _roofFallbackSkinMaterial;

        Shader shader = TryFindShader(
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/SimpleLit",
            "Universal Render Pipeline/BakedLit",
            "Standard",
            "Legacy Shaders/Diffuse");
        if (shader == null)
            shader = Shader.Find("Hidden/InternalErrorShader");

        _roofFallbackSkinMaterial = new Material(shader)
        {
            name = "HouseRoof Fallback Skin",
            hideFlags = HideFlags.DontSave,
            color = Color.white
        };
        if (_roofFallbackSkinMaterial.HasProperty("_BaseColor"))
            _roofFallbackSkinMaterial.SetColor("_BaseColor", Color.white);
        if (_roofFallbackSkinMaterial.HasProperty("_Smoothness"))
            _roofFallbackSkinMaterial.SetFloat("_Smoothness", 0.25f);
        if (_roofFallbackSkinMaterial.HasProperty("_Metallic"))
            _roofFallbackSkinMaterial.SetFloat("_Metallic", 0f);
        return _roofFallbackSkinMaterial;
    }

    Material EnsureConnectorMaterial()
    {
        Shader shader = ResolveConnectorShader();
        if (_connectorMaterial == null)
            _connectorMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        else if (_connectorMaterial.shader != shader && shader != null)
            _connectorMaterial.shader = shader;

        _connectorMaterial.name = "Roof Eave Connector Dark Brown Matte";
        ApplyConnectorBrownSurface(_connectorMaterial);
        return _connectorMaterial;
    }

    static Shader TryFindShader(params string[] paths)
    {
        if (paths == null)
            return null;
        for (int i = 0; i < paths.Length; i++)
        {
            Shader s = Shader.Find(paths[i]);
            if (s != null)
                return s;
        }

        return null;
    }

    /// <summary>En URP, Standard/Unlit intégrés peuvent être absents : chaîne de repli pour éviter un Material invalide.</summary>
    static Shader ResolveConnectorShader()
    {
        Shader s = TryFindShader(
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/SimpleLit",
            "Unlit/Color",
            "Unlit/Texture",
            "Sprites/Default",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/BakedLit",
            "Standard",
            "Legacy Shaders/Diffuse");
        return s != null ? s : Shader.Find("Hidden/InternalErrorShader");
    }

    static void ApplyConnectorBrownSurface(Material mat)
    {
        if (mat == null)
            return;

        Color brown = new Color(0.46f, 0.32f, 0.22f, 1f);
        mat.color = brown;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", brown);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", brown);

        bool likelyUnlit = mat.shader != null &&
                           (mat.shader.name.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (!likelyUnlit)
        {
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0f);
        }

        if (mat.HasProperty("_Cull"))
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
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
            h = h * 31 + RoofMeshGenerationVersion;
            WallObject wall = GetComponent<WallObject>();
            WallEditShape edit = GetComponent<WallEditShape>();
            if (wall != null)
            {
                h = h * 31 + Mathf.RoundToInt(wall.height * 1000f);
                h = h * 31 + Mathf.RoundToInt(wall.thickness * 1000f);
            }
            h = h * 31 + Mathf.RoundToInt(roofHeightMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(overhangMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(roofThicknessMeters * 1000f);
            h = h * 31 + (secondaryRidgePeakEnabled ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(secondaryPeakHeightMeters * 1000f);
            MigrateLegacyRidgePeaks();
            int nr = extraRidgePeakOffsetsXZ != null ? extraRidgePeakOffsetsXZ.Count : 0;
            h = h * 31 + nr;
            for (int ri = 0; ri < nr; ri++)
            {
                h = h * 31 + Mathf.RoundToInt(extraRidgePeakOffsetsXZ[ri].x * 1000f);
                h = h * 31 + Mathf.RoundToInt(extraRidgePeakOffsetsXZ[ri].y * 1000f);
            }
            h = h * 31 + (planarHipRoof ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(roundness * 1000f);
            h = h * 31 + Mathf.RoundToInt(yOffsetAboveWallTop * 1000f);
            h = h * 31 + Mathf.RoundToInt(RoofBuiltInVerticalLiftMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(EaveInsetPerpendicularToWallMeters * 1000f);
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
                + "\"timestamp\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                + "}";
            File.AppendAllText("debug-243ebf.log", line + Environment.NewLine);
        }
        catch
        {
            // Never break mesh generation because of debug logging.
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
