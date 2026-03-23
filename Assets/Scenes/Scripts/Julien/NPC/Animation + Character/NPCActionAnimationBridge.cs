using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class NPCActionAnimationBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;

    [Header("Animator Parameters")]
    [SerializeField] private string sleepingBool = "IsSleeping";
    [SerializeField] private string eatingBool = "IsEating";
    [SerializeField] private string socializingBool = "IsSocializing";
    [SerializeField] private string workingBool = "IsWorking";
    [SerializeField] private string panickingBool = "IsPanicking";

    [Header("Snap")]
    [SerializeField] private bool snapToAnchorPosition = true;
    [SerializeField] private bool snapToAnchorRotation = true;
    [SerializeField] private bool keepSnappedWhileActionActive = true;

    [Header("Debug")]
    [SerializeField] private bool debugWarnings = true;

    private NPCActionType activePoseAction = NPCActionType.None;
    private Transform activeAnchor;
    private bool poseActive;

    private int sleepingHash;
    private int eatingHash;
    private int socializingHash;
    private int workingHash;
    private int panickingHash;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        sleepingHash = Animator.StringToHash(sleepingBool);
        eatingHash = Animator.StringToHash(eatingBool);
        socializingHash = Animator.StringToHash(socializingBool);
        workingHash = Animator.StringToHash(workingBool);
        panickingHash = Animator.StringToHash(panickingBool);

        if (animator == null && debugWarnings)
            Debug.LogWarning($"[NPCActionAnimationBridge] Missing Animator on {name}.", this);

        if (agent == null && debugWarnings)
            Debug.LogWarning($"[NPCActionAnimationBridge] Missing NavMeshAgent on {name}.", this);
    }

    private void LateUpdate()
    {
        if (!poseActive || !keepSnappedWhileActionActive || activeAnchor == null)
            return;

        SnapToAnchor(activeAnchor);
    }

    public void BeginPose(NPCActionType actionType, Transform anchor)
    {
        if (animator == null)
            return;

        activePoseAction = actionType;
        activeAnchor = anchor;
        poseActive = true;

        ResetAllActionBools();
        SetActionBool(actionType, true);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;

        if (activeAnchor != null)
            SnapToAnchor(activeAnchor);
    }

    public void EndPose(NPCActionType actionType)
    {
        if (!poseActive)
            return;

        if (activePoseAction != actionType)
            return;

        ClearPose();
    }

    public void ClearPose()
    {
        poseActive = false;
        activePoseAction = NPCActionType.None;
        activeAnchor = null;

        ResetAllActionBools();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = false;
    }

    public void TeleportToAnchor(Transform anchor)
    {
        if (anchor == null)
            return;

        SnapToAnchor(anchor);
    }

    private void SnapToAnchor(Transform anchor)
    {
        if (anchor == null)
            return;

        if (snapToAnchorPosition)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.Warp(anchor.position);
            else
                transform.position = anchor.position;
        }

        if (snapToAnchorRotation)
            transform.rotation = anchor.rotation;
    }

    private void ResetAllActionBools()
    {
        if (animator == null)
            return;

        animator.SetBool(sleepingHash, false);
        animator.SetBool(eatingHash, false);
        animator.SetBool(socializingHash, false);
        animator.SetBool(workingHash, false);
        animator.SetBool(panickingHash, false);
    }

    private void SetActionBool(NPCActionType actionType, bool value)
    {
        if (animator == null)
            return;

        switch (actionType)
        {
            case NPCActionType.Sleep:
                animator.SetBool(sleepingHash, value);
                break;

            case NPCActionType.Eat:
                animator.SetBool(eatingHash, value);
                break;

            case NPCActionType.Socialize:
                animator.SetBool(socializingHash, value);
                break;

            case NPCActionType.Work:
                animator.SetBool(workingHash, value);
                break;

            case NPCActionType.Panic:
                animator.SetBool(panickingHash, value);
                break;
        }
    }
}