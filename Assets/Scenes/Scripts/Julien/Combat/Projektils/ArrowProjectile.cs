using UnityEngine;

public class ArrowProjectile : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider mainCollider;

    [Header("Lifetime")]
    [SerializeField] private float maxLifetime = 12f;
    [SerializeField] private float destroyDelayAfterImpact = 10f;

    [Header("Impact Behaviour")]
    [SerializeField] private bool destroyOnImpact = false;
    [SerializeField] private bool stickOnImpact = true;
    [SerializeField] private bool parentToHitObject = true;
    [SerializeField] private float embedDepth = 0.12f;
    [SerializeField] private bool disableColliderOnImpact = true;
    [SerializeField] private bool stopPhysicsOnImpact = true;
    [SerializeField] private bool allowTriggerImpacts = true;

    [Header("Critical Hits")]
    [SerializeField] private bool enableCriticalHits = true;
    [SerializeField] [Range(0f, 1f)] private float criticalChance = 0.20f;
    [SerializeField] private float criticalMultiplier = 2f;

    [Header("Rotation")]
    [SerializeField] private bool orientToVelocity = true;
    [SerializeField] private float minVelocityToOrient = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Collider[] projectileColliders;

    private GameObject owner;
    private Transform ownerTransform;
    private CombatTeam sourceTeam = CombatTeam.Player;
    private float damage;

    private bool initialized;
    private bool hasImpacted;

    public bool IsInitialized => initialized;
    public bool HasImpacted => hasImpacted;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (mainCollider == null)
        {
            mainCollider = GetComponent<Collider>();
        }

        projectileColliders = GetComponentsInChildren<Collider>(true);

        if (rb == null)
        {
            Debug.LogWarning($"[{nameof(ArrowProjectile)}] Missing Rigidbody on '{name}'.", this);
        }

        if (mainCollider == null)
        {
            Debug.LogWarning($"[{nameof(ArrowProjectile)}] Missing Collider on '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        maxLifetime = Mathf.Max(0f, maxLifetime);
        destroyDelayAfterImpact = Mathf.Max(0f, destroyDelayAfterImpact);
        embedDepth = Mathf.Max(0f, embedDepth);
        minVelocityToOrient = Mathf.Max(0f, minVelocityToOrient);
        criticalMultiplier = Mathf.Max(1f, criticalMultiplier);
    }

    private void OnEnable()
    {
        if (maxLifetime > 0f)
        {
            Destroy(gameObject, maxLifetime);
        }
    }

    private void FixedUpdate()
    {
        if (!initialized || hasImpacted)
            return;

        if (!orientToVelocity || rb == null)
            return;

        Vector3 velocity = rb.linearVelocity;
        if (velocity.sqrMagnitude < minVelocityToOrient * minVelocityToOrient)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        rb.MoveRotation(targetRotation);
    }

    public bool Initialize(
        float damage,
        float speed,
        Vector3 direction,
        GameObject owner,
        Transform ownerTransform,
        CombatTeam sourceTeam,
        Collider[] ownerCollidersToIgnore,
        bool enableCriticalHits,
        float criticalChance,
        float criticalMultiplier)
    {
        if (rb == null || mainCollider == null)
        {
            Debug.LogError($"[{nameof(ArrowProjectile)}] Cannot initialize '{name}' because Rigidbody or Collider is missing.", this);
            return false;
        }

        this.damage = Mathf.Max(0f, damage);
        this.owner = owner;
        this.ownerTransform = ownerTransform;
        this.sourceTeam = sourceTeam;
        this.enableCriticalHits = enableCriticalHits;
        this.criticalChance = Mathf.Clamp01(criticalChance);
        this.criticalMultiplier = Mathf.Max(1f, criticalMultiplier);

        Vector3 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        transform.rotation = Quaternion.LookRotation(safeDirection, Vector3.up);

        ApplyOwnerCollisionIgnore(ownerCollidersToIgnore);

        rb.isKinematic = false;
        rb.detectCollisions = true;
        rb.linearVelocity = safeDirection * Mathf.Max(0f, speed);

        initialized = true;
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || hasImpacted)
            return;

        if (collision == null || collision.collider == null)
            return;

        Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        HandleImpact(collision.collider, hitPoint);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized || hasImpacted || !allowTriggerImpacts)
            return;

        if (other == null)
            return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        if (hitPoint == Vector3.zero)
        {
            hitPoint = transform.position;
        }

        HandleImpact(other, hitPoint);
    }

    private void HandleImpact(Collider hitCollider, Vector3 hitPoint)
    {
        if (hitCollider == null)
            return;

        if (IsOwnerCollider(hitCollider))
            return;

        hasImpacted = true;

        Vector3 hitDirection = GetCurrentTravelDirection();

        TryApplyDamage(hitCollider, hitPoint, hitDirection);
        FinalizeImpact(hitCollider.transform, hitPoint, hitDirection);
    }

    private void TryApplyDamage(Collider hitCollider, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (!TryResolveTarget(hitCollider, out DamageableHealth targetHealth, out DamageableHitbox targetHitbox))
            return;

        if (targetHealth == null)
            return;

        if (ownerTransform != null)
        {
            if (targetHealth.transform == ownerTransform || targetHealth.transform.IsChildOf(ownerTransform))
                return;
        }

        if (targetHealth.IsDead)
            return;

        bool isCritical = enableCriticalHits && Random.value < criticalChance;
        float appliedCritMultiplier = isCritical ? criticalMultiplier : 1f;
        float finalDamage = damage * appliedCritMultiplier;

        DamageInfo damageInfo = new DamageInfo(
            amount: finalDamage,
            hitPoint: hitPoint,
            hitDirection: hitDirection,
            source: owner,
            sourceTransform: ownerTransform,
            sourceTeam: sourceTeam,
            ignoresFriendlyFire: false,
            damageId: "ArrowProjectile",
            isCriticalHit: isCritical,
            criticalMultiplierApplied: appliedCritMultiplier
        );

        bool applied = false;

        if (targetHitbox != null)
        {
            applied = targetHitbox.ApplyDamageFromHit(damageInfo);
        }
        else
        {
            applied = targetHealth.ApplyDamage(damageInfo);
        }

        if (debugLogs && applied)
        {
            string critLabel = isCritical ? " CRIT" : "";
            Debug.Log($"[{nameof(ArrowProjectile)}] Arrow hit '{targetHealth.name}'.{critLabel}", targetHealth);
        }
    }

    private void FinalizeImpact(Transform hitTransform, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (stopPhysicsOnImpact && rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        if (disableColliderOnImpact)
        {
            DisableProjectileColliders();
        }

        if (stickOnImpact)
        {
            Vector3 forward = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.position = hitPoint - forward * embedDepth;

            if (parentToHitObject && hitTransform != null)
            {
                transform.SetParent(hitTransform, true);
            }
        }

        if (destroyOnImpact)
        {
            Destroy(gameObject);
        }
        else if (destroyDelayAfterImpact > 0f)
        {
            Destroy(gameObject, destroyDelayAfterImpact);
        }
    }

    private void DisableProjectileColliders()
    {
        if (projectileColliders == null)
            return;

        for (int i = 0; i < projectileColliders.Length; i++)
        {
            if (projectileColliders[i] != null)
            {
                projectileColliders[i].enabled = false;
            }
        }
    }

    private bool TryResolveTarget(Collider col, out DamageableHealth targetHealth, out DamageableHitbox targetHitbox)
    {
        targetHealth = null;
        targetHitbox = null;

        targetHitbox = col.GetComponent<DamageableHitbox>();
        if (targetHitbox != null && targetHitbox.TargetHealth != null)
        {
            targetHealth = targetHitbox.TargetHealth;
            return true;
        }

        targetHealth = col.GetComponent<DamageableHealth>();
        if (targetHealth != null)
            return true;

        targetHealth = col.GetComponentInParent<DamageableHealth>();
        return targetHealth != null;
    }

    private bool IsOwnerCollider(Collider col)
    {
        if (col == null || ownerTransform == null)
            return false;

        Transform t = col.transform;
        return t == ownerTransform || t.IsChildOf(ownerTransform);
    }

    private Vector3 GetCurrentTravelDirection()
    {
        if (rb != null)
        {
            Vector3 velocity = rb.linearVelocity;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                return velocity.normalized;
            }
        }

        return transform.forward;
    }

    private void ApplyOwnerCollisionIgnore(Collider[] ownerCollidersToIgnore)
    {
        if (ownerCollidersToIgnore == null || projectileColliders == null)
            return;

        for (int i = 0; i < projectileColliders.Length; i++)
        {
            Collider projectileCollider = projectileColliders[i];
            if (projectileCollider == null)
                continue;

            for (int j = 0; j < ownerCollidersToIgnore.Length; j++)
            {
                Collider ownerCollider = ownerCollidersToIgnore[j];
                if (ownerCollider == null)
                    continue;

                Physics.IgnoreCollision(projectileCollider, ownerCollider, true);
            }
        }
    }
}
