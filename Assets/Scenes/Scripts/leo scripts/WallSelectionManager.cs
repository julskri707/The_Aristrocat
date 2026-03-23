using UnityEngine;
using UnityEngine.EventSystems;

public class WallSelectionManager : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public ControlPointOverlayManager overlay;
    public WallBuildController buildController;
    public WallContextMenuUI contextMenu;
    public WallUndoManager undoManager;

    [Header("Raycast")]
    public LayerMask wallLayerMask = ~0;
    public float maxDistance = 500f;

    [Header("Input")]
    public bool selectOnLeftClick = true;
    public bool openContextMenuOnRightClick = true;
    public bool closeContextMenuOnEmptyLeftClick = true;
    public bool clearSelectionOnEmptyLeftClick = false;

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

        if (contextMenu == null)
            contextMenu = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
    }

    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (selectOnLeftClick && Input.GetMouseButtonDown(0))
            HandleLeftClick(Input.mousePosition);

        if (openContextMenuOnRightClick && Input.GetMouseButtonDown(1))
            HandleRightClick(Input.mousePosition);
    }

    void HandleLeftClick(Vector2 screenPosition)
    {
        if (TrySelectWallAtScreenPosition(screenPosition, out _, out _))
        {
            if (contextMenu != null && contextMenu.IsOpen)
                contextMenu.Close();

            return;
        }

        if (closeContextMenuOnEmptyLeftClick && contextMenu != null && contextMenu.IsOpen)
            contextMenu.Close();

        if (clearSelectionOnEmptyLeftClick && buildController != null)
            buildController.ForceSelectWall(null);
    }

    void HandleRightClick(Vector2 screenPosition)
    {
        bool opened = TryOpenContextMenuAtScreenPosition(screenPosition);

        if (!opened && contextMenu != null && contextMenu.IsOpen)
            contextMenu.Close();
    }

    public bool TrySelectWallAtScreenPosition(Vector2 screenPosition, out WallObject wall, out MonoBehaviour providerBehaviour)
    {
        wall = null;
        providerBehaviour = null;

        if (cam == null)
            return false;

        Ray ray = cam.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, wallLayerMask, QueryTriggerInteraction.Ignore))
            return false;

        wall = hit.collider.GetComponentInParent<WallObject>();
        if (wall == null)
            return false;

        providerBehaviour = ResolveProvider(wall);
        if (providerBehaviour == null)
        {
            if (logDebug)
                Debug.LogWarning($"[WallSelectionManager] No provider found on {wall.name}");
            return false;
        }

        SelectWallInternal(wall, providerBehaviour);
        return true;
    }

    public bool TryOpenContextMenuAtScreenPosition(Vector2 screenPosition, MonoBehaviour preferredProviderBehaviour = null)
    {
        if (contextMenu == null)
            contextMenu = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);

        if (contextMenu == null)
            return false;

        WallObject wall = null;
        MonoBehaviour providerBehaviour = preferredProviderBehaviour;

        if (providerBehaviour is Component providerComponent)
        {
            wall = providerComponent.GetComponent<WallObject>();

            if (wall != null)
            {
                if (providerBehaviour == null)
                    providerBehaviour = ResolveProvider(wall);

                if (providerBehaviour == null)
                    return false;

                SelectWallInternal(wall, providerBehaviour);
                contextMenu.OpenForWall(wall, screenPosition);

                if (logDebug)
                    Debug.Log($"[WallSelectionManager] Opened context menu from provider on {wall.name}");

                return true;
            }
        }

        if (!TrySelectWallAtScreenPosition(screenPosition, out wall, out providerBehaviour))
            return false;

        if (wall == null)
            return false;

        contextMenu.OpenForWall(wall, screenPosition);

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Opened context menu on wall {wall.name}");

        return true;
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

        Vector3 insertWorldPos = GetInsertWorldPosition(ray, wall);
        return TryInsertPointAtWorldPosition(insertWorldPos, providerBehaviour ?? ResolveProvider(wall));
    }

    public bool TryInsertPointAtWorldPosition(Vector3 worldPosition, MonoBehaviour preferredProviderBehaviour = null)
    {
        WallObject wall = null;
        MonoBehaviour providerBehaviour = preferredProviderBehaviour;

        if (providerBehaviour is Component providerComponent)
            wall = providerComponent.GetComponent<WallObject>();

        if (wall == null && buildController != null)
            wall = buildController.SelectedWall;

        if (wall == null)
            return false;

        if (providerBehaviour == null)
            providerBehaviour = ResolveProvider(wall);

        if (providerBehaviour is not WallEditShape editShape)
            return false;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();

        if (undoManager != null)
            undoManager.RecordSnapshot("Insert Control Point");

        bool inserted = editShape.InsertFreeControlPointAtWorld(worldPosition);
        if (!inserted)
            return false;

        SelectWallInternal(wall, editShape);

        if (overlay != null)
            overlay.RebuildOverlay();

        if (contextMenu != null && contextMenu.IsOpen)
            contextMenu.RefreshCurrentWall();

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Inserted point on {wall.name} at {worldPosition}");

        return true;
    }

    void SelectWallInternal(WallObject wall, MonoBehaviour providerBehaviour)
    {
        if (wall == null || providerBehaviour == null)
            return;

        if (overlay != null)
            overlay.SetTarget(providerBehaviour);

        if (buildController != null)
            buildController.ForceSelectWall(wall);

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Selected {wall.name} with {providerBehaviour.GetType().Name}");
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
