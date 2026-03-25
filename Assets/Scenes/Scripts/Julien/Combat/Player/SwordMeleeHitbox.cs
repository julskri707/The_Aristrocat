using UnityEngine;

public class SwordMeleeHitbox : MonoBehaviour
{
    [Header("Shape")]
    [SerializeField] private BoxCollider boxCollider;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    public BoxCollider BoxCollider => boxCollider;

    private void Awake()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
        }

        if (boxCollider == null)
        {
            Debug.LogWarning($"[{nameof(SwordMeleeHitbox)}] No BoxCollider assigned on '{name}'.", this);
            return;
        }

        if (!boxCollider.isTrigger)
        {
            Debug.LogWarning($"[{nameof(SwordMeleeHitbox)}] BoxCollider on '{name}' should usually be Is Trigger = true.", this);
        }
    }

    private void OnValidate()
    {
        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
        }
    }

    public bool TryGetWorldBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = Vector3.zero;
        halfExtents = Vector3.zero;
        rotation = Quaternion.identity;

        if (boxCollider == null)
        {
            Debug.LogWarning($"[{nameof(SwordMeleeHitbox)}] Missing BoxCollider on '{name}'.", this);
            return false;
        }

        Transform t = boxCollider.transform;

        center = t.TransformPoint(boxCollider.center);
        rotation = t.rotation;

        Vector3 scaledSize = Vector3.Scale(boxCollider.size, AbsVector(t.lossyScale));
        halfExtents = scaledSize * 0.5f;

        halfExtents.x = Mathf.Max(0.001f, halfExtents.x);
        halfExtents.y = Mathf.Max(0.001f, halfExtents.y);
        halfExtents.z = Mathf.Max(0.001f, halfExtents.z);

        return true;
    }

    private Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z)
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        if (boxCollider == null)
        {
            boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
                return;
        }

        if (!TryGetWorldBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation))
            return;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);

        Gizmos.matrix = oldMatrix;
    }
}
