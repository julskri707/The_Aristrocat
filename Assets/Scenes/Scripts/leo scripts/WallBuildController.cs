using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class WallBuildController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public WallDrawInput drawInput;

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
        if (cam == null) return;

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

        var editShape = wall.GetComponent<WallEditShape>();
        if (editShape == null)
            editShape = wall.gameObject.AddComponent<WallEditShape>();

        editShape.wall = wall;
        editShape.InitFromPath(points);

        _walls.Add(wall);
        SelectedWall = wall;

        var overlay = FindObjectOfType<ControlPointOverlayManager>();
        if (overlay != null)
        {
            overlay.RebuildHandles();
        }
    }

    void TrySelectWallUnderMouse()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, wallRaycastMask))
        {
            var wall = hit.collider.GetComponentInParent<WallObject>();
            if (wall != null)
            {
                SelectedWall = wall;

                var overlay = FindObjectOfType<ControlPointOverlayManager>();
                if (overlay != null)
                    overlay.RebuildHandles();
            }
        }
    }

    public void ForceSelectWall(WallObject wall)
    {
        SelectedWall = wall;
    }
}