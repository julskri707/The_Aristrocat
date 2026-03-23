using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WallObject))]
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
    [SerializeField] private bool logDebug;

    private WallObject _wall;
    private WallCladdingRuntime _runtime;
    private int _lastGeometryHash;

    private readonly Dictionary<WallStoneModuleDefinition, int> _usageCounts = new Dictionary<WallStoneModuleDefinition, int>();
    private WallStoneModuleDefinition _lastUsed;
    private WallStoneModuleDefinition _secondLastUsed;

    private const float MinRowHeight = 0.10f;
    private const float MinStoneWidth = 0.14f;

    private struct StonePlacement
    {
        public WallStoneModuleDefinition module;
        public float centerX;
        public float width;
        public float height;
        public float depth;
    }

    private void Awake() => CacheRefs();

    private void OnEnable()
    {
        if (autoRegenerate)
            ForceRebuild();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoRegenerate)
            UnityEditor.EditorApplication.delayCall += DelayedEditorRebuild;
    }

    private void DelayedEditorRebuild()
    {
        if (this == null || !autoRegenerate)
            return;

        ForceRebuild();
    }
#endif

    private void LateUpdate()
    {
        if (!autoRegenerate)
            return;

        CacheRefs();
        if (_wall == null || _runtime == null)
            return;

        int hash = ComputeGeometryHash();
        if (_runtime.IsDirty || hash != _lastGeometryHash)
            ForceRebuild();
    }

    public void SetProfile(WallCladdingProfile profile, int seed = 0)
    {
        CacheRefs();
        if (_runtime == null)
            return;

        if (seed == 0)
            seed = ComputeStableSeed(profile);

        _runtime.SetProfile(profile, seed);
        ForceRebuild();
    }

    public void ForceRebuild()
    {
        CacheRefs();
        EnsureRuntimeProfile();

        if (_wall == null || _runtime == null)
            return;

        WallCladdingProfile profile = _runtime.CurrentProfile != null ? _runtime.CurrentProfile : defaultProfile;

        _runtime.ClearSpawnedImmediate();
        ResetUsageTracking();

        if (profile == null)
        {
            if (clearWhenProfileMissing)
                _runtime.MarkClean();
            return;
        }

        ApplyFallbackMaterial(profile);

        List<Vector3> path = GetWallPath();
        if (path == null || path.Count < 2)
        {
            _runtime.MarkClean();
            _lastGeometryHash = ComputeGeometryHash();
            return;
        }

        if (profile.UsesStoneMode)
        {
            Transform root = _runtime.GetOrCreateGeneratedRoot();
            System.Random rng = new System.Random(_runtime.CurrentSeed);

            if (generateOutside)
                GenerateStoneFace(profile, path, root, rng, +1f);

            if (generateInside)
                GenerateStoneFace(profile, path, root, rng, -1f);
        }

        _runtime.MarkClean();
        _lastGeometryHash = ComputeGeometryHash();
    }

    private void CacheRefs()
    {
        if (_wall == null)
            _wall = GetComponent<WallObject>();

        if (_runtime == null)
            _runtime = GetComponent<WallCladdingRuntime>();

        if (_runtime == null)
            _runtime = gameObject.AddComponent<WallCladdingRuntime>();
    }

    private void EnsureRuntimeProfile()
    {
        if (_runtime == null)
            return;

        if (_runtime.CurrentProfile == null && defaultProfile != null)
            _runtime.SetProfile(defaultProfile, ComputeStableSeed(defaultProfile));
    }

    private void ApplyFallbackMaterial(WallCladdingProfile profile)
    {
        if (!applyFallbackWallMaterial || profile == null || profile.fallbackWallMaterial == null || _wall == null)
            return;

        MeshRenderer mr = _wall.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != profile.fallbackWallMaterial)
            mr.sharedMaterial = profile.fallbackWallMaterial;

        _wall.wallMaterial = profile.fallbackWallMaterial;
    }

    private List<Vector3> GetWallPath()
    {
        List<Vector3> preview = _wall.GetPreviewPathWorld();
        if (preview != null && preview.Count >= 2)
            return preview;

        IReadOnlyList<Vector3> pts = _wall.Points;
        if (pts == null || pts.Count < 2)
            return null;

        return new List<Vector3>(pts);
    }

    private void GenerateStoneFace(WallCladdingProfile profile, List<Vector3> path, Transform root, System.Random rng, float sideSign)
    {
        List<WallStoneModuleDefinition> modules = GatherStoneModules(profile);
        if (modules.Count == 0)
            return;

        float wallHeight = Mathf.Max(0.1f, _wall.height);
        float halfThickness = Mathf.Max(0.01f, _wall.thickness) * 0.5f;
        float minY = Mathf.Max(0.005f, profile.general.sideInset);
        float maxY = Mathf.Max(minY + MinRowHeight, wallHeight - profile.general.sideInset);

        int rowIndex = 0;
        float y = minY;

        while (y < maxY - MinRowHeight * 0.5f)
        {
            float baseRow = Mathf.Max(MinRowHeight, profile.stone.targetRowHeight);
            float rowHeight = baseRow * RandomRange(rng, 1f - profile.stone.rowHeightJitter, 1f + profile.stone.rowHeightJitter);
            rowHeight = Mathf.Clamp(rowHeight, baseRow * 0.85f, baseRow * 1.15f);
            rowHeight = Mathf.Min(rowHeight, maxY - y);
            if (rowHeight < MinRowHeight)
                break;

            float rowBottom = y;
            float rowTop = y + rowHeight;
            float rowCenterY = (rowBottom + rowTop) * 0.5f;
            float rowOffset = ((rowIndex & 1) == 1) ? rowHeight * 0.45f : 0f;

            for (int segIndex = 0; segIndex < path.Count - 1; segIndex++)
            {
                Vector3 a = path[segIndex];
                Vector3 b = path[segIndex + 1];

                Vector3 tangent = b - a;
                tangent.y = 0f;
                float segLen = tangent.magnitude;
                if (segLen < MinStoneWidth)
                    continue;

                tangent /= segLen;
                Vector3 outward = Vector3.Cross(Vector3.up, tangent).normalized * sideSign;

                float segStart = profile.general.sideInset;
                float segEnd = Mathf.Max(segStart, segLen - profile.general.sideInset);
                if (segEnd - segStart < MinStoneWidth)
                    continue;

                float cursor = Mathf.Min(segEnd, segStart + rowOffset);
                while (cursor < segEnd - MinStoneWidth * 0.9f)
                {
                    float remaining = segEnd - cursor;
                    bool nearCorner = (cursor - segStart) < profile.stone.cornerSmallModuleZone || remaining < profile.stone.cornerSmallModuleZone;

                    float desiredWidth = ComputeDesiredWidth(profile, rowHeight, remaining, nearCorner, rng);
                    float desiredHeight = ComputeDesiredHeight(profile, rowHeight, rng);
                    WallStoneModuleDefinition module = PickModule(modules, desiredWidth, desiredHeight, remaining, nearCorner, rng);
                    if (module == null)
                        break;

                    float width = Mathf.Min(desiredWidth, remaining);
                    if (width < MinStoneWidth)
                        break;

                    float height = Mathf.Clamp(desiredHeight, rowHeight * 0.90f, rowHeight * 1.08f);
                    float depth = ComputeDepth(profile, module, rng);

                    float jitterX = Mathf.Min(profile.stone.positionJitter, width * 0.05f);
                    float jitterY = Mathf.Min(profile.stone.positionJitter, rowHeight * 0.05f);
                    float centerX = cursor + width * 0.5f + RandomRange(rng, -jitterX, jitterX);
                    centerX = Mathf.Clamp(centerX, segStart + width * 0.5f, segEnd - width * 0.5f);
                    float centerY = rowCenterY + RandomRange(rng, -jitterY, jitterY);
                    centerY = Mathf.Clamp(centerY, rowBottom + height * 0.5f, rowTop - height * 0.5f);

                    Vector3 pos = a + tangent * centerX + Vector3.up * centerY;
                    pos += outward * ComputeSurfaceOffset(profile, halfThickness, depth, rng);

                    Quaternion rot = BuildRotation(module, tangent, outward, profile, rng);
                    Vector3 scale = BuildScale(module, width, height, depth, profile, rng);

                    GameObject instance = Instantiate(module.prefab, pos, rot, root);
                    instance.name = module.prefab.name;
                    instance.transform.localScale = scale;

                    _runtime.RegisterSpawned(instance);
                    RegisterUsage(module);

                    cursor += width + profile.stone.horizontalSpacing;
                }
            }

            y += rowHeight + profile.stone.verticalSpacing;
            rowIndex++;
        }
    }

    private float ComputeDesiredWidth(WallCladdingProfile profile, float rowHeight, float remaining, bool nearCorner, System.Random rng)
    {
        float minWidth = Mathf.Max(MinStoneWidth, rowHeight * 1.10f);
        float maxWidth = Mathf.Min(remaining, rowHeight * 2.35f);

        if (nearCorner && profile.stone.preferSmallModulesNearCorners)
            maxWidth = Mathf.Min(maxWidth, rowHeight * 1.65f);

        float width = RandomRange(rng, minWidth, Mathf.Max(minWidth, maxWidth));
        width *= RandomRange(rng, 1f - profile.stone.widthJitter * 0.25f, 1f + profile.stone.widthJitter * 0.25f);
        return Mathf.Clamp(width, minWidth, Mathf.Max(minWidth, maxWidth));
    }

    private float ComputeDesiredHeight(WallCladdingProfile profile, float rowHeight, System.Random rng)
    {
        float h = rowHeight * RandomRange(rng, 0.92f, 1.03f);
        h *= RandomRange(rng, 1f - profile.stone.heightJitter * 0.18f, 1f + profile.stone.heightJitter * 0.18f);
        return Mathf.Clamp(h, rowHeight * 0.88f, rowHeight * 1.08f);
    }

    private float ComputeDepth(WallCladdingProfile profile, WallStoneModuleDefinition module, System.Random rng)
    {
        float d = Mathf.Clamp(module.nominalDepth, profile.stone.minStoneDepth, profile.stone.maxStoneDepth);
        d *= RandomRange(rng, 1f - profile.stone.depthJitter * 0.15f, 1f + profile.stone.depthJitter * 0.15f);
        return Mathf.Clamp(d, profile.stone.minStoneDepth, profile.stone.maxStoneDepth);
    }

    private float ComputeSurfaceOffset(WallCladdingProfile profile, float halfThickness, float placedDepth, System.Random rng)
    {
        float protrusion = Mathf.Min(profile.stone.surfaceProtrusion * RandomRange(rng, 0.95f, 1.05f), placedDepth * 0.20f);
        float embed = Mathf.Clamp(profile.stone.embedDepth * RandomRange(rng, 0.95f, 1.05f), 0.02f, placedDepth - Mathf.Max(0.005f, protrusion));
        return halfThickness + ((protrusion - embed) * 0.5f) + profile.general.depthOffset;
    }

    private Quaternion BuildRotation(WallStoneModuleDefinition module, Vector3 tangent, Vector3 outward, WallCladdingProfile profile, System.Random rng)
    {
        Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
        rot *= Quaternion.Euler(module.rotationOffsetEuler);

        float yaw = RandomRange(rng, -Mathf.Min(profile.stone.randomYaw, module.randomYaw), Mathf.Min(profile.stone.randomYaw, module.randomYaw));
        float pitch = RandomRange(rng, -Mathf.Min(profile.stone.randomPitch, module.randomPitch), Mathf.Min(profile.stone.randomPitch, module.randomPitch));
        float roll = RandomRange(rng, -Mathf.Min(profile.stone.randomRoll, module.randomRoll), Mathf.Min(profile.stone.randomRoll, module.randomRoll));

        rot = Quaternion.AngleAxis(yaw, Vector3.up) * rot;
        rot = Quaternion.AngleAxis(pitch, tangent) * rot;
        rot = Quaternion.AngleAxis(roll, outward) * rot;
        return rot;
    }

    private Vector3 BuildScale(WallStoneModuleDefinition module, float width, float height, float depth, WallCladdingProfile profile, System.Random rng)
    {
        float sx = width / Mathf.Max(0.01f, module.nominalWidth);
        float sy = height / Mathf.Max(0.01f, module.nominalHeight);
        float sz = depth / Mathf.Max(0.01f, module.nominalDepth);

        float jitter = Mathf.Min(profile.stone.scaleJitter, module.scaleJitter);
        if (jitter > 0f)
        {
            sx *= RandomRange(rng, 1f - jitter * 0.20f, 1f + jitter * 0.20f);
            sy *= RandomRange(rng, 1f - jitter * 0.16f, 1f + jitter * 0.16f);
            sz *= RandomRange(rng, 1f - jitter * 0.10f, 1f + jitter * 0.10f);
        }

        sx = Mathf.Clamp(sx, 0.80f, 1.35f);
        sy = Mathf.Clamp(sy, 0.82f, 1.18f);
        sz = Mathf.Clamp(sz, 0.90f, 1.08f);
        return new Vector3(sx, sy, sz);
    }

    private WallStoneModuleDefinition PickModule(List<WallStoneModuleDefinition> modules, float desiredWidth, float desiredHeight, float remaining, bool nearCorner, System.Random rng)
    {
        WallStoneModuleDefinition best = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < modules.Count; i++)
        {
            WallStoneModuleDefinition m = modules[i];
            if (m == null || m.prefab == null)
                continue;
            if (m.probability <= 0.001f || m.weight <= 0.001f)
                continue;
            if (nearCorner && !m.canUseNearCorners)
                continue;

            float widthScale = desiredWidth / Mathf.Max(0.01f, m.nominalWidth);
            float heightScale = desiredHeight / Mathf.Max(0.01f, m.nominalHeight);
            if (widthScale < 0.75f || widthScale > 1.40f)
                continue;
            if (heightScale < 0.80f || heightScale > 1.20f)
                continue;

            float fit = 1f - Mathf.Clamp01(Mathf.Abs(m.nominalWidth - desiredWidth) / Mathf.Max(0.1f, desiredWidth));
            float aspectDesired = desiredWidth / Mathf.Max(0.01f, desiredHeight);
            float aspectModule = m.nominalWidth / Mathf.Max(0.01f, m.nominalHeight);
            float aspectFit = 1f - Mathf.Clamp01(Mathf.Abs(aspectDesired - aspectModule) / Mathf.Max(0.5f, aspectDesired));

            int usage = GetUsageCount(m);
            float usagePenalty = 1f / (1f + usage * 0.4f);
            float repetitionPenalty = 1f;
            if (m == _lastUsed) repetitionPenalty *= 0.30f;
            else if (m == _secondLastUsed) repetitionPenalty *= 0.65f;

            float classBias = 1f;
            if (nearCorner && m.sizeClass == StoneModuleSizeClass.Small) classBias *= 1.08f;
            if (!nearCorner && m.sizeClass == StoneModuleSizeClass.Large) classBias *= 1.05f;
            if (m.preferAsGapFiller && remaining < desiredWidth * 1.15f) classBias *= 1.08f;

            float score = 0f;
            score += fit * 2.0f;
            score += aspectFit * 1.6f;
            score += usagePenalty * 0.8f;
            score *= repetitionPenalty;
            score *= classBias;
            score *= Mathf.Lerp(0.35f, 1f, m.probability);
            score *= Mathf.Max(0.1f, m.weight);
            score *= RandomRange(rng, 0.96f, 1.04f);

            if (score > bestScore)
            {
                bestScore = score;
                best = m;
            }
        }

        return best;
    }

    private List<WallStoneModuleDefinition> GatherStoneModules(WallCladdingProfile profile)
    {
        List<WallStoneModuleDefinition> list = new List<WallStoneModuleDefinition>(16);
        AddUnique(list, profile.stoneLargeModules);
        AddUnique(list, profile.stoneMediumModules);
        AddUnique(list, profile.stoneSmallModules);
        return list;
    }

    private static void AddUnique(List<WallStoneModuleDefinition> target, List<WallStoneModuleDefinition> source)
    {
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
        {
            WallStoneModuleDefinition m = source[i];
            if (m == null || m.prefab == null) continue;
            if (!target.Contains(m)) target.Add(m);
        }
    }

    private void ResetUsageTracking()
    {
        _usageCounts.Clear();
        _lastUsed = null;
        _secondLastUsed = null;
    }

    private void RegisterUsage(WallStoneModuleDefinition module)
    {
        if (module == null) return;
        _usageCounts.TryGetValue(module, out int count);
        _usageCounts[module] = count + 1;
        _secondLastUsed = _lastUsed;
        _lastUsed = module;
    }

    private int GetUsageCount(WallStoneModuleDefinition module)
    {
        if (module == null) return 0;
        return _usageCounts.TryGetValue(module, out int count) ? count : 0;
    }

    private int ComputeGeometryHash()
    {
        unchecked
        {
            int hash = 17;
            if (_wall != null)
            {
                hash = hash * 31 + _wall.height.GetHashCode();
                hash = hash * 31 + _wall.thickness.GetHashCode();
                var pts = _wall.Points;
                if (pts != null)
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector3 p = pts[i];
                        hash = hash * 31 + p.x.GetHashCode();
                        hash = hash * 31 + p.y.GetHashCode();
                        hash = hash * 31 + p.z.GetHashCode();
                    }
                }
            }
            if (_runtime != null)
                hash = hash * 31 + _runtime.CurrentSeed;
            return hash;
        }
    }

    private int ComputeStableSeed(WallCladdingProfile profile)
    {
        unchecked
        {
            int hash = 23;
            hash = hash * 31 + (profile != null && !string.IsNullOrEmpty(profile.profileId) ? profile.profileId.GetHashCode() : 0);
            hash = hash * 31 + Mathf.RoundToInt((_wall != null ? _wall.height : 0f) * 100f);
            hash = hash * 31 + Mathf.RoundToInt((_wall != null ? _wall.thickness : 0f) * 100f);
            hash = hash * 31 + Mathf.RoundToInt((profile != null ? profile.general.randomSeedOffset : 0f) * 1000f);
            return hash;
        }
    }

    private float RandomRange(System.Random rng, float min, float max)
    {
        if (Mathf.Approximately(min, max))
            return min;
        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }
}
