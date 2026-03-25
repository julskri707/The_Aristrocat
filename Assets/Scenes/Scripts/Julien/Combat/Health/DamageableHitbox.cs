using UnityEngine;

public class DamageableHitbox : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private DamageableHealth targetHealth;

    [Header("Modifier")]
    [SerializeField] private float damageMultiplier = 1f;
    [SerializeField] private bool useThisTransformPositionAsFallbackHitPoint = true;

    public DamageableHealth TargetHealth => targetHealth;
    public float DamageMultiplier => damageMultiplier;

    private void Awake()
    {
        if (targetHealth == null)
        {
            targetHealth = GetComponentInParent<DamageableHealth>();
        }

        if (targetHealth == null)
        {
            Debug.LogWarning($"[{nameof(DamageableHitbox)}] No {nameof(DamageableHealth)} found for '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        damageMultiplier = Mathf.Max(0f, damageMultiplier);
    }

    public bool ApplyDamageFromHit(DamageInfo incomingDamage)
    {
        if (targetHealth == null)
        {
            Debug.LogWarning($"[{nameof(DamageableHitbox)}] Tried to forward damage on '{name}', but targetHealth is missing.", this);
            return false;
        }

        DamageInfo finalDamage = incomingDamage;
        finalDamage.amount *= damageMultiplier;

        if (useThisTransformPositionAsFallbackHitPoint && finalDamage.hitPoint == Vector3.zero)
        {
            finalDamage.hitPoint = transform.position;
        }

        return targetHealth.ApplyDamage(finalDamage);
    }
}
