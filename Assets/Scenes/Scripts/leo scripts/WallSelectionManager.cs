using UnityEngine;
using UnityEngine.EventSystems;

public class WallSelectionManager : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public ControlPointOverlayManager overlay;

    [Header("Raycast")]
    public LayerMask wallLayerMask = ~0;  // par défaut: tout
    public float maxDistance = 500f;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (overlay == null) overlay = Object.FindFirstObjectByType<ControlPointOverlayManager>();
    }

    void Update()
    {
        // clic gauche
        if (!Input.GetMouseButtonDown(0)) return;

        // si on clique sur l'UI, on ignore (sinon ça désélectionne en drag)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (cam == null || overlay == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, wallLayerMask, QueryTriggerInteraction.Ignore))
        {
            // Cherche un WallSelectable sur l'objet touché (ou ses parents)
            var selectable = hit.collider.GetComponentInParent<WallSelectable>();
            if (selectable == null) return;

            // S'il n'a pas encore son provider, on tente de le trouver
            if (selectable.providerBehaviour == null)
                selectable.AutoFindProvider();

            if (selectable.providerBehaviour != null)
            {
                overlay.SetTarget(selectable.providerBehaviour);
            }
        }
    }
}
