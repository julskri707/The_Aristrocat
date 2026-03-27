using System.Collections.Generic;
using UnityEngine;

public class PlayerInventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Transform slotRoot;
    [SerializeField] private InventorySlotUI slotPrefab;
    [SerializeField] private InventoryTooltipUI tooltipUI;

    private readonly List<InventorySlotUI> spawnedSlots = new List<InventorySlotUI>();

    private void OnEnable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged += RebuildOrRefresh;

        RebuildOrRefresh();
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= RebuildOrRefresh;
    }

    public void RebuildOrRefresh()
    {
        if (inventory == null || slotRoot == null || slotPrefab == null)
            return;

        IReadOnlyList<PlayerInventory.InventorySlot> slots = inventory.Slots;
        if (slots == null)
            return;

        if (spawnedSlots.Count != slots.Count)
            Rebuild(slots);
        else
            Refresh(slots);
    }

    private void Rebuild(IReadOnlyList<PlayerInventory.InventorySlot> slots)
    {
        for (int i = 0; i < spawnedSlots.Count; i++)
        {
            if (spawnedSlots[i] != null)
                Destroy(spawnedSlots[i].gameObject);
        }

        spawnedSlots.Clear();

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlotUI ui = Instantiate(slotPrefab, slotRoot);
            ui.Bind(slots[i], tooltipUI);
            spawnedSlots.Add(ui);
        }
    }

    private void Refresh(IReadOnlyList<PlayerInventory.InventorySlot> slots)
    {
        for (int i = 0; i < spawnedSlots.Count; i++)
        {
            spawnedSlots[i].Bind(slots[i], tooltipUI);
        }
    }
}
