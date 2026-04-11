using UnityEngine;

[DisallowMultipleComponent]
public class WorldPickupItem : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] private InventoryItemData itemData;
    [SerializeField, Min(1)] private int amount = 1;

    [Header("Pickup")]
    [SerializeField] private bool requireTrigger = false;
    [SerializeField] private bool allowLookPickup = true;
    [SerializeField] private bool destroyOnPickup = true;
    [SerializeField] private string playerTag = "Player";

    public InventoryItemData ItemData => itemData;
    public int Amount => amount;
    public bool AllowLookPickup => allowLookPickup;

    private void Awake()
    {
        ApplyWorldScale();
    }

    private void OnValidate()
    {
        ApplyWorldScale();
    }

    public void Initialize(InventoryItemData newItemData, int newAmount)
    {
        itemData = newItemData;
        amount = Mathf.Max(1, newAmount);
        ApplyWorldScale();
    }

    public bool TryPickup(PlayerInventory inventory)
    {
        if (inventory == null || itemData == null || amount <= 0)
            return false;

        bool added = inventory.AddItem(itemData, amount);
        if (!added)
            return false;

        if (destroyOnPickup)
            Destroy(gameObject);

        return true;
    }

    public string GetPromptText()
    {
        if (itemData == null)
            return "Item";

        return itemData.itemDisplayName;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!requireTrigger)
            return;

        if (!other.CompareTag(playerTag))
            return;

        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null)
            inventory = other.GetComponent<PlayerInventory>();

        if (inventory == null)
            return;

        TryPickup(inventory);
    }

    private void ApplyWorldScale()
    {
        if (itemData == null)
            return;

        transform.localScale = itemData.worldObjectScale;
    }
}
