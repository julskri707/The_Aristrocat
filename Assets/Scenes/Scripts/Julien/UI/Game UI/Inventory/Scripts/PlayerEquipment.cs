using UnityEngine;

[DisallowMultipleComponent]
public class PlayerEquipment : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Transform handHolder;

    [Header("Slots")]
    [SerializeField] private InventoryItemData[] equippedItems = new InventoryItemData[4];
    [SerializeField, Range(0, 3)] private int activeSlotIndex = 0;

    [Header("Input")]
    [SerializeField] private bool allowNumberKeySelection = true;

    private GameObject spawnedHeldObject;

    public int ActiveSlotIndex => activeSlotIndex;

    private void Awake()
    {
        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();

        if (inventory == null)
            inventory = GetComponentInParent<PlayerInventory>();

        RefreshHeldVisual();
    }

    private void Update()
    {
        if (!allowNumberKeySelection)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlot(3);
    }

    public InventoryItemData GetEquippedItem(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= equippedItems.Length)
            return null;

        return equippedItems[slotIndex];
    }

    public bool EquipFromInventoryToSlot(int slotIndex, InventoryItemData itemData)
    {
        if (itemData == null || !itemData.canEquip)
            return false;

        if ((int)itemData.allowedEquipmentSlot != slotIndex)
            return false;

        if (inventory == null)
            return false;

        InventoryItemData currentlyEquipped = GetEquippedItem(slotIndex);
        if (currentlyEquipped == itemData)
            return true;

        if (!inventory.RemoveItem(itemData, 1))
            return false;

        if (currentlyEquipped != null)
        {
            bool returned = inventory.AddItem(currentlyEquipped, 1);
            if (!returned)
            {
                inventory.AddItem(itemData, 1);
                return false;
            }
        }

        equippedItems[slotIndex] = itemData;

        if (activeSlotIndex == slotIndex)
            RefreshHeldVisual();

        return true;
    }

    public bool SwapEquipmentSlots(int fromSlot, int toSlot)
    {
        if (fromSlot < 0 || fromSlot >= equippedItems.Length || toSlot < 0 || toSlot >= equippedItems.Length)
            return false;

        InventoryItemData dragged = equippedItems[fromSlot];
        InventoryItemData target = equippedItems[toSlot];

        if (dragged == null)
            return false;

        if ((int)dragged.allowedEquipmentSlot != toSlot)
            return false;

        if (target != null && (int)target.allowedEquipmentSlot != fromSlot)
            return false;

        equippedItems[fromSlot] = target;
        equippedItems[toSlot] = dragged;

        if (activeSlotIndex == fromSlot || activeSlotIndex == toSlot)
            RefreshHeldVisual();

        return true;
    }

    public bool UnequipToInventory(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= equippedItems.Length)
            return false;

        InventoryItemData item = equippedItems[slotIndex];
        if (item == null)
            return false;

        if (inventory == null)
            return false;

        bool added = inventory.AddItem(item, 1);
        if (!added)
            return false;

        equippedItems[slotIndex] = null;

        if (activeSlotIndex == slotIndex)
            RefreshHeldVisual();

        return true;
    }

    public void SelectSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= equippedItems.Length)
            return;

        activeSlotIndex = slotIndex;
        RefreshHeldVisual();
    }

    public InventoryItemData GetActiveItem()
    {
        return GetEquippedItem(activeSlotIndex);
    }

    public int GetActiveChopPower()
    {
        InventoryItemData item = GetActiveItem();
        if (item == null)
            return 0;

        return item.chopPower;
    }

    public void RefreshHeldVisual()
    {
        if (spawnedHeldObject != null)
            Destroy(spawnedHeldObject);

        if (handHolder == null)
            return;

        InventoryItemData activeItem = GetActiveItem();
        if (activeItem == null || activeItem.handPrefab == null)
            return;

        spawnedHeldObject = Instantiate(activeItem.handPrefab, handHolder);
        spawnedHeldObject.transform.localPosition = Vector3.zero;
        spawnedHeldObject.transform.localRotation = Quaternion.identity;
        spawnedHeldObject.transform.localScale = Vector3.one;
    }
}
