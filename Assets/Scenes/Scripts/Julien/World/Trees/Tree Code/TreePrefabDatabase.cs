using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TreePrefabDatabase", menuName = "The Aristrocat/Trees/Tree Prefab Database")]
public class TreePrefabDatabaseSO : ScriptableObject
{
    [Serializable]
    public class TreePrefabEntry
    {
        public string prefabId;
        public GameObject prefab;
    }

    [Header("Registered Tree Prefabs")]
    [SerializeField] private List<TreePrefabEntry> entries = new List<TreePrefabEntry>();

    private Dictionary<string, GameObject> lookup;

    public GameObject GetPrefabById(string prefabId)
    {
        BuildLookupIfNeeded();

        if (string.IsNullOrWhiteSpace(prefabId))
            return null;

        lookup.TryGetValue(prefabId, out GameObject prefab);
        return prefab;
    }

    private void BuildLookupIfNeeded()
    {
        if (lookup != null)
            return;

        lookup = new Dictionary<string, GameObject>();

        for (int i = 0; i < entries.Count; i++)
        {
            TreePrefabEntry entry = entries[i];

            if (entry == null)
                continue;

            if (string.IsNullOrWhiteSpace(entry.prefabId))
                continue;

            if (entry.prefab == null)
                continue;

            if (!lookup.ContainsKey(entry.prefabId))
            {
                lookup.Add(entry.prefabId, entry.prefab);
            }
        }
    }

    private void OnValidate()
    {
        lookup = null;
    }
}