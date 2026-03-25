using UnityEngine;
using UnityEngine.AI;

public class WolfSpawnArea : MonoBehaviour
{
    public enum SpawnShape
    {
        Box = 0,
        Circle = 1
    }

    [Header("Area")]
    [SerializeField] private SpawnShape shape = SpawnShape.Box;
    [SerializeField] private Vector3 boxSize = new Vector3(10f, 2f, 10f);
    [SerializeField] private float circleRadius = 8f;

    [Header("NavMesh")]
    [SerializeField] private float navMeshSampleDistance = 6f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private void OnValidate()
    {
        boxSize.x = Mathf.Max(0.1f, boxSize.x);
        boxSize.y = Mathf.Max(0.1f, boxSize.y);
        boxSize.z = Mathf.Max(0.1f, boxSize.z);
        circleRadius = Mathf.Max(0.1f, circleRadius);
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
    }

    public bool TryGetSpawnPosition(out Vector3 worldPosition)
    {
        Vector3 candidate = GetRandomRawPoint();

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            worldPosition = hit.position;
            return true;
        }

        worldPosition = transform.position;
        return false;
    }

    private Vector3 GetRandomRawPoint()
    {
        if (shape == SpawnShape.Circle)
        {
            Vector2 random2D = Random.insideUnitCircle * circleRadius;
            Vector3 local = new Vector3(random2D.x, 0f, random2D.y);
            return transform.TransformPoint(local);
        }
        else
        {
            Vector3 half = boxSize * 0.5f;

            Vector3 local = new Vector3(
                Random.Range(-half.x, half.x),
                Random.Range(-half.y, half.y),
                Random.Range(-half.z, half.z)
            );

            return transform.TransformPoint(local);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (shape == SpawnShape.Circle)
        {
            Gizmos.DrawWireSphere(Vector3.zero, circleRadius);
        }
        else
        {
            Gizmos.DrawWireCube(Vector3.zero, boxSize);
        }

        Gizmos.matrix = oldMatrix;
    }
}
