using UnityEngine;

[CreateAssetMenu(fileName = "InventoryItemData", menuName = "Julien/Inventory/Item Data")]
public class InventoryItemData : ScriptableObject
{
    [Header("Identity")]
    public string itemId = "item_default";
    public string itemDisplayName = "Neues Item";
    public Texture iconTexture;

    [Header("Stacking")]
    [Min(1)] public int maxStack = 999;

    [Header("Equip")]
    public bool canEquip = false;
    public EquipmentSlotType allowedEquipmentSlot = EquipmentSlotType.ExtraSlot1;
    public GameObject handPrefab;

    [Header("Tool")]
    [Min(0)] public int chopPower = 0;
}
