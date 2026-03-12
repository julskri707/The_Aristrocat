using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class WallBuildController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public WallDrawInput drawInput;
    public ControlPointOverlayManager overlay;

    [Header("Prefabs")]
    public WallObject wallPrefab;

    [Header("Selection")]
    public LayerMask wallRaycastMask = ~0;
    public float rayDistance = 5000f;

    private readonly List<WallObject> _walls = new List<WallObject>();

    public WallObject SelectedWall { get; private set; }

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();
    }

    void OnEnable()
    {
        if (drawInput != null)
            drawInput.OnShapeCommitted += HandleShapeCommitted;
    }

    void OnDisable()
    {
        if (drawInput != null)
            drawInput.OnShapeCommitted -= HandleShapeCommitted;
    }

    void Update()
    {
        if (cam == null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.GetMouseButtonDown(0))
            TrySelectWallUnderMouse();
    }

    void HandleShapeCommitted(List<Vector3> points)
    {
        if (wallPrefab == null) return;
        if (points == null || points.Count < 2) return;

        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.SetPath(points);

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape == null)
            editShape = wall.gameObject.AddComponent<WallEditShape>();

        editShape.wall = wall;
        editShape.InitFromPath(points);

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable == null)
            selectable = wall.gameObject.AddComponent<WallSelectable>();

        selectable.providerBehaviour = editShape;

        _walls.Add(wall);
        SelectedWall = wall;

        if (overlay != null)
            overlay.SetTarget(editShape);
    }

    void TrySelectWallUnderMouse()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, wallRaycastMask, QueryTriggerInteraction.Ignore))
            return;

        WallObject wall = hit.collider.GetComponentInParent<WallObject>();
        if (wall == null)
            return;

        SelectedWall = wall;

        MonoBehaviour provider = ResolveBestProvider(wall);
        if (overlay != null)
        {
            if (provider != null)
                overlay.SetTarget(provider);
            else
                overlay.ClearTarget();
        }
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
}