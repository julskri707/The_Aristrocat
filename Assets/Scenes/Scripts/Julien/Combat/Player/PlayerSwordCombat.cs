using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSwordCombat : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private DamageableHealth ownerHealth;
    [SerializeField] private SwordMeleeHitbox swordHitbox;
    [SerializeField] private PlayerStamina playerStamina;

    [Header("Input")]
    [SerializeField] private bool useBuiltInInput = true;
    [SerializeField] private int mouseButton = 0;

    [Header("Attack")]
    [SerializeField] private float damagePerHit = 25f;
    [SerializeField] private float attackCooldown = 0.55f;
    [SerializeField] private string animatorTriggerName = "SwordAttack";

    [Header("Critical Hits")]
    [SerializeField] private bool enableCriticalHits = true;
    [SerializeField] [Range(0f, 1f)] private float criticalChance = 0.15f;
    [SerializeField] private float criticalMultiplier = 2f;

    [Header("Stamina")]
    [SerializeField] private bool requireStamina = true;
    [SerializeField] private float staminaCostPerAttack = 12f;

    [Header("Hit Detection")]
    [SerializeField] private float hitScanInterval = 0.02f;
    [SerializeField] private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private bool hitDamageableRootWithoutHitbox = true;
    [SerializeField] private int overlapBufferSize = 32;

    [Header("Fallback Without Animation Events")]
    [SerializeField] private bool useAnimationEvents = true;
    [SerializeField] private float fallbackWindup = 0.10f;
    [SerializeField] private float fallbackHitDuration = 0.12f;
    [SerializeField] private float fallbackTotalAttackDuration = 0.45f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private readonly HashSet<DamageableHealth> hitTargetsThisAttack = new HashSet<DamageableHealth>();

    private Collider[] overlapResults;
    private Coroutine fallbackRoutine;

    private bool attackInProgress;
    private bool hitWindowActive;
    private float nextAllowedAttackTime;
    private float nextHitScanTime;
    private int currentAttackId;
    private bool warnedMissingStamina;

    public bool AttackInProgress => attackInProgress;
    public bool HitWindowActive => hitWindowActive;
    public int CurrentAttackId => currentAttackId;

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

        if (swordHitbox == null)
        {
            swordHitbox = GetComponentInChildren<SwordMeleeHitbox>(true);
        }

        if (playerStamina == null)
        {
            playerStamina = GetComponent<PlayerStamina>();
        }

        if (ownerHealth == null)
        {
            Debug.LogWarning($"[{nameof(PlayerSwordCombat)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }

        if (swordHitbox == null)
        {
            Debug.LogWarning($"[{nameof(PlayerSwordCombat)}] Missing {nameof(SwordMeleeHitbox)} on '{name}'.", this);
        }

        EnsureBuffer();
    }

    private void OnValidate()
    {
        damagePerHit = Mathf.Max(0f, damagePerHit);
        attackCooldown = Mathf.Max(0f, attackCooldown);
        staminaCostPerAttack = Mathf.Max(0f, staminaCostPerAttack);
        criticalMultiplier = Mathf.Max(1f, criticalMultiplier);
        hitScanInterval = Mathf.Max(0.001f, hitScanInterval);
        overlapBufferSize = Mathf.Max(8, overlapBufferSize);

        fallbackWindup = Mathf.Max(0f, fallbackWindup);
        fallbackHitDuration = Mathf.Max(0f, fallbackHitDuration);
        fallbackTotalAttackDuration = Mathf.Max(0f, fallbackTotalAttackDuration);
    }

    private void OnDisable()
    {
        if (fallbackRoutine != null)
        {
            StopCoroutine(fallbackRoutine);
            fallbackRoutine = null;
        }

        attackInProgress = false;
        hitWindowActive = false;
        hitTargetsThisAttack.Clear();
    }

    private void Update()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
            return;

        if (useBuiltInInput)
        {
            if (Input.GetMouseButtonDown(mouseButton))
            {
                TryStartAttack();
            }
        }

        if (hitWindowActive)
        {
            if (Time.time >= nextHitScanTime)
            {
                nextHitScanTime = Time.time + hitScanInterval;
                PerformHitScan();
            }
        }
    }

    public bool TryStartAttack()
    {
        if (!CanStartAttack())
            return false;

        if (requireStamina)
        {
            if (playerStamina == null)
            {
                WarnMissingStaminaOnce();
                return false;
            }

            if (!playerStamina.TrySpend(staminaCostPerAttack, "SwordAttack"))
            {
                return false;
            }
        }

        attackInProgress = true;
        hitWindowActive = false;
        currentAttackId++;
        hitTargetsThisAttack.Clear();
        nextAllowedAttackTime = Time.time + attackCooldown;

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerSwordCombat)}] Attack started. AttackId={currentAttackId}", this);
        }

        if (animator != null && !string.IsNullOrWhiteSpace(animatorTriggerName))
        {
            animator.ResetTrigger(animatorTriggerName);
            animator.SetTrigger(animatorTriggerName);
        }

        if (!useAnimationEvents)
        {
            if (fallbackRoutine != null)
            {
                StopCoroutine(fallbackRoutine);
            }

            fallbackRoutine = StartCoroutine(FallbackAttackRoutine());
        }

        return true;
    }

    public bool CanStartAttack()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
            return false;

        if (attackInProgress)
            return false;

        if (Time.time < nextAllowedAttackTime)
            return false;

        if (swordHitbox == null)
        {
            Debug.LogWarning($"[{nameof(PlayerSwordCombat)}] Cannot attack because swordHitbox is missing on '{name}'.", this);
            return false;
        }

        if (requireStamina)
        {
            if (playerStamina == null)
            {
                WarnMissingStaminaOnce();
                return false;
            }

            if (!playerStamina.HasEnough(staminaCostPerAttack))
                return false;
        }

        return true;
    }

    public void BeginHitWindow()
    {
        if (!attackInProgress)
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[{nameof(PlayerSwordCombat)}] BeginHitWindow called, but no attack is in progress.", this);
            }
            return;
        }

        hitWindowActive = true;
        nextHitScanTime = 0f;

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerSwordCombat)}] Hit window opened. AttackId={currentAttackId}", this);
        }

        PerformHitScan();
    }

    public void EndHitWindow()
    {
        if (!hitWindowActive)
            return;

        hitWindowActive = false;

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerSwordCombat)}] Hit window closed. AttackId={currentAttackId}", this);
        }
    }

    public void FinishAttack()
    {
        hitWindowActive = false;
        attackInProgress = false;

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerSwordCombat)}] Attack finished. AttackId={currentAttackId}", this);
        }
    }

    private IEnumerator FallbackAttackRoutine()
    {
        yield return new WaitForSeconds(fallbackWindup);
        BeginHitWindow();

        yield return new WaitForSeconds(fallbackHitDuration);
        EndHitWindow();

        float remaining = Mathf.Max(0f, fallbackTotalAttackDuration - fallbackWindup - fallbackHitDuration);
        if (remaining > 0f)
        {
            yield return new WaitForSeconds(remaining);
        }

        FinishAttack();
        fallbackRoutine = null;
    }

    private void PerformHitScan()
    {
        if (swordHitbox == null)
            return;

        if (!swordHitbox.TryGetWorldBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation))
            return;

        EnsureBuffer();

        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            overlapResults,
            rotation,
            Physics.AllLayers,
            queryTriggerInteraction
        );

        if (hitCount >= overlapResults.Length && debugLogs)
        {
            Debug.LogWarning($"[{nameof(PlayerSwordCombat)}] Overlap buffer may be too small on '{name}'. Consider increasing overlapBufferSize.", this);
        }

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = overlapResults[i];
            if (col == null)
                continue;

            if (!col.enabled)
                continue;

            if (!TryResolveTarget(col, out DamageableHealth targetHealth, out DamageableHitbox targetHitbox))
                continue;

            if (targetHealth == null)
                continue;

            if (IsSelfTarget(targetHealth))
                continue;

            if (targetHealth.IsDead)
                continue;

            if (hitTargetsThisAttack.Contains(targetHealth))
                continue;

            Vector3 hitPoint = GetBestHitPoint(col, center);
            Vector3 hitDirection = targetHealth.transform.position - transform.position;

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
            float finalDamage = damagePerHit * appliedCritMultiplier;

            CombatTeam sourceTeam = ownerHealth != null ? ownerHealth.Team : CombatTeam.Player;

            DamageInfo damageInfo = new DamageInfo(
                amount: finalDamage,
                hitPoint: hitPoint,
                hitDirection: hitDirection,
                source: gameObject,
                sourceTransform: transform,
                sourceTeam: sourceTeam,
                ignoresFriendlyFire: false,
                damageId: $"SwordAttack_{currentAttackId}",
                isCriticalHit: isCritical,
                criticalMultiplierApplied: appliedCritMultiplier
            );

            bool applied = false;

            if (targetHitbox != null)
            {
                applied = targetHitbox.ApplyDamageFromHit(damageInfo);
            }
            else if (hitDamageableRootWithoutHitbox)
            {
                applied = targetHealth.ApplyDamage(damageInfo);
            }

            if (applied)
            {
                hitTargetsThisAttack.Add(targetHealth);

                if (debugLogs)
                {
                    string critLabel = isCritical ? " CRIT" : "";
                    Debug.Log($"[{nameof(PlayerSwordCombat)}] Hit '{targetHealth.name}' during attack {currentAttackId}.{critLabel}", targetHealth);
                }
            }
        }

        for (int i = 0; i < hitCount && i < overlapResults.Length; i++)
        {
            overlapResults[i] = null;
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

        if (!hitDamageableRootWithoutHitbox)
            return false;

        targetHealth = col.GetComponent<DamageableHealth>();
        if (targetHealth != null)
            return true;

        targetHealth = col.GetComponentInParent<DamageableHealth>();
        return targetHealth != null;
    }

    private bool IsSelfTarget(DamageableHealth targetHealth)
    {
        if (targetHealth == null)
            return false;

        if (ownerHealth != null && targetHealth == ownerHealth)
            return true;

        if (targetHealth.transform == transform)
            return true;

        if (targetHealth.transform.IsChildOf(transform))
            return true;

        return false;
    }

    private Vector3 GetBestHitPoint(Collider col, Vector3 fallbackPoint)
    {
        if (col == null)
            return fallbackPoint;

        Vector3 point = col.ClosestPoint(fallbackPoint);

        if (point == Vector3.zero)
            return fallbackPoint;

        return point;
    }

    private void EnsureBuffer()
    {
        if (overlapResults == null || overlapResults.Length != overlapBufferSize)
        {
            overlapBufferSize = Mathf.Max(8, overlapBufferSize);
            overlapResults = new Collider[overlapBufferSize];
        }
    }

    private void WarnMissingStaminaOnce()
    {
        if (warnedMissingStamina)
            return;

        warnedMissingStamina = true;
        Debug.LogWarning(
            $"[{nameof(PlayerSwordCombat)}] '{name}' requires {nameof(PlayerStamina)} but none was found.",
            this
        );
    }

    public void AE_BeginSwordHitWindow()
    {
        BeginHitWindow();
    }

    public void AE_EndSwordHitWindow()
    {
        EndHitWindow();
    }

    public void AE_FinishSwordAttack()
    {
        FinishAttack();
    }

    [ContextMenu("Debug Start Sword Attack")]
    private void DebugStartSwordAttack()
    {
        TryStartAttack();
    }
}
