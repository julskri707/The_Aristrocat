using UnityEngine;
using UnityEngine.EventSystems;

public class WallSelectionManager : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public ControlPointOverlayManager overlay;
    public WallBuildController buildController;

    [Header("Raycast")]
    public LayerMask wallLayerMask = ~0;
    public float maxDistance = 500f;

    [Header("Debug")]
    public bool logDebug = false;

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (cam == null || overlay == null)
            return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, wallLayerMask, QueryTriggerInteraction.Ignore))
            return;

        var wall = hit.collider.GetComponentInParent<WallObject>();
        if (wall == null)
            return;

        MonoBehaviour provider = ResolveProvider(wall);
        if (provider == null)
        {
            if (logDebug)
                Debug.LogWarning($"[WallSelectionManager] No provider found on {wall.name}");
            return;
        }

        overlay.SetTarget(provider);

        if (buildController != null)
            buildController.ForceSelectWall(wall);

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Selected {wall.name} with {provider.GetType().Name}");
    }

    MonoBehaviour ResolveProvider(WallObject wall)
    {
        if (wall == null)
            return null;

        var editShape = wall.GetComponent<WallEditShape>();
        if (editShape != null)
            return editShape;

        var selectable = wall.GetComponent<WallSelectable>();
        if (selectable != null)
        {
            if (selectable.providerBehaviour == null)
                selectable.AutoFindProvider();

            if (selectable.providerBehaviour != null)
                return selectable.providerBehaviour;
        }

        var monos = wall.GetComponents<MonoBehaviour>();
        for (int i = 0; i < monos.Length; i++)
        {
            if (monos[i] is IControlPointProvider)
                return monos[i];
        }

        return null;
    }
}