// WorkerAssignment.cs
// Unity 2022+
// - Assigns this worker to a ResourceTickBehaviour
// - Handles Register/Unregister on reassignment
// - No LayerMasks

using UnityEngine;

public class WorkerAssignment : MonoBehaviour
{
    public ResourceTickBehaviour assignedField;

    public void AssignTo(ResourceTickBehaviour newField)
    {
        if (assignedField == newField)
        {
            Debug.Log($"[WorkerAssignment] '{name}' already assigned to '{(newField != null ? newField.name : "NULL")}'. No change.");
            return;
        }

        // Unregister from previous assignment
        if (assignedField != null)
        {
            assignedField.UnregisterWorker(this);
        }

        // Assign new
        assignedField = newField;

        // Register to new assignment
        if (assignedField != null)
        {
            assignedField.RegisterWorker(this);
            Debug.Log($"[WorkerAssignment] '{name}' assigned to '{assignedField.name}'.");
        }
        else
        {
            Debug.Log($"[WorkerAssignment] '{name}' unassigned (assignedField = NULL).");
        }
    }

    private void OnDisable()
    {
        // Safety: ensure worker is not left registered if disabled/destroyed.
        if (assignedField != null)
            assignedField.UnregisterWorker(this);
    }

    private void OnDestroy()
    {
        if (assignedField != null)
            assignedField.UnregisterWorker(this);
    }
}
