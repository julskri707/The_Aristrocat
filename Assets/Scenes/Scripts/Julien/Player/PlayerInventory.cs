using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public class InventorySlot
    {
        public InventoryItemData itemData;
        public int amount;

        public bool IsEmpty => itemData == null || amount <= 0;
    }

    [Header("Inventory")]
    [SerializeField, Min(1)] private int slotCount = 24;
    [SerializeField] private List<InventorySlot> slots = new List<InventorySlot>();

    public IReadOnlyList<InventorySlot> Slots => slots;

    public event Action OnInventoryChanged;

    private void Awake()
    {
        EnsureSlots();
    }

    public bool AddItem(InventoryItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0)
            return false;

        EnsureSlots();

        int remaining = amount;

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.itemData != itemData)
                continue;

            if (slot.amount >= itemData.maxStack)
                continue;

            int free = itemData.maxStack - slot.amount;
            int toAdd = Mathf.Min(free, remaining);
            slot.amount += toAdd;
            remaining -= toAdd;

            if (remaining <= 0)
            {
                NotifyChanged();
                return true;
            }
        }

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (!slot.IsEmpty)
                continue;

            int toAdd = Mathf.Min(itemData.maxStack, remaining);
            slot.itemData = itemData;
            slot.amount = toAdd;
            remaining -= toAdd;

            if (remaining <= 0)
            {
                NotifyChanged();
                return true;
            }
        }

        NotifyChanged();
        return remaining < amount;
    }

    public bool RemoveItem(InventoryItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0)
            return false;

        if (GetItemCount(itemData) < amount)
            return false;

        int remaining = amount;

        for (int i = slots.Count - 1; i >= 0; i--)
        {
            InventorySlot slot = slots[i];
            if (slot.itemData != itemData || slot.amount <= 0)
                continue;

            int toRemove = Mathf.Min(slot.amount, remaining);
            slot.amount -= toRemove;
            remaining -= toRemove;

            if (slot.amount <= 0)
            {
                slot.itemData = null;
                slot.amount = 0;
            }

            if (remaining <= 0)
            {
                NotifyChanged();
                return true;
            }
        }

        NotifyChanged();
        return true;
    }

    public int GetItemCount(InventoryItemData itemData)
    {
        if (itemData == null)
            return 0;

        EnsureSlots();

        int total = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].itemData == itemData)
                total += slots[i].amount;
        }

        return total;
    }

    public void ForceRefresh()
    {
        EnsureSlots();
        NotifyChanged();
    }

    private void EnsureSlots()
    {
        if (slots == null)
            slots = new List<InventorySlot>();

        while (slots.Count < slotCount)
            slots.Add(new InventorySlot());

        while (slots.Count > slotCount)
            slots.RemoveAt(slots.Count - 1);

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null)
                slots[i] = new InventorySlot();

            if (slots[i].amount <= 0)
            {
                slots[i].amount = 0;
                if (slots[i].itemData == null)
                    continue;
            }
        }
    }

    private void NotifyChanged()
    {
        OnInventoryChanged?.Invoke();
    }
}
