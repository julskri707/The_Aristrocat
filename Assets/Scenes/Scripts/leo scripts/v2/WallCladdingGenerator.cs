using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WallObject))]
public sealed class WallCladdingGenerator : MonoBehaviour
{
    [Header("Profile")]
    public WallCladdingProfile defaultProfile;

    [Header("Generation")]
    public bool autoRegenerate = true;
    public bool generateOutside = true;
    public bool generateInside = true;
    public bool clearWhenProfileMissing = true;
    public bool applyFallbackWallMaterial = true;

    [Header("Performance")]
    [Min(0.02f)] public float rebuildCheckInterval = 0.12f;
    public bool logDebug = false;

    private WallObject _wall;
    private WallCladdingRuntime _runtime;
    private float _nextCheckTime;
    private int _lastGeometryHash = int.MinValue;
    private readonly Dictionary<GameObject, Bounds> _prefabBoundsCache = new Dictionary<GameObject, Bounds>();

    private void Awake()
    {
        CacheRefs();
        EnsureRuntimeProfile();
        ForceRebuild();
    }

    private void OnEnable()
    {
        CacheRefs();
        EnsureRuntimeProfile();
        ForceRebuild();
    }

    private void LateUpdate()
    {
        if (!autoRegenerate)
            return;

        if (Time.unscaledTime < _nextCheckTime)
            return;

        _nextCheckTime = Time.unscaledTime + rebuildCheckInterval;

        CacheRefs();
        EnsureRuntimeProfile();

        int geometryHash = ComputeGeometryHash();
        if (_runtime != null && _runtime.IsDirty)
        {
            ForceRebuild();
            return;
        }

        if (geometryHash != _lastGeometryHash)
            ForceRebuild();
    }

    public void ForceRebuild()
    {
        CacheRefs();
        EnsureRuntimeProfile();

        if (_wall == null || _runtime == null)
            return;

        WallCladdingProfile profile = _runtime.CurrentProfile;
        if (profile == null)
            profile = defaultProfile;

        _runtime.ClearSpawnedImmediate();

        if (profile == null)
        {
            if (clearWhenProfileMissing && logDebug)
                Debug.Log("[WallCladdingGenerator] No profile assigned.", this);

            _runtime.MarkClean();
            _lastGeometryHash = ComputeGeometryHash();
            return;
        }

        if (applyFallbackWallMaterial && profile.fallbackWallMaterial != null)
        {
            _wall.wallMaterial = profile.fallbackWallMaterial;
            MeshRenderer mr = _wall.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = profile.fallbackWallMaterial;
        }

        if (profile.mode == WallCladdingMode.StoneRandom)
            GenerateStone(profile);
        else
            _runtime.ClearSpawnedImmediate();

        _runtime.MarkClean();
        _lastGeometryHash = ComputeGeometryHash();
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

        if (_runtime.CurrentProfile != null)
            return;

        if (defaultProfile == null)
            return;

        _runtime.SetProfile(defaultProfile, ComputeStableSeed(defaultProfile));
    }

    private int ComputeStableSeed(WallCladdingProfile profile)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + gameObject.name.GetHashCode();
            hash = hash * 31 + GetInstanceID();
            if (profile != null)
            {
                hash = hash * 31 + profile.profileId.GetHashCode();
                hash = hash * 31 + Mathf.RoundToInt(profile.general.randomSeedOffset * 1000f);
            }
            return hash;
        }
    }

    private int ComputeGeometryHash()
    {
        if (_wall == null)
            return 0;

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Mathf.RoundToInt(_wall.height * 1000f);
            hash = hash * 31 + Mathf.RoundToInt(_wall.thickness * 1000f);
            hash = hash * 31 + (_wall.closedLoop ? 1 : 0);

            IReadOnlyList<Vector3> pts = _wall.Points;
            if (pts != null)
            {
                hash = hash * 31 + pts.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 p = pts[i];
                    hash = hash * 31 + Mathf.RoundToInt(p.x * 100f);
                    hash = hash * 31 + Mathf.RoundToInt(p.y * 100f);
                    hash = hash * 31 + Mathf.RoundToInt(p.z * 100f);
                }
            }

            if (_runtime != null && _runtime.CurrentProfile != null)
            {
                hash = hash * 31 + _runtime.CurrentProfile.profileId.GetHashCode();
                hash = hash * 31 + _runtime.CurrentSeed;
            }

            return hash;
        }
    }

    private void GenerateStone(WallCladdingProfile profile)
    {
        IReadOnlyList<Vector3> src = _wall.Points;
        if (src == null || src.Count < 2)
            return;

        List<Vector3> pts = BuildOpenPoints(src, _wall.closedLoop);
        if (pts.Count < 2)
            return;

        Transform root = _runtime.GetOrCreateGeneratedRoot();
        System.Random rng = new System.Random(_runtime.CurrentSeed);

        if (generateOutside)
            GenerateStoneSide(profile, pts, root, rng, outside: true);

        if (generateInside)
            GenerateStoneSide(profile, pts, root, rng, outside: false);
    }

    private void GenerateStoneSide(WallCladdingProfile profile, List<Vector3> pts, Transform root, System.Random rng, bool outside)
    {
        float outsideSign = ComputeOutsideSign(pts, _wall.closedLoop);
        float sideSign = outside ? outsideSign : -outsideSign;
        float wallHeight = Mathf.Max(0.1f, _wall.height);
        float halfThickness = Mathf.Max(0.01f, _wall.thickness) * 0.5f;
        float rowY = profile.general.sideInset;

        while (rowY < wallHeight - profile.general.sideInset)
        {
            float baseRowHeight = Mathf.Max(0.05f, profile.stone.targetRowHeight);
            float rowJitter = Mathf.Lerp(0.82f, 1.18f, (float)rng.NextDouble());
            float rowHeight = Mathf.Min(baseRowHeight * rowJitter, wallHeight - rowY - profile.general.sideInset);
            if (rowHeight < 0.05f)
                break;

            for (int segIndex = 0; segIndex < pts.Count - 1; segIndex++)
            {
                Vector3 a = pts[segIndex];
                Vector3 b = pts[segIndex + 1];
                Vector3 tangent = b - a;
                tangent.y = 0f;

                float segLen = tangent.magnitude;
                if (segLen < 0.08f)
                    continue;

                tangent /= segLen;
                Vector3 outward = Vector3.Cross(Vector3.up, tangent).normalized * sideSign;
                float cornerZone = Mathf.Max(0.05f, profile.stone.cornerSmallModuleZone);

                float cursor = (float)rng.NextDouble() * Mathf.Min(0.12f, segLen * 0.1f);
                while (cursor < segLen - 0.04f)
                {
                    float remaining = segLen - cursor;
                    bool nearCorner = cursor < cornerZone || remaining < cornerZone;

                    WallStoneModuleDefinition module = ChooseStoneModule(profile, rng, nearCorner, remaining);
                    if (module == null || module.prefab == null)
                        break;

                    Bounds prefabBounds = GetPrefabBounds(module.prefab);
                    if (prefabBounds.size.x < 0.0001f || prefabBounds.size.y < 0.0001f || prefabBounds.size.z < 0.0001f)
                    {
                        cursor += Mathf.Max(0.1f, module.nominalWidth + profile.stone.horizontalSpacing);
                        continue;
                    }

                    float scaleRand = 1f + RandomRange(rng, -module.scaleJitter, module.scaleJitter);
                    float targetWidth = Mathf.Max(0.04f, module.nominalWidth * scaleRand);
                    float targetHeight = Mathf.Max(0.04f, rowHeight * Mathf.Lerp(0.85f, 1.12f, (float)rng.NextDouble()));
                    float targetDepth = Mathf.Max(0.02f, module.nominalDepth * Mathf.Lerp(0.92f, 1.15f, (float)rng.NextDouble()));

                    if (targetWidth > remaining && remaining < profile.stone.targetRowHeight * 0.35f)
                        break;

                    if (targetWidth > remaining)
                        targetWidth = Mathf.Max(0.05f, remaining - 0.01f);

                    float centerX = cursor + targetWidth * 0.5f;
                    float localJitter = profile.stone.positionJitter * Mathf.Min(targetWidth, rowHeight);
                    centerX += RandomRange(rng, -localJitter, localJitter);
                    centerX = Mathf.Clamp(centerX, targetWidth * 0.5f, segLen - targetWidth * 0.5f);

                    float centerY = rowY + targetHeight * 0.5f + RandomRange(rng, -localJitter, localJitter * 0.6f);
                    centerY = Mathf.Clamp(centerY, targetHeight * 0.5f, wallHeight - targetHeight * 0.5f);

                    Vector3 pos = a + tangent * centerX;
                    pos += Vector3.up * centerY;
                    pos += outward * (halfThickness + profile.general.depthOffset + targetDepth * 0.5f + module.extraEdgeInset);

                    Quaternion rot = Quaternion.LookRotation(outward, Vector3.up);
                    rot *= Quaternion.Euler(
                        RandomRange(rng, -module.randomPitch, module.randomPitch),
                        RandomRange(rng, -module.randomYaw, module.randomYaw),
                        RandomRange(rng, -module.randomRoll, module.randomRoll));

                    Vector3 scale = new Vector3(
                        targetWidth / Mathf.Max(0.001f, prefabBounds.size.x),
                        targetHeight / Mathf.Max(0.001f, prefabBounds.size.y),
                        targetDepth / Mathf.Max(0.001f, prefabBounds.size.z));

                    float profileScaleJitter = 1f + RandomRange(rng, -profile.stone.scaleJitter, profile.stone.scaleJitter);
                    scale *= profileScaleJitter;

                    GameObject instance = Instantiate(module.prefab, root);
                    instance.name = module.prefab.name;
                    instance.transform.SetPositionAndRotation(pos, rot);
                    instance.transform.localScale = scale;
                    _runtime.RegisterSpawned(instance);

                    cursor += targetWidth + profile.stone.horizontalSpacing;
                }
            }

            rowY += rowHeight + profile.stone.verticalSpacing;
        }
    }

    private WallStoneModuleDefinition ChooseStoneModule(WallCladdingProfile profile, System.Random rng, bool nearCorner, float remaining)
    {
        List<WallStoneModuleDefinition> preferred = null;
        float smallMin = GetMinimumWidth(profile.stoneSmallModules);
        float mediumMin = GetMinimumWidth(profile.stoneMediumModules);

        if (nearCorner && profile.stone.preferSmallModulesNearCorners && profile.stoneSmallModules.Count > 0)
        {
            preferred = profile.stoneSmallModules;
        }
        else if (remaining <= Mathf.Max(smallMin * 1.45f, profile.stone.targetRowHeight * 0.85f) && profile.stoneSmallModules.Count > 0)
        {
            preferred = profile.stoneSmallModules;
        }
        else if ((float)rng.NextDouble() < profile.stone.smallStoneFillChance && profile.stoneSmallModules.Count > 0)
        {
            preferred = profile.stoneSmallModules;
        }
        else if (remaining <= Mathf.Max(mediumMin * 1.2f, profile.stone.targetRowHeight * 1.35f) && profile.stoneMediumModules.Count > 0)
        {
            preferred = profile.stoneMediumModules;
        }
        else
        {
            double roll = rng.NextDouble();
            if (roll < 0.42 && profile.stoneLargeModules.Count > 0)
                preferred = profile.stoneLargeModules;
            else if (roll < 0.82 && profile.stoneMediumModules.Count > 0)
                preferred = profile.stoneMediumModules;
            else if (profile.stoneSmallModules.Count > 0)
                preferred = profile.stoneSmallModules;
            else if (profile.stoneMediumModules.Count > 0)
                preferred = profile.stoneMediumModules;
            else
                preferred = profile.stoneLargeModules;
        }

        WallStoneModuleDefinition picked = PickWeightedStone(preferred, rng, nearCorner);
        if (picked != null)
            return picked;

        picked = PickWeightedStone(profile.stoneSmallModules, rng, nearCorner);
        if (picked != null)
            return picked;

        picked = PickWeightedStone(profile.stoneMediumModules, rng, nearCorner);
        if (picked != null)
            return picked;

        return PickWeightedStone(profile.stoneLargeModules, rng, nearCorner);
    }

    private WallStoneModuleDefinition PickWeightedStone(List<WallStoneModuleDefinition> modules, System.Random rng, bool nearCorner)
    {
        if (modules == null || modules.Count == 0)
            return null;

        float total = 0f;
        for (int i = 0; i < modules.Count; i++)
        {
            WallStoneModuleDefinition module = modules[i];
            if (module == null || module.prefab == null)
                continue;
            if (nearCorner && !module.canUseNearCorners)
                continue;
            if ((float)rng.NextDouble() > module.probability)
                continue;
            total += Mathf.Max(0.0001f, module.weight);
        }

        if (total <= 0.0001f)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                WallStoneModuleDefinition module = modules[i];
                if (module != null && module.prefab != null && (!nearCorner || module.canUseNearCorners))
                    return module;
            }
            return null;
        }

        float value = (float)rng.NextDouble() * total;
        float cursor = 0f;

        for (int i = 0; i < modules.Count; i++)
        {
            WallStoneModuleDefinition module = modules[i];
            if (module == null || module.prefab == null)
                continue;
            if (nearCorner && !module.canUseNearCorners)
                continue;
            if ((float)rng.NextDouble() > module.probability)
                continue;

            cursor += Mathf.Max(0.0001f, module.weight);
            if (value <= cursor)
                return module;
        }

        return modules[0];
    }

    private float GetMinimumWidth(List<WallStoneModuleDefinition> modules)
    {
        if (modules == null || modules.Count == 0)
            return 0.1f;

        float min = float.MaxValue;
        for (int i = 0; i < modules.Count; i++)
        {
            WallStoneModuleDefinition module = modules[i];
            if (module == null) continue;
            min = Mathf.Min(min, Mathf.Max(0.05f, module.nominalWidth));
        }

        return min == float.MaxValue ? 0.1f : min;
    }

    private static List<Vector3> BuildOpenPoints(IReadOnlyList<Vector3> src, bool closedLoop)
    {
        List<Vector3> pts = new List<Vector3>(src.Count);
        for (int i = 0; i < src.Count; i++)
            pts.Add(src[i]);

        if (closedLoop && pts.Count > 2 && Vector3.Distance(pts[0], pts[pts.Count - 1]) < 0.001f)
            pts.RemoveAt(pts.Count - 1);

        return pts;
    }

    private static float ComputeOutsideSign(List<Vector3> pts, bool closedLoop)
    {
        if (!closedLoop || pts == null || pts.Count < 3)
            return 1f;

        float area = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % pts.Count];
            area += a.x * b.z - b.x * a.z;
        }

        return area > 0f ? 1f : -1f;
    }

    private Bounds GetPrefabBounds(GameObject prefab)
    {
        if (prefab == null)
            return new Bounds(Vector3.zero, Vector3.one);

        if (_prefabBoundsCache.TryGetValue(prefab, out Bounds cached))
            return cached;

        bool found = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter mf = meshFilters[i];
            if (mf.sharedMesh == null)
                continue;

            EncapsulateMeshBounds(prefab.transform, mf.transform, mf.sharedMesh.bounds, ref found, ref bounds);
        }

        SkinnedMeshRenderer[] skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer smr = skinned[i];
            if (smr.sharedMesh == null)
                continue;

            EncapsulateMeshBounds(prefab.transform, smr.transform, smr.sharedMesh.bounds, ref found, ref bounds);
        }

        if (!found)
            bounds = new Bounds(Vector3.zero, Vector3.one);

        _prefabBoundsCache[prefab] = bounds;
        return bounds;
    }

    private static void EncapsulateMeshBounds(Transform root, Transform child, Bounds localMeshBounds, ref bool found, ref Bounds combined)
    {
        Matrix4x4 matrix = root.worldToLocalMatrix * child.localToWorldMatrix;
        Vector3 c = localMeshBounds.center;
        Vector3 e = localMeshBounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3(x, y, z));
                    Vector3 p = matrix.MultiplyPoint3x4(corner);
                    if (!found)
                    {
                        combined = new Bounds(p, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        combined.Encapsulate(p);
                    }
                }
            }
        }
    }

    private static float RandomRange(System.Random rng, float min, float max)
    {
        if (Mathf.Approximately(min, max))
            return min;
        return min + (float)rng.NextDouble() * (max - min);
    }
}
