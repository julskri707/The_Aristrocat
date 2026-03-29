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

            rowBottom += rowHeight + profile.stone.verticalSpacing * 1.5f;
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
            GenerateBoundaryBlendStone(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, startBoundaryDistance, startGapMin, startGapMax, rng, ref stoneIndex);

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
                {
                    ApplyCachedEndQuoinClearance(profile, totalLength, rowCenterY, ref placement);
                    // Hard clamp: keep first-pass cladding out of connector/filler zones.
                    float mortar = Mathf.Max(profile.stone.horizontalSpacing * 2.00f, 0.0085f);
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

                CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, false);
                RegisterUsage(placement.module);

                cursor += placement.width + profile.stone.horizontalSpacing * 1.5f;
            }
        }

        if (hasEndBoundaryZone)
            GenerateBoundaryBlendStone(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, endBoundaryDistance, endGapMin, endGapMax, rng, ref stoneIndex);
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
        System.Random rng,
        ref int stoneIndex)
    {
        float zoneWidth = zoneMax - zoneMin;
        if (zoneWidth < profile.stone.minStoneWidth * 0.5f)
            return;

        float mortarGap = Mathf.Clamp(
            profile.stone.horizontalSpacing * 0.75f,
            0.0030f,
            Mathf.Max(0.0030f, profile.stone.minStoneWidth * 0.11f));

        float workingMin = zoneMin + mortarGap * 0.5f;
        float workingMax = zoneMax - mortarGap * 0.5f;
        if (workingMax <= workingMin + 0.001f)
            return;

        float availableWidth = workingMax - workingMin;
        float minPieceWidth = Mathf.Max(profile.stone.minStoneWidth * 0.52f, 0.045f);
        float maxPieceWidth = Mathf.Max(minPieceWidth, Mathf.Min(profile.stone.maxStoneWidth * 0.90f, rowHeight * 1.25f));
        if (availableWidth < minPieceWidth)
            return;

        float preferredWidth = Mathf.Clamp(
            rowHeight * 1.28f,
            minPieceWidth,
            maxPieceWidth);

        int pieceCount = Mathf.Clamp(
            Mathf.RoundToInt((availableWidth + mortarGap) / (preferredWidth + mortarGap)),
            1,
            5);

        while (pieceCount > 1)
        {
            float candidate = (availableWidth - mortarGap * (pieceCount - 1)) / pieceCount;
            if (candidate >= minPieceWidth * 0.98f)
                break;
            pieceCount--;
        }

        float cursor = workingMin;
        for (int i = 0; i < pieceCount; i++)
        {
            int remainingPieces = pieceCount - i;
            float remainingWidth = workingMax - cursor;
            float idealWidth = (remainingWidth - mortarGap * (remainingPieces - 1)) / remainingPieces;

            float minAllowed = minPieceWidth;
            float maxAllowed = Mathf.Min(
                maxPieceWidth,
                remainingWidth - (remainingPieces - 1) * (minPieceWidth + mortarGap));
            if (maxAllowed < minAllowed)
                break;

            float width = idealWidth;
            if (remainingPieces > 1)
                width *= RandomRange(rng, 0.86f, 1.18f);
            width *= ((i & 1) == 0) ? RandomRange(rng, 0.94f, 1.12f) : RandomRange(rng, 0.88f, 1.02f);
            width = Mathf.Clamp(width, minAllowed, maxAllowed);
            width = Mathf.Min(width + mortarGap * 1.18f, maxAllowed);

            WallStoneModuleDefinition module = PickGapFillerModule(profile, rng);
            if (module == null)
                module = PickEndQuoinModule(profile, rng);
            if (module == null)
                break;

            float pieceHeight = Mathf.Clamp(
                rowHeight * RandomRange(rng, 0.82f, 1.02f),
                profile.stone.minStoneHeight * 0.78f,
                profile.stone.maxStoneHeight);
            pieceHeight = Mathf.Min(pieceHeight + rowHeight * 0.10f, profile.stone.maxStoneHeight);
            // Avoid "vertical blade" fillers: enforce a minimum width/height ratio.
            float minWidthFromHeight = pieceHeight * 0.72f;
            if (width < minWidthFromHeight)
            {
                float targetWidth = Mathf.Min(maxAllowed, minWidthFromHeight);
                if (targetWidth > width)
                    width = targetWidth;

                float maxHeightFromWidth = width / 0.72f;
                pieceHeight = Mathf.Min(pieceHeight, maxHeightFromWidth);
            }

            float protrusion = Mathf.Max(profile.stone.surfaceProtrusion * RandomRange(rng, 0.94f, 1.03f), 0.014f);
            float throughWallEmbed = Mathf.Max(
                wall.thickness + protrusion + mortarGap * 0.6f,
                profile.stone.minStoneDepth * 1.15f);

            float wallTop = Mathf.Max(0.10f, wall.height) - profile.general.sideInset;
            float wallBottom = Mathf.Max(0f, profile.general.sideInset);
            float maxAllowedHeightInWall = Mathf.Max(profile.stone.minStoneHeight * 0.75f, wallTop - wallBottom - 0.002f);
            pieceHeight = Mathf.Min(pieceHeight, maxAllowedHeightInWall);

            float centerY = rowCenterY + RandomRange(rng, -rowHeight * 0.07f, rowHeight * 0.07f);
            centerY = Mathf.Clamp(centerY, wallBottom + pieceHeight * 0.5f, wallTop - pieceHeight * 0.5f);

            StonePlacement placement = new StonePlacement
            {
                module = module,
                centerDistance = cursor + width * 0.5f,
                centerY = centerY,
                width = width,
                height = pieceHeight,
                depth = throughWallEmbed,
                protrusion = protrusion,
                embed = throughWallEmbed
            };

            // rigidFacePlacement removes random yaw/pitch/roll and position jitter.
            CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, true);
            RegisterUsage(module);

            cursor += width + mortarGap * 0.92f;
            if (cursor >= workingMax - minPieceWidth * 0.15f)
                break;
        }
    }

    private void CreateGapFillerRectObject(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float centerDistance,
        float centerY,
        float width,
        float height,
        float protrusion,
        float embed,
        System.Random rng,
        int index)
    {
        WallFrame frame = GetFrameAtDistance(samples, centerDistance, sideSign);
        float halfThickness = Mathf.Max(0.01f, wall.thickness) * 0.5f;

        Vector3 wallFacePoint = frame.centerline + Vector3.up * centerY;
        wallFacePoint += frame.faceNormal * (halfThickness - profile.general.sideInset);

        float visibleEmbedForPlacement = Mathf.Max(profile.stone.embedDepth, profile.stone.minStoneDepth * 0.35f);
        float meshEmbed = Mathf.Max(embed, wall.thickness + protrusion);
        float centerOffset = ((protrusion - visibleEmbedForPlacement) * 0.5f) + profile.general.depthOffset;
        Vector3 center = wallFacePoint + frame.faceNormal * centerOffset;

        Quaternion rot = Quaternion.LookRotation(frame.faceNormal, Vector3.up);
        Mesh mesh = BuildGapFillerMesh(width, height, protrusion, meshEmbed, profile.stone.uvMetersPerUnit);
        if (mesh == null || mesh.vertexCount == 0)
            return;

        GameObject go = new GameObject($"GapFiller_{index:000}");
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
        ApplyPerStoneMaterialVariation(profile, mr, rng, false);
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

    private Vector3 ApplyConnectorRightShift(Vector3 center, WallFrame frame)
    {
        return center + frame.tangent * connectorRightShift;
    }

    private void CreateRebuiltGapFillerObject(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        StonePlacement placement,
        System.Random rng,
        int index,
        float leftSideTrim,
        float rightSideExtension,
        float forcedFrontDepth,
        float forcedBackDepth)
    {
        WallFrame frame = GetFrameAtDistance(samples, placement.centerDistance, sideSign);
        Quaternion rot = Quaternion.LookRotation(frame.faceNormal, Vector3.up);

        float halfThickness = Mathf.Max(0.01f, wall.thickness) * 0.5f;

        Vector3 wallFacePoint = frame.centerline + Vector3.up * placement.centerY;
        wallFacePoint += frame.faceNormal * (halfThickness - profile.general.sideInset);

        float frontDepth = Mathf.Max(forcedFrontDepth, placement.protrusion);
        float positionEmbed = Mathf.Max(
            placement.embed,
            profile.stone.minStoneDepth * 0.35f);

        float backDepth = Mathf.Max(
            forcedBackDepth,
            positionEmbed);

        float targetFrontWorld = profile.stone.surfaceProtrusion + profile.general.depthOffset;
        float centerOffset = targetFrontWorld - frontDepth;
        Vector3 center = wallFacePoint + frame.faceNormal * centerOffset;

        Mesh mesh = BuildRebuiltGapFillerMesh(
            placement.width,
            placement.height,
            frontDepth,
            backDepth,
            leftSideTrim,
            rightSideExtension,
            profile.stone.uvMetersPerUnit);

        if (mesh == null || mesh.vertexCount == 0)
            return;

        GameObject go = new GameObject($"GapFiller_{index:000}");
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

        ApplyPerStoneMaterialVariation(profile, mr, rng, false);
    }

    private Mesh BuildRebuiltGapFillerMesh(
        float width,
        float height,
        float frontDepth,
        float backDepth,
        float leftSideTrim,
        float rightSideExtension,
        float uvMetersPerUnit)
    {
        if (width <= 0.01f || height <= 0.01f || frontDepth <= 0.01f || backDepth <= 0.01f)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = frontDepth;
        float backZ = -backDepth;

        float cornerCutW = width * 0.08f;
        float cornerCutH = height * 0.08f;

        float leftX = -halfW + leftSideTrim;
        float rightX = halfW + rightSideExtension;

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(leftX + cornerCutW, -halfH, frontZ);
        front[1] = new Vector3(rightX - cornerCutW, -halfH, frontZ);
        front[2] = new Vector3(rightX, -halfH + cornerCutH, frontZ);
        front[3] = new Vector3(rightX, halfH - cornerCutH, frontZ);
        front[4] = new Vector3(rightX - cornerCutW, halfH, frontZ);
        front[5] = new Vector3(leftX + cornerCutW, halfH, frontZ);
        front[6] = new Vector3(leftX, halfH - cornerCutH, frontZ);
        front[7] = new Vector3(leftX, -halfH + cornerCutH, frontZ);

        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
            back[i] = new Vector3(front[i].x, front[i].y, backZ);

        Mesh mesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit);
        if (mesh != null)
            mesh.name = "GeneratedRebuiltGapFiller";
        return mesh;
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
