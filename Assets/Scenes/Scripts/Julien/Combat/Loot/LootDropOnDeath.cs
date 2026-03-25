using System.Collections.Generic;
using UnityEngine;

public class LootDropOnDeath : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DamageableHealth health;
    [SerializeField] private LootDropTableSO lootTable;
    [SerializeField] private Transform dropOrigin;

    [Header("Spawn")]
    [SerializeField] private float scatterRadius = 1.0f;
    [SerializeField] private float verticalOffset = 0.15f;
    [SerializeField] private bool randomYaw = true;

    [Header("Impulse Optional")]
    [SerializeField] private bool applyDropImpulse = true;
    [SerializeField] private float horizontalImpulse = 1.5f;
    [SerializeField] private float upwardImpulse = 1.5f;

    [Header("Rules")]
    [SerializeField] private bool dropOnlyOnce = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private readonly List<LootDropResult> rolledDrops = new List<LootDropResult>();

    private bool hasDropped;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<DamageableHealth>();
        }

        if (dropOrigin == null)
        {
            dropOrigin = transform;
        }

        if (health == null)
        {
            Debug.LogWarning($"[{nameof(LootDropOnDeath)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }

        if (lootTable == null)
        {
            Debug.LogWarning($"[{nameof(LootDropOnDeath)}] No {nameof(LootDropTableSO)} assigned on '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        scatterRadius = Mathf.Max(0f, scatterRadius);
        horizontalImpulse = Mathf.Max(0f, horizontalImpulse);
        upwardImpulse = Mathf.Max(0f, upwardImpulse);
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += OnDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= OnDied;
        }
    }

    private void OnDied(DamageableHealth deadHealth, DamageInfo killingDamage)
    {
        if (dropOnlyOnce && hasDropped)
            return;

        SpawnLoot();
    }

    public void SpawnLoot()
    {
        if (dropOnlyOnce && hasDropped)
            return;

        if (lootTable == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[{nameof(LootDropOnDeath)}] No loot table on '{name}', nothing to drop.", this);
            }
            return;
        }

        lootTable.RollDrops(rolledDrops);

        if (rolledDrops.Count == 0)
        {
            hasDropped = true;

            if (debugLogs)
            {
                Debug.Log($"[{nameof(LootDropOnDeath)}] '{name}' rolled no loot.", this);
            }

            return;
        }

        Vector3 origin = dropOrigin != null ? dropOrigin.position : transform.position;
        origin.y += verticalOffset;

        for (int i = 0; i < rolledDrops.Count; i++)
        {
            LootDropResult result = rolledDrops[i];

            if (result.itemDefinition == null)
                continue;

            GameObject pickupPrefab = result.itemDefinition.worldPickupPrefab;
            if (pickupPrefab == null)
            {
                Debug.LogWarning(
                    $"[{nameof(LootDropOnDeath)}] Item '{result.itemDefinition.displayName}' has no worldPickupPrefab assigned.",
                    this
                );
                continue;
            }

            Vector2 offset2D = Random.insideUnitCircle * scatterRadius;
            Vector3 spawnPosition = origin + new Vector3(offset2D.x, 0f, offset2D.y);

            Quaternion rotation = randomYaw
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : pickupPrefab.transform.rotation;

            GameObject instance = Instantiate(pickupPrefab, spawnPosition, rotation);
            if (instance == null)
            {
                Debug.LogError($"[{nameof(LootDropOnDeath)}] Failed to instantiate loot pickup.", this);
                continue;
            }

            LootWorldPickup pickup = instance.GetComponent<LootWorldPickup>();
            if (pickup == null)
            {
                Debug.LogWarning(
                    $"[{nameof(LootDropOnDeath)}] Spawned pickup '{instance.name}' has no {nameof(LootWorldPickup)} component.",
                    instance
                );
            }
            else
            {
                pickup.Initialize(result.itemDefinition, result.quantity);

                if (applyDropImpulse)
                {
                    Vector3 impulseDir = new Vector3(offset2D.x, 0f, offset2D.y);
                    if (impulseDir.sqrMagnitude > 0.0001f)
                    {
                        impulseDir.Normalize();
                    }

                    Vector3 impulse = impulseDir * horizontalImpulse + Vector3.up * upwardImpulse;
                    pickup.AddImpulse(impulse, ForceMode.Impulse);
                }
            }

            if (debugLogs)
            {
                Debug.Log(
                    $"[{nameof(LootDropOnDeath)}] Dropped {result.quantity}x {result.itemDefinition.displayName} from '{name}'.",
                    this
                );
            }
        }

        hasDropped = true;
    }
}
