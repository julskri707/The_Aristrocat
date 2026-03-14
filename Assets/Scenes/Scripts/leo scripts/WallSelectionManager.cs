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

    public bool TryInsertPointAtScreenPosition(Vector2 screenPosition, MonoBehaviour preferredProviderBehaviour = null)
    {
        if (cam == null)
            return false;

        WallObject wall = null;
        MonoBehaviour providerBehaviour = preferredProviderBehaviour;

        if (providerBehaviour is Component providerComponent)
            wall = providerComponent.GetComponent<WallObject>();

        Ray ray = cam.ScreenPointToRay(screenPosition);

        if (wall == null && Physics.Raycast(ray, out RaycastHit hit, maxDistance, wallLayerMask, QueryTriggerInteraction.Ignore))
            wall = hit.collider.GetComponentInParent<WallObject>();

        if (wall == null && buildController != null)
            wall = buildController.SelectedWall;

        if (wall == null)
            return false;

        if (providerBehaviour == null)
            providerBehaviour = ResolveProvider(wall);

        if (providerBehaviour is not WallEditShape editShape)
            return false;

        Vector3 insertWorldPos = GetInsertWorldPosition(ray, wall);
        bool inserted = editShape.InsertFreeControlPointAtWorld(insertWorldPos);

        if (!inserted)
            return false;

        if (buildController != null)
            buildController.ForceSelectWall(wall);
        else if (overlay != null)
            overlay.SetTarget(editShape);

        if (overlay != null)
            overlay.RebuildOverlay();

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Inserted point on {wall.name}");

        return true;
    }

    Vector3 GetInsertWorldPosition(Ray ray, WallObject wall)
    {
        if (wall != null && wall.Points != null && wall.Points.Count >= 2)
        {
            float y = wall.Points[0].y;
            Plane plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            if (plane.Raycast(ray, out float enter))
                return ray.GetPoint(enter);
        }

        Plane fallbackPlane = new Plane(Vector3.up, Vector3.zero);
        if (fallbackPlane.Raycast(ray, out float fallbackEnter))
            return ray.GetPoint(fallbackEnter);

        return wall != null ? wall.transform.position : Vector3.zero;
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
