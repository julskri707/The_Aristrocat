using System.Collections;
using UnityEngine;

public class PlayerBowCombat : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private DamageableHealth ownerHealth;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform arrowSpawnPoint;
    [SerializeField] private Transform aimPivot;
    [SerializeField] private ArrowProjectile arrowProjectilePrefab;
    [SerializeField] private PlayerStamina playerStamina;

    [Header("Input")]
    [SerializeField] private bool useBuiltInInput = true;
    [SerializeField] private int aimMouseButton = 1;
    [SerializeField] private int fireMouseButton = 0;

    [Header("Shot")]
    [SerializeField] private float arrowDamage = 20f;
    [SerializeField] private float arrowSpeed = 45f;
    [SerializeField] private float shotCooldown = 0.45f;
    [SerializeField] private float drawTimeBeforeRelease = 0.10f;
    [SerializeField] private bool requireAimingToShoot = true;

    [Header("Critical Hits")]
    [SerializeField] private bool enableCriticalHits = true;
    [SerializeField] [Range(0f, 1f)] private float criticalChance = 0.20f;
    [SerializeField] private float criticalMultiplier = 2f;

    [Header("Stamina")]
    [SerializeField] private bool requireStamina = true;
    [SerializeField] private float staminaCostPerShot = 15f;

    [Header("Release Timing")]
    [SerializeField] private bool releaseShotByAnimationEvent = false;
    [SerializeField] private float animationEventReleaseTimeout = 0.75f;
    [SerializeField] private bool fallbackToTimedReleaseIfEventMissing = true;

    [Header("Aim")]
    [SerializeField] private float aimMaxDistance = 250f;
    [SerializeField] private bool rotateAimPivot = true;
    [SerializeField] private float aimRotationSpeed = 20f;
    [SerializeField] private bool drawSpawnPointGizmo = true;
    [SerializeField] private float spawnPointGizmoLength = 1.25f;

    [Header("Animator Optional")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private string aimBoolName = "IsAiming";
    [SerializeField] private string fireTriggerName = "BowFire";

    [Header("Debug")]
    [SerializeField] private bool debugDrawAimRay = false;
    [SerializeField] private bool debugLogs = false;

    private Collider[] ownerColliders;
    private Coroutine pendingShotRoutine;

    private bool isAiming;
    private bool shotQueued;
    private bool waitingForReleaseEvent;

    private float nextAllowedShotTime;

    private Vector3 currentAimPoint;
    private Vector3 currentAimDirection = Vector3.forward;

    private bool warnedMissingStamina;

    public bool IsAiming => isAiming;
    public bool ShotQueued => shotQueued;

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

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (playerStamina == null)
        {
            playerStamina = GetComponent<PlayerStamina>();
        }

        if (ownerHealth == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowCombat)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }

        if (arrowSpawnPoint == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowCombat)}] Missing arrowSpawnPoint on '{name}'.", this);
        }

        if (arrowProjectilePrefab == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowCombat)}] Missing arrowProjectilePrefab on '{name}'.", this);
        }

        RefreshOwnerColliders();
    }

    private void OnValidate()
    {
        arrowDamage = Mathf.Max(0f, arrowDamage);
        arrowSpeed = Mathf.Max(0f, arrowSpeed);
        shotCooldown = Mathf.Max(0f, shotCooldown);
        drawTimeBeforeRelease = Mathf.Max(0f, drawTimeBeforeRelease);
        staminaCostPerShot = Mathf.Max(0f, staminaCostPerShot);
        criticalMultiplier = Mathf.Max(1f, criticalMultiplier);
        animationEventReleaseTimeout = Mathf.Max(0.05f, animationEventReleaseTimeout);
        aimMaxDistance = Mathf.Max(1f, aimMaxDistance);
        aimRotationSpeed = Mathf.Max(0f, aimRotationSpeed);
        spawnPointGizmoLength = Mathf.Max(0.1f, spawnPointGizmoLength);
    }

    private void OnEnable()
    {
        RefreshOwnerColliders();
        SetAiming(false);
        ClearQueuedShot();
    }

    private void OnDisable()
    {
        CancelBowState();
    }

    private void Update()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
        {
            CancelBowState();
            return;
        }

        if (useBuiltInInput)
        {
            HandleInput();
        }

        if (isAiming)
        {
            UpdateAim();
        }
    }

    private void HandleInput()
    {
        bool wantsAim = Input.GetMouseButton(aimMouseButton);
        SetAiming(wantsAim);

        if (!isAiming)
            return;

        if (Input.GetMouseButtonDown(fireMouseButton))
        {
            TryQueueShot();
        }
    }

    public void CancelBowState()
    {
        if (pendingShotRoutine != null)
        {
            StopCoroutine(pendingShotRoutine);
            pendingShotRoutine = null;
        }

        ClearQueuedShot();
        SetAiming(false);
    }

    public bool TryQueueShot()
    {
        if (!CanShoot())
            return false;

        ClearQueuedShot();

        shotQueued = true;
        waitingForReleaseEvent = releaseShotByAnimationEvent;

        if (driveAnimator && animator != null && !string.IsNullOrWhiteSpace(fireTriggerName))
        {
            animator.ResetTrigger(fireTriggerName);
            animator.SetTrigger(fireTriggerName);
        }

        if (releaseShotByAnimationEvent)
        {
            pendingShotRoutine = StartCoroutine(WaitForReleaseEventRoutine());
        }
        else
        {
            pendingShotRoutine = StartCoroutine(TimedReleaseRoutine());
        }

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerBowCombat)}] Shot queued.", this);
        }

        return true;
    }

    public bool CanShoot()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
            return false;

        if (requireAimingToShoot && !isAiming)
            return false;

        if (shotQueued)
            return false;

        if (Time.time < nextAllowedShotTime)
            return false;

        if (arrowProjectilePrefab == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowCombat)}] Cannot shoot because arrowProjectilePrefab is missing.", this);
            return false;
        }

        if (arrowSpawnPoint == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowCombat)}] Cannot shoot because arrowSpawnPoint is missing.", this);
            return false;
        }

        if (requireStamina)
        {
            if (playerStamina == null)
            {
                WarnMissingStaminaOnce();
                return false;
            }

            if (!playerStamina.HasEnough(staminaCostPerShot))
                return false;
        }

        return true;
    }

    public void AE_ReleaseBowShot()
    {
        if (!shotQueued)
            return;

        waitingForReleaseEvent = false;

        if (pendingShotRoutine != null)
        {
            StopCoroutine(pendingShotRoutine);
            pendingShotRoutine = null;
        }

        FireQueuedArrowIfPossible();
    }

    private IEnumerator TimedReleaseRoutine()
    {
        if (drawTimeBeforeRelease > 0f)
        {
            yield return new WaitForSeconds(drawTimeBeforeRelease);
        }

        pendingShotRoutine = null;
        FireQueuedArrowIfPossible();
    }

    private IEnumerator WaitForReleaseEventRoutine()
    {
        yield return new WaitForSeconds(animationEventReleaseTimeout);

        pendingShotRoutine = null;

        if (!shotQueued || !waitingForReleaseEvent)
            yield break;

        if (fallbackToTimedReleaseIfEventMissing)
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[{nameof(PlayerBowCombat)}] No bow release Animation Event received on '{name}'. Falling back to automatic release.", this);
            }

            waitingForReleaseEvent = false;
            FireQueuedArrowIfPossible();
        }
        else
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[{nameof(PlayerBowCombat)}] No bow release Animation Event received on '{name}'. Shot was cancelled.", this);
            }

            ClearQueuedShot();
        }
    }

    private bool FireQueuedArrowIfPossible()
    {
        if (!shotQueued)
            return false;

        if (ownerHealth != null && ownerHealth.IsDead)
        {
            ClearQueuedShot();
            return false;
        }

        if (requireAimingToShoot && !isAiming)
        {
            ClearQueuedShot();
            return false;
        }

        if (arrowProjectilePrefab == null || arrowSpawnPoint == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowCombat)}] Cannot release queued shot on '{name}' because prefab or spawn point is missing.", this);
            ClearQueuedShot();
            return false;
        }

        if (requireStamina)
        {
            if (playerStamina == null)
            {
                WarnMissingStaminaOnce();
                ClearQueuedShot();
                return false;
            }

            if (!playerStamina.TrySpend(staminaCostPerShot, "BowShot"))
            {
                ClearQueuedShot();
                return false;
            }
        }

        UpdateAim();

        Vector3 direction = currentAimDirection;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = arrowSpawnPoint.forward;
        }

        ArrowProjectile projectile = Instantiate(
            arrowProjectilePrefab,
            arrowSpawnPoint.position,
            Quaternion.LookRotation(direction, Vector3.up)
        );

        CombatTeam sourceTeam = ownerHealth != null ? ownerHealth.Team : CombatTeam.Player;

        bool initialized = projectile.Initialize(
            damage: arrowDamage,
            speed: arrowSpeed,
            direction: direction,
            owner: gameObject,
            ownerTransform: transform,
            sourceTeam: sourceTeam,
            ownerCollidersToIgnore: ownerColliders,
            enableCriticalHits: enableCriticalHits,
            criticalChance: criticalChance,
            criticalMultiplier: criticalMultiplier
        );

        if (!initialized)
        {
            Debug.LogError($"[{nameof(PlayerBowCombat)}] Failed to initialize arrow projectile on '{projectile.name}'.", projectile);
            Destroy(projectile.gameObject);
            ClearQueuedShot();
            return false;
        }

        nextAllowedShotTime = Time.time + shotCooldown;
        ClearQueuedShot();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerBowCombat)}] Arrow fired.", this);
        }

        return true;
    }

    private void UpdateAim()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
            if (aimCamera == null)
            {
                currentAimPoint = arrowSpawnPoint != null
                    ? arrowSpawnPoint.position + transform.forward * aimMaxDistance
                    : transform.position + transform.forward * aimMaxDistance;

                currentAimDirection = transform.forward;
                return;
            }
        }

        if (arrowSpawnPoint == null)
        {
            currentAimPoint = transform.position + transform.forward * aimMaxDistance;
            currentAimDirection = transform.forward;
            return;
        }

        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, aimMaxDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            currentAimPoint = hit.point;
        }
        else
        {
            currentAimPoint = ray.origin + ray.direction * aimMaxDistance;
        }

        currentAimDirection = currentAimPoint - arrowSpawnPoint.position;
        if (currentAimDirection.sqrMagnitude < 0.0001f)
        {
            currentAimDirection = arrowSpawnPoint.forward;
        }
        else
        {
            currentAimDirection.Normalize();
        }

        if (rotateAimPivot && aimPivot != null)
        {
            Quaternion targetRotation = Quaternion.LookRotation(currentAimDirection, Vector3.up);
            aimPivot.rotation = Quaternion.Slerp(
                aimPivot.rotation,
                targetRotation,
                Mathf.Clamp01(aimRotationSpeed * Time.deltaTime)
            );
        }

        if (debugDrawAimRay)
        {
            Debug.DrawLine(arrowSpawnPoint.position, currentAimPoint, Color.yellow);
        }
    }

    private void SetAiming(bool value)
    {
        if (isAiming == value)
            return;

        isAiming = value;

        if (!isAiming && shotQueued)
        {
            if (pendingShotRoutine != null)
            {
                StopCoroutine(pendingShotRoutine);
                pendingShotRoutine = null;
            }

            ClearQueuedShot();
        }

        if (driveAnimator && animator != null && !string.IsNullOrWhiteSpace(aimBoolName))
        {
            animator.SetBool(aimBoolName, isAiming);
        }
    }

    private void RefreshOwnerColliders()
    {
        ownerColliders = GetComponentsInChildren<Collider>(true);
    }

    private void ClearQueuedShot()
    {
        shotQueued = false;
        waitingForReleaseEvent = false;
    }

    private void WarnMissingStaminaOnce()
    {
        if (warnedMissingStamina)
            return;

        warnedMissingStamina = true;
        Debug.LogWarning(
            $"[{nameof(PlayerBowCombat)}] '{name}' requires {nameof(PlayerStamina)} but none was found.",
            this
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSpawnPointGizmo || arrowSpawnPoint == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(arrowSpawnPoint.position, 0.08f);
        Gizmos.DrawLine(
            arrowSpawnPoint.position,
            arrowSpawnPoint.position + arrowSpawnPoint.forward * spawnPointGizmoLength
        );
    }
}
