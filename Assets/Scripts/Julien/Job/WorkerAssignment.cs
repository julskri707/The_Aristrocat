using UnityEngine;

[DisallowMultipleComponent]
public class WorkerAssignment : MonoBehaviour
{
    [Tooltip("The field/worksite this worker is assigned to.")]
    public ResourceTickBehaviour assignedField;

    public void AssignTo(ResourceTickBehaviour field)
    {
        assignedField = field;
    }

    public void Unassign()
    {
        assignedField = null;
    }
}
