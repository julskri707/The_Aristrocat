using UnityEngine;

[DisallowMultipleComponent]
public class JobSite : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform workPoint;
    [SerializeField] private ResourceTickBehaviour resourceBehaviour;
    [SerializeField, Min(1)] private int maxWorkers = 1;

    [Header("Debug")]
    [SerializeField] private bool debugWarnings = true;

    public Transform WorkPoint => workPoint != null ? workPoint : transform;
    public ResourceTickBehaviour ResourceBehaviour => resourceBehaviour;
    public int MaxWorkers => Mathf.Max(1, maxWorkers);

    private void OnEnable()
    {
        SiteRegistry.Instance?.RegisterJobSite(this);
        JobAssigner.Instance?.RegisterJobSite(this);
    }

    private void OnDisable()
    {
        SiteRegistry.Instance?.UnregisterJobSite(this);
        JobAssigner.Instance?.UnregisterJobSite(this);
    }

    private void Awake()
    {
        if (resourceBehaviour == null && debugWarnings)
        {
            Debug.LogWarning($"[JobSite] '{name}' has no ResourceTickBehaviour assigned.", this);
        }
    }

    public int GetCurrentWorkerCount()
    {
        if (resourceBehaviour == null)
            return 0;

        return resourceBehaviour.GetWorkerCount();
    }

    public bool HasFreeSlot()
    {
        if (resourceBehaviour == null)
            return false;

        return GetCurrentWorkerCount() < MaxWorkers;
    }

    public bool HasFreeSlot(WorkerAssignment worker)
    {
        if (worker != null && resourceBehaviour != null && worker.assignedField == resourceBehaviour)
            return true;

        return HasFreeSlot();
    }

    public bool IsAssignedWorker(WorkerAssignment worker)
    {
        if (worker == null || resourceBehaviour == null)
            return false;

        return worker.assignedField == resourceBehaviour;
    }

    public Vector3 GetWorkPosition()
    {
        return WorkPoint.position;
    }
}