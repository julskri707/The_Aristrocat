using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FieldArea : MonoBehaviour
{
    [Header("Ground Projection")]
    public string groundTag = "Ground";
    public float raycastUp = 50f;
    public float raycastDown = 200f;

    [Header("Visual")]
    public float yOffset = 0.03f;
    public float lineWidth = 0.08f;
    public Color normalColor = Color.black;
    public Color selectedColor = Color.yellow;

    [Header("Collider")]
    public bool createTriggerMeshCollider = true;
    public float colliderThickness = 0.1f; // tiny thickness so it's selectable
    public bool colliderConvex = true;

    [Header("Terrain Conform")]
    public bool autoConformToGround = false;
    public float conformInterval = 0.5f;

    private LineRenderer _lr;
    private MeshCollider _meshCol;
    private Mesh _mesh;
    private bool _selected;
    private float _timer;

    // stored points in XZ (world) – Y will be projected
    private readonly List<Vector3> _worldPoints = new();

    public IReadOnlyList<Vector3> WorldPoints => _worldPoints;

    private void Awake()
    {
        EnsureLineRenderer();
        EnsureCollider();
    }

    private void Update()
    {
        if (!autoConformToGround) return;
        _timer += Time.deltaTime;
        if (_timer >= conformInterval)
        {
            _timer = 0f;
            ConformToGround();
        }
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyColor();
    }

    public void SetPolygonWorldPoints(List<Vector3> pointsWorld)
    {
        _worldPoints.Clear();
        _worldPoints.AddRange(pointsWorld);

        ConformToGround();    // project to ground & rebuild
    }

    public void ConformToGround()
    {
        if (_worldPoints.Count < 3) return;

        // project each point down onto ground
        for (int i = 0; i < _worldPoints.Count; i++)
        {
            Vector3 p = _worldPoints[i];
            Vector3 origin = new Vector3(p.x, p.y + raycastUp, p.z);

            if (Physics.Raycast(origin, Vector3.down, out var hit, raycastUp + raycastDown, ~0, QueryTriggerInteraction.Ignore))
            {
                // require ground tag in parents (optional)
                if (HasTagInParents(hit.collider.transform, groundTag))
                {
                    _worldPoints[i] = new Vector3(p.x, hit.point.y, p.z);
                }
            }
        }

        RebuildOutline();
        if (createTriggerMeshCollider) RebuildTriggerMesh();
    }

    private void EnsureLineRenderer()
    {
        _lr = GetComponent<LineRenderer>();
        if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();

        _lr.useWorldSpace = true;
        _lr.loop = true;
        _lr.alignment = LineAlignment.View;
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;

        if (_lr.sharedMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _lr.sharedMaterial = new Material(shader);
        }

        ApplyColor();
    }

    private void EnsureCollider()
    {
        if (!createTriggerMeshCollider) return;

        _meshCol = GetComponent<MeshCollider>();
        if (_meshCol == null) _meshCol = gameObject.AddComponent<MeshCollider>();

        _meshCol.convex = colliderConvex;
        _meshCol.isTrigger = true;

        if (_mesh == null) _mesh = new Mesh { name = "FieldAreaMesh" };
    }

    private void ApplyColor()
    {
        var c = _selected ? selectedColor : normalColor;
        if (_lr != null)
        {
            _lr.startColor = c;
            _lr.endColor = c;
        }
    }

    private void RebuildOutline()
    {
        if (_lr == null) return;
        if (_worldPoints.Count < 3) return;

        _lr.positionCount = _worldPoints.Count;

        for (int i = 0; i < _worldPoints.Count; i++)
        {
            var p = _worldPoints[i];
            _lr.SetPosition(i, new Vector3(p.x, p.y + yOffset, p.z));
        }

        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;
    }

    // Simple convex fan triangulation (works best if your drawn polygon is convex & ordered)
    private void RebuildTriggerMesh()
    {
        if (_meshCol == null || _mesh == null) return;
        if (_worldPoints.Count < 3) return;

        // Build top vertices
        int n = _worldPoints.Count;
        var verts = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            var p = _worldPoints[i];
            verts[i] = new Vector3(p.x, p.y + yOffset, p.z);
            verts[i + n] = new Vector3(p.x, p.y + yOffset - colliderThickness, p.z);
        }

        // Triangles for top face (fan from 0)
        var tris = new List<int>(n * 6);
        for (int i = 1; i < n - 1; i++)
        {
            tris.Add(0); tris.Add(i); tris.Add(i + 1);
        }

        // Bottom face (reverse winding)
        int baseIdx = n;
        for (int i = 1; i < n - 1; i++)
        {
            tris.Add(baseIdx + 0); tris.Add(baseIdx + i + 1); tris.Add(baseIdx + i);
        }

        // Sides
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;

            int topA = i;
            int topB = next;
            int botA = i + n;
            int botB = next + n;

            tris.Add(topA); tris.Add(topB); tris.Add(botB);
            tris.Add(topA); tris.Add(botB); tris.Add(botA);
        }

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.triangles = tris.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        _meshCol.sharedMesh = null;
        _meshCol.sharedMesh = _mesh;
    }

    private static bool HasTagInParents(Transform t, string tag)
    {
        if (string.IsNullOrEmpty(tag)) return true;
        while (t != null)
        {
            if (t.CompareTag(tag)) return true;
            t = t.parent;
        }
        return false;
    }
}
