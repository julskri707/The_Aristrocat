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
            // Keep only one dedicated D-bollard on acute triangle corners (< 35°).
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
                float altScale = useA ? settings.alternateLongScale : settings.alternateShortScale;
                float length = baseLength * altScale * 1.08f * RandomRange(rng, 1f - settings.lengthJitter, 1f + settings.lengthJitter);
                length = Mathf.Clamp(length, settings.minLength * 0.85f, settings.maxLength * 1.35f);

                float centerY = rowBottom + rowHeight * 0.5f;

                bool isReflexCorner = TryGetClosedLoopPathIsCCW(out bool triPathCcw) &&
                    WallObject.IsReflexCornerXZ(prev.tangent, next.tangent, triPathCcw);

                // Same corner-quoin path as GenerateClosedLoopCornerQuoins (RectangleEndWalls.cs) — triangle copy had drifted
                // (fullDepth scale + bisector/back-side centering), which skewed stones to one side.
                {
                    float fullDepthRect = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f + 0.04f, wall.thickness + 0.01f);
                    fullDepthRect *= Mathf.Max(1f, settings.cornerLDepthMul) * 1.20f;

                    float cornerWidth = Mathf.Clamp(
                        Mathf.Max(length * 0.78f, fullDepthRect * 1.10f),
                        settings.minLength * 0.90f,
                        settings.maxLength * 1.60f);

                    Vector3 quoinOutward = useA ? outwardA : outwardB;
                    Quaternion rot = Quaternion.LookRotation(quoinOutward, Vector3.up);
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
                    {
                        center += cornerBisector * cornerExposeNudge;
                        center -= quoinOutward * backSideBias;
                    }

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

                    ApplyCornerQuoinUserOffsets(ref center, rot, settings, isReflexCorner, useA);

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
                        fullDepthRect,
                        widenRightSideForMesh,
                        sideExtra,
                        profile.stone.facePlaneJitter,
                        GetEffectiveUvMetersPerUnit(profile),
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
                        if (forceDoubleSidedStoneMaterials && stoneMaterial != null)
                            ApplyMaterialDoubleSided(stoneMaterial);
                        if (EffectiveDebugColorizeTriangleBollardFaces())
                            ApplyDebugFaceColors(mesh, mr, stoneMaterial);
                        ApplyPerStoneMaterialVariation(profile, mr, rng, true);
                        AttachQuoinRuntimeLodIfEnabled(go, mf, mesh, GetEffectiveUvMetersPerUnit(profile));
                        if (combineGeneratedStonesPerSide && profile != null && mf.sharedMesh != null)
                            ApplyPerStoneTintAsVertexColors(mf.sharedMesh, profile, rng, true);
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
        // Pure angle-based physical offset:
        // small corner angle => farther from wall, large angle => closer to wall.
        float angleOut01 = Mathf.Clamp01(Mathf.InverseLerp(35f, 5f, cornerAngleDeg));
        float angleResponse = Mathf.Pow(angleOut01, triangleBollardAngleOffsetResponse);
        float shellForwardMeters = Mathf.Lerp(
            Mathf.Min(triangleBollardAngleOffsetMin, triangleBollardAngleOffsetMax),
            Mathf.Max(triangleBollardAngleOffsetMin, triangleBollardAngleOffsetMax),
            angleResponse);
        Vector3 physicalOffsetWorld = outDir * shellForwardMeters;

        float minWallParallelLeg = Mathf.Max(0f, triangleBollardMinWallParallelLeg);
        float maxWallParallelLeg = Mathf.Max(minWallParallelLeg, triangleBollardMaxWallParallelLeg);
        const float baselineWallParallelLeg = 0.20f;

        float footprintShiftMag = Mathf.Lerp(0.10f, 0.85f, angleResponse);
        footprintShiftMag = Mathf.Max(0.06f, footprintShiftMag);
        Vector3 footprintShiftWorldH = outDir * footprintShiftMag;
        footprintShiftWorldH.y = 0f;

        // Extra gap between stacked stones to avoid clipping at the acute peak.
        const float bollardExtraRowGap = 0.022f;

        float rowBottom = yMin;
        int rowIndex = 0;
        // Run until we reach the wall top — do not stop at (yMax - 0.08f): that left a gap and often the
        // top stone never got isLastRow, so the forced ceiling height never applied.
        while (rowBottom < yMax - 1e-4f && rowIndex < maxRowsPerSide && stoneIndex < maxGeneratedStonesPerSide)
        {
            float rowHeight = settings.targetHeight * RandomRange(rng, 1f - settings.rowHeightJitter, 1f + settings.rowHeightJitter);
            rowHeight = Mathf.Clamp(
                rowHeight,
                profile.stone.minStoneHeight * 1.12f,
                Mathf.Max(profile.stone.minStoneHeight * 1.22f, profile.stone.maxStoneHeight * 1.72f));
            // Advance after this row (same formula as rowBottom += at end of iteration).
            bool isLastRow = (rowBottom + rowHeight + settings.verticalSpacing + bollardExtraRowGap) >= yMax - 1e-4f;
            float topOvershoot = isLastRow ? Mathf.Max(wall.thickness * 0.18f, profile.stone.surfaceProtrusion * 1.45f, 0.04f) : 0f;
            rowHeight = Mathf.Min(rowHeight, yMax - rowBottom + topOvershoot);
            // Sliver left (< min stone height): still place one cap block whose mesh height is forced to the ceiling.
            if (rowHeight < 0.08f)
            {
                if (rowBottom >= yMax - 1e-4f)
                    break;
                isLastRow = true;
                const float topCoverOverWallSliver = 0.03f;
                float targetTopYSliver = yMax + topCoverOverWallSliver;
                float prevTopY = rowBottom - settings.verticalSpacing - bollardExtraRowGap;
                const float minGapBelowSliver = 0.005f;
                float minBottomYSliver = Mathf.Max(rowBottom, prevTopY + minGapBelowSliver);
                rowHeight = Mathf.Max(0.08f, targetTopYSliver - minBottomYSliver);
            }
            // Final check after clamp / sliver fix (covers edge cases).
            isLastRow = isLastRow || (rowBottom + rowHeight + settings.verticalSpacing + bollardExtraRowGap >= yMax - 1e-4f);
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
            // Smallest scaled outer must stay past innerLen with at least 20 cm wall-parallel leg (Q-P).
            float innerLenMaxGeom = baseOuter * outerScaleMin - baselineWallParallelLeg;
            float legAlongWall = Mathf.Max(armRef * 0.12f, 0.04f);
            float innerMin = armRef * 0.55f;
            float innerMax = Mathf.Min(armRef * 0.88f, innerLenMaxGeom);
            float innerLen = armRef - legAlongWall;
            if (innerMax > innerMin + 1e-4f)
                innerLen = Mathf.Clamp(innerLen, innerMin, innerMax);
            else
                innerLen = Mathf.Clamp(innerLen, 0.02f, Mathf.Max(0.02f, innerLenMaxGeom - 0.01f));

            float wallParallelScale = RandomRange(rng, 0.62f, 1.38f);
            float outerArmLength = baseOuter * wallParallelScale;
            float outerCap = (settings.maxLength * 2.85f) + Mathf.Max(settings.maxLength * 2.85f * 0.32f, 0.06f);
            outerArmLength = Mathf.Clamp(outerArmLength, innerLen + minWallParallelLeg, outerCap * outerScaleMax);
            const float columnDiameterBoost = 1.50f;
            innerLen *= columnDiameterBoost;
            // Keep column diameter more regular on very small angles (avoid over-thinning near the apex).
            float smallAngle01 = Mathf.Clamp01(Mathf.InverseLerp(
                triangleBollardColumnCompStartAngleDeg,
                triangleBollardColumnCompEndAngleDeg,
                cornerAngleDeg));
            float smallAngleColumnScale = Mathf.Lerp(1f, triangleBollardColumnSmallAngleScale, smallAngle01);
            innerLen *= smallAngleColumnScale;
            outerArmLength = Mathf.Max(outerArmLength, innerLen + minWallParallelLeg);
            outerArmLength = Mathf.Min(outerArmLength, innerLen + maxWallParallelLeg);
            if (cornerAngleDeg < 30f)
            {
                // Acute corners: force stronger per-row variance on lateral-face length,
                // so blocks don't look like uniform stacked slices.
                float legMin = minWallParallelLeg;
                float legMax = maxWallParallelLeg;
                float t = RandomRange(rng, 0f, 1f);
                float leg;
                if (t < 0.34f)
                    leg = RandomRange(rng, legMin, Mathf.Lerp(legMin, legMax, 0.30f));
                else if (t < 0.68f)
                    leg = RandomRange(rng, Mathf.Lerp(legMin, legMax, 0.30f), Mathf.Lerp(legMin, legMax, 0.70f));
                else
                    leg = RandomRange(rng, Mathf.Lerp(legMin, legMax, 0.70f), legMax);
                outerArmLength = innerLen + leg;
            }
            float meshHeight = rowHeight;

            Vector3 centerForRow = center;
            Vector3 footprintShiftForRow = footprintShiftWorldH;
            if (isLastRow)
            {
                // Cap stone: always fills from a legal bottom to the ceiling line (forced — not "only if there is room").
                const float topCoverOverWall = 0.03f;
                const float minGapToStoneBelow = 0.005f; // 0.5 cm above previous stone's top
                const float minCapMeshHeight = 0.08f;
                float targetTopY = yMax + topCoverOverWall;
                // Bottom of this stone cannot start above rowBottom; must stay below previous stone's top + gap.
                float previousStoneTopY = rowBottom - settings.verticalSpacing - bollardExtraRowGap;
                float minBottomY = Mathf.Max(rowBottom, previousStoneTopY + minGapToStoneBelow);
                float bottomY = minBottomY;
                // Exact height so the top face lands on the ceiling line (world Y).
                meshHeight = targetTopY - bottomY;
                if (meshHeight < minCapMeshHeight)
                {
                    bottomY = targetTopY - minCapMeshHeight;
                    if (bottomY < minBottomY)
                        bottomY = minBottomY;
                    meshHeight = targetTopY - bottomY;
                }
                centerForRow.y = bottomY + meshHeight * 0.5f;
            }
            Mesh mesh = BuildTriangleCornerHalfColumnTrapezoidMesh(
                PickEndQuoinModule(profile, rng),
                shellCorner,
                inwardAlongWallA,
                inwardAlongWallB,
                inwardBisector,
                outerArmLength,
                innerLen,
                meshHeight,
                cornerAngleDeg,
                profile.stone.facePlaneJitter,
                GetEffectiveUvMetersPerUnit(profile),
                rng,
                centerForRow,
                footprintShiftForRow,
                root);

            if (mesh == null || mesh.vertexCount == 0)
            {
                rowBottom += rowHeight + settings.verticalSpacing + bollardExtraRowGap;
                rowIndex++;
                continue;
            }

            GameObject go = new GameObject($"TriangleCornerBollard_{stoneIndex:000}_r{rowIndex:00}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = root.InverseTransformVector(physicalOffsetWorld);
            go.transform.localEulerAngles = Vector3.zero;
            go.transform.localScale = Vector3.one;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = stoneMaterial;
            if (forceDoubleSidedStoneMaterials && stoneMaterial != null)
                ApplyMaterialDoubleSided(stoneMaterial);
            // Always keep textured/material rendering for these stones.
            ApplyPerStoneMaterialVariation(profile, mr, rng, true);
            AttachQuoinRuntimeLodIfEnabled(go, mf, mesh, GetEffectiveUvMetersPerUnit(profile));
            if (combineGeneratedStonesPerSide && profile != null && mf.sharedMesh != null)
                ApplyPerStoneTintAsVertexColors(mf.sharedMesh, profile, rng, true);
            stoneIndex++;

            rowBottom += rowHeight + settings.verticalSpacing + bollardExtraRowGap;
            rowIndex++;
            if (isLastRow)
                break;
        }
    }

    /// <summary>
    /// Chamfers top/bottom caps by moving each vertex slightly toward the ring centroid.
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

        // Clamp the followed angle between the two lateral-face directions:
        // below the threshold, faces stop following and stay at the minimum angle.
        float lateralMinAngleDeg = Mathf.Max(0.1f, triangleBollardLateralFollowMinAngleDeg);
        float currentLateralAngle = Vector3.Angle(u1, u2);
        if (currentLateralAngle < lateralMinAngleDeg)
        {
            float half = lateralMinAngleDeg * 0.5f;
            Quaternion qPos = Quaternion.AngleAxis(half, Vector3.up);
            Quaternion qNeg = Quaternion.AngleAxis(-half, Vector3.up);
            Vector3 c1 = (qPos * bisectorH).normalized;
            Vector3 c2 = (qNeg * bisectorH).normalized;
            // Keep side assignment stable relative to original u1/u2.
            if (Vector3.Dot(c1, u1) + Vector3.Dot(c2, u2) >= Vector3.Dot(c2, u1) + Vector3.Dot(c1, u2))
            {
                u1 = c1;
                u2 = c2;
            }
            else
            {
                u1 = c2;
                u2 = c1;
            }
            bisectorH = (u1 + u2).normalized;
        }

        Vector3 cornerOffsetH = Vector3.ProjectOnPlane(cornerPointWorld - worldCenter, Vector3.up);

        float minWallParallelLeg = Mathf.Max(0f, triangleBollardMinWallParallelLeg);
        outerArmLength = Mathf.Max(outerArmLength, innerLen + minWallParallelLeg);
        innerLen = Mathf.Clamp(innerLen, 0.02f, outerArmLength - minWallParallelLeg);

        // Near corner: chord P0–P1 (arc / column). Far along walls: straight parallel Q0–Q1 (back face).
        Vector3 p0 = cornerOffsetH + u1 * innerLen;
        Vector3 p1 = cornerOffsetH + u2 * innerLen;

        Vector3 q0 = cornerOffsetH + u1 * outerArmLength;
        Vector3 q1 = cornerOffsetH + u2 * outerArmLength;
        // Separate both wall-parallel side faces without changing their individual Q-P lengths:
        // shift side A and side B in opposite directions by the same rigid offset.
        Vector3 spreadAxis = Vector3.ProjectOnPlane(u1 - u2, Vector3.up);
        if (spreadAxis.sqrMagnitude < 1e-10f)
            spreadAxis = Vector3.Cross(Vector3.up, bisectorH);
        if (spreadAxis.sqrMagnitude > 1e-10f)
        {
            spreadAxis.Normalize();
            float lateralFaceSeparation = Mathf.Max(0.03f, innerLen * 0.08f);
            if (cornerAngleDeg < 5f)
                lateralFaceSeparation *= 2f;
            Vector3 sep = spreadAxis * (lateralFaceSeparation * 0.5f);
            p0 += sep;
            q0 += sep;
            p1 -= sep;
            q1 -= sep;
        }
        Vector3 chord = p1 - p0;
        float chordLen = chord.magnitude;
        if (chordLen < 1e-5f)
            return null;

        Vector3 m = 0.5f * (p0 + p1);
        float radiusAlongChord = chordLen * 0.5f;
        float radiusOutward = radiusAlongChord * 1.25f;
        Vector3 vMtoP0 = (p0 - m).normalized;
        Vector3 perp = Vector3.Cross(Vector3.up, chord);
        if (perp.sqrMagnitude < 1e-12f)
            return null;
        perp.Normalize();
        // Semicircle bulges on the side of chord P0–P1 opposite the triangle apex (Dot(perp, bisectorH) <= 0).
        if (Vector3.Dot(perp, bisectorH) > 0f)
            perp = -perp;

        int arcSegments = Mathf.Clamp(Mathf.RoundToInt(16f + cornerAngleDeg * 0.50f), 20, 40);
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

        // Use the same 3D texture relief settings as the other generated stones.
        float relief = module != null ? module.frontRelief : 0.025f;
        Mesh mesh = BuildExtrudedPolygonReliefMesh(
            front,
            back,
            uvMetersPerUnit,
            planeJitter,
            relief,
            rng,
            includeBackCap: IncludeStoneBackCapInExtrusion());
        if (mesh != null)
            mesh.name = "TriangleCornerHalfColumnTrapezoid";
        return mesh;
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
