using UnityEngine;

public class LootWorldPickup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Collider pickupTrigger;
    [SerializeField] private Rigidbody pickupRigidbody;

    [Header("Pickup")]
    [SerializeField] private bool pickupOnTrigger = true;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float pickupDelay = 0.25f;
    [SerializeField] private float autoDestroyAfter = 60f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private LootItemDefinitionSO itemDefinition;
    private int quantity;
    private bool canBePickedUp;
    private bool wasCollected;

    public LootItemDefinitionSO ItemDefinition => itemDefinition;
    public int Quantity => quantity;

    private void Awake()
    {
        if (pickupTrigger == null)
        {
            pickupTrigger = GetComponent<Collider>();
        }

        if (pickupRigidbody == null)
        {
            pickupRigidbody = GetComponent<Rigidbody>();
        }

        if (pickupTrigger == null)
        {
            Debug.LogWarning($"[{nameof(LootWorldPickup)}] Missing Collider on '{name}'.", this);
        }
        else if (!pickupTrigger.isTrigger)
        {
            Debug.LogWarning($"[{nameof(LootWorldPickup)}] Collider on '{name}' should usually be Is Trigger = true.", this);
        }
    }

    private void OnEnable()
    {
        canBePickedUp = false;
        wasCollected = false;

        if (pickupDelay > 0f)
        {
            Invoke(nameof(EnablePickup), pickupDelay);
        }
        else
        {
            canBePickedUp = true;
        }

        if (autoDestroyAfter > 0f)
        {
            Destroy(gameObject, autoDestroyAfter);
        }
    }

    private void OnDisable()
    {
        CancelInvoke();
    }

    public void Initialize(LootItemDefinitionSO newItemDefinition, int newQuantity)
    {
        itemDefinition = newItemDefinition;
        quantity = Mathf.Max(1, newQuantity);

        if (debugLogs && itemDefinition != null)
        {
            Debug.Log($"[{nameof(LootWorldPickup)}] Initialized '{name}' with {quantity}x {itemDefinition.displayName}.", this);
        }
    }

    public void AddImpulse(Vector3 impulse, ForceMode forceMode = ForceMode.Impulse)
    {
        if (pickupRigidbody == null)
            return;

        pickupRigidbody.AddForce(impulse, forceMode);
    }

    private void EnablePickup()
    {
        canBePickedUp = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!pickupOnTrigger)
            return;

        if (wasCollected)
            return;

        if (!canBePickedUp)
            return;

        if (other == null)
            return;

        if (!other.CompareTag(playerTag))
            return;

        Collect(other.gameObject);
    }

    public void Collect(GameObject collector)
    {
        if (wasCollected)
            return;

        wasCollected = true;

        string itemName = itemDefinition != null ? itemDefinition.displayName : "Unknown Loot";

        if (debugLogs)
        {
            string collectorName = collector != null ? collector.name : "Unknown Collector";
            Debug.Log($"[{nameof(LootWorldPickup)}] {collectorName} collected {quantity}x {itemName}.", this);
        }

        Destroy(gameObject);
    }
}
