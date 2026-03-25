using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LootTable_", menuName = "THE ARISTROCAT/Loot/Drop Table")]
public class LootDropTableSO : ScriptableObject
{
    [Serializable]
    public class LootDropEntry
    {
        public LootItemDefinitionSO itemDefinition;
        [Range(0f, 1f)] public float dropChance = 1f;
        public int minQuantity = 1;
        public int maxQuantity = 1;
    }

    [Header("Entries")]
    [SerializeField] private List<LootDropEntry> entries = new List<LootDropEntry>();

    public IReadOnlyList<LootDropEntry> Entries => entries;

    public void RollDrops(List<LootDropResult> output)
    {
        if (output == null)
            return;

        output.Clear();

        for (int i = 0; i < entries.Count; i++)
        {
            LootDropEntry entry = entries[i];
            if (entry == null)
                continue;

            if (entry.itemDefinition == null)
                continue;

            float chance = Mathf.Clamp01(entry.dropChance);
            if (UnityEngine.Random.value > chance)
                continue;

            int min = Mathf.Max(0, entry.minQuantity);
            int max = Mathf.Max(min, entry.maxQuantity);

            int quantity = UnityEngine.Random.Range(min, max + 1);
            if (quantity <= 0)
                continue;

            output.Add(new LootDropResult(entry.itemDefinition, quantity));
        }
    }
}
