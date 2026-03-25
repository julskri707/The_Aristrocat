using UnityEngine;
using UnityEngine.AI;

public class WolfBrain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DamageableHealth ownerHealth;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private WolfMeleeAttack meleeAttack;
    [SerializeField] private Animator animator;

    [Header("Target")]
    [SerializeField] private Transform targetRoot;
    [SerializeField] private DamageableHealth targetHealth;
    [SerializeField] private bool autoFindTargetByTag = true;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private float targetSearchInterval = 2f;

    [Header("Idle / Wander")]
    [SerializeField] private float idleDurationMin = 1.0f;
    [SerializeField] private float idleDurationMax = 2.5f;
    [SerializeField] private float postChaseIdleDurationMin = 0.35f;
    [SerializeField] private float postChaseIdleDurationMax = 1.0f;

    [SerializeField] private bool useHomePositionAsWanderCenter = true;
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private float minWanderDistance = 2.0f;
    [SerializeField] private float minDistanceFromLastWanderTarget = 2.0f;
    [SerializeField] private int wanderPointTryCount = 12;
    [SerializeField] private float wanderSampleDistance = 8f;
    [SerializeField] private float wanderArrivalDistance = 0.8f;

    [Header("Detection / Chase")]
    [SerializeField] private float detectionRange = 14f;
    [SerializeField] private float loseInterestRange = 20f;
    [SerializeField] private float loseInterestDelay = 2.0f;
    [SerializeField] private float chaseRepathInterval = 0.2f;

    [Header("Attack State")]
    [SerializeField] private float attackEnterBuffer = 0.10f;
    [SerializeField] private float attackExitBuffer = 0.75f;
    [SerializeField] private float faceTargetRotationSpeed = 10f;

    [Header("Simple Stuck Recovery")]
    [SerializeField] private bool enableStuckRecovery = true;
    [SerializeField] private float stuckCheckInterval = 0.75f;
    [SerializeField] private float stuckMinMovedDistance = 0.2f;
    [SerializeField] private float stuckTimeBeforeRecover = 1.5f;

    [Header("Animator Optional")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private string moveBoolName = "Move";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    private WolfAIState currentState = WolfAIState.Idle;
    private Vector3 homePosition;

    private float idleTimer;
    private float loseInterestTimer;
    private float nextRepathTime;
    private float nextTargetSearchTime;

    private Vector3 lastWanderDestination;
    private bool hasLastWanderDestination;

    private Vector3 lastStuckCheckPosition;
    private float stuckCheckTimer;
    private float stuckAccumulatedTime;

    private bool warnedMissingTarget;
    private bool warnedAgentNotReady;
    private bool homePositionInitialized;

    public WolfAIState CurrentState => currentState;

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponent<DamageableHealth>();
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (meleeAttack == null)
        {
            meleeAttack = GetComponent<WolfMeleeAttack>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (ownerHealth == null)
        {
            Debug.LogWarning($"[{nameof(WolfBrain)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }

        if (agent == null)
        {
            Debug.LogWarning($"[{nameof(WolfBrain)}] Missing {nameof(NavMeshAgent)} on '{name}'.", this);
        }

        if (meleeAttack == null)
        {
            Debug.LogWarning($"[{nameof(WolfBrain)}] Missing {nameof(WolfMeleeAttack)} on '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        idleDurationMin = Mathf.Max(0f, idleDurationMin);
        idleDurationMax = Mathf.Max(idleDurationMin, idleDurationMax);

        postChaseIdleDurationMin = Mathf.Max(0f, postChaseIdleDurationMin);
        postChaseIdleDurationMax = Mathf.Max(postChaseIdleDurationMin, postChaseIdleDurationMax);

        wanderRadius = Mathf.Max(0.5f, wanderRadius);
        minWanderDistance = Mathf.Max(0f, minWanderDistance);
        minDistanceFromLastWanderTarget = Mathf.Max(0f, minDistanceFromLastWanderTarget);
        wanderPointTryCount = Mathf.Max(1, wanderPointTryCount);
        wanderSampleDistance = Mathf.Max(0.1f, wanderSampleDistance);
        wanderArrivalDistance = Mathf.Max(0.1f, wanderArrivalDistance);

        detectionRange = Mathf.Max(0.1f, detectionRange);
        loseInterestRange = Mathf.Max(detectionRange, loseInterestRange);
        loseInterestDelay = Mathf.Max(0f, loseInterestDelay);
        chaseRepathInterval = Mathf.Max(0.05f, chaseRepathInterval);

        attackEnterBuffer = Mathf.Max(0f, attackEnterBuffer);
        attackExitBuffer = Mathf.Max(attackEnterBuffer, attackExitBuffer);
        faceTargetRotationSpeed = Mathf.Max(0f, faceTargetRotationSpeed);

        stuckCheckInterval = Mathf.Max(0.1f, stuckCheckInterval);
        stuckMinMovedDistance = Mathf.Max(0f, stuckMinMovedDistance);
        stuckTimeBeforeRecover = Mathf.Max(0.1f, stuckTimeBeforeRecover);
    }

    private void OnEnable()
    {
        if (ownerHealth != null)
        {
            ownerHealth.Died += OnOwnerDied;
        }
    }

    private void Start()
    {
        if (!homePositionInitialized)
        {
            homePosition = transform.position;
            homePositionInitialized = true;
        }

        ResolveTargetReference(false);

        if (ownerHealth != null && ownerHealth.IsDead)
        {
            EnterDeadState();
            return;
        }

        WarnAgentNotReadyOnce();
        ResetStuckTracking();
        EnterIdleState(false);
    }

    private void OnDisable()
    {
        if (ownerHealth != null)
        {
            ownerHealth.Died -= OnOwnerDied;
        }
    }

    public void SetTarget(Transform newTargetRoot, DamageableHealth newTargetHealth)
    {
        targetRoot = newTargetRoot;
        targetHealth = newTargetHealth;
        warnedMissingTarget = false;
    }

    public void SetHomePosition(Vector3 newHomePosition)
    {
        homePosition = newHomePosition;
        homePositionInitialized = true;
    }

    private void Update()
    {
        if (ownerHealth == null)
            return;

        if (ownerHealth.IsDead)
        {
            if (currentState != WolfAIState.Dead)
            {
                EnterDeadState();
            }

            return;
        }

        ResolveTargetReference(true);

        switch (currentState)
        {
            case WolfAIState.Idle:
                UpdateIdleState();
                break;

            case WolfAIState.Wander:
                UpdateWanderState();
                break;

            case WolfAIState.Chase:
                UpdateChaseState();
                break;

            case WolfAIState.Attack:
                UpdateAttackState();
                break;

            case WolfAIState.Dead:
                break;
        }

        UpdateStuckRecovery();
        UpdateAnimator();
    }

    private void UpdateIdleState()
    {
        if (TryDetectTarget())
            return;

        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            EnterWanderState();
        }
    }

    private void UpdateWanderState()
    {
        if (TryDetectTarget())
            return;

        if (!CanUseAgent())
        {
            EnterIdleState(false);
            return;
        }

        if (agent.pathPending)
            return;

        if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            if (debugLogs)
            {
                Debug.Log($"[{nameof(WolfBrain)}] {name} wander path ended or became invalid. Returning to Idle.", this);
            }

            EnterIdleState(false);
            return;
        }

        if (agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, wanderArrivalDistance))
        {
            EnterIdleState(false);
        }
    }

    private void UpdateChaseState()
    {
        if (!HasValidTarget())
        {
            EnterIdleState(true);
            return;
        }

        float distance = Vector3.Distance(transform.position, targetRoot.position);

        if (distance <= GetAttackEnterDistance())
        {
            EnterAttackState();
            return;
        }

        if (distance > loseInterestRange)
        {
            loseInterestTimer += Time.deltaTime;
            if (loseInterestTimer >= loseInterestDelay)
            {
                if (debugLogs)
                {
                    Debug.Log($"[{nameof(WolfBrain)}] {name} lost interest in target and returns to normal behaviour.", this);
                }

                EnterIdleState(true);
                return;
            }
        }
        else
        {
            loseInterestTimer = 0f;
        }

        if (!CanUseAgent())
            return;

        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + chaseRepathInterval;
            agent.isStopped = false;
            agent.SetDestination(targetRoot.position);
        }
    }

    private void UpdateAttackState()
    {
        if (!HasValidTarget())
        {
            EnterIdleState(true);
            return;
        }

        float distance = Vector3.Distance(transform.position, targetRoot.position);

        FaceTarget(targetRoot.position);
        KeepAgentStopped();

        if (distance > GetAttackExitDistance())
        {
            if (meleeAttack != null)
            {
                meleeAttack.CancelAttack();
            }

            EnterChaseState();
            return;
        }

        if (meleeAttack == null)
            return;

        if (meleeAttack.IsAttacking)
            return;

        if (meleeAttack.CanStartAttack(targetHealth, targetRoot))
        {
            meleeAttack.TryStartAttack(targetHealth, targetRoot);
        }
    }

    private void EnterIdleState(bool afterChase)
    {
        if (currentState == WolfAIState.Dead)
            return;

        currentState = WolfAIState.Idle;
        loseInterestTimer = 0f;

        if (afterChase)
        {
            idleTimer = Random.Range(postChaseIdleDurationMin, postChaseIdleDurationMax);
        }
        else
        {
            idleTimer = Random.Range(idleDurationMin, idleDurationMax);
        }

        StopAndClearAgentPath();
        ResetStuckTracking();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfBrain)}] {name} -> Idle", this);
        }
    }

    private void EnterWanderState()
    {
        if (currentState == WolfAIState.Dead)
            return;

        if (!CanUseAgent())
        {
            EnterIdleState(false);
            return;
        }

        if (!TryGetRandomWanderPoint(out Vector3 wanderPoint))
        {
            Debug.LogWarning($"[{nameof(WolfBrain)}] '{name}' could not find a valid wander point. Returning to Idle.", this);
            EnterIdleState(false);
            return;
        }

        currentState = WolfAIState.Wander;
        loseInterestTimer = 0f;
        agent.isStopped = false;
        agent.SetDestination(wanderPoint);

        lastWanderDestination = wanderPoint;
        hasLastWanderDestination = true;

        ResetStuckTracking();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfBrain)}] {name} -> Wander ({wanderPoint})", this);
        }
    }

    private void EnterChaseState()
    {
        if (currentState == WolfAIState.Dead)
            return;

        if (!HasValidTarget())
        {
            EnterIdleState(true);
            return;
        }

        currentState = WolfAIState.Chase;
        loseInterestTimer = 0f;

        if (CanUseAgent())
        {
            agent.isStopped = false;
            agent.SetDestination(targetRoot.position);
            nextRepathTime = Time.time + chaseRepathInterval;
        }

        ResetStuckTracking();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfBrain)}] {name} -> Chase", this);
        }
    }

    private void EnterAttackState()
    {
        if (currentState == WolfAIState.Dead)
            return;

        if (!HasValidTarget())
        {
            EnterIdleState(true);
            return;
        }

        currentState = WolfAIState.Attack;
        loseInterestTimer = 0f;

        StopAndClearAgentPath();
        ResetStuckTracking();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfBrain)}] {name} -> Attack", this);
        }
    }

    private void EnterDeadState()
    {
        currentState = WolfAIState.Dead;

        if (meleeAttack != null)
        {
            meleeAttack.CancelAttack();
        }

        StopAndClearAgentPath();
        ResetStuckTracking();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(WolfBrain)}] {name} -> Dead", this);
        }
    }

    private bool TryDetectTarget()
    {
        if (!HasValidTarget())
            return false;

        float distance = Vector3.Distance(transform.position, targetRoot.position);
        if (distance > detectionRange)
            return false;

        if (distance <= GetAttackEnterDistance())
        {
            EnterAttackState();
        }
        else
        {
            EnterChaseState();
        }

        return true;
    }

    private bool HasValidTarget()
    {
        if (targetHealth != null && targetRoot == null)
        {
            targetRoot = targetHealth.transform;
        }

        if (targetRoot == null || targetHealth == null)
            return false;

        if (targetHealth.IsDead)
            return false;

        return true;
    }

    private float GetAttackEnterDistance()
    {
        float baseRange = meleeAttack != null ? meleeAttack.AttackRange : 1.8f;
        return Mathf.Max(0.1f, baseRange + attackEnterBuffer);
    }

    private float GetAttackExitDistance()
    {
        float baseRange = meleeAttack != null ? meleeAttack.AttackRange : 1.8f;
        return Mathf.Max(0.1f, baseRange + attackExitBuffer);
    }

    private bool CanUseAgent()
    {
        if (agent == null)
            return false;

        if (!agent.enabled)
            return false;

        if (!agent.isOnNavMesh)
            return false;

        return true;
    }

    private void StopAndClearAgentPath()
    {
        if (!CanUseAgent())
            return;

        agent.isStopped = true;

        if (agent.hasPath)
        {
            agent.ResetPath();
        }
    }

    private void KeepAgentStopped()
    {
        if (!CanUseAgent())
            return;

        agent.isStopped = true;
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        Vector3 flatDirection = targetPosition - transform.position;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Mathf.Clamp01(faceTargetRotationSpeed * Time.deltaTime)
        );
    }

    private bool TryGetRandomWanderPoint(out Vector3 result)
    {
        result = transform.position;

        Vector3 center = useHomePositionAsWanderCenter ? homePosition : transform.position;
        float clampedRadius = Mathf.Max(minWanderDistance + 0.1f, wanderRadius);

        for (int i = 0; i < wanderPointTryCount; i++)
        {
            Vector2 dir2D = Random.insideUnitCircle.normalized;
            if (dir2D.sqrMagnitude < 0.0001f)
            {
                dir2D = Vector2.right;
            }

            float distance = Random.Range(minWanderDistance, clampedRadius);
            Vector3 candidate = center + new Vector3(dir2D.x, 0f, dir2D.y) * distance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderSampleDistance, NavMesh.AllAreas))
                continue;

            if (Vector3.Distance(transform.position, hit.position) < minWanderDistance)
                continue;

            if (hasLastWanderDestination && Vector3.Distance(hit.position, lastWanderDestination) < minDistanceFromLastWanderTarget)
                continue;

            result = hit.position;
            return true;
        }

        if (useHomePositionAsWanderCenter)
        {
            float distanceToHome = Vector3.Distance(transform.position, homePosition);
            if (distanceToHome > Mathf.Max(minWanderDistance, wanderArrivalDistance + 1f))
            {
                if (NavMesh.SamplePosition(homePosition, out NavMeshHit homeHit, wanderSampleDistance, NavMesh.AllAreas))
                {
                    result = homeHit.position;
                    return true;
                }
            }
        }

        return false;
    }

    private void UpdateStuckRecovery()
    {
        if (!enableStuckRecovery)
            return;

        if (currentState != WolfAIState.Wander && currentState != WolfAIState.Chase)
        {
            ResetStuckTracking();
            return;
        }

        if (!CanUseAgent())
        {
            ResetStuckTracking();
            return;
        }

        if (agent.isStopped || agent.pathPending || !agent.hasPath)
        {
            lastStuckCheckPosition = transform.position;
            stuckCheckTimer = 0f;
            stuckAccumulatedTime = 0f;
            return;
        }

        stuckCheckTimer += Time.deltaTime;
        if (stuckCheckTimer < stuckCheckInterval)
            return;

        float movedDistance = Vector3.Distance(transform.position, lastStuckCheckPosition);
        bool shouldBeMoving = agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.25f);

        if (shouldBeMoving && movedDistance < stuckMinMovedDistance)
        {
            stuckAccumulatedTime += stuckCheckTimer;
        }
        else
        {
            stuckAccumulatedTime = 0f;
        }

        lastStuckCheckPosition = transform.position;
        stuckCheckTimer = 0f;

        if (stuckAccumulatedTime >= stuckTimeBeforeRecover)
        {
            RecoverFromStuck();
            stuckAccumulatedTime = 0f;
        }
    }

    private void RecoverFromStuck()
    {
        if (currentState == WolfAIState.Wander)
        {
            if (debugLogs)
            {
                Debug.Log($"[{nameof(WolfBrain)}] {name} seemed stuck while wandering. Picking a new point.", this);
            }

            EnterIdleState(false);
            return;
        }

        if (currentState == WolfAIState.Chase)
        {
            if (!HasValidTarget())
            {
                EnterIdleState(true);
                return;
            }

            if (CanUseAgent())
            {
                if (debugLogs)
                {
                    Debug.Log($"[{nameof(WolfBrain)}] {name} seemed stuck while chasing. Refreshing chase destination.", this);
                }

                agent.ResetPath();
                agent.isStopped = false;
                agent.SetDestination(targetRoot.position);
                nextRepathTime = Time.time + chaseRepathInterval;
                ResetStuckTracking();
            }
        }
    }

    private void ResetStuckTracking()
    {
        lastStuckCheckPosition = transform.position;
        stuckCheckTimer = 0f;
        stuckAccumulatedTime = 0f;
    }

    private void ResolveTargetReference(bool allowTimedSearch)
    {
        if (HasValidTarget())
        {
            warnedMissingTarget = false;
            return;
        }

        if (targetRoot != null && targetHealth == null)
        {
            targetHealth = targetRoot.GetComponent<DamageableHealth>();
            if (targetHealth == null)
            {
                targetHealth = targetRoot.GetComponentInParent<DamageableHealth>();
            }

            if (targetHealth == null)
            {
                targetHealth = targetRoot.GetComponentInChildren<DamageableHealth>(true);
            }
        }

        if (HasValidTarget())
        {
            warnedMissingTarget = false;
            return;
        }

        if (!autoFindTargetByTag)
        {
            WarnMissingTargetOnce();
            return;
        }

        if (allowTimedSearch && Time.time < nextTargetSearchTime)
            return;

        nextTargetSearchTime = Time.time + Mathf.Max(0.25f, targetSearchInterval);

        try
        {
            GameObject found = GameObject.FindGameObjectWithTag(targetTag);
            if (found != null)
            {
                targetRoot = found.transform;
                targetHealth = found.GetComponent<DamageableHealth>();

                if (targetHealth == null)
                {
                    targetHealth = found.GetComponentInParent<DamageableHealth>();
                }

                if (targetHealth == null)
                {
                    targetHealth = found.GetComponentInChildren<DamageableHealth>(true);
                }

                if (debugLogs && targetHealth != null)
                {
                    Debug.Log($"[{nameof(WolfBrain)}] {name} found target '{targetRoot.name}'.", this);
                }

                warnedMissingTarget = false;
            }
            else
            {
                WarnMissingTargetOnce();
            }
        }
        catch (UnityException)
        {
            WarnMissingTargetOnce();
        }
    }

    private void WarnMissingTargetOnce()
    {
        if (warnedMissingTarget)
            return;

        warnedMissingTarget = true;
        Debug.LogWarning(
            $"[{nameof(WolfBrain)}] '{name}' has no valid player target. Assign targetRoot + targetHealth in the Inspector or make sure a GameObject with tag '{targetTag}' exists and has {nameof(DamageableHealth)}.",
            this
        );
    }

    private void WarnAgentNotReadyOnce()
    {
        if (warnedAgentNotReady)
            return;

        if (agent == null)
            return;

        if (agent.enabled && agent.isOnNavMesh)
            return;

        warnedAgentNotReady = true;
        Debug.LogWarning(
            $"[{nameof(WolfBrain)}] '{name}' has a NavMeshAgent that is not ready. Make sure the wolf stands on a baked NavMesh and the agent is enabled.",
            this
        );
    }

    private void UpdateAnimator()
    {
        if (!driveAnimator || animator == null)
            return;

        if (!string.IsNullOrWhiteSpace(moveBoolName))
        {
            bool moving = false;

            if (CanUseAgent() && !agent.isStopped)
            {
                moving = agent.velocity.sqrMagnitude > 0.01f || agent.pathPending;
            }

            animator.SetBool(moveBoolName, moving);
        }
    }

    private void OnOwnerDied(DamageableHealth deadHealth, DamageInfo killingDamage)
    {
        EnterDeadState();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        float attackEnter = 1.8f + attackEnterBuffer;
        float attackExit = 1.8f + attackExitBuffer;

        if (meleeAttack != null)
        {
            attackEnter = GetAttackEnterDistance();
            attackExit = GetAttackExitDistance();
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, loseInterestRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackEnter);

        Gizmos.color = new Color(1f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, attackExit);

        Vector3 gizmoHome = Application.isPlaying && homePositionInitialized ? homePosition : transform.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(gizmoHome, wanderRadius);

        if (useHomePositionAsWanderCenter)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(gizmoHome, 0.2f);
        }

        if (hasLastWanderDestination)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(lastWanderDestination, 0.35f);
            Gizmos.DrawLine(transform.position, lastWanderDestination);
        }
    }
}
