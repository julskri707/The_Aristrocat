// GroundMarker.cs
// Purpose: Marks ground objects so other systems can identify valid raycast targets
// without using LayerMasks. Optional debug visualization/logging.
// Unity 2022+ compatible.

using UnityEngine;

public class GroundMarker : MonoBehaviour
{
    [Tooltip("If enabled, systems that raycast against GroundMarker can use this flag to decide whether to log/debug hits.")]
    public bool debugGroundHits = false;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!debugGroundHits) return;

        // Simple visual cue in the editor: a small marker at the object's position.
        Gizmos.DrawWireSphere(transform.position, 0.15f);

        // If the object has a collider, draw its bounds for quick inspection.
        var col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
#endif
}
