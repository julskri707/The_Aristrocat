using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[DisallowMultipleComponent]
public class NPCMovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.8f;
    [SerializeField] private float acceleration = 14f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float stopDistance = 0.4f;
    [SerializeField] private float waypointReachDistance = 0.35f;

    [Header("Pathfinding")]
    [SerializeField] private NPCPathfindingGrid pathGrid;
    [SerializeField] private float repathInterval = 0.35f;
    [SerializeField] private float targetMoveRepathDistance = 0.75f;
    [SerializeField] private float maxDirectFallbackDistance = 4f;

    [Header("Anti-Stuck")]
    [SerializeField] private float progressSampleInterval = 0.35f;
    [SerializeField] private float minProgressDistance = 0.08f;
    [SerializeField] private float stuckRepathThreshold = 0.9f;
    [SerializeField] private float waypointMaxTime = 1.8f;

    [Header("Grounding")]
    [SerializeField] private float groundCheckDistance = 0.9f;
    [SerializeField] private float extraDownforce = 12f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawPathGizmos = true;

    private Rigidbody rb;
    private CapsuleCollider capsule;

    private Transform currentTarget;
    private Vector3 lastPlannedTargetPosition;

    private readonly List<Vector3> currentPath = new List<Vector3>(64);
    private int pathIndex = 0;

    private float repathTimer = 0f;
    private float waypointTimer = 0f;
    private float progressTimer = 0f;
    private float stuckTimer = 0f;
    private Vector3 progressReferencePosition;

    public Transform CurrentTarget => currentTarget;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        if (pathGrid == null)
            pathGrid = NPCPathfindingGrid.Instance;

        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        progressReferencePosition = transform.position;
    }

    public void SetTarget(Transform target)
    {
        if (currentTarget == target)
            return;

        currentTarget = target;
        ForceRepath();
    }

    private void Update()
    {
        if (pathGrid == null)
            pathGrid = NPCPathfindingGrid.Instance;

        if (currentTarget == null)
        {
            ClearPath();
            return;
        }

        if (pathGrid == null)
            return;

        repathTimer += Time.deltaTime;

        Vector3 targetPos = currentTarget.position;
        bool targetMoved = (targetPos - lastPlannedTargetPosition).sqrMagnitude >= targetMoveRepathDistance * targetMoveRepathDistance;
        bool needRepath = repathTimer >= repathInterval || targetMoved || currentPath.Count == 0;

        if (needRepath)
        {
            RebuildPath(targetPos);
        }
    }

    private void FixedUpdate()
    {
        if (currentTarget == null)
        {
            StopHorizontalMotion();
            return;
        }

        Vector3 currentPos = transform.position;
        Vector3 flatToFinal = currentTarget.position - currentPos;
        flatToFinal.y = 0f;

        if (flatToFinal.sqrMagnitude <= stopDistance * stopDistance)
        {
            ClearPath();
            StopHorizontalMotion();
            return;
        }

        waypointTimer += Time.fixedDeltaTime;
        progressTimer += Time.fixedDeltaTime;

        if (currentPath.Count == 0 || pathIndex >= currentPath.Count)
        {
            if (flatToFinal.magnitude <= maxDirectFallbackDistance)
            {
                MoveTowards(currentTarget.position);
            }
            else
            {
                StopHorizontalMotion();
            }

            UpdateStuckState();
            return;
        }

        TrySkipToFurtherWaypoint();

        if (pathIndex >= currentPath.Count)
        {
            StopHorizontalMotion();
            return;
        }

        Vector3 waypoint = currentPath[pathIndex];
        Vector3 flatToWaypoint = waypoint - currentPos;
        flatToWaypoint.y = 0f;

        if (flatToWaypoint.sqrMagnitude <= waypointReachDistance * waypointReachDistance)
        {
            pathIndex++;
            waypointTimer = 0f;

            if (pathIndex >= currentPath.Count)
            {
                StopHorizontalMotion();
                return;
            }

            waypoint = currentPath[pathIndex];
        }

        if (waypointTimer >= waypointMaxTime)
        {
            if (debugLogs)
                Debug.LogWarning($"[NPCMovementController] {name} waypoint timeout -> repath", this);

            ForceRepath();
            return;
        }

        MoveTowards(waypoint);
        UpdateStuckState();
    }

    private void MoveTowards(Vector3 targetWorld)
    {
        Vector3 position = transform.position;
        Vector3 toTarget = targetWorld - position;
        toTarget.y = 0f;

        if (TryGetGroundNormal(out Vector3 groundNormal))
            toTarget = Vector3.ProjectOnPlane(toTarget, groundNormal);

        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            StopHorizontalMotion();
            return;
        }

        Vector3 desiredDir = toTarget.normalized;
        Vector3 desiredVelocity = desiredDir * moveSpeed;

        Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 newHorizontal = Vector3.MoveTowards(currentHorizontal, desiredVelocity, acceleration * Time.fixedDeltaTime);

        rb.linearVelocity = new Vector3(newHorizontal.x, rb.linearVelocity.y, newHorizontal.z);

        if (desiredDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(desiredDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }

        if (IsGrounded())
        {
            rb.AddForce(Vector3.down * extraDownforce, ForceMode.Acceleration);
        }
    }

    private void UpdateStuckState()
    {
        if (progressTimer < progressSampleInterval)
            return;

        Vector3 now = transform.position;
        Vector3 a = progressReferencePosition;
        Vector3 b = now;
        a.y = 0f;
        b.y = 0f;

        float progress = Vector3.Distance(a, b);

        if (progress < minProgressDistance)
        {
            stuckTimer += progressTimer;

            if (stuckTimer >= stuckRepathThreshold)
            {
                if (debugLogs)
                    Debug.LogWarning($"[NPCMovementController] {name} stuck detected -> repath", this);

                ForceRepath();
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        progressReferencePosition = now;
        progressTimer = 0f;
    }

    private void TrySkipToFurtherWaypoint()
    {
        if (currentPath.Count == 0 || pathIndex >= currentPath.Count)
            return;

        int bestIndex = pathIndex;

        for (int i = currentPath.Count - 1; i > pathIndex; i--)
        {
            if (HasLineOfSight(currentPath[i]))
            {
                bestIndex = i;
                break;
            }
        }

        if (bestIndex != pathIndex)
        {
            pathIndex = bestIndex;
            waypointTimer = 0f;
        }
    }

    private bool HasLineOfSight(Vector3 targetWorld)
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.4f, capsule.radius);
        Vector3 to = targetWorld - origin;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist <= 0.01f)
            return true;

        Vector3 dir = to.normalized;
        RaycastHit[] hits = Physics.SphereCastAll(origin, capsule.radius * 0.85f, dir, dist, ~0, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return true;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (ShouldIgnoreCollider(c))
                continue;

            return false;
        }

        return true;
    }

    private bool ShouldIgnoreCollider(Collider c)
    {
        if (c == null || c.isTrigger)
            return true;

        if (c.GetComponentInParent<GroundMarker>() != null)
            return true;

        if (c.GetComponentInParent<FieldArea>() != null)
            return true;

        if (c.GetComponentInParent<NPCMovementController>() != null)
            return true;

        if (c.GetComponentInParent<NPCDecisionBrain>() != null)
            return true;

        if (c.GetComponentInParent<NPCBrain>() != null)
            return true;

        return false;
    }

    private void RebuildPath(Vector3 targetPos)
    {
        if (pathGrid == null)
            return;

        if (pathGrid.TryFindPath(transform.position, targetPos, currentPath))
        {
            pathIndex = 0;
            waypointTimer = 0f;
            repathTimer = 0f;
            lastPlannedTargetPosition = targetPos;
            progressReferencePosition = transform.position;
            progressTimer = 0f;
            stuckTimer = 0f;

            if (debugLogs)
                Debug.Log($"[NPCMovementController] {name} path rebuilt, waypoints={currentPath.Count}", this);
        }
        else
        {
            currentPath.Clear();
            pathIndex = 0;
            repathTimer = 0f;

            if (debugLogs)
                Debug.LogWarning($"[NPCMovementController] {name} no path found", this);
        }
    }

    private void ForceRepath()
    {
        repathTimer = repathInterval;
        waypointTimer = 0f;
        progressTimer = 0f;
        stuckTimer = 0f;
        progressReferencePosition = transform.position;
    }

    private void ClearPath()
    {
        currentPath.Clear();
        pathIndex = 0;
        waypointTimer = 0f;
        progressTimer = 0f;
        stuckTimer = 0f;
        progressReferencePosition = transform.position;
    }

    private void StopHorizontalMotion()
    {
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(0f, v.y, 0f);
    }

    private bool IsGrounded()
    {
        return TryGetGroundNormal(out _);
    }

    private bool TryGetGroundNormal(out Vector3 normal)
    {
        normal = Vector3.up;

        Vector3 origin = transform.position + Vector3.up * 0.2f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            GroundMarker marker = hit.collider.GetComponentInParent<GroundMarker>();
            if (marker == null)
                continue;

            normal = hit.normal;
            return true;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos || currentPath == null || currentPath.Count == 0)
            return;

        Gizmos.color = Color.yellow;

        Vector3 prev = transform.position;
        for (int i = pathIndex; i < currentPath.Count; i++)
        {
            Vector3 p = currentPath[i];
            Gizmos.DrawLine(prev + Vector3.up * 0.1f, p + Vector3.up * 0.1f);
            Gizmos.DrawSphere(p + Vector3.up * 0.1f, 0.08f);
            prev = p;
        }
    }
}