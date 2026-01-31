using System.Collections.Generic;
using UnityEngine;

public class WallBuildController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public WallDrawInput drawInput;

    [Header("Prefabs")]
    public WallObject wallPrefab;

    [Header("Optional")]
    public int wallLayer = 0; // 0 = Default, sinon mets un layer "Wall"

    private readonly List<WallObject> _walls = new();

    void Awake()
    {
        if (cam == null) cam = Camera.main;
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

    private void HandleShapeCommitted(List<Vector3> points)
    {
        if (wallPrefab == null) return;
        if (points == null || points.Count < 2) return;

        // 1) Create wall
        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.gameObject.layer = wallLayer;

        wall.SetPath(points);

        _walls.Add(wall);

        // 2) Ensure provider on wall for UI overlay
        var provider = wall.GetComponent<WallControlPointProvider_WallObject>();
        if (provider == null)
            provider = wall.gameObject.AddComponent<WallControlPointProvider_WallObject>();

        provider.wall = wall;

        // 3) Ensure selectable on wall
        var selectable = wall.GetComponent<WallSelectable>();
        if (selectable == null)
            selectable = wall.gameObject.AddComponent<WallSelectable>();

        selectable.providerBehaviour = provider;
    }
}
