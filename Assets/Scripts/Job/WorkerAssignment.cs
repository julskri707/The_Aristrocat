using UnityEngine;

[DisallowMultipleComponent]
public class WorkerAssignment : MonoBehaviour
{
    public ResourceTickBehaviour assignedSite;

    public bool autoRegister = true;

    private void OnEnable()
    {
        if (!autoRegister) return;
        JobManager.Instance?.RegisterWorker(this);
    }

    private void OnDisable()
    {
        if (!autoRegister) return;
        JobManager.Instance?.UnregisterWorker(this);
    }

    public void AssignTo(ResourceTickBehaviour site) => assignedSite = site;
    public void Unassign() => assignedSite = null;
}
