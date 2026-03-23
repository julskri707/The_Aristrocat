using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class NPCAnimatorBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Transform visualRootToLock;

    [Header("Animator")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private float speedDampTime = 0.08f;
    [SerializeField] private bool normalizeByAgentSpeed = true;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float minSpeedValue = 0f;
    [SerializeField] private float maxSpeedValue = 1.25f;
    [SerializeField] private float movementDeadZone = 0.03f;
    [SerializeField] private bool useDesiredVelocityIfAvailable = true;

    [Header("Safety")]
    [SerializeField] private bool forceDisableRootMotion = true;
    [SerializeField] private bool lockVisualRootLocalTransform = true;

    [Header("Debug")]
    [SerializeField] private bool debugWarnings = true;

    private int speedHash;

    private Vector3 cachedVisualLocalPosition;
    private Quaternion cachedVisualLocalRotation;
    private Vector3 cachedVisualLocalScale;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (agent == null)
            agent = GetComponentInParent<NavMeshAgent>();

        if (visualRootToLock == null && animator != null)
            visualRootToLock = animator.transform;

        speedHash = Animator.StringToHash(speedParameter);

        CacheVisualLocalTransform();
        ApplyRootMotionSafety();

        if (animator == null && debugWarnings)
            Debug.LogWarning($"[NPCAnimatorBridge] Missing Animator on {name}.", this);

        if (agent == null && debugWarnings)
            Debug.LogWarning($"[NPCAnimatorBridge] Missing NavMeshAgent on {name}.", this);
    }

    private void OnEnable()
    {
        ApplyRootMotionSafety();
        CacheVisualLocalTransform();
    }

    private void Update()
    {
        if (animator == null)
            return;

        ApplyRootMotionSafety();

        float speedValue = 0f;

        if (agent != null)
        {
            Vector3 horizontalVelocity = GetPreferredHorizontalVelocity();
            float worldSpeed = horizontalVelocity.magnitude;

            if (worldSpeed <= movementDeadZone)
            {
                speedValue = 0f;
            }
            else if (normalizeByAgentSpeed)
            {
                float referenceSpeed = Mathf.Max(0.01f, agent.speed);
                speedValue = (worldSpeed / referenceSpeed) * speedMultiplier;
            }
            else
            {
                speedValue = worldSpeed * speedMultiplier;
            }
        }

        speedValue = Mathf.Clamp(speedValue, minSpeedValue, maxSpeedValue);
        animator.SetFloat(speedHash, speedValue, speedDampTime, Time.deltaTime);
    }

    private void LateUpdate()
    {
        ApplyRootMotionSafety();

        if (!lockVisualRootLocalTransform)
            return;

        if (visualRootToLock == null)
            return;

        if (visualRootToLock.parent == null)
            return;

        visualRootToLock.localPosition = cachedVisualLocalPosition;
        visualRootToLock.localRotation = cachedVisualLocalRotation;
        visualRootToLock.localScale = cachedVisualLocalScale;
    }

    private Vector3 GetPreferredHorizontalVelocity()
    {
        if (agent == null)
            return Vector3.zero;

        Vector3 velocity = agent.velocity;

        if (useDesiredVelocityIfAvailable)
        {
            Vector3 desired = agent.desiredVelocity;
            desired.y = 0f;

            if (desired.sqrMagnitude > velocity.sqrMagnitude)
                velocity = desired;
        }

        velocity.y = 0f;
        return velocity;
    }

    private void ApplyRootMotionSafety()
    {
        if (!forceDisableRootMotion)
            return;

        if (animator == null)
            return;

        if (animator.applyRootMotion)
            animator.applyRootMotion = false;
    }

    private void CacheVisualLocalTransform()
    {
        if (visualRootToLock == null)
            return;

        cachedVisualLocalPosition = visualRootToLock.localPosition;
        cachedVisualLocalRotation = visualRootToLock.localRotation;
        cachedVisualLocalScale = visualRootToLock.localScale;
    }
}