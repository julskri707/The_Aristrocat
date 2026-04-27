using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class SwordMeleeHitbox : MonoBehaviour
{
    private BoxCollider box;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
    }

    public bool TryGetWorldBox(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        center = default;
        halfExtents = default;
        rotation = default;

        if (box == null)
            box = GetComponent<BoxCollider>();

        if (box == null)
            return false;

        Bounds b = box.bounds;
        center = b.center;
        halfExtents = b.extents;
        rotation = transform.rotation;
        return true;
    }
}
