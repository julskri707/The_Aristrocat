using UnityEngine;
using UnityEngine.AI;

public class WolfSpawnPoint : MonoBehaviour
{
    [Header("NavMesh")]
    [SerializeField] private float navMeshSampleDistance = 4f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private float gizmoRadius = 0.4f;

    private void OnValidate()
    {
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
        gizmoRadius = Mathf.Max(0.05f, gizmoRadius);
    }

    public bool TryGetSpawnPosition(out Vector3 worldPosition)
    {
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            worldPosition = hit.position;
            return true;
        }

        worldPosition = transform.position;
        return false;
    }

    public Quaternion GetSpawnRotation()
    {
        return transform.rotation;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.2f);
    }
}
