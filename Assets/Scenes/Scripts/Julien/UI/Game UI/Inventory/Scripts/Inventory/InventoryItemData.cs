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

    [Header("World Drop")]
    public GameObject worldDropPrefab;
    public Vector3 worldObjectScale = new Vector3(115f, 115f, 115f);
    public Vector3 handObjectScale = new Vector3(200f, 200f, 200f);

    [Header("Tool")]
    [Min(0)] public int chopPower = 0;
}
