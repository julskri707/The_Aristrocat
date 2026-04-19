using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class NPCNavMeshMovementController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NavMeshAgent agent;

    [Header("Movement")]
    [SerializeField] private float repathDistanceThreshold = 0.35f;
    [SerializeField] private bool stopWhenNoTarget = true;

    [Header("Arrival")]
    [SerializeField] private float arrivalVelocityThreshold = 0.08f;
    [SerializeField] private float extraArrivalDistance = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugWarnings = true;

    private Transform currentTarget;
    private Vector3 lastRequestedDestination;
    private bool hasRequestedDestination;

    public NavMeshAgent Agent => agent;
    public Transform CurrentTarget => currentTarget;

    public bool HasTarget => currentTarget != null;

    public bool IsMoving
    {
        get
        {
            if (agent == null)
                return false;

            return !ReachedDestination() && agent.velocity.sqrMagnitude > 0.01f;
        }
    }

    private void Awake()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (agent == null && debugWarnings)
        {
            Debug.LogWarning($"[NPCNavMeshMovementController] Missing NavMeshAgent on {name}.", this);
        }

        NpcPhysicsMeshColliderSanitizer.DisableMeshCollidersUnderDynamicRigidbodies(transform);
    }

    public void SetTarget(Transform target)
    {
        if (agent == null)
            return;

        if (currentTarget == target)
        {
            if (currentTarget != null)
                UpdateDestinationIfNeeded(currentTarget.position);
            else if (stopWhenNoTarget)
                StopMovement();

            return;
        }

        currentTarget = target;

        if (currentTarget == null)
        {
            if (stopWhenNoTarget)
                StopMovement();

            if (debugLogs)
                Debug.Log($"[NPCNavMeshMovementController] {name} target cleared.", this);

            return;
        }

        ForceSetDestination(currentTarget.position);

        if (debugLogs)
            Debug.Log($"[NPCNavMeshMovementController] {name} target set to '{currentTarget.name}'.", this);
    }

    private void Update()
    {
        if (agent == null)
            return;

        if (currentTarget == null)
            return;

        UpdateDestinationIfNeeded(currentTarget.position);
    }

    public bool ReachedDestination()
    {
        if (agent == null)
            return false;

        if (!agent.enabled)
            return false;

        if (!agent.isOnNavMesh)
            return false;

        if (agent.pathPending)
            return false;

        float allowedDistance = agent.stoppingDistance + extraArrivalDistance;

        if (agent.remainingDistance > allowedDistance)
            return false;

        if (agent.hasPath && agent.velocity.sqrMagnitude > arrivalVelocityThreshold * arrivalVelocityThreshold)
            return false;

        return true;
    }

    public bool ReachedTarget(Transform target)
    {
        if (target == null)
            return false;

        if (currentTarget == target)
            return ReachedDestination();

        Vector3 a = transform.position;
        Vector3 b = target.position;
        a.y = 0f;
        b.y = 0f;

        float allowedDistance = (agent != null ? agent.stoppingDistance : 0.5f) + extraArrivalDistance;
        return (a - b).sqrMagnitude <= allowedDistance * allowedDistance;
    }

    public void StopMovement()
    {
        if (agent == null)
            return;

        if (!agent.enabled)
            return;

        if (!agent.isOnNavMesh)
            return;

        agent.ResetPath();
        hasRequestedDestination = false;
    }

    public void WarpToCurrentNavMesh()
    {
        if (agent == null)
            return;

        if (!agent.enabled)
            return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
        else if (debugWarnings)
        {
            Debug.LogWarning($"[NPCNavMeshMovementController] Could not find NavMesh near {name} to warp.", this);
        }
    }

    private void UpdateDestinationIfNeeded(Vector3 targetPosition)
    {
        if (agent == null)
            return;

        if (!agent.enabled)
            return;

        if (!agent.isOnNavMesh)
        {
            if (debugWarnings)
                Debug.LogWarning($"[NPCNavMeshMovementController] {name} agent is not on NavMesh.", this);
            return;
        }

        if (!hasRequestedDestination)
        {
            ForceSetDestination(targetPosition);
            return;
        }

        float sqr = (lastRequestedDestination - targetPosition).sqrMagnitude;
        if (sqr >= repathDistanceThreshold * repathDistanceThreshold)
        {
            ForceSetDestination(targetPosition);
        }
    }

    private void ForceSetDestination(Vector3 targetPosition)
    {
        if (agent == null)
            return;

        if (!agent.enabled)
            return;

        if (!agent.isOnNavMesh)
        {
            if (debugWarnings)
                Debug.LogWarning($"[NPCNavMeshMovementController] {name} agent is not on NavMesh, destination ignored.", this);
            return;
        }

        if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            lastRequestedDestination = hit.position;
            hasRequestedDestination = true;
        }
        else
        {
            if (debugWarnings)
                Debug.LogWarning($"[NPCNavMeshMovementController] Could not sample NavMesh near target for {name}.", this);
        }
    }
}