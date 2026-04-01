using System.Collections.Generic;
using UnityEngine;

public sealed partial class WallCladdingGenerator
{
    [Header("Triangle D-Bollard Debug")]
    [SerializeField] private bool debugColorizeTriangleBollardFaces = false;
    private const bool forceDebugColorizeTriangleBollardFacesFromCode = false;

    private bool EffectiveDebugColorizeTriangleBollardFaces()
    {
        return forceDebugColorizeTriangleBollardFacesFromCode || debugColorizeTriangleBollardFaces;
    }

    private void GenerateClosedLoopTriangleEndQuoins(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float yMin,
        float yMax,
        System.Random rng,
        ref int stoneIndex)
    {
        if (profile == null || profile.stone == null || profile.stone.endQuoins == null)
            return;

        EndQuoinSettings settings = profile.stone.endQuoins;
        if (!settings.enabled || wall == null || !wall.closedLoop || samples == null || samples.Count != 3)
            return;

        for (int i = 0; i < samples.Count; i++)
        {
            PathSample prev = samples[i];
            PathSample next = samples[(i + 1) % samples.Count];
            Vector3 cornerPoint = prev.b;
            float cornerAngleDeg = Vector3.Angle(-prev.tangent, next.tangent);
            if (cornerAngleDeg > 140f)
                continue;
            bool useConnectorForAcuteCorner = cornerAngleDeg < 35f;
            // Keep only one dedicated D-bollard on acute triangle corners.
            bool useBollardForVeryAcuteCorner = cornerAngleDeg < 35f;

            Vector3 inwardA = -prev.tangent.normalized;
            Vector3 inwardB = next.tangent.normalized;
            Vector3 inwardBisector = (inwardA + inwardB).normalized;
            if (inwardBisector.sqrMagnitude < 0.000001f)
                inwardBisector = inwardA.sqrMagnitude > 0.000001f ? inwardA : inwardB;

            Vector3 outwardA = Vector3.Cross(Vector3.up, prev.tangent).normalized * sideSign;
            Vector3 outwardB = Vector3.Cross(Vector3.up, next.tangent).normalized * sideSign;
            Vector3 outward = (outwardA + outwardB).normalized;
            if (outward.sqrMagnitude < 0.000001f)
                outward = outwardA.sqrMagnitude > 0.000001f ? outwardA : outwardB;

            if (useBollardForVeryAcuteCorner)
            {
                EmitTriangleAcuteCornerBollard(
                    profile,
                    root,
                    stoneMaterial,
                    settings,
                    cornerPoint,
                    inwardA,
                    inwardB,
                    inwardBisector,
                    outwardA,
                    outwardB,
                    outward,
                    cornerAngleDeg,
                    yMin,
                    yMax,
                    rng,
                    ref stoneIndex);
                // Stacked acute-corner bollards (varying span along walls per row).
                continue;
            }

            float rowBottom = yMin;
            int rowIndex = 0;
            while (rowBottom < yMax - 0.10f)
            {
                float rowHeight = settings.targetHeight * RandomRange(rng, 1f - settings.rowHeightJitter, 1f + settings.rowHeightJitter);
                rowHeight = Mathf.Clamp(
                    rowHeight,
                    profile.stone.minStoneHeight * 1.15f,
                    Mathf.Max(profile.stone.minStoneHeight * 1.25f, profile.stone.maxStoneHeight * 1.75f));
                bool isLastQuoinRow = (rowBottom + rowHeight + settings.verticalSpacing) >= yMax;
                float topOvershoot = isLastQuoinRow ? Mathf.Max(wall.thickness * 0.18f, profile.stone.surfaceProtrusion * 1.45f, 0.04f) : 0f;
                rowHeight = Mathf.Min(rowHeight, yMax - rowBottom + topOvershoot);
                if (rowHeight < 0.10f)
                    break;

                float baseLength = RandomRange(rng, settings.minLength, settings.maxLength);
                bool useA = (rowIndex & 1) == 0;
                float altScale = useConnectorForAcuteCorner
                    ? 1f
                    : (useA ? settings.alternateLongScale : settings.alternateShortScale);
                float length = baseLength * altScale * 1.08f * RandomRange(rng, 1f - settings.lengthJitter, 1f + settings.lengthJitter);
                length = Mathf.Clamp(length, settings.minLength * 0.85f, settings.maxLength * 1.35f);

                float revealAtCorner = Mathf.Clamp(
                    Mathf.Max(wall.thickness * 0.10f, settings.extraOutsideDepth * 0.55f),
                    0.02f,
                    Mathf.Max(0.02f, length * 0.20f));

                float fullDepth = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f + 0.04f, wall.thickness + 0.01f);
                float centerY = rowBottom + rowHeight * 0.5f;
                if (useConnectorForAcuteCorner)
                {
                    Vector3 center = cornerPoint;
                    float inwardPush = Mathf.Max(0f, length * 0.34f - settings.edgeInset - revealAtCorner * 0.45f);
                    center += inwardBisector * inwardPush;
                    Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
                    center += Vector3.up * centerY;

                    WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);
                    Mesh mesh = BuildTerminalHalfRoundStoneMesh(
                        module,
                        length,
                        rowHeight,
                        Mathf.Max(profile.stone.surfaceProtrusion * 1.08f, 0.01f),
                        Mathf.Max(fullDepth, profile.stone.minStoneDepth),
                        profile.stone.facePlaneJitter,
                        profile.stone.uvMetersPerUnit,
                        rng,
                        true);
                    if (mesh != null && mesh.vertexCount > 0)
                    {
                        GameObject go = new GameObject($"TriangleEndQuoin_{i:00}_{rowIndex:00}");
                        go.transform.SetParent(root, false);
                        go.transform.localPosition = transform.InverseTransformPoint(center);
                        go.transform.localRotation = Quaternion.LookRotation(
                            transform.InverseTransformDirection(rot * Vector3.forward),
                            transform.InverseTransformDirection(rot * Vector3.up));
                        go.transform.localScale = Vector3.one;

                        MeshFilter mf = go.AddComponent<MeshFilter>();
                        MeshRenderer mr = go.AddComponent<MeshRenderer>();
                        mf.sharedMesh = mesh;
                        mr.sharedMaterial = stoneMaterial;
                        if (EffectiveDebugColorizeTriangleBollardFaces())
                            ApplyDebugFaceColors(mesh, mr, stoneMaterial);
                        ApplyPerStoneMaterialVariation(profile, mr, rng, true);
                        stoneIndex++;
                    }
                }
                else
                {
                    // Rectangle-style stacked corner logic for triangle corners >= 35°.
                    float cornerWidth = Mathf.Clamp(
                        Mathf.Max(length * 0.78f, fullDepth * 1.10f),
                        settings.minLength * 0.90f,
                        settings.maxLength * 1.60f);

                    Vector3 outwardDir = useA ? outwardA : outwardB;
                    Quaternion rot = Quaternion.LookRotation(outwardDir, Vector3.up);
                    ComputeCornerLateralExtension(profile, settings, cornerWidth, useA, rng, out bool widenRightSide, out float sideExtra);

                    float sideOffset = Mathf.Max(0f, wall.thickness * 0.5f - profile.general.sideInset);
                    Vector3 exteriorCorner = cornerPoint + (outwardA + outwardB) * sideOffset;

                    float cornerAnchorInset = Mathf.Clamp(
                        Mathf.Max(profile.stone.horizontalSpacing * 0.18f, 0.002f),
                        0.001f,
                        0.006f);
                    float halfLen = cornerWidth * 0.5f;
                    float baseAnchorX = useA
                        ? (-halfLen + cornerAnchorInset)
                        : ( halfLen - cornerAnchorInset);
                    float anchorX = baseAnchorX;
                    float faceReferenceOffsetX = useA ? -cornerFaceReferenceShift : cornerFaceReferenceShift;
                    anchorX += faceReferenceOffsetX;
                    anchorX = ApplyCornerLateralStackAlignment(anchorX);
                    anchorX = ResolveOtherWallColumnOffset(useA, anchorX);
                    Vector3 localInnerCornerAnchor = new Vector3(anchorX, 0f, 0f);
                    Vector3 center = exteriorCorner - (rot * localInnerCornerAnchor) + Vector3.up * centerY;

                    Vector3 cornerBisector = (outwardA + outwardB).normalized;
                    float cornerExposeNudge = Mathf.Clamp(
                        Mathf.Max(profile.stone.horizontalSpacing * 0.16f, profile.stone.surfaceProtrusion * 0.18f),
                        0.0015f,
                        0.006f);
                    center += cornerBisector * cornerExposeNudge;

                    float backSideBias = Mathf.Clamp(
                        settings.extraOutsideDepth * 0.24f + profile.stone.surfaceProtrusion * 0.18f,
                        0.003f,
                        0.011f);
                    if (alignExteriorCornerColumn)
                        center += cornerBisector * (cornerExposeNudge - backSideBias);
                    else
                        center -= outwardDir * backSideBias;

                    float sideExtrusionT = EvaluateCornerExtrusionStrength(EffectiveCornerSideExtensionMultiplier());
                    float sideWallPop = Mathf.Clamp(
                        Mathf.Max(profile.stone.surfaceProtrusion * 10f, rowHeight * 0.06f) * sideExtrusionT,
                        0f,
                        Mathf.Max(0.200f, wall.thickness * 0.45f));
                    if (!alignExteriorCornerColumn)
                    {
                        float signedSideWallPop = ResolveCornerSignedSideOffset(useA, sideWallPop, EffectiveCornerSideExtensionMultiplier());
                        center += (rot * Vector3.right) * signedSideWallPop;
                    }

                    WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);
                    float anchorShiftX = anchorX - baseAnchorX;
                    float meshFollowExtra = Mathf.Abs(anchorShiftX);
                    bool meshFollowRightSide = anchorShiftX >= 0f;
                    bool widenRightSideForMesh = growOppositeVoidLateralFace ? widenRightSide : !widenRightSide;
                    if (meshFollowExtra > 0.0001f)
                    {
                        widenRightSideForMesh = meshFollowRightSide;
                        sideExtra += meshFollowExtra;
                    }

                    Mesh mesh = BuildCornerFourFaceReliefMesh(
                        module,
                        cornerWidth,
                        rowHeight,
                        fullDepth,
                        widenRightSideForMesh,
                        sideExtra,
                        profile.stone.facePlaneJitter,
                        profile.stone.uvMetersPerUnit,
                        rng);
                    if (mesh != null && mesh.vertexCount > 0)
                    {
                        GameObject go = new GameObject($"TriangleCornerQuoin_{i:00}_{rowIndex:00}");
                        go.transform.SetParent(root, false);
                        go.transform.localPosition = transform.InverseTransformPoint(center);
                        go.transform.localRotation = Quaternion.LookRotation(
                            transform.InverseTransformDirection(rot * Vector3.forward),
                            transform.InverseTransformDirection(rot * Vector3.up));
                        go.transform.localScale = Vector3.one;

                        MeshFilter mf = go.AddComponent<MeshFilter>();
                        MeshRenderer mr = go.AddComponent<MeshRenderer>();
                        mf.sharedMesh = mesh;
                        mr.sharedMaterial = stoneMaterial;
                        if (EffectiveDebugColorizeTriangleBollardFaces())
                            ApplyDebugFaceColors(mesh, mr, stoneMaterial);
                        ApplyPerStoneMaterialVariation(profile, mr, rng, true);
                        stoneIndex++;
                    }
                }

                rowBottom += rowHeight + settings.verticalSpacing;
                rowIndex++;
            }
        }
    }

    /// <summary>Horizontal unit vector: cladding “outside” for acute corners (same basis as <c>outwardA/B</c>).</summary>
    private static Vector3 ComputeBollardOutwardWorldDir(
        Vector3 outwardAWorld,
        Vector3 outwardBWorld,
        Vector3 claddingOutwardWorld,
        Vector3 inwardAlongWallA,
        Vector3 inwardAlongWallB,
        Vector3 inwardBisector)
    {
        Vector3 u1 = Vector3.ProjectOnPlane(inwardAlongWallA, Vector3.up);
        Vector3 u2 = Vector3.ProjectOnPlane(inwardAlongWallB, Vector3.up);
        Vector3 sumOut = Vector3.ProjectOnPlane(outwardAWorld + outwardBWorld, Vector3.up);
        Vector3 fromPath = Vector3.ProjectOnPlane(claddingOutwardWorld, Vector3.up);
        Vector3 bi = Vector3.ProjectOnPlane(inwardBisector, Vector3.up);
        if (bi.sqrMagnitude > 1e-12f)
            bi.Normalize();
        Vector3 fromTriangle = -bi;

        Vector3 dir;
        if (sumOut.sqrMagnitude > 1e-8f)
            dir = sumOut.normalized;
        else if (fromPath.sqrMagnitude > 1e-8f)
            dir = fromPath.normalized;
        else if (u1.sqrMagnitude > 1e-12f && u2.sqrMagnitude > 1e-12f)
        {
            u1.Normalize();
            u2.Normalize();
            Vector3 nA = Vector3.Cross(Vector3.up, u1).normalized;
            Vector3 nB = Vector3.Cross(Vector3.up, u2).normalized;
            Vector3 fromWalls = (nA + nB).normalized;
            dir = fromWalls.sqrMagnitude > 1e-12f ? fromWalls : fromTriangle;
        }
        else
            dir = fromTriangle;

        return dir;
    }

    private void EmitTriangleAcuteCornerBollard(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        EndQuoinSettings settings,
        Vector3 cornerPoint,
        Vector3 inwardAlongWallA,
        Vector3 inwardAlongWallB,
        Vector3 inwardBisector,
        Vector3 outwardAWorld,
        Vector3 outwardBWorld,
        Vector3 claddingOutwardWorld,
        float cornerAngleDeg,
        float yMin,
        float yMax,
        System.Random rng,
        ref int stoneIndex)
    {
        if (profile == null || profile.stone == null)
            return;

        float refStoneHeight = Mathf.Clamp(
            settings.targetHeight * 1.08f,
            profile.stone.minStoneHeight * 1.30f,
            Mathf.Max(profile.stone.minStoneHeight * 1.40f, profile.stone.maxStoneHeight * 0.92f));
        float armLengthBase = Mathf.Clamp(
            settings.minLength * 0.96f,
            settings.minLength * 0.70f,
            settings.maxLength * 0.95f);
        armLengthBase = Mathf.Max(armLengthBase, refStoneHeight * 0.38f);
        armLengthBase *= 1.35f;
        float armLengthHorizontalBase = armLengthBase * 2f;

        float thWall = wall != null ? wall.thickness : 0.25f;
        float halfToOuterFace = Mathf.Max(0f, thWall * 0.5f - profile.general.sideInset);
        float acuteShellMul = Mathf.Clamp(55f / Mathf.Max(cornerAngleDeg, 8f), 1f, 2.1f);
        halfToOuterFace *= acuteShellMul;
        Vector3 shellCorner = cornerPoint + (outwardAWorld + outwardBWorld) * halfToOuterFace;

        Vector3 outDir = ComputeBollardOutwardWorldDir(
            outwardAWorld,
            outwardBWorld,
            claddingOutwardWorld,
            inwardAlongWallA,
            inwardAlongWallB,
            inwardBisector);
        float surf = Mathf.Max(0.02f, profile.stone.surfaceProtrusion);
        float footprintShiftMag = 0.40f + thWall * 0.48f + surf * 1.6f;
        Vector3 footprintShiftWorldH = outDir * footprintShiftMag;
        footprintShiftWorldH.y = 0f;

        // Extra gap between stacked stones (on top of profile verticalSpacing) — a few mm of mortar.
        const float bollardExtraRowGap = 0.007f;

        float rowBottom = yMin;
        int rowIndex = 0;
        while (rowBottom < yMax - 0.08f && rowIndex < maxRowsPerSide && stoneIndex < maxGeneratedStonesPerSide)
        {
            float rowHeight = settings.targetHeight * RandomRange(rng, 1f - settings.rowHeightJitter, 1f + settings.rowHeightJitter);
            rowHeight = Mathf.Clamp(
                rowHeight,
                profile.stone.minStoneHeight * 1.12f,
                Mathf.Max(profile.stone.minStoneHeight * 1.22f, profile.stone.maxStoneHeight * 1.72f));
            bool isLastRow = (rowBottom + rowHeight + settings.verticalSpacing) >= yMax;
            float topOvershoot = isLastRow ? Mathf.Max(wall.thickness * 0.18f, profile.stone.surfaceProtrusion * 1.45f, 0.04f) : 0f;
            rowHeight = Mathf.Min(rowHeight, yMax - rowBottom + topOvershoot);
            if (rowHeight < 0.08f)
                break;

            float centerY = rowBottom + rowHeight * 0.5f;
            Vector3 center = shellCorner + Vector3.up * centerY;

            // Chord P0–P1 + semicircle (outward “nose”) fixed for the stack so the tips line up on the outside.
            // Only outerArmLength (Q along walls) varies — wall-parallel legs Q–P depth changes toward the corner.
            float armRef = Mathf.Clamp(
                armLengthHorizontalBase,
                Mathf.Max(settings.minLength * 0.42f, refStoneHeight * 0.42f),
                settings.maxLength * 2.85f);
            float baseOuter = armRef + Mathf.Max(armRef * 0.32f, 0.06f);
            const float outerScaleMin = 0.62f;
            const float outerScaleMax = 1.38f;
            // Smallest scaled outer must stay past innerLen (same relation as old single-armLength model).
            float innerLenMaxGeom = baseOuter * outerScaleMin - 0.03f;
            float legAlongWall = Mathf.Max(armRef * 0.12f, 0.04f);
            float innerMin = armRef * 0.55f;
            float innerMax = Mathf.Min(armRef * 0.88f, innerLenMaxGeom);
            float innerLen = armRef - legAlongWall;
            if (innerMax > innerMin + 1e-4f)
                innerLen = Mathf.Clamp(innerLen, innerMin, innerMax);
            else
                innerLen = Mathf.Clamp(innerLen, 0.02f, Mathf.Max(0.02f, innerLenMaxGeom - 0.02f));

            float wallParallelScale = RandomRange(rng, outerScaleMin, outerScaleMax);
            float outerArmLength = baseOuter * wallParallelScale;
            float outerCap = (settings.maxLength * 2.85f) + Mathf.Max(settings.maxLength * 2.85f * 0.32f, 0.06f);
            outerArmLength = Mathf.Clamp(outerArmLength, innerLen + 0.02f, outerCap * outerScaleMax);

            Mesh mesh = BuildTriangleCornerHalfColumnTrapezoidMesh(
                PickEndQuoinModule(profile, rng),
                shellCorner,
                inwardAlongWallA,
                inwardAlongWallB,
                inwardBisector,
                outerArmLength,
                innerLen,
                rowHeight,
                cornerAngleDeg,
                profile.stone.facePlaneJitter,
                profile.stone.uvMetersPerUnit,
                rng,
                center,
                footprintShiftWorldH,
                root);

            if (mesh == null || mesh.vertexCount == 0)
            {
                rowBottom += rowHeight + settings.verticalSpacing + bollardExtraRowGap;
                rowIndex++;
                continue;
            }

            EnsureMeshDoubleSided(mesh);

            GameObject go = new GameObject($"TriangleCornerBollard_{stoneIndex:000}_r{rowIndex:00}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localEulerAngles = Vector3.zero;
            go.transform.localScale = Vector3.one;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = stoneMaterial;
            if (EffectiveDebugColorizeTriangleBollardFaces())
                ApplyDebugFaceColors(mesh, mr, stoneMaterial);
            else
                ApplyPerStoneMaterialVariation(profile, mr, rng, true);
            stoneIndex++;

            rowBottom += rowHeight + settings.verticalSpacing + bollardExtraRowGap;
            rowIndex++;
        }
    }

    /// <summary>
    /// Chamfers top/bottom caps by moving each vertex slightly toward the ring centroid (horizontal taper / beveled read).
    /// </summary>
    private static void BevelExtrudedPolygonCaps(Vector3[] bottomRing, Vector3[] topRing, float height)
    {
        if (bottomRing == null || topRing == null || bottomRing.Length != topRing.Length || bottomRing.Length < 3)
            return;
        float t = Mathf.Clamp(0.042f + height * 0.028f, 0.038f, 0.11f);
        InsetPolygonRingTowardCentroid(bottomRing, t);
        InsetPolygonRingTowardCentroid(topRing, t);
    }

    private static void InsetPolygonRingTowardCentroid(Vector3[] ring, float lerpT)
    {
        if (ring == null || ring.Length < 3 || lerpT <= 0f)
            return;
        Vector3 c = Vector3.zero;
        for (int i = 0; i < ring.Length; i++)
            c += ring[i];
        c /= ring.Length;
        for (int i = 0; i < ring.Length; i++)
            ring[i] = Vector3.Lerp(ring[i], c, Mathf.Clamp01(lerpT));
    }

    /// <summary>
    /// Trapezoid in plan: legs parallel to u1/u2; semicircle on P0–P1. Footprint shift slides all vertices in XZ together.
    /// <paramref name="innerLen"/> fixes chord P0–P1 (outward nose column); <paramref name="outerArmLength"/> varies per row (Q along walls).
    /// </summary>
    private Mesh BuildTriangleCornerHalfColumnTrapezoidMesh(
        WallStoneModuleDefinition module,
        Vector3 cornerPointWorld,
        Vector3 inwardAlongWallA,
        Vector3 inwardAlongWallB,
        Vector3 inwardBisector,
        float outerArmLength,
        float innerLen,
        float height,
        float cornerAngleDeg,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng,
        Vector3 worldCenter,
        Vector3 footprintShiftWorldHorizontal,
        Transform parentForVertices)
    {
        if (module == null)
            return null;

        footprintShiftWorldHorizontal.y = 0f;

        Transform bake = parentForVertices != null ? parentForVertices : transform;
        float halfH = height * 0.5f;
        float hJitter = Mathf.Clamp(planeJitter * 0.10f + module.frontRelief * 0.06f, 0f, 0.0014f);

        Vector3 u1 = Vector3.ProjectOnPlane(inwardAlongWallA, Vector3.up);
        Vector3 u2 = Vector3.ProjectOnPlane(inwardAlongWallB, Vector3.up);
        if (u1.sqrMagnitude < 1e-10f || u2.sqrMagnitude < 1e-10f)
            return null;
        u1.Normalize();
        u2.Normalize();

        Vector3 bisectorH = Vector3.ProjectOnPlane(inwardBisector, Vector3.up);
        if (bisectorH.sqrMagnitude < 1e-10f)
            bisectorH = (u1 + u2).normalized;
        else
            bisectorH.Normalize();

        Vector3 cornerOffsetH = Vector3.ProjectOnPlane(cornerPointWorld - worldCenter, Vector3.up);

        outerArmLength = Mathf.Max(outerArmLength, innerLen + 0.02f);
        innerLen = Mathf.Clamp(innerLen, 0.02f, outerArmLength - 0.02f);

        // Near corner: chord P0–P1 (arc / column). Far along walls: straight parallel Q0–Q1 (back face).
        Vector3 p0 = cornerOffsetH + u1 * innerLen;
        Vector3 p1 = cornerOffsetH + u2 * innerLen;

        Vector3 q0 = cornerOffsetH + u1 * outerArmLength;
        Vector3 q1 = cornerOffsetH + u2 * outerArmLength;
        Vector3 chord = p1 - p0;
        float chordLen = chord.magnitude;
        if (chordLen < 1e-5f)
            return null;

        Vector3 m = 0.5f * (p0 + p1);
        float radiusAlongChord = chordLen * 0.5f;
        // True semicircle (smooth circular read); prominence comes from a long chord, not an ellipse.
        float radiusOutward = radiusAlongChord;
        Vector3 vMtoP0 = (p0 - m).normalized;
        Vector3 perp = Vector3.Cross(Vector3.up, chord);
        if (perp.sqrMagnitude < 1e-12f)
            return null;
        perp.Normalize();
        // Semicircle bulges on the side of chord P0–P1 opposite the triangle apex (Dot(perp, bisectorH) <= 0).
        if (Vector3.Dot(perp, bisectorH) > 0f)
            perp = -perp;

        int arcSegments = Mathf.Clamp(Mathf.RoundToInt(14f + cornerAngleDeg * 0.45f), 18, 36);
        var plan = new List<Vector3>(arcSegments + 6);
        // Q0/Q1 vary per row when outerArmLength is randomized; arc from fixed P stays column-aligned.
        plan.Add(q0);
        for (int s = 0; s <= arcSegments; s++)
        {
            float t = Mathf.PI * (s / (float)arcSegments);
            Vector3 arcPt = m + radiusAlongChord * Mathf.Cos(t) * vMtoP0 + radiusOutward * Mathf.Sin(t) * perp;
            arcPt += new Vector3(
                RandomRange(rng, -hJitter, hJitter),
                0f,
                RandomRange(rng, -hJitter, hJitter));
            plan.Add(arcPt);
        }
        plan.Add(q1);

        int n = plan.Count;
        var front = new Vector3[n];
        var back = new Vector3[n];
        // Do not mirror offsets around worldCenter: that breaks parallelism of side faces with each wall plane.
        for (int i = 0; i < n; i++)
        {
            Vector3 h = plan[i];
            h.y = 0f;
            Vector3 bottomW = worldCenter + h + footprintShiftWorldHorizontal + Vector3.up * (-halfH);
            Vector3 topW = worldCenter + h + footprintShiftWorldHorizontal + Vector3.up * halfH;
            front[i] = bake.InverseTransformPoint(bottomW);
            back[i] = bake.InverseTransformPoint(topW);
        }

        BevelExtrudedPolygonCaps(front, back, height);

        float relief = module != null ? module.frontRelief : 0.025f;
        Mesh mesh = BuildExtrudedPolygonReliefMesh(front, back, uvMetersPerUnit, planeJitter, relief, rng);
        if (mesh != null)
            mesh.name = "TriangleCornerHalfColumnTrapezoid";
        return mesh;
    }

    private static void EnsureMeshDoubleSided(Mesh mesh)
    {
        if (mesh == null)
            return;

        int[] tris = mesh.triangles;
        if (tris == null || tris.Length < 3)
            return;

        Vector3[] verts = mesh.vertices;
        if (verts == null || verts.Length == 0)
            return;

        Vector2[] uv = mesh.uv;
        if (uv == null || uv.Length != verts.Length)
            uv = new Vector2[verts.Length];

        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != verts.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        int vCount = verts.Length;
        int tCount = tris.Length;

        var dsVerts = new Vector3[vCount * 2];
        var dsUv = new Vector2[vCount * 2];
        var dsNormals = new Vector3[vCount * 2];

        for (int i = 0; i < vCount; i++)
        {
            dsVerts[i] = verts[i];
            dsUv[i] = uv[i];
            dsNormals[i] = normals[i];

            int bi = i + vCount;
            dsVerts[bi] = verts[i];
            dsUv[bi] = uv[i];
            dsNormals[bi] = -normals[i];
        }

        var dsTris = new int[tCount * 2];
        for (int i = 0; i < tCount; i++)
            dsTris[i] = tris[i];

        int w = tCount;
        for (int i = 0; i < tCount; i += 3)
        {
            int a = tris[i] + vCount;
            int b = tris[i + 1] + vCount;
            int c = tris[i + 2] + vCount;
            dsTris[w++] = c;
            dsTris[w++] = b;
            dsTris[w++] = a;
        }

        mesh.Clear();
        mesh.vertices = dsVerts;
        mesh.uv = dsUv;
        mesh.normals = dsNormals;
        mesh.triangles = dsTris;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
    }

    private void ApplyDebugFaceColors(Mesh mesh, MeshRenderer renderer, Material baseMaterial)
    {
        if (mesh == null || renderer == null || baseMaterial == null)
            return;

        int[] tris = mesh.triangles;
        Vector3[] verts = mesh.vertices;
        if (tris == null || verts == null || tris.Length < 3 || verts.Length == 0)
            return;

        List<int>[] groups = new List<int>[6];
        for (int i = 0; i < groups.Length; i++)
            groups[i] = new List<int>(tris.Length / 6);

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 0.000001f)
                continue;
            n.Normalize();

            int group = GetNormalGroup(n);
            groups[group].Add(tris[i]);
            groups[group].Add(tris[i + 1]);
            groups[group].Add(tris[i + 2]);
        }

        Color[] palette =
        {
            new Color(1f, 0f, 0.2f, 1f), // +X neon red
            new Color(0f, 1f, 1f, 1f),   // -X neon cyan
            new Color(0.1f, 1f, 0f, 1f), // +Y neon lime
            new Color(1f, 0f, 1f, 1f),   // -Y neon magenta
            new Color(0f, 0.35f, 1f, 1f),// +Z neon blue
            new Color(1f, 0.85f, 0f, 1f),// -Z neon yellow
        };

        int used = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].Count > 0)
                used++;
        }
        if (used == 0)
            return;

        mesh.subMeshCount = used;
        renderer.SetPropertyBlock(null);
        Material[] mats = new Material[used];
        int sub = 0;
        Shader debugShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (debugShader == null)
            debugShader = Shader.Find("Unlit/Color");
        if (debugShader == null)
            debugShader = baseMaterial.shader;
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].Count == 0)
                continue;

            mesh.SetTriangles(groups[i], sub);
            Material m = new Material(debugShader);
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", palette[i]);
            else if (m.HasProperty("_Color"))
                m.SetColor("_Color", palette[i]);
            if (m.HasProperty("_EmissionColor"))
            {
                // Make debug faces punch through scene lighting.
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", palette[i] * 2.5f);
            }
            if (m.HasProperty("_MainTex"))
                m.SetTexture("_MainTex", null);
            mats[sub] = m;
            sub++;
        }

        renderer.sharedMaterials = mats;
    }

    private static int GetNormalGroup(Vector3 n)
    {
        float ax = Mathf.Abs(n.x);
        float ay = Mathf.Abs(n.y);
        float az = Mathf.Abs(n.z);

        if (ax >= ay && ax >= az)
            return n.x >= 0f ? 0 : 1;
        if (ay >= ax && ay >= az)
            return n.y >= 0f ? 2 : 3;
        return n.z >= 0f ? 4 : 5;
    }
}
