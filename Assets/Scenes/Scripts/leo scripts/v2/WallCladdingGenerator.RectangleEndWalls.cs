using System.Collections.Generic;
using UnityEngine;

public sealed partial class WallCladdingGenerator
{
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
        if (loopShapeKind != WallLoopShapeKind.Rectangle)
            return;

        for (int i = 0; i < samples.Count; i++)
        {
            PathSample prev = samples[i];
            PathSample next = samples[(i + 1) % samples.Count];
            Vector3 cornerPoint = prev.b;

            float cornerDot = Vector3.Dot(prev.tangent, next.tangent);
            // Ignore near-straight joints; only build crossed corner quoins on real corners.
            if (cornerDot > 0.965f)
                continue;

            Vector3 outwardA = Vector3.Cross(Vector3.up, prev.tangent).normalized * sideSign;
            Vector3 outwardB = Vector3.Cross(Vector3.up, next.tangent).normalized * sideSign;
            Vector3 inwardA = -prev.tangent.normalized;
            Vector3 inwardB = next.tangent.normalized;

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

                // Corner block should read as a bigger near-square mass (not a long thin rectangle).
                float cornerWidth = Mathf.Clamp(
                    Mathf.Max(length * 0.78f, fullDepth * 1.10f),
                    settings.minLength * 0.90f,
                    settings.maxLength * 1.60f);
                float centerY = rowBottom + rowHeight * 0.5f;

                // One stone per row, alternating wall side (zipper pattern).
                bool useA = (rowIndex & 1) == 0;
                Vector3 outward = useA ? outwardA : outwardB;
                Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
                ComputeCornerLateralExtension(profile, settings, cornerWidth, useA, rng, out bool widenRightSide, out float sideExtra);

                // Use the true exterior rectangle corner (offset from centerline corner),
                // then anchor each quoin by its local inner corner.
                float sideOffset = Mathf.Max(0f, wall.thickness * 0.5f - profile.general.sideInset);
                Vector3 exteriorCorner = cornerPoint + (outwardA + outwardB) * sideOffset;

                // Anchor by the inner corner of the stone (not by center):
                // worldCenter = exteriorCorner - rot * localInnerCornerAnchor
                float cornerAnchorInset = Mathf.Clamp(
                    Mathf.Max(profile.stone.horizontalSpacing * 0.18f, 0.002f),
                    0.001f,
                    0.006f);
                float halfLen = cornerWidth * 0.5f;
                float baseAnchorX = useA
                    ? (-halfLen + cornerAnchorInset)  // anchor on base left corner
                    : ( halfLen - cornerAnchorInset); // anchor on base right corner
                float anchorX = baseAnchorX;
                // Lateral move referenced from the anchor face (not mesh center):
                // - A rows: push from right side
                // - B rows: push from left side
                float faceReferenceOffsetX = useA ? -cornerFaceReferenceShift : cornerFaceReferenceShift;
                anchorX += faceReferenceOffsetX;
                anchorX = ApplyCornerLateralStackAlignment(anchorX);
                anchorX = ResolveOtherWallColumnOffset(useA, anchorX);
                Vector3 localInnerCornerAnchor = new Vector3(anchorX, 0f, 0f);
                Vector3 center = exteriorCorner - (rot * localInnerCornerAnchor) + Vector3.up * centerY;

                // Fine tuning:
                // - tiny outward nudge on corner bisector so the corner read stays visible,
                // - slight recess on active face to prevent excessive protrusion.
                Vector3 cornerBisector = (outwardA + outwardB).normalized;
                float cornerExposeNudge = Mathf.Clamp(
                    Mathf.Max(profile.stone.horizontalSpacing * 0.16f, profile.stone.surfaceProtrusion * 0.18f),
                    0.0015f,
                    0.006f);
                center += cornerBisector * cornerExposeNudge;

                // Slightly bias toward the wall interior so the back side reads a bit more.
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

                // Make the active side (left or right depending row/wall) pop out more
                // so mortar read stays visible around corner bricks.
                float sideExtrusionT = EvaluateCornerExtrusionStrength(EffectiveCornerSideExtensionMultiplier());
                float sideWallPop = Mathf.Clamp(
                    Mathf.Max(profile.stone.surfaceProtrusion * 10f, rowHeight * 0.06f) * sideExtrusionT,
                    0f,
                    Mathf.Max(0.200f, wall.thickness * 0.45f));
                if (!alignExteriorCornerColumn)
                {
                    // Mirror side direction for A/B corner rows so both stone types push toward the intended side.
                    float signedSideWallPop = ResolveCornerSignedSideOffset(useA, sideWallPop, EffectiveCornerSideExtensionMultiplier());
                    center += (rot * Vector3.right) * signedSideWallPop;
                }

                WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);
                // Keep mesh growth coherent with anchor-face lateral displacement:
                // if anchor shifts by X on one side, mesh must gain at least X on that same side.
                float anchorShiftX = anchorX - baseAnchorX;
                float meshFollowExtra = Mathf.Abs(anchorShiftX);
                bool meshFollowRightSide = anchorShiftX >= 0f;

                // Swap lateral growth side according to requested artistic orientation.
                bool widenRightSideForMesh = growOppositeVoidLateralFace ? widenRightSide : !widenRightSide;
                if (meshFollowExtra > 0.0001f)
                {
                    widenRightSideForMesh = meshFollowRightSide;
                    sideExtra += meshFollowExtra;
                }
                // Dedicated corner-quoin mesh: 4 vertical faces receive 3D relief (front/back/right/left).
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
                    GameObject go = new GameObject($"CornerQuoin_{i:00}_{rowIndex:00}");
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
                    ApplyPerStoneMaterialVariation(profile, mr, rng, true);
                    stoneIndex++;
                }

                rowBottom += rowHeight + settings.verticalSpacing;
                rowIndex++;
            }
        }
    }
}
