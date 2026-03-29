using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WallObject))]
[RequireComponent(typeof(WallCladdingRuntime))]
public sealed class WallCladdingGenerator : MonoBehaviour
{
    [Header("Profile")]
    [SerializeField] private WallCladdingProfile defaultProfile;
    [SerializeField] private bool autoRegenerate = true;

    [Header("Sides")]
    [SerializeField] private bool generateOutside = true;
    [SerializeField] private bool generateInside = false;

    [Header("Base Wall")]
    [SerializeField] private bool applyFallbackWallMaterial = true;
    [SerializeField] private bool clearWhenProfileMissing = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    [Header("Connector Stones")]
    [SerializeField] private float connectorRightShift = 0.10f;

    private WallObject wall;
    private WallCladdingRuntime runtime;

    private readonly List<WallStoneModuleDefinition> allModules = new List<WallStoneModuleDefinition>(16);
    private readonly Dictionary<WallStoneModuleDefinition, int> usageCounts = new Dictionary<WallStoneModuleDefinition, int>();
    private MaterialPropertyBlock propertyBlock;

    private readonly List<QuoinRowSpan> startQuoinSpans = new List<QuoinRowSpan>(32);
    private readonly List<QuoinRowSpan> endQuoinSpans = new List<QuoinRowSpan>(32);


    private WallStoneModuleDefinition lastUsed;
    private WallStoneModuleDefinition secondLastUsed;

    /// <summary>Detected once per rebuild for closed-loop walls; drives which corner / edge rules run.</summary>
    private WallLoopShapeKind loopShapeKind = WallLoopShapeKind.Unknown;
    private readonly List<float> rectangleCornerDistances = new List<float>(8);

    private enum WallLoopShapeKind
    {
        Unknown = 0,
        OpenPolyline = 1,
        GenericClosedPolygon = 2,
        /// <summary>Exactly 4 segments, ~90° at each vertex (square or rectangle).</summary>
        Rectangle = 3,
        Triangle = 4,
        /// <summary>Many short edges, approximately constant radius from centroid (XZ).</summary>
        CircleLike = 5,
    }

    private struct PathSample
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 tangent;
        public float length;
        public float startDistance;
        public float endDistance;
    }

    private struct WallFrame
    {
        public Vector3 centerline;
        public Vector3 tangent;
        public Vector3 faceNormal;
    }


    private struct QuoinRowSpan
    {
        public float yMin;
        public float yMax;
        public float innerLimit;
    }

    private struct StonePlacement
    {
        public WallStoneModuleDefinition module;
        public float centerDistance;
        public float centerY;
        public float width;
        public float height;
        public float depth;
        public float protrusion;
        public float embed;
    }

    private void Awake()
    {
        CacheRefs();
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        CacheRefs();
        if (autoRegenerate)
            ForceRebuild();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        CacheRefs();
        if (autoRegenerate)
            runtime?.MarkDirty();
    }
#endif

    private void LateUpdate()
    {
        if (!autoRegenerate)
            return;

        CacheRefs();
        if (wall == null || runtime == null)
            return;

        WallCladdingProfile profile = runtime.CurrentProfile != null ? runtime.CurrentProfile : defaultProfile;
        if (profile == null)
        {
            if (clearWhenProfileMissing)
                ClearGenerated();
            return;
        }

        int geometryHash = ComputeGeometryHash();
        if (runtime.IsDirty || runtime.LastGeometryHash != geometryHash)
        {
            if (logDebug)
                Debug.Log($"[WallCladdingGenerator] LateUpdate rebuild on {name} (dirty={runtime.IsDirty}, last={runtime.LastGeometryHash}, new={geometryHash})", this);

            ForceRebuild();
        }
    }

    public void ForceRebuild()
    {
        CacheRefs();
        if (wall == null || runtime == null)
            return;

        WallCladdingProfile profile = runtime.CurrentProfile != null ? runtime.CurrentProfile : defaultProfile;
        if (profile == null)
        {
            if (logDebug)
                Debug.LogWarning("[WallCladdingGenerator] No profile assigned.", this);

            if (clearWhenProfileMissing)
                ClearGenerated();

            return;
        }

        runtime.SetProfile(profile, runtime.CurrentSeed != 0 ? runtime.CurrentSeed : ComputeStableSeed(profile));
        ApplyFallbackMaterial(profile);

        List<Vector3> path = GetWallPath();
        if (path == null || path.Count < 2)
        {
            if (logDebug)
                Debug.LogWarning("[WallCladdingGenerator] Wall path is null or too short.", this);

            ClearGenerated();
            return;
        }

        List<PathSample> samples = BuildPathSamples(path);
        if (samples.Count == 0)
        {
            if (logDebug)
                Debug.LogWarning("[WallCladdingGenerator] No valid path samples.", this);

            ClearGenerated();
            return;
        }

        GatherModules(profile);
        if (allModules.Count == 0)
        {
            if (logDebug)
                Debug.LogWarning("[WallCladdingGenerator] No stone modules found in profile.", this);

            ClearGenerated();
            return;
        }

        ResetUsage();
        runtime.ClearRoot(true);
        runtime.ClearRoot(false);

        System.Random rng = new System.Random(runtime.CurrentSeed);

        if (generateOutside)
            GenerateStoneSide(profile, samples, true, +1f, rng);

        if (generateInside)
            GenerateStoneSide(profile, samples, false, -1f, rng);

        runtime.LastGeometryHash = ComputeGeometryHash();
        runtime.MarkClean();

        if (logDebug)
            Debug.Log($"[WallCladdingGenerator] Rebuild OK on {name}", this);
    }

    public void MarkDirty()
    {
        CacheRefs();
        runtime?.MarkDirty();
    }

    private void CacheRefs()
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (runtime == null)
            runtime = GetComponent<WallCladdingRuntime>();

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    private void ClearGenerated()
    {
        if (runtime == null)
            return;

        runtime.ClearRoot(true);
        runtime.ClearRoot(false);
        runtime.MarkClean();
    }

    private void ApplyFallbackMaterial(WallCladdingProfile profile)
    {
        if (!applyFallbackWallMaterial || profile == null || profile.fallbackWallMaterial == null || wall == null)
            return;

        wall.wallMaterial = profile.fallbackWallMaterial;

        MeshRenderer mr = wall.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sharedMaterial = profile.fallbackWallMaterial;
    }

    private List<Vector3> GetWallPath()
    {
        List<Vector3> preview = wall.GetPreviewPathWorld();
        if (preview != null && preview.Count >= 2)
            return preview;

        IReadOnlyList<Vector3> pts = wall.Points;
        if (pts == null || pts.Count < 2)
            return null;

        return new List<Vector3>(pts);
    }

    private List<PathSample> BuildPathSamples(List<Vector3> path)
    {
        List<PathSample> result = new List<PathSample>(path.Count);
        if (path == null || path.Count < 2)
            return result;

        List<Vector3> work = new List<Vector3>(path);
        if (work.Count > 2 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        float distance = 0f;

        for (int i = 0; i < work.Count - 1; i++)
        {
            Vector3 a = work[i];
            Vector3 b = work[i + 1];

            Vector3 tangent = b - a;
            tangent.y = 0f;
            float len = tangent.magnitude;

            if (len < 0.001f)
                continue;

            tangent /= len;

            result.Add(new PathSample
            {
                a = a,
                b = b,
                tangent = tangent,
                length = len,
                startDistance = distance,
                endDistance = distance + len,
            });

            distance += len;
        }

        if (wall != null && wall.closedLoop && work.Count > 2)
        {
            Vector3 a = work[work.Count - 1];
            Vector3 b = work[0];

            Vector3 tangent = b - a;
            tangent.y = 0f;
            float len = tangent.magnitude;
            if (len >= 0.001f)
            {
                tangent /= len;
                result.Add(new PathSample
                {
                    a = a,
                    b = b,
                    tangent = tangent,
                    length = len,
                    startDistance = distance,
                    endDistance = distance + len,
                });
            }
        }

        return result;
    }

    private WallLoopShapeKind DetectClosedLoopShape(List<PathSample> samples)
    {
        if (wall == null || !wall.closedLoop || samples == null)
            return WallLoopShapeKind.GenericClosedPolygon;
        if (samples.Count < 3)
            return WallLoopShapeKind.GenericClosedPolygon;

        int n = samples.Count;
        if (n == 3)
            return WallLoopShapeKind.Triangle;
        if (n == 4 && IsRectangleFourSegmentLoop(samples))
            return WallLoopShapeKind.Rectangle;
        if (n >= 10 && IsCircleLikeLoop(samples))
            return WallLoopShapeKind.CircleLike;

        return WallLoopShapeKind.GenericClosedPolygon;
    }

    private static bool IsRectangleFourSegmentLoop(List<PathSample> samples)
    {
        if (samples == null || samples.Count != 4)
            return false;

        for (int i = 0; i < 4; i++)
        {
            PathSample prev = samples[i];
            PathSample next = samples[(i + 1) % 4];
            float dot = Vector3.Dot(prev.tangent, next.tangent);
            if (Mathf.Abs(dot) > 0.28f)
                return false;
        }

        return true;
    }

    private static bool IsCircleLikeLoop(List<PathSample> samples)
    {
        if (samples == null || samples.Count < 8)
            return false;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < samples.Count; i++)
            sum += samples[i].b;
        sum /= samples.Count;

        float meanR = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 p = samples[i].b;
            float dx = p.x - sum.x;
            float dz = p.z - sum.z;
            meanR += Mathf.Sqrt(dx * dx + dz * dz);
        }

        meanR /= samples.Count;
        if (meanR < 0.02f)
            return false;

        float acc = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 p = samples[i].b;
            float dx = p.x - sum.x;
            float dz = p.z - sum.z;
            float r = Mathf.Sqrt(dx * dx + dz * dz);
            float d = r - meanR;
            acc += d * d;
        }

        acc /= samples.Count;
        float rel = Mathf.Sqrt(acc) / meanR;
        return rel < 0.085f;
    }

    private void RefreshRectangleCornerDistances(List<PathSample> samples)
    {
        rectangleCornerDistances.Clear();
        if (samples == null || samples.Count == 0)
            return;

        for (int i = 0; i < samples.Count; i++)
        {
            float d = samples[i].endDistance;
            if (d > 0.001f)
                rectangleCornerDistances.Add(d);
        }
    }

    private void GatherModules(WallCladdingProfile profile)
    {
        allModules.Clear();
        AddUnique(allModules, profile.stoneLargeModules);
        AddUnique(allModules, profile.stoneMediumModules);
        AddUnique(allModules, profile.stoneSmallModules);
    }

    private static void AddUnique(List<WallStoneModuleDefinition> target, List<WallStoneModuleDefinition> source)
    {
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            WallStoneModuleDefinition m = source[i];
            if (m == null)
                continue;

            if (!target.Contains(m))
                target.Add(m);
        }
    }

    private void ResetUsage()
    {
        usageCounts.Clear();
        lastUsed = null;
        secondLastUsed = null;
    }

    private void RegisterUsage(WallStoneModuleDefinition module)
    {
        if (module == null)
            return;

        usageCounts.TryGetValue(module, out int count);
        usageCounts[module] = count + 1;
        secondLastUsed = lastUsed;
        lastUsed = module;
    }

    private int GetUsageCount(WallStoneModuleDefinition module)
    {
        return module != null && usageCounts.TryGetValue(module, out int count) ? count : 0;
    }

    private void GenerateStoneSide(WallCladdingProfile profile, List<PathSample> samples, bool outside, float sideSign, System.Random rng)
    {
        Transform root = runtime.GetOrCreateRoot(outside);
        Material stoneMat = profile.stoneMaterial != null ? profile.stoneMaterial : profile.fallbackWallMaterial;
        if (stoneMat == null)
            return;

        float totalLength = samples[samples.Count - 1].endDistance;
        float wallHeight = Mathf.Max(0.1f, wall.height);
        float yMin = Mathf.Max(0f, profile.general.sideInset);
        float yMax = Mathf.Max(yMin + 0.05f, wallHeight - profile.general.sideInset);

        int stoneIndex = 0;

        startQuoinSpans.Clear();
        endQuoinSpans.Clear();

        loopShapeKind = WallLoopShapeKind.OpenPolyline;
        rectangleCornerDistances.Clear();
        if (wall != null && wall.closedLoop && samples != null && samples.Count >= 3)
        {
            loopShapeKind = DetectClosedLoopShape(samples);
            if (loopShapeKind == WallLoopShapeKind.Rectangle)
                RefreshRectangleCornerDistances(samples);
        }

        if (logDebug)
            Debug.Log($"[WallCladdingGenerator] loop shape = {loopShapeKind}", this);

        if (outside)
        {
            if (wall != null && wall.closedLoop)
            {
                if (profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
                {
                    switch (loopShapeKind)
                    {
                        case WallLoopShapeKind.Rectangle:
                            GenerateClosedLoopCornerQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
                            break;
                        case WallLoopShapeKind.CircleLike:
                            break;
                        case WallLoopShapeKind.Triangle:
                            break;
                    }
                }
            }
            else
                GenerateOpenEndQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
        }

        float rowBottom = yMin;
        int rowIndex = 0;

        while (rowBottom < yMax - profile.stone.minStoneHeight)
        {
            float rowHeight = BuildRowHeight(profile, yMax - rowBottom, rng);
            if (rowHeight < profile.stone.minStoneHeight)
                break;

            bool isTopRow = (rowBottom + rowHeight + profile.stone.verticalSpacing) >= (yMax - profile.stone.minStoneHeight * 0.35f);
            if (isTopRow)
            {
                float topCover = Mathf.Max(wall.thickness * 0.18f, profile.stone.surfaceProtrusion * 1.45f, 0.04f);
                rowHeight = Mathf.Min(
                    (yMax - rowBottom) + topCover,
                    Mathf.Max(profile.stone.maxStoneHeight * 1.28f, rowHeight));
            }

            float rowCenterY = rowBottom + rowHeight * 0.5f;
            GenerateRow(profile, root, stoneMat, samples, totalLength, outside, rowIndex, rowCenterY, rowHeight, sideSign, rng, ref stoneIndex);

            rowBottom += rowHeight + profile.stone.verticalSpacing * 1.18f;
            rowIndex++;
        }
    }

    private float BuildRowHeight(WallCladdingProfile profile, float remainingHeight, System.Random rng)
    {
        float h = profile.stone.targetRowHeight * RandomRange(rng, 1f - profile.stone.rowHeightJitter, 1f + profile.stone.rowHeightJitter);
        h = Mathf.Clamp(h, profile.stone.minStoneHeight, profile.stone.maxStoneHeight);
        return Mathf.Min(h, remainingHeight);
    }

    private void GenerateRow(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float totalLength,
        bool outside,
        int rowIndex,
        float rowCenterY,
        float rowHeight,
        float sideSign,
        System.Random rng,
        ref int stoneIndex)
    {
        float usableStart = Mathf.Max(0f, profile.general.sideInset);
        float usableEnd = Mathf.Max(usableStart, totalLength - profile.general.sideInset);

        float startGapMin = 0f;
        float startGapMax = 0f;
        float endGapMin = 0f;
        float endGapMax = 0f;
        float startBoundaryDistance = 0f;
        float endBoundaryDistance = 0f;
        bool hasStartBoundaryZone = false;
        bool hasEndBoundaryZone = false;

        if (outside && !wall.closedLoop && profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
        {
            startBoundaryDistance = GetCachedQuoinInnerLimit(rowCenterY, true);
            float endInnerLimit = GetCachedQuoinInnerLimit(rowCenterY, false);
            endBoundaryDistance = totalLength - endInnerLimit;

            float gapHalfWidth = Mathf.Max(profile.stone.minStoneWidth * 0.85f, profile.stone.horizontalSpacing * 2.0f);
            float safetyInset  = Mathf.Max(profile.stone.horizontalSpacing * 0.5f, 0.008f);
            float clippingGuard = Mathf.Max(profile.stone.horizontalSpacing * 2.10f, profile.stone.minStoneWidth * 0.30f);

            startGapMin = Mathf.Max(0f, startBoundaryDistance - safetyInset);
            startGapMax = startBoundaryDistance + gapHalfWidth + clippingGuard;

            endGapMin = endBoundaryDistance - gapHalfWidth - clippingGuard;
            endGapMax = Mathf.Min(totalLength, endBoundaryDistance + safetyInset);

            usableStart = Mathf.Max(usableStart, startGapMax);
            usableEnd   = Mathf.Min(usableEnd,   endGapMin);

            hasStartBoundaryZone = startBoundaryDistance > 0.001f;
            hasEndBoundaryZone   = endInnerLimit > 0.001f;
        }

        if (hasStartBoundaryZone)
            GenerateBoundaryBlendStone(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, startBoundaryDistance, startGapMin, startGapMax, true, rng, ref stoneIndex);

        float usableLength = usableEnd - usableStart;
        if (usableLength > profile.stone.minRowUsableWidth)
        {
            float stagger = ((rowIndex & 1) == 1) ? rowHeight * profile.stone.staggerFraction : 0f;
            float cursor = Mathf.Min(usableEnd, usableStart + stagger);

            while (cursor < usableEnd - profile.stone.minRowUsableWidth)
            {
                if (outside && wall != null && wall.closedLoop && loopShapeKind == WallLoopShapeKind.Rectangle &&
                    profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
                {
                    if (TryAdvancePastRectangleCornerZone(profile, totalLength, cursor, usableEnd, out float advancedCursor))
                    {
                        cursor = advancedCursor;
                        continue;
                    }
                }

                float remaining = usableEnd - cursor;

                bool nearCorner =
                    profile.stone.preferSmallModulesNearCorners &&
                    (cursor - usableStart < profile.stone.cornerSmallModuleZone ||
                     remaining < profile.stone.cornerSmallModuleZone);

                if (!ChoosePlacement(profile, rowHeight, remaining, nearCorner, rng, out StonePlacement placement))
                    break;

                placement.centerDistance = cursor + placement.width * 0.5f;
                placement.centerY = rowCenterY;

                if (outside && !wall.closedLoop && profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
                {
                    ApplyCachedEndQuoinClearance(profile, totalLength, rowCenterY, ref placement);
                    // Hard clamp: keep first-pass cladding out of connector/filler zones.
                    float mortar = Mathf.Max(profile.stone.horizontalSpacing * 1.52f, 0.0075f);
                    float startLimit = GetCachedQuoinInnerLimit(rowCenterY, true) + mortar;
                    float endLimit = totalLength - GetCachedQuoinInnerLimit(rowCenterY, false) - mortar;
                    if (endLimit > startLimit + profile.stone.minStoneWidth * 0.35f)
                    {
                        float maxAllowedWidth = Mathf.Max(profile.stone.minStoneWidth * 0.30f, endLimit - startLimit);
                        placement.width = Mathf.Min(placement.width, maxAllowedWidth);
                        placement.centerDistance = Mathf.Clamp(
                            placement.centerDistance,
                            startLimit + placement.width * 0.5f,
                            endLimit - placement.width * 0.5f);
                    }
                }

                if (placement.width < profile.stone.minStoneWidth * 0.35f)
                {
                    cursor += profile.stone.horizontalSpacing * 0.8f;
                    continue;
                }

                if (outside && wall != null && wall.closedLoop && loopShapeKind == WallLoopShapeKind.Rectangle &&
                    profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
                {
                    float stoneLeft = placement.centerDistance - placement.width * 0.5f;
                    float stoneRight = placement.centerDistance + placement.width * 0.5f;
                    if (StoneOverlapsRectangleCornerBand(profile, totalLength, stoneLeft, stoneRight))
                    {
                        if (TryAdvancePastRectangleCornerZone(profile, totalLength, cursor, usableEnd, out float jumpCursor))
                            cursor = jumpCursor;
                        else
                            cursor = Mathf.Min(usableEnd, stoneRight + profile.stone.horizontalSpacing * 0.55f);
                        continue;
                    }
                }

                CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, false);
                RegisterUsage(placement.module);

                cursor += placement.width + profile.stone.horizontalSpacing * 1.08f;
            }
        }

        if (hasEndBoundaryZone)
            GenerateBoundaryBlendStone(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, endBoundaryDistance, endGapMin, endGapMax, false, rng, ref stoneIndex);
    }

    private void GenerateBoundaryBlendStone(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float rowCenterY,
        float rowHeight,
        float boundaryDistance,
        float zoneMin,
        float zoneMax,
        bool startBoundary,
        System.Random rng,
        ref int stoneIndex)
    {
        float zoneWidth = zoneMax - zoneMin;
        if (zoneWidth < profile.stone.minStoneWidth * 0.30f)
            return;

        float mortarGap = Mathf.Clamp(
            profile.stone.horizontalSpacing * 0.48f,
            0.0022f,
            Mathf.Max(0.0022f, profile.stone.minStoneWidth * 0.08f));

        float workingMin = zoneMin + mortarGap * 0.35f;
        float workingMax = zoneMax - mortarGap * 0.35f;
        if (workingMax <= workingMin + 0.001f)
            return;

        float availableWidth = workingMax - workingMin;
        float hardMinWidth = Mathf.Max(profile.stone.minStoneWidth * 0.38f, 0.036f);
        if (availableWidth < hardMinWidth)
            return;

        WallStoneModuleDefinition module = PickGapFillerModule(profile, rng);
        if (module == null)
            module = PickEndQuoinModule(profile, rng);
        if (module == null)
            return;

        float wallTop = Mathf.Max(0.10f, wall.height) - profile.general.sideInset;
        float wallBottom = Mathf.Max(0f, profile.general.sideInset);
        float maxHeightInWall = Mathf.Max(profile.stone.minStoneHeight * 0.72f, wallTop - wallBottom - 0.002f);

        float minHeight = Mathf.Max(profile.stone.minStoneHeight * 0.72f, 0.045f);
        float targetHeight = Mathf.Clamp(
            rowHeight + profile.stone.verticalSpacing * 0.62f,
            minHeight,
            Mathf.Min(profile.stone.maxStoneHeight, maxHeightInWall));
        float width = availableWidth;
        float minWidthFromHeight = targetHeight * 0.72f;

        int reductionSteps = 0;
        while (width < minWidthFromHeight && targetHeight > minHeight + 0.001f && reductionSteps < 8)
        {
            targetHeight = Mathf.Max(minHeight, targetHeight * 0.86f);
            minWidthFromHeight = targetHeight * 0.72f;
            reductionSteps++;
        }

        if (width < minWidthFromHeight)
            return;

        float sideContactNudge = Mathf.Clamp(mortarGap * 0.92f, 0.0012f, 0.0058f);
        width += sideContactNudge * 2f;

        bool nearTopRow = (rowCenterY + rowHeight * 0.5f) >= (wallTop - Mathf.Max(rowHeight * 0.55f, 0.02f));
        float topOvershoot = nearTopRow
            ? Mathf.Max(wall.thickness * 0.16f, profile.stone.surfaceProtrusion * 1.35f, 0.03f)
            : 0f;
        float allowedTop = wallTop + topOvershoot;

        if (nearTopRow)
        {
            float boostedTopHeight = Mathf.Max(
                targetHeight,
                Mathf.Min(rowHeight * 1.08f, profile.stone.maxStoneHeight * 1.24f));
            targetHeight = Mathf.Min(boostedTopHeight, allowedTop - wallBottom - 0.002f);
        }

        float centerY = rowCenterY;
        float topLimit = allowedTop - targetHeight * 0.5f - 0.0015f;
        float bottomLimit = wallBottom + targetHeight * 0.5f + 0.0015f;
        centerY = Mathf.Clamp(centerY, bottomLimit, topLimit);

        float protrusion = Mathf.Max(profile.stone.surfaceProtrusion * RandomRange(rng, 0.94f, 1.03f), 0.014f);
        if (nearTopRow)
        {
            // Keep top connector stones flush with the top row read.
            float topTarget = Mathf.Max(profile.stone.surfaceProtrusion * 1.14f, protrusion);
            protrusion = Mathf.Min(topTarget, profile.stone.surfaceProtrusion * 1.30f);
        }
        float embedMortarRef = Mathf.Max(profile.stone.horizontalSpacing * 0.75f, 0.0030f);
        float throughWallEmbed = Mathf.Max(
            wall.thickness + protrusion + embedMortarRef * 0.35f,
            profile.stone.minStoneDepth * 1.10f);

        float stableShiftSeed = Mathf.Sin((rowCenterY + boundaryDistance) * 17.123f);
        float stableShift = Mathf.Clamp(stableShiftSeed * mortarGap * 0.40f, -mortarGap * 0.45f, mortarGap * 0.45f);
        float seamBias = Mathf.Clamp(Mathf.Abs(connectorRightShift) * 0.12f, 0f, availableWidth * 0.04f);
        float centerDistance = (workingMin + workingMax) * 0.5f + stableShift + (startBoundary ? seamBias : -seamBias);

        float halfWidth = width * 0.5f;
        float minCenter = zoneMin + halfWidth - sideContactNudge;
        float maxCenter = zoneMax - halfWidth + sideContactNudge;
        if (maxCenter >= minCenter)
            centerDistance = Mathf.Clamp(centerDistance, minCenter, maxCenter);
        else
            centerDistance = (zoneMin + zoneMax) * 0.5f;

        StonePlacement placement = new StonePlacement
        {
            module = module,
            centerDistance = centerDistance,
            centerY = centerY,
            width = width,
            height = targetHeight,
            depth = throughWallEmbed,
            protrusion = protrusion,
            embed = throughWallEmbed
        };

        CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, true);
        RegisterUsage(module);
    }

    private WallStoneModuleDefinition PickGapFillerModule(WallCladdingProfile profile, System.Random rng)
    {
        WallStoneModuleDefinition best = PickPreferredGapFiller(profile != null ? profile.stoneSmallModules : null, rng);
        if (best != null)
            return best;

        best = PickPreferredGapFiller(profile != null ? profile.stoneMediumModules : null, rng);
        if (best != null)
            return best;

        best = PickWeightedModule(profile != null ? profile.stoneSmallModules : null, rng);
        if (best != null)
            return best;

        return PickWeightedModule(profile != null ? profile.stoneMediumModules : null, rng);
    }

    private WallStoneModuleDefinition PickPreferredGapFiller(List<WallStoneModuleDefinition> list, System.Random rng)
    {
        if (list == null || list.Count == 0)
            return null;

        List<WallStoneModuleDefinition> preferred = null;
        for (int i = 0; i < list.Count; i++)
        {
            WallStoneModuleDefinition m = list[i];
            if (m == null || m.weight <= 0f || m.probability <= 0f)
                continue;

            if (!m.preferAsGapFiller)
                continue;

            preferred ??= new List<WallStoneModuleDefinition>();
            preferred.Add(m);
        }

        if (preferred == null || preferred.Count == 0)
            return null;

        return PickWeightedModule(preferred, rng);
    }

    private bool ChoosePlacement(WallCladdingProfile profile, float rowHeight, float remainingWidth, bool nearCorner, System.Random rng, out StonePlacement result)
    {
        result = default;

        WallStoneModuleDefinition best = null;
        float bestScore = float.MinValue;
        float bestWidth = 0f;
        float bestHeight = 0f;
        float bestDepth = 0f;
        float desiredWidth = ComputeDesiredWidth(profile, rowHeight, remainingWidth, nearCorner, rng);

        for (int i = 0; i < allModules.Count; i++)
        {
            WallStoneModuleDefinition m = allModules[i];
            if (m == null || m.probability <= 0f || m.weight <= 0f)
                continue;

            if (nearCorner && !m.canUseNearCorners)
                continue;

            if (RandomValue(rng) > m.probability)
                continue;

            float ratio = RandomRange(rng, m.minWidthToHeight, m.maxWidthToHeight);
            float width = Mathf.Clamp(
                rowHeight * ratio * RandomRange(rng, 1f - profile.stone.widthJitter, 1f + profile.stone.widthJitter),
                profile.stone.minStoneWidth,
                profile.stone.maxStoneWidth);

            width = Mathf.Min(width, remainingWidth);

            if (width < profile.stone.minRowUsableWidth)
                continue;

            float height = Mathf.Clamp(
                rowHeight * RandomRange(rng, 1f - profile.stone.heightJitter, 1f + profile.stone.heightJitter),
                profile.stone.minStoneHeight,
                profile.stone.maxStoneHeight);

            float depth = Mathf.Lerp(profile.stone.minStoneDepth, profile.stone.maxStoneDepth, 0.5f) * m.depthMultiplier;
            depth *= RandomRange(rng, 1f - profile.stone.depthJitter, 1f + profile.stone.depthJitter);
            depth = Mathf.Clamp(depth, profile.stone.minStoneDepth, profile.stone.maxStoneDepth);

            float widthFit = 1f - Mathf.Clamp01(Mathf.Abs(width - desiredWidth) / Mathf.Max(0.001f, desiredWidth));
            float usagePenalty = 1f / (1f + GetUsageCount(m) * 0.35f);
            float repeatPenalty = (m == lastUsed) ? 0.35f : (m == secondLastUsed ? 0.70f : 1f);

            float sliverPenalty = 1f;
            float after = remainingWidth - width - profile.stone.horizontalSpacing;
            if (after > 0f && after < profile.stone.rejectSliverGapBelow)
                sliverPenalty = 0.55f;

            float classBias = 1f;
            if (nearCorner)
            {
                if (m.sizeClass == StoneModuleSizeClass.Small) classBias *= 1.15f;
                if (m.sizeClass == StoneModuleSizeClass.Large) classBias *= 0.88f;
            }
            else
            {
                if (remainingWidth > rowHeight * 2f && m.sizeClass == StoneModuleSizeClass.Large) classBias *= 1.08f;
                if (remainingWidth < rowHeight * 1.25f && (m.sizeClass == StoneModuleSizeClass.Small || m.preferAsGapFiller)) classBias *= 1.12f;
            }

            float score = (widthFit * 2.2f + usagePenalty * 0.8f) * repeatPenalty * sliverPenalty * classBias * m.weight;
            score *= RandomRange(rng, 0.97f, 1.03f);

            if (score > bestScore)
            {
                bestScore = score;
                best = m;
                bestWidth = width;
                bestHeight = height;
                bestDepth = depth;
            }
        }

        if (best == null)
            return false;

        ClampWallFaceStoneToElongatedRectangle(profile, remainingWidth, rng, ref bestWidth, ref bestHeight);

        if (bestWidth < profile.stone.minRowUsableWidth || bestHeight < profile.stone.minStoneHeight * 0.75f)
            return false;

        result.module = best;
        result.width = bestWidth;
        result.height = bestHeight;
        result.depth = bestDepth;
        result.protrusion = Mathf.Min(profile.stone.surfaceProtrusion, bestDepth * 0.45f);
        result.embed = Mathf.Min(profile.stone.embedDepth, bestDepth * 0.65f);
        return true;
    }

    /// <summary>
    /// Face stones stay long rectangles along the wall run: width (path) must stay clearly above height (row band).
    /// </summary>
    private void ClampWallFaceStoneToElongatedRectangle(
        WallCladdingProfile profile,
        float remainingWidth,
        System.Random rng,
        ref float width,
        ref float height)
    {
        float minWidthOverHeight = 1.28f;
        if (width >= height * minWidthOverHeight - 0.0001f)
            return;

        float targetW = height * minWidthOverHeight * RandomRange(rng, 1.0f, 1.16f);
        if (targetW <= remainingWidth + 0.0001f)
        {
            width = Mathf.Min(remainingWidth, targetW);
            return;
        }

        width = remainingWidth;
        float maxH = width / minWidthOverHeight;
        height = Mathf.Max(
            profile.stone.minStoneHeight * 0.78f,
            Mathf.Min(height, maxH));
    }

    private float ComputeDesiredWidth(WallCladdingProfile profile, float rowHeight, float remainingWidth, bool nearCorner, System.Random rng)
    {
        float ratioMin = Mathf.Max(profile.stone.minWidthVsHeight, 1.18f);
        float ratioMax = nearCorner ? profile.stone.nearCornerMaxWidthVsHeight : profile.stone.maxWidthVsHeight;
        if (ratioMax < ratioMin)
            ratioMax = ratioMin;

        float desired = rowHeight * RandomRange(rng, ratioMin, ratioMax);
        desired = Mathf.Clamp(desired, profile.stone.minStoneWidth, profile.stone.maxStoneWidth);
        return Mathf.Min(desired, remainingWidth);
    }

    private void AddQuoinSpan(bool startEnd, float yMin, float yMax, float innerLimit)
    {
        QuoinRowSpan span = new QuoinRowSpan
        {
            yMin = yMin,
            yMax = yMax,
            innerLimit = Mathf.Max(0f, innerLimit)
        };

        if (startEnd)
            startQuoinSpans.Add(span);
        else
            endQuoinSpans.Add(span);
    }

    private float GetRectangleCornerHalfZone(WallCladdingProfile profile)
    {
        float cornerReserve = Mathf.Max(
            profile.stone.endQuoins != null ? profile.stone.endQuoins.reserveWidth * 0.68f : 0f,
            profile.stone.minStoneWidth * 0.72f,
            wall != null ? wall.thickness * 0.28f : 0f);
        float cornerGap = Mathf.Max(profile.stone.horizontalSpacing * 1.08f, 0.008f);
        return cornerReserve + cornerGap;
    }

    private bool StoneOverlapsRectangleCornerBand(
        WallCladdingProfile profile,
        float totalLength,
        float stoneLeft,
        float stoneRight)
    {
        if (rectangleCornerDistances == null || rectangleCornerDistances.Count == 0 || totalLength <= 0.001f)
            return false;

        float halfZone = GetRectangleCornerHalfZone(profile) + Mathf.Max(profile.stone.horizontalSpacing * 0.35f, 0.006f);

        for (int i = 0; i < rectangleCornerDistances.Count; i++)
        {
            float corner = rectangleCornerDistances[i];
            for (int k = -1; k <= 1; k++)
            {
                float c = corner + totalLength * k;
                float z0 = c - halfZone;
                float z1 = c + halfZone;
                if (stoneRight > z0 && stoneLeft < z1)
                    return true;
            }
        }

        return false;
    }

    private bool TryAdvancePastRectangleCornerZone(
        WallCladdingProfile profile,
        float totalLength,
        float cursor,
        float usableEnd,
        out float advancedCursor)
    {
        advancedCursor = cursor;
        if (rectangleCornerDistances == null || rectangleCornerDistances.Count == 0 || totalLength <= 0.001f)
            return false;

        float halfZone = GetRectangleCornerHalfZone(profile);

        for (int i = 0; i < rectangleCornerDistances.Count; i++)
        {
            float corner = rectangleCornerDistances[i];
            for (int k = -1; k <= 1; k++)
            {
                float shifted = corner + totalLength * k;
                float zoneStart = shifted - halfZone;
                float zoneEnd = shifted + halfZone;
                if (cursor >= zoneStart && cursor < zoneEnd)
                {
                    advancedCursor = Mathf.Min(usableEnd, zoneEnd + profile.stone.horizontalSpacing * 0.42f);
                    return true;
                }
            }
        }

        return false;
    }

    private float GetCachedQuoinInnerLimit(float y, bool startEnd)
    {
        List<QuoinRowSpan> spans = startEnd ? startQuoinSpans : endQuoinSpans;
        float best = 0f;

        for (int i = 0; i < spans.Count; i++)
        {
            QuoinRowSpan span = spans[i];
            if (y >= span.yMin && y <= span.yMax)
                return span.innerLimit;

            if (Mathf.Abs(y - (span.yMin + span.yMax) * 0.5f) < 0.12f)
                best = Mathf.Max(best, span.innerLimit);
        }

        return best;
    }

    private void ApplyCachedEndQuoinClearance(WallCladdingProfile profile, float totalLength, float rowCenterY, ref StonePlacement placement)
    {
        float startLimit = GetCachedQuoinInnerLimit(rowCenterY, true);
        float endLimit = totalLength - GetCachedQuoinInnerLimit(rowCenterY, false);

        float stoneLeft = placement.centerDistance - placement.width * 0.5f;
        float stoneRight = placement.centerDistance + placement.width * 0.5f;

        float distToStart = stoneLeft - startLimit;
        float distToEnd = endLimit - stoneRight;

        float blendWidth = Mathf.Max(0.20f, profile.stone.minStoneWidth * 1.70f);

        if (distToStart < blendWidth)
        {
            float t = Mathf.Clamp01(Mathf.Max(0f, distToStart) / blendWidth);
            placement.width *= Mathf.Lerp(0.70f, 1f, t);
        }

        if (distToEnd < blendWidth)
        {
            float t = Mathf.Clamp01(Mathf.Max(0f, distToEnd) / blendWidth);
            placement.width *= Mathf.Lerp(0.70f, 1f, t);
        }

        placement.protrusion = Mathf.Max(0.0065f, placement.protrusion);
        placement.embed = Mathf.Max(profile.stone.minStoneDepth * 0.30f, placement.embed);
        placement.width = Mathf.Max(profile.stone.minStoneWidth * 0.65f, placement.width);
    }

    private float GetReservedEndQuoinWidth(WallCladdingProfile profile)
    {
        if (profile == null || profile.stone == null || profile.stone.endQuoins == null || !profile.stone.endQuoins.enabled)
            return 0f;

        return Mathf.Max(profile.stone.endQuoins.reserveWidth, profile.stone.endQuoins.maxLength + profile.stone.horizontalSpacing);
    }

    private void GenerateOpenEndQuoins(
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
        if (!settings.enabled || wall == null || wall.closedLoop || samples == null || samples.Count == 0)
            return;

        PathSample first = samples[0];
        PathSample last = samples[samples.Count - 1];

        GenerateSingleEndQuoinStack(profile, root, stoneMaterial, first.a, first.tangent, sideSign, true, yMin, yMax, settings, rng, ref stoneIndex);
        GenerateSingleEndQuoinStack(profile, root, stoneMaterial, last.b, last.tangent, sideSign, false, yMin, yMax, settings, rng, ref stoneIndex);
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

                float revealAtWallEnd = Mathf.Clamp(
                    Mathf.Max(wall.thickness * 0.10f, settings.extraOutsideDepth * 0.55f),
                    0.02f,
                    Mathf.Max(0.02f, length * 0.20f));

                float fullDepth = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f + 0.04f, wall.thickness + 0.01f);
                fullDepth *= Mathf.Max(1f, settings.cornerLDepthMul);
                float centerY = rowBottom + rowHeight * 0.5f;
                float cornerPull = Mathf.Clamp(Mathf.Max(profile.stone.horizontalSpacing * 0.45f, 0.010f), 0.006f, length * 0.25f);
                float runFromCorner = Mathf.Max(0f, length * 0.5f - settings.edgeInset - revealAtWallEnd - cornerPull);

                // One stone per row, alternating wall side (zipper pattern).
                bool useA = (rowIndex & 1) == 0;
                Vector3 inward = useA ? inwardA : inwardB;
                Vector3 outward = useA ? outwardA : outwardB;
                Vector3 center = cornerPoint + inward * runFromCorner + Vector3.up * centerY;
                Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);

                WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);
                // Dedicated corner-quoin mesh: 4 vertical faces receive 3D relief (front/back/right/left).
                Mesh mesh = BuildCornerFourFaceReliefMesh(
                    module,
                    length,
                    rowHeight,
                    fullDepth,
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

    private void GenerateSingleEndQuoinStack(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        Vector3 endPoint,
        Vector3 segmentTangent,
        float sideSign,
        bool startEnd,
        float yMin,
        float yMax,
        EndQuoinSettings settings,
        System.Random rng,
        ref int stoneIndex)
    {
        Vector3 tangent = segmentTangent.normalized;
        if (tangent.sqrMagnitude < 0.000001f)
            return;

        Vector3 inwardTangent = startEnd ? tangent : -tangent;
        Vector3 outwardNormal = Vector3.Cross(Vector3.up, tangent).normalized * sideSign;

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

            float revealAtWallEnd = Mathf.Clamp(
                Mathf.Max(wall.thickness * 0.10f, settings.extraOutsideDepth * 0.55f),
                0.02f,
                Mathf.Max(0.02f, length * 0.20f));

            float inwardCoverage = Mathf.Max(0f, length - settings.edgeInset - revealAtWallEnd);
            AddQuoinSpan(startEnd, rowBottom, rowBottom + rowHeight, inwardCoverage);

            // Make end quoins read as structural pillars: +2 cm protrusion
            // on both front and back faces.
            float fullDepth = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f + 0.04f, wall.thickness + 0.01f);
            float centerY = rowBottom + rowHeight * 0.5f;

            Vector3 center = endPoint;
            center += inwardTangent * Mathf.Max(0f, length * 0.5f - settings.edgeInset - revealAtWallEnd);
            center += Vector3.up * centerY;

            Quaternion rot = Quaternion.LookRotation(outwardNormal, Vector3.up);

            WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);
            Mesh mesh = BuildEndQuoinMesh(module, length, rowHeight, fullDepth, profile.stone.facePlaneJitter, profile.stone.uvMetersPerUnit, rng);
            if (mesh != null && mesh.vertexCount > 0)
            {
                GameObject go = new GameObject(startEnd ? $"EndQuoin_Start_{rowIndex:00}" : $"EndQuoin_End_{rowIndex:00}");
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

    private WallStoneModuleDefinition PickEndQuoinModule(WallCladdingProfile profile, System.Random rng)
    {
        WallStoneModuleDefinition best = PickWeightedModule(profile != null ? profile.stoneLargeModules : null, rng);
        if (best != null)
            return best;

        best = PickWeightedModule(profile != null ? profile.stoneMediumModules : null, rng);
        if (best != null)
            return best;

        return PickWeightedModule(profile != null ? profile.stoneSmallModules : null, rng);
    }

    private WallStoneModuleDefinition PickWeightedModule(List<WallStoneModuleDefinition> list, System.Random rng)
    {
        if (list == null || list.Count == 0)
            return null;

        float total = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            WallStoneModuleDefinition m = list[i];
            if (m == null || m.weight <= 0f || m.probability <= 0f)
                continue;
            total += m.weight;
        }

        if (total <= 0f)
            return null;

        float roll = RandomRange(rng, 0f, total);
        float acc = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            WallStoneModuleDefinition m = list[i];
            if (m == null || m.weight <= 0f || m.probability <= 0f)
                continue;

            acc += m.weight;
            if (roll <= acc)
                return m;
        }

        return null;
    }

    private Mesh BuildEndQuoinMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float depth,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        if (width <= 0.01f || height <= 0.01f || depth <= 0.01f)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = depth * 0.5f;
        float backZ = -depth * 0.5f;

        float leftX = -halfW;
        float rightX = halfW;
        float totalFrontWidth = width;

        float cutMin = module != null ? module.minCornerCut : 0.05f;
        float cutMax = module != null ? module.maxCornerCut : 0.12f;
        // Emphasize front corner cuts for a clearer beveled-edge read.
        cutMin = Mathf.Clamp01(cutMin * 1.18f);
        cutMax = Mathf.Clamp(cutMax * 1.24f, cutMin, 0.45f);
        float cutBottom = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTop = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutBL = cutBottom;
        float cutBR = cutBottom;
        float cutTR = cutTop;
        float cutTL = cutTop;

        float relief = module != null ? module.frontRelief : 0.025f;
        float frontJitter = planeJitter + relief;

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(leftX + totalFrontWidth * cutBL, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[1] = new Vector3(rightX - totalFrontWidth * cutBR, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[2] = new Vector3(rightX, -halfH + height * cutBR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[3] = new Vector3(rightX,  halfH - height * cutTR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[4] = new Vector3(rightX - totalFrontWidth * cutTR,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[5] = new Vector3(leftX + totalFrontWidth * cutTL,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[6] = new Vector3(leftX,  halfH - height * cutTL, frontZ + RandomRange(rng, 0f, frontJitter));
        front[7] = new Vector3(leftX, -halfH + height * cutBL, frontZ + RandomRange(rng, 0f, frontJitter));

        float backJitterQuoin = frontJitter;
        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            back[i] = new Vector3(
                front[i].x,
                front[i].y,
                backZ - RandomRange(rng, 0f, backJitterQuoin));
        }

        return BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit);
    }

    /// <summary>
    /// Corner quoin variant with explicit 3D relief on the 4 vertical faces.
    /// Top and bottom remain simple caps (no extra relief intent).
    /// </summary>
    private Mesh BuildCornerFourFaceReliefMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float depth,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        if (width <= 0.01f || height <= 0.01f || depth <= 0.01f)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float halfD = depth * 0.5f;

        float relief = module != null ? module.frontRelief : 0.025f;
        // Push corner-stone face relief a bit to match the visual intensity of regular stones.
        float maxRelief = Mathf.Max(0.001f, (planeJitter + relief) * 1.22f);

        float xLeft = -halfW;
        float xRight = halfW;
        float zBack = -halfD;
        float zFront = halfD;
        float yBottom = -halfH;
        float yTop = halfH;

        List<Vector3> verts = new List<Vector3>(80);
        List<int> tris = new List<int>(120);
        List<Vector2> uvs = new List<Vector2>(80);

        // 4 vertical faces receive stone-like relief.
        AddReliefQuad(
            verts, tris, uvs,
            new Vector3(xLeft, yBottom, zFront),
            new Vector3(xRight, yBottom, zFront),
            new Vector3(xRight, yTop, zFront),
            new Vector3(xLeft, yTop, zFront),
            Vector3.forward,
            maxRelief,
            rng,
            uvMetersPerUnit);

        AddReliefQuad(
            verts, tris, uvs,
            new Vector3(xRight, yBottom, zBack),
            new Vector3(xLeft, yBottom, zBack),
            new Vector3(xLeft, yTop, zBack),
            new Vector3(xRight, yTop, zBack),
            Vector3.back,
            maxRelief,
            rng,
            uvMetersPerUnit);

        AddReliefQuad(
            verts, tris, uvs,
            new Vector3(xRight, yBottom, zFront),
            new Vector3(xRight, yBottom, zBack),
            new Vector3(xRight, yTop, zBack),
            new Vector3(xRight, yTop, zFront),
            Vector3.right,
            maxRelief * 0.92f,
            rng,
            uvMetersPerUnit);

        AddReliefQuad(
            verts, tris, uvs,
            new Vector3(xLeft, yBottom, zBack),
            new Vector3(xLeft, yBottom, zFront),
            new Vector3(xLeft, yTop, zFront),
            new Vector3(xLeft, yTop, zBack),
            Vector3.left,
            maxRelief * 0.92f,
            rng,
            uvMetersPerUnit);

        // Top and bottom remain flat (as requested).
        AddDoubleSidedQuad(
            verts, tris, uvs,
            new Vector3(xLeft, yTop, zFront),
            new Vector3(xRight, yTop, zFront),
            new Vector3(xRight, yTop, zBack),
            new Vector3(xLeft, yTop, zBack),
            uvMetersPerUnit);
        AddDoubleSidedQuad(
            verts, tris, uvs,
            new Vector3(xRight, yBottom, zBack),
            new Vector3(xRight, yBottom, zFront),
            new Vector3(xLeft, yBottom, zFront),
            new Vector3(xLeft, yBottom, zBack),
            uvMetersPerUnit);

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedCornerQuoin4Faces";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddReliefQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 outward,
        float maxRelief,
        System.Random rng,
        float uvMetersPerUnit)
    {
        int start = verts.Count;
        float baseU = (b - a).magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        float baseV = (d - a).magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);

        // 5x5 grid lets us create a subtle beveled ring near face borders
        // plus a softer inner bump, closer to regular stone look.
        int gridCount = 5;
        int vertCount = gridCount * gridCount;
        Vector2[] grid = new Vector2[vertCount];
        float[] weights = new float[vertCount];
        int idx = 0;
        for (int gy = 0; gy < gridCount; gy++)
        {
            float v = (float)gy / (gridCount - 1);
            for (int gx = 0; gx < gridCount; gx++, idx++)
            {
                float u = (float)gx / (gridCount - 1);
                grid[idx] = new Vector2(u, v);

                bool border = gx == 0 || gy == 0 || gx == gridCount - 1 || gy == gridCount - 1;
                if (border)
                {
                    // Keep shared face borders locked to avoid corner cracks.
                    weights[idx] = 0f;
                    continue;
                }

                int edgeDist = Mathf.Min(Mathf.Min(gx, gridCount - 1 - gx), Mathf.Min(gy, gridCount - 1 - gy));
                if (edgeDist == 1)
                {
                    // Bevel ring near borders.
                    weights[idx] = RandomRange(rng, -0.34f, -0.14f);
                }
                else
                {
                    // Inner mass: stronger outward relief (still no center spike).
                    weights[idx] = RandomRange(rng, 0.30f, 0.62f);
                }
            }
        }

        for (int i = 0; i < vertCount; i++)
        {
            float u = grid[i].x;
            float v = grid[i].y;
            Vector3 p =
                a * ((1f - u) * (1f - v)) +
                b * (u * (1f - v)) +
                d * ((1f - u) * v) +
                c * (u * v);

            float jitter = RandomRange(rng, 0.72f, 1.00f);
            p += outward * (maxRelief * weights[i] * jitter);
            verts.Add(p);
            uvs.Add(new Vector2(u * baseU, v * baseV));
        }

        for (int gy = 0; gy < gridCount - 1; gy++)
        {
            for (int gx = 0; gx < gridCount - 1; gx++)
            {
                int i00 = start + gy * gridCount + gx;
                int i10 = start + gy * gridCount + (gx + 1);
                int i01 = start + (gy + 1) * gridCount + gx;
                int i11 = start + (gy + 1) * gridCount + (gx + 1);

                tris.Add(i00); tris.Add(i10); tris.Add(i11);
                tris.Add(i00); tris.Add(i11); tris.Add(i01);
            }
        }
    }

    /// <summary>
    /// Exterior 90° corner: L footprint in local XZ (Unity: +X along first wall arm, +Z along second), Y up.
    /// All vertical perimeter faces get the same relief/UV treatment as flat quoins (top/bottom caps stay flat).
    /// </summary>
    private Mesh BuildCornerLQuoinMesh(
        WallStoneModuleDefinition module,
        float armLength,
        float legThickness,
        float height,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        float L = Mathf.Max(0.02f, armLength);
        float t = Mathf.Clamp(legThickness, 0.02f, Mathf.Max(0.02f, L * 0.72f));
        if (height <= 0.01f)
            return null;

        float halfH = height * 0.5f;
        float relief = module != null ? module.frontRelief : 0.025f;
        // Keep displacement uniform per face so each vertical quad stays planar (per-vertex jitter twists the quad and shatters the mesh).
        float faceOffsetMax = Mathf.Min(planeJitter + relief, Mathf.Min(L, t) * 0.12f);

        // CCW outer boundary in XZ (view from +Y): inner building corner at (0,0).
        Vector2[] xz = new Vector2[6];
        xz[0] = new Vector2(0f, 0f);
        xz[1] = new Vector2(L, 0f);
        xz[2] = new Vector2(L, t);
        xz[3] = new Vector2(t, t);
        xz[4] = new Vector2(t, L);
        xz[5] = new Vector2(0f, L);

        List<Vector3> verts = new List<Vector3>(128);
        List<int> tris = new List<int>(256);
        List<Vector2> uvs = new List<Vector2>(128);

        // Top / bottom caps: two non-overlapping quads (union of horizontal strip + vertical strip above notch).
        void AddCap(float y, bool flipWinding)
        {
            // R1: [0,L]x[0,t] (non-crossed quad order).
            Vector3 r1_00 = new Vector3(0f, y, 0f);
            Vector3 r1_L0 = new Vector3(L, y, 0f);
            Vector3 r1_Lt = new Vector3(L, y, t);
            Vector3 r1_0t = new Vector3(0f, y, t);
            AddQuad(verts, tris, uvs, r1_00, r1_L0, r1_Lt, r1_0t, uvMetersPerUnit, flipWinding);

            // R2: [0,t]×[t,L] (remainder of vertical arm)
            if (L > t + 0.0001f)
            {
                Vector3 r2_0t = new Vector3(0f, y, t);
                Vector3 r2_tt = new Vector3(t, y, t);
                Vector3 r2_tL = new Vector3(t, y, L);
                Vector3 r2_0L = new Vector3(0f, y, L);
                AddQuad(verts, tris, uvs, r2_0t, r2_tt, r2_tL, r2_0L, uvMetersPerUnit, flipWinding);
            }
        }

        // With AddQuad default winding on XZ plane, normal points -Y.
        AddCap(-halfH, false); // bottom -> -Y
        AddCap(halfH, true);   // top -> +Y

        // Vertical perimeter: one quad per edge with outward horizontal displacement (3D stone look).
        for (int i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            Vector2 p0 = xz[i];
            Vector2 p1 = xz[j];
            float dx = p1.x - p0.x;
            float dz = p1.y - p0.y;
            if (dx * dx + dz * dz < 1e-10f)
                continue;

            // CCW footprint in XZ: outward in the horizontal plane is perpendicular to (dx,dz).
            Vector3 outward = new Vector3(dz, 0f, -dx);
            outward.Normalize();

            float rFace = RandomRange(rng, 0f, faceOffsetMax);

            // Planar quad: one offset per face. Vertex order a→b→c→d is CCW on the face when viewed from
            // outside (normal ≈ outward), matching AddQuad's triangulation.
            Vector3 a = new Vector3(p0.x, -halfH, p0.y) + outward * rFace;
            Vector3 b = new Vector3(p0.x, halfH, p0.y) + outward * rFace;
            Vector3 c = new Vector3(p1.x, halfH, p1.y) + outward * rFace;
            Vector3 d = new Vector3(p1.x, -halfH, p1.y) + outward * rFace;

            AddQuad(verts, tris, uvs, a, b, c, d, uvMetersPerUnit);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedCornerLStone";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uvMetersPerUnit,
        bool flipWinding)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        Vector3 edgeU = b - a;
        Vector3 edgeV = d - a;
        float u1 = edgeU.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        float v1 = edgeV.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(u1, 0f));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(0f, v1));

        if (!flipWinding)
        {
            tris.Add(start + 0);
            tris.Add(start + 1);
            tris.Add(start + 2);
            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }
        else
        {
            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);
            tris.Add(start + 0);
            tris.Add(start + 3);
            tris.Add(start + 2);
        }
    }

    private Mesh BuildExtrudedPolygonMesh(Vector3[] front, Vector3[] back, float uvMetersPerUnit)
    {
        if (front == null || back == null || front.Length < 3 || back.Length != front.Length)
            return null;

        List<Vector3> verts = new List<Vector3>(front.Length * 10);
        List<int> tris = new List<int>(front.Length * 18);
        List<Vector2> uvs = new List<Vector2>(front.Length * 10);

        AddPolygonFace(verts, tris, uvs, front, true, uvMetersPerUnit);
        AddPolygonFace(verts, tris, uvs, back, false, uvMetersPerUnit);

        for (int i = 0; i < front.Length; i++)
        {
            int next = (i + 1) % front.Length;
            AddDoubleSidedQuad(verts, tris, uvs, front[i], front[next], back[next], back[i], uvMetersPerUnit);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedExtrudedStone";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddPolygonFace(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3[] points,
        bool frontFace,
        float uvMetersPerUnit)
    {
        int start = verts.Count;
        for (int i = 0; i < points.Length; i++)
        {
            verts.Add(points[i]);
            uvs.Add(new Vector2(points[i].x / uvMetersPerUnit, points[i].y / uvMetersPerUnit));
        }

        for (int i = 1; i < points.Length - 1; i++)
        {
            if (frontFace)
            {
                tris.Add(start + 0);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
            else
            {
                tris.Add(start + 0);
                tris.Add(start + i + 1);
                tris.Add(start + i);
            }
        }
    }

    private void AddDoubleSidedQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uvMetersPerUnit)
    {
        AddQuad(verts, tris, uvs, a, b, c, d, uvMetersPerUnit);
        AddQuad(verts, tris, uvs, a, d, c, b, uvMetersPerUnit);
    }

    private void AddQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uvMetersPerUnit)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        Vector3 edgeU = b - a;
        Vector3 edgeV = d - a;
        float u1 = edgeU.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        float v1 = edgeV.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(u1, 0f));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(0f, v1));

        tris.Add(start + 0);
        tris.Add(start + 1);
        tris.Add(start + 2);
        tris.Add(start + 0);
        tris.Add(start + 2);
        tris.Add(start + 3);
    }

    private void CreateStoneObject(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        StonePlacement placement,
        System.Random rng,
        int index,
        bool rigidFacePlacement = false)
    {
        WallFrame frame = GetFrameAtDistance(samples, placement.centerDistance, sideSign);
        float halfThickness = Mathf.Max(0.01f, wall.thickness) * 0.5f;

        Vector3 wallFacePoint = frame.centerline + Vector3.up * placement.centerY;
        wallFacePoint += frame.faceNormal * (halfThickness - profile.general.sideInset);

        Vector3 center;
        Quaternion rot = Quaternion.LookRotation(frame.faceNormal, Vector3.up);

        float meshEmbed = placement.embed;

        if (!rigidFacePlacement)
        {
            float throughWallEmbed = Mathf.Max(
                placement.embed,
                wall.thickness + Mathf.Max(profile.stone.surfaceProtrusion, 0.02f));

            float centerOffset = ((placement.protrusion - placement.embed) * 0.5f) + profile.general.depthOffset;
            center = wallFacePoint + frame.faceNormal * centerOffset;

            center += frame.tangent * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);
            center += Vector3.up * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);

            rot *= Quaternion.Euler(
                RandomRange(rng, -profile.stone.randomPitch, profile.stone.randomPitch),
                RandomRange(rng, -profile.stone.randomYaw, profile.stone.randomYaw),
                RandomRange(rng, -profile.stone.randomRoll, profile.stone.randomRoll));

            meshEmbed = throughWallEmbed;
        }
        else
        {
            meshEmbed = Mathf.Max(placement.embed, wall.thickness + Mathf.Max(profile.stone.surfaceProtrusion, 0.02f));
            // For rigid connector/filler stones, anchor from the wall face
            // to keep a balanced read on both sides.
            float centerOffset = profile.general.depthOffset;
            center = wallFacePoint + frame.faceNormal * centerOffset;
        }

        Vector3 up = rot * Vector3.up;
        Vector3 normal = rot * Vector3.forward;

        Mesh mesh = BuildStoneMesh(
            placement.module,
            placement.width,
            placement.height,
            placement.protrusion,
            meshEmbed,
            profile.stone.facePlaneJitter,
            profile.stone.uvMetersPerUnit,
            rng);

        if (mesh == null || mesh.vertexCount == 0)
            return;

        GameObject go = new GameObject($"Stone_{index:000}");
        go.transform.SetParent(root, false);
        go.transform.localPosition = transform.InverseTransformPoint(center);
        go.transform.localRotation = Quaternion.LookRotation(
            transform.InverseTransformDirection(normal),
            transform.InverseTransformDirection(up));
        go.transform.localScale = Vector3.one;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = stoneMaterial;

        ApplyPerStoneMaterialVariation(profile, mr, rng, false);
    }

    private void ApplyPerStoneMaterialVariation(WallCladdingProfile profile, MeshRenderer mr, System.Random rng, bool preferDarker)
    {
        if (mr == null || profile == null || propertyBlock == null)
            return;

        propertyBlock.Clear();
        Color tint = profile.stone.baseTint;

        if (profile.stone.enablePerStoneColorVariation)
        {
            Color.RGBToHSV(tint, out float h, out float s, out float v);

            float paletteRoll = RandomValue(rng);
            if (paletteRoll < 0.22f)
            {
                s *= 0.70f;
                v += 0.10f;
            }
            else if (paletteRoll < 0.44f)
            {
                s *= 0.85f;
                v += 0.04f;
            }
            else if (paletteRoll < 0.72f)
            {
                v -= 0.02f;
            }
            else
            {
                s += 0.03f;
                v -= 0.05f;
            }

            h = Mathf.Repeat(h + RandomRange(rng, -profile.stone.hueJitter, profile.stone.hueJitter), 1f);
            s = Mathf.Clamp01(s + RandomRange(rng, -profile.stone.saturationJitter, profile.stone.saturationJitter));
            v = Mathf.Clamp01(v + RandomRange(rng, -profile.stone.valueJitter, profile.stone.valueJitter));
            tint = Color.HSVToRGB(h, s, v);
        }

        propertyBlock.SetColor("_BaseColor", tint);
        mr.SetPropertyBlock(propertyBlock);
    }

    private Mesh BuildStoneMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float protrusion,
        float embed,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        if (module == null)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = protrusion;
        float backZ = -embed;

        float cutMin = Mathf.Clamp01(module.minCornerCut * 1.18f);
        float cutMax = Mathf.Clamp(module.maxCornerCut * 1.24f, cutMin, 0.45f);
        float cutBL = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutBR = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTR = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTL = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());

        float frontJitter = planeJitter + module.frontRelief;

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(-halfW + width * cutBL, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[1] = new Vector3( halfW - width * cutBR, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[2] = new Vector3( halfW, -halfH + height * cutBR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[3] = new Vector3( halfW,  halfH - height * cutTR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[4] = new Vector3( halfW - width * cutTR,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[5] = new Vector3(-halfW + width * cutTL,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[6] = new Vector3(-halfW,  halfH - height * cutTL, frontZ + RandomRange(rng, 0f, frontJitter));
        front[7] = new Vector3(-halfW, -halfH + height * cutBL, frontZ + RandomRange(rng, 0f, frontJitter));

        float backJitter = frontJitter;

        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            back[i] = new Vector3(
                front[i].x,
                front[i].y,
                backZ + RandomRange(rng, 0f, backJitter));
        }

        Mesh mesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit);
        if (mesh != null)
            mesh.name = "GeneratedStone";

        return mesh;
    }

    private WallFrame GetFrameAtDistance(List<PathSample> samples, float distance, float sideSign)
    {
        PathSample s = samples[samples.Count - 1];

        for (int i = 0; i < samples.Count; i++)
        {
            if (distance <= samples[i].endDistance || i == samples.Count - 1)
            {
                s = samples[i];
                break;
            }
        }

        float t = Mathf.InverseLerp(s.startDistance, s.endDistance, distance);
        Vector3 center = Vector3.Lerp(s.a, s.b, t);
        Vector3 tangent = s.tangent;
        Vector3 faceNormal = Vector3.Cross(Vector3.up, tangent).normalized * sideSign;

        return new WallFrame
        {
            centerline = center,
            tangent = tangent,
            faceNormal = faceNormal,
        };
    }

    private int ComputeGeometryHash()
    {
        unchecked
        {
            int hash = 17;

            if (wall != null)
            {
                hash = hash * 31 + Mathf.RoundToInt(wall.height * 1000f);
                hash = hash * 31 + Mathf.RoundToInt(wall.thickness * 1000f);
                hash = hash * 31 + (wall.closedLoop ? 1 : 0);

                IReadOnlyList<Vector3> pts = wall.Points;
                if (pts != null)
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector3 p = pts[i];
                        hash = hash * 31 + Mathf.RoundToInt(p.x * 1000f);
                        hash = hash * 31 + Mathf.RoundToInt(p.y * 1000f);
                        hash = hash * 31 + Mathf.RoundToInt(p.z * 1000f);
                    }
                }
            }

            WallCladdingProfile profile = runtime != null ? runtime.CurrentProfile : defaultProfile;
            hash = hash * 31 + (profile != null ? profile.GetInstanceID() : 0);
            hash = hash * 31 + (generateOutside ? 1 : 0);
            hash = hash * 31 + (generateInside ? 1 : 0);

            return hash;
        }
    }

    private int ComputeStableSeed(WallCladdingProfile profile)
    {
        unchecked
        {
            int hash = 23;
            hash = hash * 31 + (profile != null && !string.IsNullOrEmpty(profile.profileId) ? profile.profileId.GetHashCode() : 0);
            hash = hash * 31 + Mathf.RoundToInt((profile != null ? profile.general.randomSeedOffset : 0f) * 1000f);
            hash = hash * 31 + Mathf.RoundToInt((wall != null ? wall.height : 0f) * 100f);
            hash = hash * 31 + Mathf.RoundToInt((wall != null ? wall.thickness : 0f) * 100f);
            hash = hash * 31 + gameObject.GetInstanceID();
            return hash;
        }
    }

    private static float RandomRange(System.Random rng, float min, float max)
    {
        if (Mathf.Approximately(min, max))
            return min;

        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }

    private static float RandomValue(System.Random rng)
    {
        return (float)rng.NextDouble();
    }
}
