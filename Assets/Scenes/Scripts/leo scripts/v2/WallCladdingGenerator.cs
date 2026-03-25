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
    [SerializeField] private bool generateHiddenBackFaces = false;

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

        float rowBottom = yMin;
        int rowIndex = 0;
        int stoneIndex = 0;

        while (rowBottom < yMax - profile.stone.minStoneHeight)
        {
            float rowHeight = BuildRowHeight(profile, yMax - rowBottom, rng);
            if (rowHeight < profile.stone.minStoneHeight)
                break;

            float rowCenterY = rowBottom + rowHeight * 0.5f;
            GenerateRow(profile, root, stoneMat, samples, totalLength, rowIndex, rowCenterY, rowHeight, sideSign, rng, ref stoneIndex);

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
        int rowIndex,
        float rowCenterY,
        float rowHeight,
        float sideSign,
        System.Random rng,
        ref int stoneIndex)
    {
        float usableStart = Mathf.Max(0f, profile.general.sideInset);
        float usableEnd = Mathf.Max(usableStart, totalLength - profile.general.sideInset);
        float usableLength = usableEnd - usableStart;

        if (usableLength <= profile.stone.minRowUsableWidth)
            return;

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

            CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++);
            RegisterUsage(placement.module);

            cursor += placement.width + profile.stone.horizontalSpacing;
        }
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

    private void CreateStoneObject(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        StonePlacement placement,
        System.Random rng,
        int index)
    {
        WallFrame frame = GetFrameAtDistance(samples, placement.centerDistance, sideSign);
        float halfThickness = Mathf.Max(0.01f, wall.thickness) * 0.5f;

        Vector3 facePoint = frame.centerline + Vector3.up * placement.centerY;
        facePoint += frame.faceNormal * (halfThickness - profile.general.sideInset);

        float centerOffset = ((placement.protrusion - placement.embed) * 0.5f) + profile.general.depthOffset;
        Vector3 center = facePoint + frame.faceNormal * centerOffset;
        center += frame.tangent * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);
        center += Vector3.up * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);

        Quaternion rot = Quaternion.LookRotation(frame.faceNormal, Vector3.up);
        rot *= Quaternion.Euler(
            RandomRange(rng, -profile.stone.randomPitch, profile.stone.randomPitch),
            RandomRange(rng, -profile.stone.randomYaw, profile.stone.randomYaw),
            RandomRange(rng, -profile.stone.randomRoll, profile.stone.randomRoll));

        Vector3 up = rot * Vector3.up;
        Vector3 normal = rot * Vector3.forward;

        Mesh mesh = BuildStoneMesh(placement.module, placement.width, placement.height, placement.protrusion, placement.embed, profile.stone.facePlaneJitter, profile.stone.uvMetersPerUnit, rng);
        if (mesh == null || mesh.vertexCount == 0)
            return;

        GameObject go = new GameObject($"Stone_{index:000}");
        go.transform.SetParent(root, false);
        go.transform.localPosition = transform.InverseTransformPoint(center);
        go.transform.localRotation = Quaternion.LookRotation(transform.InverseTransformDirection(normal), transform.InverseTransformDirection(up));
        go.transform.localScale = Vector3.one;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = stoneMaterial;

        ApplyPerStoneMaterialVariation(profile, placement.module, placement.width, placement.height, mr, rng);
    }

    private void ApplyPerStoneMaterialVariation(
        WallCladdingProfile profile,
        WallStoneModuleDefinition module,
        float width,
        float height,
        MeshRenderer mr,
        System.Random rng)
    {
        if (mr == null || profile == null || propertyBlock == null)
            return;

        propertyBlock.Clear();

        Color tint = profile.stone.baseTint;

        if (profile.stone.enablePerStoneColorVariation)
        {
            Color.RGBToHSV(tint, out float baseH, out float baseS, out float baseV);

            float familyRoll = RandomValue(rng);
            float familyHueShift;
            float familySatShift;
            float familyValueShift;

            if (familyRoll < 0.22f)
            {
                // pierres plus claires / poudrées
                familyHueShift = RandomRange(rng, -0.012f, 0.010f);
                familySatShift = RandomRange(rng, -0.08f, -0.01f);
                familyValueShift = RandomRange(rng, 0.10f, 0.22f);
            }
            else if (familyRoll < 0.48f)
            {
                // gris neutre légèrement clair
                familyHueShift = RandomRange(rng, -0.010f, 0.010f);
                familySatShift = RandomRange(rng, -0.03f, 0.03f);
                familyValueShift = RandomRange(rng, 0.04f, 0.12f);
            }
            else if (familyRoll < 0.74f)
            {
                // teinte de base, presque inchangée
                familyHueShift = RandomRange(rng, -0.008f, 0.008f);
                familySatShift = RandomRange(rng, -0.02f, 0.04f);
                familyValueShift = RandomRange(rng, -0.03f, 0.05f);
            }
            else
            {
                // pierres plus foncées / plus denses
                familyHueShift = RandomRange(rng, -0.015f, 0.012f);
                familySatShift = RandomRange(rng, 0.00f, 0.06f);
                familyValueShift = RandomRange(rng, -0.12f, -0.03f);
            }

            if (module != null)
            {
                switch (module.sizeClass)
                {
                    case StoneModuleSizeClass.Large:
                        familyValueShift += RandomRange(rng, -0.04f, 0.02f);
                        familySatShift += RandomRange(rng, -0.01f, 0.02f);
                        break;

                    case StoneModuleSizeClass.Small:
                        familyValueShift += RandomRange(rng, 0.00f, 0.06f);
                        familySatShift += RandomRange(rng, -0.02f, 0.01f);
                        break;
                }
            }

            float aspect = height > 0.0001f ? width / height : 1f;
            float aspectBias = Mathf.InverseLerp(1.0f, 2.4f, aspect);
            familyValueShift += Mathf.Lerp(-0.015f, 0.02f, aspectBias);

            float h = Mathf.Repeat(baseH + familyHueShift + RandomRange(rng, -profile.stone.hueJitter * 1.35f, profile.stone.hueJitter * 1.35f), 1f);
            float s = Mathf.Clamp01(baseS + familySatShift + RandomRange(rng, -profile.stone.saturationJitter * 1.20f, profile.stone.saturationJitter * 1.20f));
            float v = Mathf.Clamp01(baseV + familyValueShift + RandomRange(rng, -profile.stone.valueJitter * 1.40f, profile.stone.valueJitter * 1.40f));

            v = Mathf.Clamp(v, 0.42f, 0.92f);
            s = Mathf.Clamp(s, 0f, 0.30f);

            tint = Color.HSVToRGB(h, s, v);
        }

        propertyBlock.SetColor("_BaseColor", tint);

        if (profile.stone.uvOffsetJitter > 0f)
        {
            Vector4 st = new Vector4(
                1f,
                1f,
                RandomRange(rng, -profile.stone.uvOffsetJitter, profile.stone.uvOffsetJitter),
                RandomRange(rng, -profile.stone.uvOffsetJitter, profile.stone.uvOffsetJitter));

            propertyBlock.SetVector("_BaseMap_ST", st);
        }

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

        float cutBL = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());
        float cutBR = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());
        float cutTR = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());
        float cutTL = Mathf.Lerp(module.minCornerCut, module.maxCornerCut, (float)rng.NextDouble());

        float relief = module.frontRelief * RandomRange(rng, 0.90f, 1.45f);
        float horizontalLean = module.horizontalEdgeLean * RandomRange(rng, 0.65f, 1.25f);
        float verticalLean = module.verticalEdgeLean * RandomRange(rng, 0.65f, 1.25f);

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(-halfW + width * cutBL, -halfH, frontZ);
        front[1] = new Vector3( halfW - width * cutBR, -halfH, frontZ);
        front[2] = new Vector3( halfW, -halfH + height * cutBR, frontZ);
        front[3] = new Vector3( halfW,  halfH - height * cutTR, frontZ);
        front[4] = new Vector3( halfW - width * cutTR,  halfH, frontZ);
        front[5] = new Vector3(-halfW + width * cutTL,  halfH, frontZ);
        front[6] = new Vector3(-halfW,  halfH - height * cutTL, frontZ);
        front[7] = new Vector3(-halfW, -halfH + height * cutBL, frontZ);

        // Rend la pierre moins "brique lisse" : contour un peu irrégulier + façade bombée/facettée.
        for (int i = 0; i < front.Length; i++)
        {
            Vector3 p = front[i];

            float nx = halfW > 0.0001f ? Mathf.Clamp(p.x / halfW, -1f, 1f) : 0f;
            float ny = halfH > 0.0001f ? Mathf.Clamp(p.y / halfH, -1f, 1f) : 0f;

            p.x += width * horizontalLean * RandomRange(rng, -0.35f, 0.35f) * (0.35f + Mathf.Abs(ny) * 0.65f);
            p.y += height * verticalLean * RandomRange(rng, -0.35f, 0.35f) * (0.35f + Mathf.Abs(nx) * 0.65f);

            float edgeRelief = relief * RandomRange(rng, 0.18f, 0.45f);
            float cornerBias = Mathf.Clamp01((Mathf.Abs(nx) + Mathf.Abs(ny)) * 0.5f);
            float inwardBias = 1f - cornerBias;

            p.z = frontZ
                + RandomRange(rng, 0f, planeJitter)
                + edgeRelief * (0.35f + inwardBias * 0.65f);

            front[i] = p;
        }

        Vector3 frontCenter = new Vector3(
            width * RandomRange(rng, -0.08f, 0.08f),
            height * RandomRange(rng, -0.08f, 0.08f),
            frontZ + relief * RandomRange(rng, 0.95f, 1.55f) + RandomRange(rng, 0f, planeJitter));

        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = front[i];
            back[i] = new Vector3(
                p.x * RandomRange(rng, 0.985f, 1.015f),
                p.y * RandomRange(rng, 0.985f, 1.015f),
                backZ);
        }

        List<Vector3> verts = new List<Vector3>(128);
        List<int> tris = new List<int>(192);
        List<Vector2> uvs = new List<Vector2>(128);
        List<Vector3> normals = new List<Vector3>(128);

        Vector3 frontNormal = Vector3.forward;
        Vector3 backNormal = Vector3.back;

        Vector2 centerUv = new Vector2(frontCenter.x / uvMetersPerUnit, frontCenter.y / uvMetersPerUnit);
        for (int i = 0; i < 8; i++)
        {
            int next = (i + 1) % 8;
            AddTriangleAuto(verts, tris, uvs, normals,
                frontCenter, front[i], front[next],
                centerUv,
                new Vector2(front[i].x / uvMetersPerUnit, front[i].y / uvMetersPerUnit),
                new Vector2(front[next].x / uvMetersPerUnit, front[next].y / uvMetersPerUnit),
                frontNormal);
        }

        if (generateHiddenBackFaces)
        {
            for (int i = 1; i < 7; i++)
            {
                AddTriangleAuto(verts, tris, uvs, normals,
                    back[0], back[i + 1], back[i],
                    new Vector2(back[0].x / uvMetersPerUnit, back[0].y / uvMetersPerUnit),
                    new Vector2(back[i + 1].x / uvMetersPerUnit, back[i + 1].y / uvMetersPerUnit),
                    new Vector2(back[i].x / uvMetersPerUnit, back[i].y / uvMetersPerUnit),
                    backNormal);
            }
        }

        for (int i = 0; i < 8; i++)
        {
            int next = (i + 1) % 8;

            Vector3 fa = front[i];
            Vector3 fb = front[next];
            Vector3 bb = back[next];
            Vector3 ba = back[i];

            Vector3 edge = fb - fa;
            Vector3 edgeMid = (fa + fb) * 0.5f;
            Vector3 outwardXY = new Vector3(edgeMid.x, edgeMid.y, 0f);
            if (outwardXY.sqrMagnitude < 0.000001f)
                outwardXY = Vector3.Cross(Vector3.back, edge).normalized;
            else
                outwardXY.Normalize();

            AddQuadAuto(verts, tris, uvs, normals,
                fa, fb, bb, ba,
                new Vector2(0f, 0f),
                new Vector2(edge.magnitude / uvMetersPerUnit, 0f),
                new Vector2(edge.magnitude / uvMetersPerUnit, Mathf.Abs(frontZ - backZ) / uvMetersPerUnit),
                new Vector2(0f, Mathf.Abs(frontZ - backZ) / uvMetersPerUnit),
                outwardXY);
        }

        Mesh mesh = new Mesh();
        mesh.name = generateHiddenBackFaces ? "GeneratedStone_Full_Relief" : "GeneratedStone_OpenBack_Relief";
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddTriangleAuto(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        List<Vector3> normals,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector3 desiredNormal)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, desiredNormal) < 0f)
        {
            Vector3 tempV = b;
            b = c;
            c = tempV;

            Vector2 tempUv = uvB;
            uvB = uvC;
            uvC = tempUv;
        }

        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);

        Vector3 finalNormal = desiredNormal.normalized;
        normals.Add(finalNormal);
        normals.Add(finalNormal);
        normals.Add(finalNormal);

        tris.Add(start + 0);
        tris.Add(start + 1);
        tris.Add(start + 2);
    }

    private static void AddQuadAuto(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        List<Vector3> normals,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD,
        Vector3 desiredNormal)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, desiredNormal) < 0f)
        {
            Vector3 temp = b;
            b = d;
            d = temp;

            Vector2 tempUv = uvB;
            uvB = uvD;
            uvD = tempUv;
        }

        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        Vector3 finalNormal = desiredNormal.normalized;
        normals.Add(finalNormal);
        normals.Add(finalNormal);
        normals.Add(finalNormal);
        normals.Add(finalNormal);

        tris.Add(start + 0);
        tris.Add(start + 1);
        tris.Add(start + 2);

        tris.Add(start + 0);
        tris.Add(start + 2);
        tris.Add(start + 3);
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