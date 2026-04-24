// TerrainDetailSessionBackup.cs
// Snapshots TerrainData detail layers (grass/stones) before FieldArea clears them, and restores
// when play mode ends (Editor) or the application quits (Player), so the terrain asset/scene
// returns to its original painted details without manual repainting.

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class TerrainDetailSessionBackup
{
    private static readonly Dictionary<int, List<int[,]>> SnapshotsByTerrainDataId = new Dictionary<int, List<int[,]>>();
    private static bool QuitHookRegistered;

    /// <summary>
    /// Call before any code modifies detail density on this terrain's TerrainData.
    /// Copies all detail layers once per TerrainData instance for later restore.
    /// </summary>
    public static void EnsureBaselineBeforeModify(Terrain terrain)
    {
        if (terrain == null)
            return;

        TerrainData td = terrain.terrainData;
        if (td == null || td.detailPrototypes == null || td.detailPrototypes.Length == 0)
            return;

        int id = td.GetInstanceID();
        if (SnapshotsByTerrainDataId.ContainsKey(id))
            return;

        int dw = td.detailWidth;
        int dh = td.detailHeight;
        if (dw <= 0 || dh <= 0)
            return;

        int layers = td.detailPrototypes.Length;
        var copies = new List<int[,]>(layers);

        for (int layer = 0; layer < layers; layer++)
        {
            int[,] src = td.GetDetailLayer(0, 0, dw, dh, layer);
            if (src == null)
            {
                copies.Add(null);
                continue;
            }

            copies.Add((int[,])src.Clone());
        }

        SnapshotsByTerrainDataId[id] = copies;
        RegisterQuitHookOnce();
    }

    /// <summary>
    /// Writes stored detail layers back to TerrainData and flushes affected terrains.
    /// Safe to call multiple times (no-op when nothing was captured).
    /// </summary>
    public static void RestoreAllBaselines()
    {
        if (SnapshotsByTerrainDataId.Count == 0)
            return;

        foreach (var kv in SnapshotsByTerrainDataId)
        {
            TerrainData td = EditorOrRuntimeResolveTerrainData(kv.Key);
            if (td == null)
                continue;

            List<int[,]> layers = kv.Value;
            if (layers == null || layers.Count == 0)
                continue;

            int dw = td.detailWidth;
            int dh = td.detailHeight;
            if (dw <= 0 || dh <= 0)
                continue;

            int n = Mathf.Min(layers.Count, td.detailPrototypes != null ? td.detailPrototypes.Length : 0);
            for (int layer = 0; layer < n; layer++)
            {
                int[,] data = layers[layer];
                if (data == null)
                    continue;

                td.SetDetailLayer(0, 0, layer, data);
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(td);
#endif
        }

        Terrain[] active = Terrain.activeTerrains;
        if (active != null)
        {
            for (int i = 0; i < active.Length; i++)
            {
                Terrain t = active[i];
                if (t == null || t.terrainData == null)
                    continue;
                if (SnapshotsByTerrainDataId.ContainsKey(t.terrainData.GetInstanceID()))
                    t.Flush();
            }
        }

        SnapshotsByTerrainDataId.Clear();
    }

    private static TerrainData EditorOrRuntimeResolveTerrainData(int terrainDataInstanceId)
    {
#if UNITY_EDITOR
        return EditorUtility.InstanceIDToObject(terrainDataInstanceId) as TerrainData;
#else
        Terrain[] active = Terrain.activeTerrains;
        if (active == null)
            return null;

        for (int i = 0; i < active.Length; i++)
        {
            Terrain t = active[i];
            if (t != null && t.terrainData != null && t.terrainData.GetInstanceID() == terrainDataInstanceId)
                return t.terrainData;
        }

        return null;
#endif
    }

    private static void RegisterQuitHookOnce()
    {
        if (QuitHookRegistered)
            return;

        QuitHookRegistered = true;
        Application.quitting += RestoreAllBaselines;
    }

#if UNITY_EDITOR
    [InitializeOnLoad]
    private static class PlayModeHook
    {
        static PlayModeHook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                    RestoreAllBaselines();
            };
        }
    }
#endif
}
