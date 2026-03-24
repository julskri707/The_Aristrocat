using UnityEngine;

[DisallowMultipleComponent]
public class NPCBrain : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private NPCDecisionBrain decisionBrain;
    [SerializeField] private NPCNavMeshMovementController movement;
    [SerializeField] private WorkerAssignment workerAssignment;

    [Header("Debug")]
    [SerializeField] private bool debugWarnings = true;

    public NPCDecisionBrain Decision => decisionBrain;
    public NPCNavMeshMovementController Movement => movement;
    public WorkerAssignment WorkerAssignment => workerAssignment;

    private void Awake()
    {
        if (decisionBrain == null)
            decisionBrain = GetComponent<NPCDecisionBrain>();

        if (movement == null)
            movement = GetComponent<NPCNavMeshMovementController>();

        if (workerAssignment == null)
            workerAssignment = GetComponent<WorkerAssignment>();

        if (decisionBrain == null && debugWarnings)
            Debug.LogWarning($"[NPCBrain] Missing NPCDecisionBrain on {name}.", this);

        if (movement == null && debugWarnings)
            Debug.LogWarning($"[NPCBrain] Missing NPCNavMeshMovementController on {name}.", this);
    }

    private void Update()
    {
        if (movement == null || decisionBrain == null)
            return;

        movement.SetTarget(decisionBrain.CurrentTarget);
    }
}