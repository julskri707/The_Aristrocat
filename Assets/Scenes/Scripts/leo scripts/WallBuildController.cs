using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class WallBuildController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public WallDrawInput drawInput;
    public ControlPointOverlayManager overlay;
    public WallUndoManager undoManager;

    [Header("Prefabs")]
    public WallObject wallPrefab;

    [Header("Default Style")]
    public WallStyleDefinition defaultWallStyle;

    [Header("Selection")]
    public LayerMask wallRaycastMask = ~0;
    public float rayDistance = 5000f;
    public bool handleSelectionInput = false;

    [Header("Debug")]
    public bool logDebug = false;

    private readonly List<WallObject> _walls = new List<WallObject>();

    public IReadOnlyList<WallObject> Walls => _walls;
    public WallObject SelectedWall { get; private set; }

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
    }

    void OnEnable()
    {
        if (drawInput != null)
            drawInput.OnShapeCommittedDetailed += HandleShapeCommittedDetailed;
    }

    void OnDisable()
    {
        if (drawInput != null)
            drawInput.OnShapeCommittedDetailed -= HandleShapeCommittedDetailed;
    }

    void Update()
    {
        CleanupNullWalls();

        if (!handleSelectionInput || cam == null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.GetMouseButtonDown(0))
            TrySelectWallUnderMouse();
    }

    void HandleShapeCommittedDetailed(List<Vector3> points, WallDrawInput.DetectedShapeKind detectedKind, string detectedName)
    {
        if (wallPrefab == null)
            return;

        if (points == null || points.Count < 2)
            return;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Create Wall");

        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.SetPath(points);

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape == null)
            editShape = wall.gameObject.AddComponent<WallEditShape>();

        editShape.wall = wall;
        editShape.InitFromDetectedPath(points, detectedKind);

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable == null)
            selectable = wall.gameObject.AddComponent<WallSelectable>();

        selectable.providerBehaviour = editShape;

        if (defaultWallStyle != null)
            WallStyleApplier.Apply(wall, defaultWallStyle);

        RegisterExistingWall(wall);
        ForceSelectWall(wall);

        if (logDebug)
            Debug.Log($"[WallBuildController] Spawned wall '{wall.name}' from detected shape '{detectedName}'.");
    }

    void TrySelectWallUnderMouse()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, wallRaycastMask, QueryTriggerInteraction.Ignore))
            return;

        WallObject wall = hit.collider.GetComponentInParent<WallObject>();
        if (wall == null)
            return;

        RegisterExistingWall(wall);
        ForceSelectWall(wall);
    }

    MonoBehaviour ResolveBestProvider(WallObject wall)
    {
        if (wall == null)
            return null;

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape != null)
            return editShape;

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable != null)
        {
            if (selectable.providerBehaviour == null)
                selectable.AutoFindProvider();

            if (selectable.providerBehaviour != null)
                return selectable.providerBehaviour;
        }

        MonoBehaviour[] monos = wall.GetComponents<MonoBehaviour>();
        for (int i = 0; i < monos.Length; i++)
        {
            if (monos[i] is IControlPointProvider)
                return monos[i];
        }

        return null;
    }

    public void ForceSelectWall(WallObject wall)
    {
        SelectedWall = wall;

        if (wall == null)
        {
            if (overlay != null)
                overlay.ClearTarget();
            return;
        }

        MonoBehaviour provider = ResolveBestProvider(wall);
        if (overlay != null)
        {
            if (provider != null)
                overlay.SetTarget(provider);
            else
                overlay.ClearTarget();
        }
    }

    public void RegisterExistingWall(WallObject wall)
    {
        if (wall == null)
            return;

        CleanupNullWalls();

        if (_walls.Contains(wall))
            return;

        _walls.Add(wall);
    }

    public bool UnregisterWall(WallObject wall)
    {
        if (wall == null)
            return false;

        bool removed = _walls.Remove(wall);

        if (SelectedWall == wall)
            ForceSelectWall(null);

        return removed;
    }

    public void ClearManagedWalls()
    {
        CleanupNullWalls();
        _walls.Clear();

        if (SelectedWall != null)
            ForceSelectWall(null);
    }

    public void ClearManagedWalls(bool destroyWallObjects)
    {
        if (destroyWallObjects)
        {
            for (int i = _walls.Count - 1; i >= 0; i--)
            {
                WallObject wall = _walls[i];
                if (wall == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(wall.gameObject);
                else
                    DestroyImmediate(wall.gameObject);
            }
        }

        ClearManagedWalls();
    }

    void CleanupNullWalls()
    {
        for (int i = _walls.Count - 1; i >= 0; i--)
        {
            if (_walls[i] == null)
                _walls.RemoveAt(i);
        }
    }
}
