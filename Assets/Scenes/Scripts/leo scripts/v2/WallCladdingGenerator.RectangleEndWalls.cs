using System.Collections.Generic;
using UnityEngine;

public sealed partial class WallCladdingGenerator
{
    /// <summary>
    /// Rapproche les quoins au coin rentrant (~270°) vers le sommet du mur pour réduire la fente verticale.
    /// </summary>
    private const float ReflexCornerQuoinVertexCloseMeters = 0.05f;

    /// <summary>
    /// Au coin rentrant, aligne le pivot de la pierre sur la ligne centrale (axe du mur) du segment
    /// correspondant à la rangée (mur A ou B), sans changer le mesh.
    /// </summary>
    private static void SnapReflexCornerQuoinPivotOntoWallCenterline(
        ref Vector3 center,
        Vector3 cornerPoint,
        Vector3 tangentPrev,
        Vector3 tangentNext,
        bool useWallA)
    {
        Vector3 t = useWallA ? tangentPrev : tangentNext;
        t.y = 0f;
        if (t.sqrMagnitude < 1e-12f)
            return;
        t.Normalize();
        Vector3 d = center - cornerPoint;
        d.y = 0f;
        float along = Vector3.Dot(d, t);
        Vector3 onLine = cornerPoint + t * along;
        Vector3 perp = center - onLine;
        perp.y = 0f;
        center -= perp;
    }

    private void GenerateClosedLoopCornerQuoins(
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
        if (!settings.enabled || wall == null || !wall.closedLoop || samples == null || samples.Count < 3)
            return;

        bool allowCornerQuoins = settings.useGridRightAngleCornerQuoins
            || loopShapeKind == WallLoopShapeKind.Rectangle;
        if (!allowCornerQuoins)
            return;

        for (int i = 0; i < samples.Count; i++)
        {
            PathSample prev = samples[i];
            PathSample next = samples[(i + 1) % samples.Count];
            if (!ShouldPlaceRectangleStyleCornerQuoin(prev, next, settings))
                continue;

            EmitCornerQuoinStackForVertex(
                profile, root, stoneMaterial, sideSign, yMin, yMax, rng, ref stoneIndex,
                prev, next, i);
        }
    }

    private void GenerateOpenPolylineRightAngleCornerQuoins(
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
        if (!settings.enabled || wall == null || wall.closedLoop || samples == null || samples.Count < 2)
            return;

        for (int i = 1; i < samples.Count; i++)
        {
            PathSample prev = samples[i - 1];
            PathSample next = samples[i];
            if (!IsApproximatelyRightAngleBetweenSegments(prev, next))
                continue;

            EmitCornerQuoinStackForVertex(
                profile, root, stoneMaterial, sideSign, yMin, yMax, rng, ref stoneIndex,
                prev, next, i);
        }
    }

    /// <summary>
    /// Même convention que <see cref="WallObject"/> pour le signe d’aire (CCW = positif).
    /// </summary>
    private bool TryGetClosedLoopPathIsCCW(out bool ccw)
    {
        ccw = true;
        if (wall == null || !wall.closedLoop)
            return false;

        List<Vector3> path = GetWallPath();
        if (path == null || path.Count < 3)
            return false;

        int n = path.Count;
        if (Vector3.Distance(path[0], path[n - 1]) < 0.0001f)
            n--;
        if (n < 3)
            return false;

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[(i + 1) % n];
            area += a.x * b.z - b.x * a.z;
        }

        ccw = area > 0f;
        return true;
    }

    private void EmitCornerQuoinStackForVertex(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        float sideSign,
        float yMin,
        float yMax,
        System.Random rng,
        ref int stoneIndex,
        PathSample prev,
        PathSample next,
        int cornerIndex)
    {
        EndQuoinSettings settings = profile.stone.endQuoins;
        Vector3 cornerPoint = prev.b;

        Vector3 outwardA = Vector3.Cross(Vector3.up, prev.tangent).normalized * sideSign;
        Vector3 outwardB = Vector3.Cross(Vector3.up, next.tangent).normalized * sideSign;

        bool isReflex = TryGetClosedLoopPathIsCCW(out bool pathCcw) &&
                        WallObject.IsReflexCornerXZ(prev.tangent, next.tangent, pathCcw);

        Vector3 sumOutward = outwardA + outwardB;
        sumOutward.y = 0f;

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
            float altScale = ((rowIndex & 1) == 0) ? settings.alternateLongScale : settings.alternateShortScale;
            float length = baseLength * altScale * 1.08f * RandomRange(rng, 1f - settings.lengthJitter, 1f + settings.lengthJitter);
            length = Mathf.Clamp(length, settings.minLength * 0.85f, settings.maxLength * 1.35f);

            float fullDepth = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f + 0.04f, wall.thickness + 0.01f);
            fullDepth *= Mathf.Max(1f, settings.cornerLDepthMul) * 1.20f;

            float cornerWidth = Mathf.Clamp(
                Mathf.Max(length * 0.78f, fullDepth * 1.10f),
                settings.minLength * 0.90f,
                settings.maxLength * 1.60f);
            float centerY = rowBottom + rowHeight * 0.5f;

            float sideOffset = Mathf.Max(0f, wall.thickness * 0.5f - profile.general.sideInset);

            // Une rangée sur deux : alignement sur le mur A puis sur le mur B (effet « zip » / quoin classique).
            bool useA = (rowIndex & 1) == 0;
            Vector3 outward = useA ? outwardA : outwardB;
            Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
            ComputeCornerLateralExtension(profile, settings, cornerWidth, useA, rng, out bool widenRightSide, out float sideExtra);

            // Coin saillant : mitre. Coin rentrant : biseau (norme bissectrice). Sinon arête dégénérée.
            Vector3 exteriorCornerOffset;
            if (sumOutward.sqrMagnitude < 1e-12f)
                exteriorCornerOffset = outwardA * sideOffset;
            else if (isReflex)
                exteriorCornerOffset = sumOutward.normalized * sideOffset;
            else
                exteriorCornerOffset = sumOutward * sideOffset;

            Vector3 exteriorCorner = cornerPoint + exteriorCornerOffset;
            if (isReflex && ReflexCornerQuoinVertexCloseMeters > 0f && sumOutward.sqrMagnitude > 1e-12f)
                exteriorCorner -= sumOutward.normalized * ReflexCornerQuoinVertexCloseMeters;

            float cornerAnchorInset = Mathf.Clamp(
                Mathf.Max(profile.stone.horizontalSpacing * 0.18f, 0.002f),
                0.001f,
                0.006f);
            float halfLen = cornerWidth * 0.5f;
            float baseAnchorX = useA
                ? (-halfLen + cornerAnchorInset)
                : (halfLen - cornerAnchorInset);
            float anchorX = baseAnchorX;
            float faceReferenceOffsetX = useA ? -cornerFaceReferenceShift : cornerFaceReferenceShift;
            anchorX += faceReferenceOffsetX;
            anchorX = ApplyCornerLateralStackAlignment(anchorX);
            anchorX = ResolveOtherWallColumnOffset(useA, anchorX);
            Vector3 localInnerCornerAnchor = new Vector3(anchorX, 0f, 0f);
            Vector3 center = exteriorCorner - (rot * localInnerCornerAnchor) + Vector3.up * centerY;

            Vector3 cornerBisector = sumOutward.sqrMagnitude > 1e-12f ? sumOutward.normalized : outwardA;
            if (isReflex)
                cornerBisector = -cornerBisector;
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
                center -= outward * backSideBias;
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

            float anchorShiftX = anchorX - baseAnchorX;
            float meshFollowExtra = Mathf.Abs(anchorShiftX);
            bool meshFollowRightSide = anchorShiftX >= 0f;

            bool widenRightSideForMesh = growOppositeVoidLateralFace ? widenRightSide : !widenRightSide;
            float sideExtraForMesh = sideExtra;
            if (meshFollowExtra > 0.0001f)
            {
                widenRightSideForMesh = meshFollowRightSide;
                sideExtraForMesh += meshFollowExtra;
            }

            if (isReflex)
                SnapReflexCornerQuoinPivotOntoWallCenterline(ref center, cornerPoint, prev.tangent, next.tangent, useA);

            ApplyCornerQuoinUserOffsets(ref center, rot, settings, isReflex, useA);
            if (isReflex)
                ApplyReflexCornerQuoinFreeOffsets(ref center, rot, useA);

            WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);

            Mesh mesh = BuildCornerFourFaceReliefMesh(
                module,
                cornerWidth,
                rowHeight,
                fullDepth,
                widenRightSideForMesh,
                sideExtraForMesh,
                profile.stone.facePlaneJitter,
                GetEffectiveUvMetersPerUnit(profile),
                rng);
            if (mesh != null && mesh.vertexCount > 0)
            {
                GameObject go = new GameObject($"CornerQuoin_{cornerIndex:00}_{rowIndex:00}");
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
                ApplyPerStoneMaterialVariation(profile, mr, rng, true);
                AttachQuoinRuntimeLodIfEnabled(go, mf, mesh, GetEffectiveUvMetersPerUnit(profile));
                if (combineGeneratedStonesPerSide && profile != null && mf.sharedMesh != null)
                    ApplyPerStoneTintAsVertexColors(mf.sharedMesh, profile, rng, true);
                stoneIndex++;
            }

            rowBottom += rowHeight + settings.verticalSpacing;
            rowIndex++;
        }
    }
}
