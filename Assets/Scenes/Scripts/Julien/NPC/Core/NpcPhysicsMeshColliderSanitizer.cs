using UnityEngine;

/// <summary>
/// Désactive les MeshColliders sous chaque Rigidbody dynamique du sous-arbre.
/// Un Rigidbody non cinématique + MeshCollider concave (FBX) déclenche l’erreur PhysX sur la console.
/// </summary>
public static class NpcPhysicsMeshColliderSanitizer
{
    public static void DisableMeshCollidersUnderDynamicRigidbodies(Transform root)
    {
        if (root == null)
            return;

        foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null || rb.isKinematic)
                continue;

            foreach (MeshCollider mc in rb.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc != null)
                    mc.enabled = false;
            }
        }
    }
}
