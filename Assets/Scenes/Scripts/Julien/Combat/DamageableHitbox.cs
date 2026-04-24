using UnityEngine;

public class DamageableHitbox : MonoBehaviour
{
    [SerializeField] private DamageableHealth targetHealth;

    public DamageableHealth TargetHealth => targetHealth;

    private void Awake()
    {
        if (targetHealth == null)
            targetHealth = GetComponentInParent<DamageableHealth>();
    }

    public bool ApplyDamageFromHit(DamageInfo info)
    {
        if (targetHealth == null)
            return false;

        return targetHealth.ApplyDamage(info);
    }
}
