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

    private WallObject wall;
    private WallCladdingRuntime runtime;

    private readonly List<WallStoneModuleDefinition> allModules = new List<WallStoneModuleDefinition>(16);
    private readonly Dictionary<WallStoneModuleDefinition, int> usageCounts = new Dictionary<WallStoneModuleDefinition, int>();
    private MaterialPropertyBlock propertyBlock;

    private readonly List<QuoinRowSpan> startQuoinSpans = new List<QuoinRowSpan>(32);
    private readonly List<QuoinRowSpan> endQuoinSpans = new List<QuoinRowSpan>(32);


    private WallStoneModuleDefinition lastUsed;
    private WallStoneModuleDefinition secondLastUsed;

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

        return result;
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

        if (outside)
            GenerateOpenEndQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);

        float rowBottom = yMin;
        int rowIndex = 0;

        while (rowBottom < yMax - profile.stone.minStoneHeight)
        {
            float rowHeight = BuildRowHeight(profile, yMax - rowBottom, rng);
            if (rowHeight < profile.stone.minStoneHeight)
                break;

            float rowCenterY = rowBottom + rowHeight * 0.5f;
            GenerateRow(profile, root, stoneMat, samples, totalLength, outside, rowIndex, rowCenterY, rowHeight, sideSign, rng, ref stoneIndex);

            rowBottom += rowHeight + profile.stone.verticalSpacing;
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
        bool useGapFillers = false;

        if (outside && !wall.closedLoop && profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
        {
            float startInnerBoundary = GetCachedQuoinInnerLimit(rowCenterY, true);
            float endInnerBoundary = totalLength - GetCachedQuoinInnerLimit(rowCenterY, false);

            float antiClipMargin = Mathf.Max(profile.stone.horizontalSpacing * 0.38f, 0.012f);
            float gapInset = Mathf.Max(profile.stone.horizontalSpacing * 0.04f, 0.0015f);

            startGapMin = startInnerBoundary + gapInset;
            startGapMax = startInnerBoundary + antiClipMargin;

            endGapMin = endInnerBoundary - antiClipMargin;
            endGapMax = endInnerBoundary - gapInset;

            usableStart = Mathf.Max(usableStart, startGapMax);
            usableEnd = Mathf.Min(usableEnd, endGapMin);

            useGapFillers = (startGapMax - startGapMin) > 0.025f || (endGapMax - endGapMin) > 0.025f;
        }

        float usableLength = usableEnd - usableStart;
        if (usableLength > profile.stone.minRowUsableWidth)
        {
            float stagger = ((rowIndex & 1) == 1) ? rowHeight * profile.stone.staggerFraction : 0f;
            float cursor = Mathf.Min(usableEnd, usableStart + stagger);

            while (cursor < usableEnd - profile.stone.minRowUsableWidth)
            {
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
                    ApplyCachedEndQuoinClearance(profile, totalLength, rowCenterY, ref placement);

                CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, true);
                RegisterUsage(placement.module);

                cursor += placement.width + profile.stone.horizontalSpacing;
            }
        }

        if (useGapFillers)
        {
            GenerateEndGapFiller(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, startGapMin, startGapMax, rng, ref stoneIndex);
            GenerateEndGapFiller(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, endGapMin, endGapMax, rng, ref stoneIndex);
        }
    }



    private void GenerateEndGapFiller(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float rowCenterY,
        float rowHeight,
        float gapMin,
        float gapMax,
        System.Random rng,
        ref int stoneIndex)
    {
        float gapWidth = gapMax - gapMin;
        float minVisibleGap = Mathf.Max(profile.stone.minStoneWidth * 0.16f, 0.016f);
        if (gapWidth <= minVisibleGap)
            return;

        // Strict no-clipping rule:
        // keep a hard clearance on both sides and also between two fillers if we split the gap.
        float hardClearance = Mathf.Max(profile.stone.horizontalSpacing * 0.45f, 0.010f);
        float outerPad = Mathf.Max(hardClearance, Mathf.Min(gapWidth * 0.18f, 0.035f));

        float workingMin = gapMin + outerPad;
        float workingMax = gapMax - outerPad;
        float workingWidth = workingMax - workingMin;
        if (workingWidth <= Mathf.Max(profile.stone.minStoneWidth * 0.10f, 0.010f))
            return;

        int fillerCount = 1;
        float splitThreshold = Mathf.Max(profile.stone.minStoneWidth * 0.72f, 0.11f);
        if (workingWidth >= splitThreshold)
            fillerCount = 2;

        float innerGap = fillerCount == 2
            ? Mathf.Max(hardClearance, Mathf.Min(workingWidth * 0.16f, 0.028f))
            : 0f;

        float availableForFillers = workingWidth - innerGap;
        if (availableForFillers <= Mathf.Max(profile.stone.minStoneWidth * 0.10f, 0.010f))
            return;

        float perWidth = availableForFillers / fillerCount;
        float minPerFiller = Mathf.Max(profile.stone.minStoneWidth * 0.08f, 0.010f);
        if (perWidth <= minPerFiller)
            return;

        float cursor = workingMin;

        for (int i = 0; i < fillerCount; i++)
        {
            WallStoneModuleDefinition module = PickGapFillerModule(profile, rng);
            if (module == null)
                break;

            float fillerMin = cursor;
            float fillerMax = cursor + perWidth;

            float sidePad = Mathf.Max(hardClearance * 0.45f, Mathf.Min(perWidth * 0.10f, 0.012f));
            float usableWidth = (fillerMax - fillerMin) - sidePad * 2f;
            if (usableWidth > minPerFiller)
            {
                StonePlacement placement = new StonePlacement
                {
                    module = module,
                    centerDistance = (fillerMin + fillerMax) * 0.5f,
                    centerY = rowCenterY,
                    width = usableWidth,
                    height = Mathf.Clamp(rowHeight * 0.78f, profile.stone.minStoneHeight, profile.stone.maxStoneHeight),
                    depth = Mathf.Clamp(profile.stone.minStoneDepth * RandomRange(rng, 0.82f, 0.90f), profile.stone.minStoneDepth, profile.stone.maxStoneDepth),
                    protrusion = Mathf.Max(profile.stone.surfaceProtrusion * 0.14f, 0.006f),
                    embed = Mathf.Max(profile.stone.embedDepth * 0.70f, profile.stone.minStoneDepth * 0.24f)
                };

                CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, true);
            }

            cursor += perWidth + innerGap;
        }
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

        result.module = best;
        result.width = bestWidth;
        result.height = bestHeight;
        result.depth = bestDepth;
        result.protrusion = Mathf.Min(profile.stone.surfaceProtrusion, bestDepth * 0.45f);
        result.embed = Mathf.Min(profile.stone.embedDepth, bestDepth * 0.65f);
        return true;
    }

    private float ComputeDesiredWidth(WallCladdingProfile profile, float rowHeight, float remainingWidth, bool nearCorner, System.Random rng)
    {
        float ratioMin = profile.stone.minWidthVsHeight;
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
            placement.protrusion *= Mathf.Lerp(0.10f, 1f, t);
            placement.embed *= Mathf.Lerp(2.80f, 1f, t);
            placement.width *= Mathf.Lerp(0.84f, 1f, t);
        }

        if (distToEnd < blendWidth)
        {
            float t = Mathf.Clamp01(Mathf.Max(0f, distToEnd) / blendWidth);
            placement.protrusion *= Mathf.Lerp(0.10f, 1f, t);
            placement.embed *= Mathf.Lerp(2.80f, 1f, t);
            placement.width *= Mathf.Lerp(0.84f, 1f, t);
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
            rowHeight = Mathf.Min(rowHeight, yMax - rowBottom);

            if (rowHeight < 0.10f)
                break;

            float baseLength = RandomRange(rng, settings.minLength, settings.maxLength);
            float altScale = ((rowIndex & 1) == 0) ? settings.alternateLongScale : settings.alternateShortScale;
            float length = baseLength * altScale * RandomRange(rng, 1f - settings.lengthJitter, 1f + settings.lengthJitter);
            length = Mathf.Clamp(length, settings.minLength * 0.85f, settings.maxLength * 1.35f);

            float revealAtWallEnd = Mathf.Clamp(
                Mathf.Max(wall.thickness * 0.10f, settings.extraOutsideDepth * 0.55f),
                0.02f,
                Mathf.Max(0.02f, length * 0.20f));

            float inwardCoverage = Mathf.Max(0f, length - settings.edgeInset - revealAtWallEnd);
            AddQuoinSpan(startEnd, rowBottom, rowBottom + rowHeight, inwardCoverage);

            float fullDepth = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f, wall.thickness + 0.01f);
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

        float cutMin = module != null ? module.minCornerCut : 0.05f;
        float cutMax = module != null ? module.maxCornerCut : 0.12f;
        float cutBL = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutBR = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTR = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTL = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());

        float relief = module != null ? module.frontRelief : 0.025f;
        float frontJitter = planeJitter + relief;
        float backJitter = planeJitter * 0.90f + relief * 0.50f;

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(-halfW + width * cutBL, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[1] = new Vector3( halfW - width * cutBR, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[2] = new Vector3( halfW, -halfH + height * cutBR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[3] = new Vector3( halfW,  halfH - height * cutTR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[4] = new Vector3( halfW - width * cutTR,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[5] = new Vector3(-halfW + width * cutTL,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[6] = new Vector3(-halfW,  halfH - height * cutTL, frontZ + RandomRange(rng, 0f, frontJitter));
        front[7] = new Vector3(-halfW, -halfH + height * cutBL, frontZ + RandomRange(rng, 0f, frontJitter));

        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float leanX = module != null ? module.verticalEdgeLean : 0f;
            float leanY = module != null ? module.horizontalEdgeLean : 0f;

            float shiftX = RandomRange(rng, -leanX, leanX) * width * 0.16f;
            float shiftY = RandomRange(rng, -leanY, leanY) * height * 0.12f;
            back[i] = new Vector3(
                front[i].x + shiftX,
                front[i].y + shiftY,
                backZ + RandomRange(rng, -backJitter, backJitter));
        }

        return BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit);
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

        if (!rigidFacePlacement)
        {
            float centerOffset = ((placement.protrusion - placement.embed) * 0.5f) + profile.general.depthOffset;
            center = wallFacePoint + frame.faceNormal * centerOffset;

            center += frame.tangent * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);
            center += Vector3.up * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);

            rot *= Quaternion.Euler(
                RandomRange(rng, -profile.stone.randomPitch, profile.stone.randomPitch),
                RandomRange(rng, -profile.stone.randomYaw, profile.stone.randomYaw),
                RandomRange(rng, -profile.stone.randomRoll, profile.stone.randomRoll));
        }
        else
        {
            // Gap filler:
            // - no random movement in depth
            // - no vertical jitter
            // - front face locked to the same visible wall-face level as normal wall stones
            float targetFrontFaceOffset = Mathf.Max(profile.stone.surfaceProtrusion, 0.018f) + profile.general.depthOffset;
            center = wallFacePoint + frame.faceNormal * (targetFrontFaceOffset - placement.protrusion);
        }

        Vector3 up = rot * Vector3.up;
        Vector3 normal = rot * Vector3.forward;

        Mesh mesh = BuildStoneMesh(
            placement.module,
            placement.width,
            placement.height,
            placement.protrusion,
            placement.embed,
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


    private Mesh BuildGapFillerMesh(
        float width,
        float height,
        float protrusion,
        float embed,
        float uvMetersPerUnit)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = protrusion;
        float backZ = -embed;

        Vector3[] front = new Vector3[4]
        {
            new Vector3(-halfW, -halfH, frontZ),
            new Vector3( halfW, -halfH, frontZ),
            new Vector3( halfW,  halfH, frontZ),
            new Vector3(-halfW,  halfH, frontZ),
        };

        Vector3[] back = new Vector3[4]
        {
            new Vector3(-halfW, -halfH, backZ),
            new Vector3( halfW, -halfH, backZ),
            new Vector3( halfW,  halfH, backZ),
            new Vector3(-halfW,  halfH, backZ),
        };

        Mesh mesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit);
        if (mesh != null)
            mesh.name = "GeneratedGapFiller";
        return mesh;
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

        float cutBL = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());
        float cutBR = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());
        float cutTR = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());
        float cutTL = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());

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

        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float shiftX = RandomRange(rng, -module.verticalEdgeLean, module.verticalEdgeLean) * width * 0.14f;
            float shiftY = RandomRange(rng, -module.horizontalEdgeLean, module.horizontalEdgeLean) * height * 0.10f;
            back[i] = new Vector3(front[i].x + shiftX, front[i].y + shiftY, backZ);
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
