using System.Collections;
using UnityEngine;

public class WolfMeleeAttack : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DamageableHealth ownerHealth;
    [SerializeField] private Animator animator;

    [Header("Attack")]
    [SerializeField] private float damage = 12f;
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float attackWindup = 0.25f;
    [SerializeField] private float attackRecovery = 0.35f;
    [SerializeField] private float hitRangeLeeway = 0.2f;

    [Header("Critical Hits")]
    [SerializeField] private bool enableCriticalHits = false;
    [SerializeField] [Range(0f, 1f)] private float criticalChance = 0.10f;
    [SerializeField] private float criticalMultiplier = 1.75f;

    [Header("Animator Optional")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private string attackTriggerName = "Attack";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawAttackRangeGizmo = true;

    private Coroutine attackRoutine;
    private DamageableHealth currentTargetHealth;
    private Transform currentTargetTransform;
    private bool isAttacking;
    private bool damageAppliedThisAttack;
    private float nextAllowedAttackTime;

    public bool IsAttacking => isAttacking;
    public float AttackRange => attackRange;
    public float AttackCooldown => attackCooldown;
    public float NextAllowedAttackTime => nextAllowedAttackTime;

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponent<DamageableHealth>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (ownerHealth == null)
        {
            Debug.LogWarning($"[{nameof(WolfMeleeAttack)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        damage = Mathf.Max(0f, damage);
        attackRange = Mathf.Max(0.1f, attackRange);
        attackCooldown = Mathf.Max(0f, attackCooldown);
        attackWindup = Mathf.Max(0f, attackWindup);
        attackRecovery = Mathf.Max(0f, attackRecovery);
        hitRangeLeeway = Mathf.Max(0f, hitRangeLeeway);
        criticalMultiplier = Mathf.Max(1f, criticalMultiplier);
    }

    private void OnEnable()
    {
        if (ownerHealth != null)
        {
            ownerHealth.Died += OnOwnerDied;
        }
    }

    private void OnDisable()
    {
        if (ownerHealth != null)
        {
            ownerHealth.Died -= OnOwnerDied;
        }

        CancelAttack();
    }

    public bool CanStartAttack(DamageableHealth targetHealth, Transform targetTransform)
    {
        if (ownerHealth == null)
            return false;

        if (ownerHealth.IsDead)
            return false;

        if (isAttacking)
            return false;

        if (Time.time < nextAllowedAttackTime)
            return false;

        if (targetHealth == null || targetTransform == null)
            return false;

        if (targetHealth.IsDead)
            return false;

        float distance = Vector3.Distance(transform.position, targetTransform.position);
        if (distance > attackRange)
            return false;

        return true;
    }

    public bool TryStartAttack(DamageableHealth targetHealth, Transform targetTransform)
    {
        if (!CanStartAttack(targetHealth, targetTransform))
            return false;

        CancelAttack();

        currentTargetHealth = targetHealth;
        currentTargetTransform = targetTransform;
        damageAppliedThisAttack = false;

        attackRoutine = StartCoroutine(AttackRoutine());
        return true;
    }

    public void CancelAttack()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        currentTargetHealth = null;
        currentTargetTransform = null;
        damageAppliedThisAttack = false;
        isAttacking = false;
    }

    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        nextAllowedAttackTime = Time.time + attackCooldown;

        if (driveAnimator && animator != null && !string.IsNullOrWhiteSpace(attackTriggerName))
        {
            animator.ResetTrigger(attackTriggerName);
            animator.SetTrigger(attackTriggerName);
        }

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfMeleeAttack)}] Attack started on '{name}'.", this);
        }

        if (attackWindup > 0f)
        {
            yield return new WaitForSeconds(attackWindup);
        }

        TryApplyDamageOnce();

        if (attackRecovery > 0f)
        {
            yield return new WaitForSeconds(attackRecovery);
        }

        currentTargetHealth = null;
        currentTargetTransform = null;
        damageAppliedThisAttack = false;
        isAttacking = false;
        attackRoutine = null;

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfMeleeAttack)}] Attack finished on '{name}'.", this);
        }
    }

    private void TryApplyDamageOnce()
    {
        if (damageAppliedThisAttack)
            return;

        if (ownerHealth == null || ownerHealth.IsDead)
            return;

        if (currentTargetHealth == null || currentTargetTransform == null)
            return;

        if (currentTargetHealth.IsDead)
            return;

        float distance = Vector3.Distance(transform.position, currentTargetTransform.position);
        if (distance > attackRange + hitRangeLeeway)
            return;

        Vector3 hitDirection = currentTargetTransform.position - transform.position;
        if (hitDirection.sqrMagnitude > 0.0001f)
        {
            hitDirection.Normalize();
        }
        else
        {
            hitDirection = transform.forward;
        }

        bool isCritical = enableCriticalHits && Random.value < criticalChance;
        float appliedCritMultiplier = isCritical ? criticalMultiplier : 1f;
        float finalDamage = damage * appliedCritMultiplier;

        DamageInfo damageInfo = new DamageInfo(
            amount: finalDamage,
            hitPoint: currentTargetTransform.position,
            hitDirection: hitDirection,
            source: gameObject,
            sourceTransform: transform,
            sourceTeam: ownerHealth.Team,
            ignoresFriendlyFire: false,
            damageId: "WolfMeleeAttack",
            isCriticalHit: isCritical,
            criticalMultiplierApplied: appliedCritMultiplier
        );

        bool applied = currentTargetHealth.ApplyDamage(damageInfo);
        if (applied)
        {
            damageAppliedThisAttack = true;

            if (debugLogs)
            {
                string critLabel = isCritical ? " CRIT" : "";
                Debug.Log($"[{nameof(WolfMeleeAttack)}] '{name}' damaged '{currentTargetHealth.name}'.{critLabel}", currentTargetHealth);
            }
        }
    }

    private void OnOwnerDied(DamageableHealth deadHealth, DamageInfo killingDamage)
    {
        CancelAttack();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawAttackRangeGizmo)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
