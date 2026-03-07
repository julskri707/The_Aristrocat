using System.Collections.Generic;
using UnityEngine;

public class FieldPlacer : MonoBehaviour
{
    [Header("Camera")]
    public Camera cam;

    [Header("Keys")]
    public KeyCode buildKey = KeyCode.B;
    public KeyCode cancelKey = KeyCode.Escape;

    [Header("Tags")]
    public string groundTag = "Ground";
    public string fieldTag = "WorkArea";

    [Header("Build Settings")]
    public int minPoints = 3;
    public float closeDistance = 0.75f;

    [Header("Preview Line")]
    public float previewLineWidth = 0.08f;
    public float previewYOffset = 0.03f;

    [Header("Debug")]
    public bool debugLogs = true;

    private bool _building;
    private readonly List<Vector3> _points = new();

    private LineRenderer _preview;
    private GameObject _previewGO;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        EnsurePreview();
        SetPreview(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(buildKey))
        {
            _building = true;
            _points.Clear();
            SetPreview(true);
            UpdatePreview();
            Log("BUILD: Linksklick Punkte. ENTER = Erstellen. BACKSPACE = letzter Punkt. ESC/RightClick = Abbrechen.");
        }

        if (!_building) return;

        if (Input.GetKeyDown(cancelKey) || Input.GetMouseButtonDown(1))
        {
            CancelBuild();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
            UpdatePreview();
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetGroundPoint(out var p))
            {
                if (_points.Count >= 3 && Vector3.Distance(p, _points[0]) <= closeDistance)
                {
                    CreateField();
                    return;
                }

                _points.Add(p);
                UpdatePreview();
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (_points.Count >= minPoints) CreateField();
            else Log("BUILD: Zu wenige Punkte.");
        }
    }

    private void CreateField()
    {
        // ✅ Field creation happens inside FieldArea
        FieldArea.CreateField(new List<Vector3>(_points), fieldTag);

        CancelBuild();
        Log("BUILD: Feld erstellt.");
    }

    private void CancelBuild()
    {
        _building = false;
        _points.Clear();
        UpdatePreview();
        SetPreview(false);
        Log("BUILD: beendet.");
    }

    private bool TryGetGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
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

    // --- Preview line ---
    private void EnsurePreview()
    {
        if (_previewGO != null) return;

        _previewGO = new GameObject("FieldPreviewLine");
        _preview = _previewGO.AddComponent<LineRenderer>();
        _preview.useWorldSpace = true;
        _preview.loop = false;
        _preview.alignment = LineAlignment.View;
        _preview.startWidth = previewLineWidth;
        _preview.endWidth = previewLineWidth;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        _preview.material = new Material(shader);
    }

    private void SetPreview(bool visible)
    {
        if (_previewGO != null) _previewGO.SetActive(visible);
    }

    private void UpdatePreview()
    {
        if (_preview == null) return;

        if (_points.Count == 0)
        {
            _preview.positionCount = 0;
            return;
        }

        _preview.positionCount = _points.Count;
        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            _preview.SetPosition(i, new Vector3(p.x, p.y + previewYOffset, p.z));
        }
    }

    private void Log(string msg)
    {
        if (debugLogs) Debug.Log("[FieldPlacer] " + msg);
    }
}
