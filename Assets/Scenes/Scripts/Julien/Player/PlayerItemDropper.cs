using UnityEngine;

[DisallowMultipleComponent]
public class PlayerItemDropper : MonoBehaviour
{
    public static PlayerItemDropper Instance { get; private set; }

    [Header("References")]
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private Camera lookCamera;
    [SerializeField] private Transform dropSpawnPoint;

    [Header("Drop")]
    [SerializeField] private float dropDistance = 1.5f;
    [SerializeField] private float upwardOffset = 0.2f;
    [SerializeField] private float forwardImpulse = 2.5f;
    [SerializeField] private float upwardImpulse = 1.25f;

    private void Awake()
    {
        Instance = this;

        if (playerInventory == null)
            playerInventory = GetComponent<PlayerInventory>();

        if (playerInventory == null)
            playerInventory = GetComponentInParent<PlayerInventory>();

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (playerEquipment == null)
            playerEquipment = GetComponentInParent<PlayerEquipment>();

        if (lookCamera == null)
            lookCamera = Camera.main;
    }

    public bool TryDropDraggedItem(InventoryUIDragController.DragOriginType originType, int sourceIndex, InventoryItemData itemData)
    {
        if (itemData == null)
            return false;

        if (originType == InventoryUIDragController.DragOriginType.Inventory)
            return DropFromInventory(sourceIndex, itemData);

        if (originType == InventoryUIDragController.DragOriginType.Equipment)
            return DropFromEquipment(sourceIndex);

        return false;
    }

    private bool DropFromInventory(int slotIndex, InventoryItemData itemData)
    {
        if (playerInventory == null || itemData == null)
            return false;

        bool removed = playerInventory.RemoveItem(itemData, 1);
        if (!removed)
            return false;

        bool spawned = SpawnWorldDrop(itemData, 1);
        if (!spawned)
        {
            playerInventory.AddItem(itemData, 1);
            return false;
        }

        playerInventory.ForceRefresh();
        return true;
    }

    private bool DropFromEquipment(int slotIndex)
    {
        if (playerEquipment == null)
            return false;

        InventoryItemData itemData = playerEquipment.GetEquippedItem(slotIndex);
        if (itemData == null)
            return false;

        bool spawned = SpawnWorldDrop(itemData, 1);
        if (!spawned)
            return false;

        bool unequipped = playerEquipment.UnequipToInventory(slotIndex);
        if (!unequipped)
            return false;

        if (playerInventory != null)
            playerInventory.RemoveItem(itemData, 1);

        if (playerInventory != null)
            playerInventory.ForceRefresh();

        return true;
    }

    private bool SpawnWorldDrop(InventoryItemData itemData, int amount)
    {
        if (itemData == null || itemData.worldDropPrefab == null)
            return false;

        Vector3 spawnPosition = GetSpawnPosition();
        Quaternion spawnRotation = GetSpawnRotation();

        GameObject instance = Instantiate(itemData.worldDropPrefab, spawnPosition, spawnRotation);

        WorldPickupItem pickup = instance.GetComponent<WorldPickupItem>();
        if (pickup == null)
            pickup = instance.GetComponentInChildren<WorldPickupItem>();

        if (pickup != null)
            pickup.Initialize(itemData, amount);

        Rigidbody rb = instance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = instance.GetComponentInChildren<Rigidbody>();

        if (rb != null)
        {
            Vector3 impulseDirection = lookCamera != null ? lookCamera.transform.forward : transform.forward;
            impulseDirection.y = 0f;
            if (impulseDirection.sqrMagnitude < 0.0001f)
                impulseDirection = transform.forward;

            impulseDirection.Normalize();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(impulseDirection * forwardImpulse + Vector3.up * upwardImpulse, ForceMode.Impulse);
        }

        return true;
    }

    private Vector3 GetSpawnPosition()
    {
        if (dropSpawnPoint != null)
            return dropSpawnPoint.position;

        if (lookCamera != null)
            return lookCamera.transform.position + lookCamera.transform.forward * dropDistance + Vector3.up * upwardOffset;

        return transform.position + transform.forward * dropDistance + Vector3.up * upwardOffset;
    }

    private Quaternion GetSpawnRotation()
    {
        if (lookCamera != null)
        {
            Vector3 forward = lookCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        return Quaternion.LookRotation(transform.forward, Vector3.up);
    }
}
