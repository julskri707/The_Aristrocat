using System.Collections.Generic;
using UnityEngine;

public class RuntimeFieldDrawBuild : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject fieldPrefab; // should have: FieldArea + ResourceTickBehaviour (+ optional FieldOutline no longer needed)

    [Header("Tags")]
    public string groundTag = "Ground";
    public string fieldTag = "WorkArea";

    [Header("Keys")]
    public KeyCode buildKey = KeyCode.B;
    public KeyCode finishKey = KeyCode.Return;
    public KeyCode undoKey = KeyCode.Backspace;

    [Header("Snap")]
    public float snapSize = 0f; // 0 = no snap

    [Header("Preview")]
    public float previewYOffset = 0.03f;
    public float previewLineWidth = 0.08f;

    private bool _drawing;
    private readonly List<Vector3> _points = new();
    private LineRenderer _preview;

    private void Update()
    {
        if (Input.GetKeyDown(buildKey))
        {
            StartDrawing();
        }

        if (!_drawing) return;

        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            CancelDrawing();
            return;
        }

        if (Input.GetKeyDown(undoKey))
        {
            if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
            UpdatePreview();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetGroundPoint(out var p))
            {
                if (snapSize > 0.0001f)
                {
                    p.x = Mathf.Round(p.x / snapSize) * snapSize;
                    p.z = Mathf.Round(p.z / snapSize) * snapSize;
                }

                _points.Add(p);
                UpdatePreview();
            }
        }

        if (Input.GetKeyDown(finishKey))
        {
            FinishField();
        }
    }

    private void StartDrawing()
    {
        _drawing = true;
        _points.Clear();

        if (_preview == null)
        {
            var go = new GameObject("FieldPreviewLine");
            _preview = go.AddComponent<LineRenderer>();
            _preview.useWorldSpace = true;
            _preview.loop = false;
            _preview.startWidth = previewLineWidth;
            _preview.endWidth = previewLineWidth;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _preview.sharedMaterial = new Material(shader);
            _preview.startColor = Color.black;
            _preview.endColor = Color.black;
        }

        _preview.positionCount = 0;
    }

    private void CancelDrawing()
    {
        _drawing = false;
        _points.Clear();
        if (_preview != null) _preview.positionCount = 0;
    }

    private void UpdatePreview()
    {
        if (_preview == null) return;

        if (_points.Count < 1)
        {
            _preview.positionCount = 0;
            return;
        }

        // show line through points + current mouse ground point
        var temp = new List<Vector3>(_points);

        if (TryGetGroundPoint(out var mouseP))
            temp.Add(mouseP);

        _preview.positionCount = temp.Count;
        for (int i = 0; i < temp.Count; i++)
        {
            var p = temp[i];
            _preview.SetPosition(i, new Vector3(p.x, p.y + previewYOffset, p.z));
        }
    }

    private void FinishField()
    {
        if (_points.Count < 3) return;
        if (fieldPrefab == null) return;

        // spawn field at center
        Vector3 center = Vector3.zero;
        foreach (var p in _points) center += p;
        center /= _points.Count;

        var fieldGo = Instantiate(fieldPrefab, center, Quaternion.identity);

        if (!fieldGo.CompareTag(fieldTag)) fieldGo.tag = fieldTag;

        // Ensure FieldArea exists
        var fa = fieldGo.GetComponent<FieldArea>();
        if (fa == null) fa = fieldGo.AddComponent<FieldArea>();

        // Points are world points, keep them
        fa.SetPolygonWorldPoints(new List<Vector3>(_points));

        // Optional: rename
        fieldGo.name = "FieldArea";

        // clear preview
        CancelDrawing();
    }

    private bool TryGetGroundPoint(out Vector3 point)
    {
        point = default;
        var cam = Camera.main;
        if (cam == null) return false;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 9999f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        if (!HasTagInParents(hit.collider.transform, groundTag))
            return false;

        point = hit.point;
        return true;
    }

    private static bool HasTagInParents(Transform t, string tag)
    {
        while (t != null)
        {
            if (t.CompareTag(tag)) return true;
            t = t.parent;
        }
        return false;
    }
}
