using UnityEngine;
using UnityEngine.EventSystems;

public class WallSelectionManager : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public ControlPointOverlayManager overlay;
    public WallBuildController buildController;
    public WallContextMenuUI contextMenu;
    public LotBuildMenuUI lotMenu;
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
        if (TrySelectCatalogPlacedOverlayAtScreen(screenPosition))
        {
            if (contextMenu != null && contextMenu.IsOpen)
                contextMenu.Close();

            if (lotMenu != null && lotMenu.IsOpen)
                lotMenu.Close();

            return;
        }

        if (TrySelectWallAtScreenPosition(screenPosition, out _, out _))
        {
            if (contextMenu != null && contextMenu.IsOpen)
                contextMenu.Close();

            if (lotMenu != null && lotMenu.IsOpen)
                lotMenu.Close();

            return;
        }

        if (closeContextMenuOnEmptyLeftClick)
        {
            if (contextMenu != null && contextMenu.IsOpen)
                contextMenu.Close();

            if (lotMenu != null && lotMenu.IsOpen)
                lotMenu.Close();
        }

        if (clearSelectionOnEmptyLeftClick && buildController != null)
            buildController.ForceSelectWall(null);
    }

    bool TrySelectCatalogPlacedOverlayAtScreen(Vector2 screenPosition)
    {
        if (cam == null || buildController == null)
            return false;

        Ray ray = cam.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, wallLayerMask, QueryTriggerInteraction.Ignore))
            return false;

        PlacedStairManipulator stair = hit.collider.GetComponentInParent<PlacedStairManipulator>();
        if (stair != null)
        {
            buildController.ForceSelectOverlayOnly(stair);
            return true;
        }

        CatalogPlacedObjectDraggable placed = hit.collider.GetComponentInParent<CatalogPlacedObjectDraggable>();
        if (placed != null)
        {
            buildController.ForceSelectOverlayOnly(placed);
            return true;
        }

        PlacedWallOpeningManipulator opening = hit.collider.GetComponentInParent<PlacedWallOpeningManipulator>();
        if (opening != null)
        {
            buildController.ForceSelectOverlayOnly(opening);
            return true;
        }

        return false;
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

        HouseRoofControlPointProvider roofPick =
            hit.collider.GetComponentInParent<HouseRoofControlPointProvider>();
        providerBehaviour = roofPick != null ? roofPick : ResolveProvider(wall);
        if (providerBehaviour == null)
        {
            if (logDebug)
                Debug.LogWarning($"[WallSelectionManager] No provider found on {wall.name}");
            return false;
        }

        SelectWallInternal(wall, providerBehaviour, hit.point);
        return true;
    }

    public bool TryOpenContextMenuAtScreenPosition(Vector2 screenPosition, MonoBehaviour preferredProviderBehaviour = null)
    {
        if (contextMenu == null)
            contextMenu = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);

        if (contextMenu == null)
            return false;

        if (lotMenu == null)
            lotMenu = FindFirstObjectByType<LotBuildMenuUI>(FindObjectsInactive.Include);

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
                if (lotMenu != null && lotMenu.IsOpen)
                    lotMenu.Close();

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

        if (lotMenu != null && lotMenu.IsOpen)
            lotMenu.Close();

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

        Vector3 insertPos = SnapInsertWorldXZBeforeWallInsert(editShape, worldPosition);
#region agent log
        DebugSessionAgentLog.Write(
            "H1",
            "WallSelectionManager.TryInsertPointAtWorldPosition",
            "after_snap",
            "{\"wx\":" + worldPosition.x.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"wz\":" + worldPosition.z.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"sx\":" + insertPos.x.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"sz\":" + insertPos.z.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
#endregion

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();

        if (undoManager != null)
            undoManager.RecordSnapshot("Insert Control Point");

        bool inserted = editShape.InsertFreeControlPointAtWorld(insertPos);
#region agent log
        DebugSessionAgentLog.Write(
            "H3",
            "WallSelectionManager.TryInsertPointAtWorldPosition",
            inserted ? "insert_ok" : "insert_failed",
            "{\"inserted\":" + (inserted ? "true" : "false") + "}");
#endregion
        if (!inserted)
            return false;

        SelectWallInternal(wall, editShape, insertPos);

        if (contextMenu != null && contextMenu.IsOpen)
            contextMenu.RefreshCurrentWall();

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Inserted point on {wall.name} at {insertPos}");

        return true;
    }

    void SelectWallInternal(WallObject wall, MonoBehaviour providerBehaviour, Vector3? envelopeClickHitWorld = null)
    {
        if (wall == null || providerBehaviour == null)
            return;

        MonoBehaviour overlayOverride = providerBehaviour is HouseRoofControlPointProvider r ? r : null;

        if (buildController != null)
            buildController.ForceSelectWall(wall, envelopeClickHitWorld, null, overlayOverride);

        if (logDebug)
            Debug.Log($"[WallSelectionManager] Selected {wall.name} with {providerBehaviour.GetType().Name}");
    }

    /// <summary>
    /// Même référentiel que les poignées : grille 9 pts / centre feuille avant insert (meilleur choix d’arête).
    /// </summary>
    Vector3 SnapInsertWorldXZBeforeWallInsert(WallEditShape editShape, Vector3 worldPosition)
    {
        WallDrawInput di = null;
        if (buildController != null && buildController.drawInput != null)
            di = buildController.drawInput;
        if (di == null)
        {
            WallBuildController bc = FindFirstObjectByType<WallBuildController>(FindObjectsInactive.Include);
            if (bc != null && bc.drawInput != null)
                di = bc.drawInput;
        }

        if (di == null)
            di = FindFirstObjectByType<WallDrawInput>(FindObjectsInactive.Include);

        if (di == null || !di.enableGridSnap)
        {
#region agent log
            DebugSessionAgentLog.Write(
                "H1",
                "WallSelectionManager.SnapInsertWorldXZBeforeWallInsert",
                "snap_skipped",
                "{\"diNull\":" + (di == null ? "true" : "false") +
                ",\"enableGridSnap\":" + (di != null && di.enableGridSnap ? "true" : "false") + "}");
#endregion
            return worldPosition;
        }

        Vector3 p = di.SnapWorldPointForVertexInsert(worldPosition);
        p.y = editShape.shapeY;
#region agent log
        DebugSessionAgentLog.Write(
            "H5",
            "WallSelectionManager.SnapInsertWorldXZBeforeWallInsert",
            "snap_applied",
            "{\"inx\":" + worldPosition.x.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"inz\":" + worldPosition.z.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"outx\":" + p.x.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"outz\":" + p.z.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
#endregion
        return p;
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

    /// <summary>
    /// Choix du provider de poignées : <see cref="WallEditShape"/> si présent, sinon <see cref="WallSelectable"/>, sinon premier <see cref="IControlPointProvider"/>.
    /// Voir aussi <see cref="ControlPointShapeMembership.BelongsToWallShape"/>.
    /// </summary>
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
