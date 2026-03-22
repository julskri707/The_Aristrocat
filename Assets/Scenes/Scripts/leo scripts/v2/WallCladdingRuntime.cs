using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WallCladdingRuntime : MonoBehaviour
{
    [SerializeField] private WallCladdingProfile currentProfile;
    [SerializeField] private int currentSeed;
    [SerializeField] private Transform generatedRoot;
    [SerializeField] private bool dirty = true;

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();

    public WallCladdingProfile CurrentProfile => currentProfile;
    public int CurrentSeed => currentSeed;
    public Transform GeneratedRoot => generatedRoot;
    public bool IsDirty => dirty;
    public IReadOnlyList<GameObject> SpawnedObjects => spawnedObjects;

    public void SetProfile(WallCladdingProfile profile, int seed)
    {
        currentProfile = profile;
        currentSeed = seed;
        dirty = true;
    }

    public void MarkDirty()
    {
        dirty = true;
    }

    public void MarkClean()
    {
        dirty = false;
    }

    public Transform GetOrCreateGeneratedRoot()
    {
        if (generatedRoot != null)
            return generatedRoot;

        Transform child = transform.Find("GeneratedWallCladding");
        if (child != null)
        {
            generatedRoot = child;
            return generatedRoot;
        }

        GameObject go = new GameObject("GeneratedWallCladding");
        go.transform.SetParent(transform, false);
        generatedRoot = go.transform;
        return generatedRoot;
    }

    public void RegisterSpawned(GameObject go)
    {
        if (go == null) return;
        spawnedObjects.Add(go);
    }

    public void ClearSpawnedImmediate()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            GameObject go = spawnedObjects[i];
            if (go == null) continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(go);
            else
                Object.Destroy(go);
#else
            Object.Destroy(go);
#endif
        }

        spawnedObjects.Clear();
    }
}
