using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerDefense : MonoBehaviour, IIncomingDamageModifier
{
    [Header("References")]
    [SerializeField] private DamageableHealth ownerHealth;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform orientationSource;
    [SerializeField] private PlayerCombatWeaponSwitcher weaponSwitcher;
    [SerializeField] private PlayerSwordCombat swordCombat;
    [SerializeField] private PlayerBowCombat bowCombat;
    [SerializeField] private PlayerStamina playerStamina;

    [Header("Input")]
    [SerializeField] private bool useBuiltInInput = true;
    [SerializeField] private KeyCode blockKey = KeyCode.Q;
    [SerializeField] private KeyCode dodgeKey = KeyCode.LeftShift;
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private string verticalAxis = "Vertical";

    [Header("Block")]
    [SerializeField] private bool canBlock = true;
    [SerializeField] private bool blockOnlyInSwordMode = true;
    [SerializeField] private bool directionalBlock = true;
    [SerializeField] [Range(1f, 360f)] private float blockAngle = 120f;
    [SerializeField] [Range(0f, 1f)] private float blockDamageMultiplier = 0f;
    [SerializeField] private Behaviour[] behavioursToDisableWhileBlocking = new Behaviour[0];

    [Header("Block Stamina")]
    [SerializeField] private bool requireStaminaForBlock = true;
    [SerializeField] private float blockStaminaDrainPerSecond = 12f;
    [SerializeField] private bool breakBlockWhenStaminaEmpty = true;

    [Header("Dodge")]
    [SerializeField] private bool canDodge = true;
    [SerializeField] private float dodgeCooldown = 0.8f;
    [SerializeField] private float dodgeDuration = 0.22f;
    [SerializeField] private float dodgeDistance = 3.5f;
    [SerializeField] private float dodgeInvulnerabilityDuration = 0.18f;
    [SerializeField] private bool useMovementInputForDodge = true;
    [SerializeField] private bool useForwardFallbackIfNoInput = true;
    [SerializeField] private Behaviour[] behavioursToDisableWhileDodging = new Behaviour[0];

    [Header("Dodge Stamina")]
    [SerializeField] private bool requireStaminaForDodge = true;
    [SerializeField] private float dodgeStaminaCost = 20f;

    [Header("Animator Optional")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private string blockBoolName = "IsBlocking";
    [SerializeField] private string dodgeTriggerName = "Dodge";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private float gizmoLength = 1.75f;

    private readonly List<Behaviour> temporarilyDisabledForBlock = new List<Behaviour>();
    private readonly List<Behaviour> temporarilyDisabledForDodge = new List<Behaviour>();

    private Coroutine dodgeRoutine;

    private bool isBlocking;
    private bool isDodging;
    private float nextAllowedDodgeTime;
    private float dodgeInvulnerableUntil;

    private bool warnedMissingStamina;

    public int Priority => 100;
    public bool IsBlocking => isBlocking;
    public bool IsDodging => isDodging;
    public bool IsInvulnerableFromDodge => isDodging && Time.time < dodgeInvulnerableUntil;

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponent<DamageableHealth>();
        }

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (orientationSource == null)
        {
            orientationSource = transform;
        }

        if (weaponSwitcher == null)
        {
            weaponSwitcher = GetComponent<PlayerCombatWeaponSwitcher>();
        }

        if (swordCombat == null)
        {
            swordCombat = GetComponent<PlayerSwordCombat>();
        }

        if (bowCombat == null)
        {
            bowCombat = GetComponent<PlayerBowCombat>();
        }

        if (playerStamina == null)
        {
            playerStamina = GetComponent<PlayerStamina>();
        }

        if (ownerHealth == null)
        {
            Debug.LogWarning($"[{nameof(PlayerDefense)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        dodgeCooldown = Mathf.Max(0f, dodgeCooldown);
        dodgeDuration = Mathf.Max(0.01f, dodgeDuration);
        dodgeDistance = Mathf.Max(0f, dodgeDistance);
        dodgeInvulnerabilityDuration = Mathf.Max(0f, dodgeInvulnerabilityDuration);
        dodgeStaminaCost = Mathf.Max(0f, dodgeStaminaCost);
        blockStaminaDrainPerSecond = Mathf.Max(0f, blockStaminaDrainPerSecond);
        gizmoLength = Mathf.Max(0.1f, gizmoLength);
    }

    private void OnDisable()
    {
        ForceStopAllDefenseStates();
    }

    private void Update()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
        {
            ForceStopAllDefenseStates();
            return;
        }

        if (!useBuiltInInput)
            return;

        HandleBlockInput();
        UpdateBlockStaminaDrain();
        HandleDodgeInput();
    }

    public void ModifyIncomingDamage(DamageableHealth target, IncomingDamageContext context)
    {
        if (target == null || target != ownerHealth)
            return;

        if (ownerHealth == null || ownerHealth.IsDead)
            return;

        if (context == null)
            return;

        if (context.CancelDamage)
            return;

        if (IsInvulnerableFromDodge)
        {
            context.CancelDamage = true;

            if (debugLogs)
            {
                Debug.Log($"[{nameof(PlayerDefense)}] Dodged incoming damage on '{name}'.", this);
            }

            return;
        }

        if (!isBlocking)
            return;

        if (!CanBlockCurrentDamage(context.DamageInfo))
            return;

        DamageInfo modified = context.DamageInfo;
        modified.amount *= blockDamageMultiplier;
        context.DamageInfo = modified;

        if (modified.amount <= 0.0001f)
        {
            context.CancelDamage = true;
        }

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerDefense)}] Blocked incoming damage on '{name}'.", this);
        }
    }

    private void HandleBlockInput()
    {
        if (!canBlock)
        {
            SetBlocking(false);
            return;
        }

        bool wantsBlock = Input.GetKey(blockKey);

        if (blockOnlyInSwordMode && weaponSwitcher != null)
        {
            wantsBlock &= weaponSwitcher.CurrentMode == PlayerCombatWeaponMode.Sword;
        }

        if (isDodging)
        {
            wantsBlock = false;
        }

        if (wantsBlock && !CanStartBlock())
        {
            wantsBlock = false;
        }

        SetBlocking(wantsBlock);
    }

    private void UpdateBlockStaminaDrain()
    {
        if (!isBlocking)
            return;

        if (!requireStaminaForBlock)
            return;

        if (playerStamina == null)
        {
            WarnMissingStaminaOnce();
            StopBlockingInternal();
            return;
        }

        float drainAmount = blockStaminaDrainPerSecond * Time.deltaTime;
        if (drainAmount <= 0f)
            return;

        bool spent = playerStamina.TrySpend(drainAmount, "BlockDrain");
        if (!spent && breakBlockWhenStaminaEmpty)
        {
            if (debugLogs)
            {
                Debug.Log($"[{nameof(PlayerDefense)}] Block ended because stamina is empty on '{name}'.", this);
            }

            StopBlockingInternal();
        }
    }

    private void HandleDodgeInput()
    {
        if (!canDodge)
            return;

        if (!Input.GetKeyDown(dodgeKey))
            return;

        TryStartDodge();
    }

    public bool TryStartDodge()
    {
        if (!canDodge)
            return false;

        if (ownerHealth != null && ownerHealth.IsDead)
            return false;

        if (isDodging)
            return false;

        if (Time.time < nextAllowedDodgeTime)
            return false;

        if (requireStaminaForDodge)
        {
            if (playerStamina == null)
            {
                WarnMissingStaminaOnce();
                return false;
            }

            if (!playerStamina.HasEnough(dodgeStaminaCost))
                return false;
        }

        if (!TryGetDodgeDirection(out Vector3 dodgeDirection))
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[{nameof(PlayerDefense)}] No valid dodge direction found on '{name}'.", this);
            }

            return false;
        }

        if (requireStaminaForDodge)
        {
            if (!playerStamina.TrySpend(dodgeStaminaCost, "Dodge"))
                return false;
        }

        StopBlockingInternal();
        CancelCombatActionsBeforeDodge();

        if (dodgeRoutine != null)
        {
            StopCoroutine(dodgeRoutine);
        }

        dodgeRoutine = StartCoroutine(DodgeRoutine(dodgeDirection));
        return true;
    }

    private IEnumerator DodgeRoutine(Vector3 dodgeDirection)
    {
        isDodging = true;
        nextAllowedDodgeTime = Time.time + dodgeCooldown;
        dodgeInvulnerableUntil = Time.time + dodgeInvulnerabilityDuration;

        ApplyDisableList(behavioursToDisableWhileDodging, temporarilyDisabledForDodge);

        if (driveAnimator && animator != null && !string.IsNullOrWhiteSpace(dodgeTriggerName))
        {
            animator.ResetTrigger(dodgeTriggerName);
            animator.SetTrigger(dodgeTriggerName);
        }

        float elapsed = 0f;
        float speed = dodgeDistance / dodgeDuration;

        while (elapsed < dodgeDuration)
        {
            if (ownerHealth != null && ownerHealth.IsDead)
            {
                break;
            }

            float dt = Time.deltaTime;
            float moveAmount = speed * dt;
            Vector3 displacement = dodgeDirection * moveAmount;

            if (characterController != null && characterController.enabled)
            {
                characterController.Move(displacement);
            }
            else
            {
                transform.position += displacement;
            }

            elapsed += dt;
            yield return null;
        }

        isDodging = false;
        dodgeRoutine = null;

        RestoreDisableList(temporarilyDisabledForDodge);
    }

    private bool TryGetDodgeDirection(out Vector3 direction)
    {
        direction = Vector3.zero;

        Transform basis = orientationSource != null ? orientationSource : transform;

        if (useMovementInputForDodge)
        {
            float h = 0f;
            float v = 0f;

            try
            {
                h = Input.GetAxisRaw(horizontalAxis);
                v = Input.GetAxisRaw(verticalAxis);
            }
            catch
            {
                if (debugLogs)
                {
                    Debug.LogWarning($"[{nameof(PlayerDefense)}] Invalid input axes on '{name}'. Falling back to forward dodge.", this);
                }
            }

            Vector3 raw = (basis.right * h) + (basis.forward * v);
            raw.y = 0f;

            if (raw.sqrMagnitude > 0.0001f)
            {
                direction = raw.normalized;
                return true;
            }
        }

        if (useForwardFallbackIfNoInput)
        {
            direction = basis.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();
                return true;
            }
        }

        return false;
    }

    private bool CanStartBlock()
    {
        if (!canBlock)
            return false;

        if (ownerHealth != null && ownerHealth.IsDead)
            return false;

        if (isDodging)
            return false;

        if (!requireStaminaForBlock)
            return true;

        if (playerStamina == null)
        {
            WarnMissingStaminaOnce();
            return false;
        }

        return playerStamina.CurrentStamina > 0.0001f;
    }

    private bool CanBlockCurrentDamage(DamageInfo damageInfo)
    {
        if (!canBlock)
            return false;

        if (!isBlocking)
            return false;

        if (!directionalBlock)
            return true;

        Vector3 defenderForward = orientationSource != null ? orientationSource.forward : transform.forward;
        defenderForward.y = 0f;

        if (defenderForward.sqrMagnitude <= 0.0001f)
            return true;

        defenderForward.Normalize();

        Vector3 directionFromDefenderToAttacker = Vector3.zero;

        if (damageInfo.hitDirection.sqrMagnitude > 0.0001f)
        {
            directionFromDefenderToAttacker = -damageInfo.hitDirection.normalized;
        }
        else if (damageInfo.sourceTransform != null)
        {
            directionFromDefenderToAttacker = damageInfo.sourceTransform.position - transform.position;
            directionFromDefenderToAttacker.y = 0f;
        }

        if (directionFromDefenderToAttacker.sqrMagnitude <= 0.0001f)
            return true;

        directionFromDefenderToAttacker.Normalize();

        float angle = Vector3.Angle(defenderForward, directionFromDefenderToAttacker);
        return angle <= blockAngle * 0.5f;
    }

    private void SetBlocking(bool value)
    {
        if (value)
        {
            StartBlockingInternal();
        }
        else
        {
            StopBlockingInternal();
        }
    }

    private void StartBlockingInternal()
    {
        if (isBlocking)
            return;

        if (ownerHealth != null && ownerHealth.IsDead)
            return;

        if (isDodging)
            return;

        isBlocking = true;

        ApplyDisableList(behavioursToDisableWhileBlocking, temporarilyDisabledForBlock);

        if (driveAnimator && animator != null && !string.IsNullOrWhiteSpace(blockBoolName))
        {
            animator.SetBool(blockBoolName, true);
        }
    }

    private void StopBlockingInternal()
    {
        if (!isBlocking)
            return;

        isBlocking = false;

        RestoreDisableList(temporarilyDisabledForBlock);

        if (driveAnimator && animator != null && !string.IsNullOrWhiteSpace(blockBoolName))
        {
            animator.SetBool(blockBoolName, false);
        }
    }

    private void CancelCombatActionsBeforeDodge()
    {
        if (swordCombat != null)
        {
            swordCombat.FinishAttack();
        }

        if (bowCombat != null)
        {
            bowCombat.CancelBowState();
        }
    }

    private void ApplyDisableList(Behaviour[] behaviours, List<Behaviour> storage)
    {
        storage.Clear();

        if (behaviours == null)
            return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (behaviour == this)
                continue;

            if (!behaviour.enabled)
                continue;

            behaviour.enabled = false;
            storage.Add(behaviour);
        }
    }

    private void RestoreDisableList(List<Behaviour> storage)
    {
        for (int i = 0; i < storage.Count; i++)
        {
            if (storage[i] != null)
            {
                storage[i].enabled = true;
            }
        }

        storage.Clear();
    }

    private void ForceStopAllDefenseStates()
    {
        if (dodgeRoutine != null)
        {
            StopCoroutine(dodgeRoutine);
            dodgeRoutine = null;
        }

        isDodging = false;
        dodgeInvulnerableUntil = 0f;

        RestoreDisableList(temporarilyDisabledForDodge);
        StopBlockingInternal();
    }

    private void WarnMissingStaminaOnce()
    {
        if (warnedMissingStamina)
            return;

        warnedMissingStamina = true;
        Debug.LogWarning(
            $"[{nameof(PlayerDefense)}] '{name}' requires {nameof(PlayerStamina)} but none was found.",
            this
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform basis = orientationSource != null ? orientationSource : transform;
        Vector3 origin = transform.position + Vector3.up * 1.0f;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + basis.forward * gizmoLength);

        if (directionalBlock)
        {
            Vector3 left = Quaternion.Euler(0f, -blockAngle * 0.5f, 0f) * basis.forward;
            Vector3 right = Quaternion.Euler(0f, blockAngle * 0.5f, 0f) * basis.forward;

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + left.normalized * gizmoLength);
            Gizmos.DrawLine(origin, origin + right.normalized * gizmoLength);
        }
    }
}
