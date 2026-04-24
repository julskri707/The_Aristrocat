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
    [Tooltip("Bits 0–3 = Ausrüstungs-Slots 1–4 (Index 0–3). 0 = nur Legacy-Feld „allowedEquipmentSlot“ verwenden.")]
    public int allowedSlotMask = 0;
    public GameObject handPrefab;

    [Header("Combat")]
    [Tooltip("Wenn an: PlayerSwordCombat reagiert nur, wenn dieses Item im aktiven Slot liegt.")]
    public bool enableSwordCombat = false;

    [Header("Hand pose (when equipped)")]
    [Tooltip("Wenn an: Position/Rotation am handHolder statt (0,0,0) und Identität.")]
    public bool overrideHandPose = false;
    public Vector3 handLocalPosition = Vector3.zero;
    public Vector3 handLocalEulerAngles = Vector3.zero;

    [Header("World Drop")]
    public GameObject worldDropPrefab;
    [Tooltip("Nicht mehr von PlayerEquipment/WorldPickupItem angewendet — Größe nur am Prefab im Inspector.")]
    public Vector3 worldObjectScale = new Vector3(115f, 115f, 115f);
    [Tooltip("Nicht mehr von PlayerEquipment angewendet — Größe nur am handPrefab im Inspector.")]
    public Vector3 handObjectScale = new Vector3(200f, 200f, 200f);

    [Header("Tool")]
    [Min(0)] public int chopPower = 0;

    /// <summary>
    /// Bitmaske für erlaubte Slots (0–3). allowedSlotMask 0 → ein Slot wie bisher über allowedEquipmentSlot.
    /// </summary>
    public int GetEffectiveAllowedMask()
    {
        if (allowedSlotMask != 0)
            return allowedSlotMask & 0x0F;

        return 1 << (int)allowedEquipmentSlot;
    }

    public bool IsAllowedInEquipmentSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex > 3)
            return false;

        return (GetEffectiveAllowedMask() & (1 << slotIndex)) != 0;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        allowedSlotMask &= 0x0F;
    }
#endif
}
