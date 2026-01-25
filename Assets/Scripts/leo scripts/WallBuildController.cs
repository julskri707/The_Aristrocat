using System.Collections.Generic;
using UnityEngine;

public class WallBuildController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public WallDrawInput drawInput;

    [Header("Prefabs")]
    public WallObject wallPrefab;
    public WallControlPointHandle controlPointPrefab;

    [Header("Layer/Collider")]
    public Collider groundCollider;

    [Header("Runtime Settings")]
    public float controlPointSize = 0.25f;
    public float heightScrollSpeed = 0.4f;
    public float thicknessScrollSpeed = 0.05f;

    private readonly List<WallObject> _walls = new List<WallObject>();

    private WallObject _selectedWall;
    private WallControlPointHandle _dragHandle;
    private Vector3 _dragOffset;

    void Reset()
    {
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

        HandleSelection();
        HandleDragging();
        HandleWallAdjustments();
    }

    void HandleShapeCommitted(List<Vector3> points)
    {
        if (wallPrefab == null || controlPointPrefab == null) return;
        if (points == null || points.Count < 2) return;

        // Create wall
        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.SetPath(points);

        _walls.Add(wall);
        _selectedWall = wall;

        // Create control points
        CreateControlPoints(wall);
    }

    void CreateControlPoints(WallObject wall)
    {
        int count = wall.closedLoop ? wall.Points.Count - 1 : wall.Points.Count;

        for (int i = 0; i < count; i++)
        {
            var cp = Instantiate(controlPointPrefab);
            cp.transform.localScale = Vector3.one * controlPointSize;
            cp.Init(wall, i, wall.Points[i]);
        }
    }

    void HandleSelection()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // Priorité: click sur control point
            if (Physics.Raycast(ray, out RaycastHit hit, 10000f))
            {
                var cp = hit.collider.GetComponentInParent<WallControlPointHandle>();
                if (cp != null && cp.wall != null)
                {
                    _selectedWall = cp.wall;
                    _dragHandle = cp;

                    // offset pour drag plus smooth
                    _dragOffset = cp.transform.position - GetMouseGroundPoint(cp.transform.position);
                    return;
                }

                // click sur wall mesh ?
                var wall = hit.collider.GetComponentInParent<WallObject>();
                if (wall != null)
                {
                    _selectedWall = wall;
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            _dragHandle = null;
        }
    }

    void HandleDragging()
    {
        if (_dragHandle == null) return;
        if (!Input.GetMouseButton(0)) return;

        Vector3 gp = GetMouseGroundPoint(_dragHandle.transform.position);
        Vector3 newPos = gp + _dragOffset;

        // move point on ground (keep y from wall points)
        newPos.y = _dragHandle.wall.Points[_dragHandle.pointIndex].y;

        _dragHandle.transform.position = newPos;
        _dragHandle.wall.SetPoint(_dragHandle.pointIndex, newPos);
    }

    void HandleWallAdjustments()
    {
        if (_selectedWall == null) return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.0001f) return;

        // Shift + scroll => hauteur
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            float h = _selectedWall.height + scroll * heightScrollSpeed;
            _selectedWall.SetHeight(h);
        }
        else
        {
            float t = _selectedWall.thickness + scroll * thicknessScrollSpeed;
            _selectedWall.SetThickness(t);
        }
    }

    Vector3 GetMouseGroundPoint(Vector3 fallback)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (groundCollider != null && groundCollider.Raycast(ray, out RaycastHit hit, 10000f))
            return hit.point;

        if (Physics.Raycast(ray, out RaycastHit hit2, 10000f))
            return hit2.point;

        return fallback;
    }
}
