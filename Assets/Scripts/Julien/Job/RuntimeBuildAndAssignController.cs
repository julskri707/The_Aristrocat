using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RuntimeBuildAndAssignController : MonoBehaviour
{
    private enum Mode { None, Build, Assign }

    [Header("Camera")]
    public Camera cam;

    [Header("Tags")]
    public string groundTag = "Ground";
    public string fieldTag = "WorkArea";
    public string workerTag = "Bauer";

    [Header("Keys")]
    public KeyCode buildKey = KeyCode.B;     // ✅ START PLACING
    public KeyCode assignKey = KeyCode.A;    // ✅ ASSIGN MODE
    public KeyCode clearKey = KeyCode.Escape;

    [Header("Build Settings")]
    public int minPoints = 3;
    public float closeDistance = 0.75f;

    [Tooltip("Optional prefab for field root. If empty, creates empty GO.")]
    public GameObject fieldPrefab;

    [Header("Preview Line")]
    public float previewLineWidth = 0.08f;
    public float previewYOffset = 0.03f;

    [Header("Debug")]
    public bool debugLogs = true;

    private Mode _mode = Mode.None;

    private readonly List<Vector3> _points = new();
    private LineRenderer _preview;
    private GameObject _previewGO;

    private ResourceTickBehaviour _selectedFieldTick;

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
            StartBuildMode();
        }

        if (Input.GetKeyDown(assignKey))
        {
            StartAssignMode();
        }

        if (Input.GetKeyDown(clearKey) || Input.GetMouseButtonDown(1))
        {
            CancelOrClear();
        }

        if (_mode == Mode.Build)
            UpdateBuild();

        if (_mode == Mode.Assign)
        {
            if (Input.GetMouseButtonDown(0))
                HandleAssignClick();
        }
    }

    // =======================
    // BUILD MODE
    // =======================
    private void StartBuildMode()
    {
        _mode = Mode.Build;
        _points.Clear();
        ClearSelection();
        SetPreview(true);
        UpdatePreview();
        Log("BUILD: Linksklick Punkte setzen. ENTER = Feld erstellen. BACKSPACE = letzter Punkt. ESC/RightClick = Abbrechen.");
    }

    private void UpdateBuild()
    {
        // remove last point
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (_points.Count > 0)
            {
                _points.RemoveAt(_points.Count - 1);
                UpdatePreview();
            }
        }

        // add point
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetGroundPoint(out Vector3 p))
            {
                // close if near first point
                if (_points.Count >= 3 && Vector3.Distance(p, _points[0]) <= closeDistance)
                {
                    CreateField(_points);
                    FinishBuild();
                    return;
                }

                _points.Add(p);
                UpdatePreview();
            }
        }

        // finalize on Enter
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (_points.Count >= minPoints)
            {
                CreateField(_points);
                FinishBuild();
            }
            else
            {
                Log("BUILD: Zu wenige Punkte.");
            }
        }
    }

    private void FinishBuild()
    {
        _points.Clear();
        UpdatePreview();
        SetPreview(false);
        _mode = Mode.None;
        Log("BUILD: fertig.");
    }

    // =======================
    // ASSIGN MODE
    // =======================
    private void StartAssignMode()
    {
        _mode = Mode.Assign;
        _points.Clear();
        SetPreview(false);
        Log("ASSIGN: Klick Feld, dann Klick Bauer. ESC/RightClick = Auswahl löschen.");
    }

    private void HandleAssignClick()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        var hits = Physics.RaycastAll(ray, 9999f, ~0, QueryTriggerInteraction.Collide)
                          .OrderBy(h => h.distance)
                          .ToArray();

        if (hits.Length == 0) return;

        // 1) Select field
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!HasTagInParents(h.collider.transform, fieldTag))
                continue;

            var fa = h.collider.GetComponentInParent<FieldArea>();
            if (fa == null) continue;

            if (!fa.ContainsWorldPoint(h.point))
                continue;

            var tick = fa.GetComponent<ResourceTickBehaviour>();
            if (tick == null) tick = fa.gameObject.AddComponent<ResourceTickBehaviour>();

            SelectField(tick);
            return;
        }

        // 2) Assign worker
        if (_selectedFieldTick == null) return;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!HasTagInParents(h.collider.transform, workerTag))
                continue;

            var workerGO = FindTaggedObjectInParents(h.collider.transform, workerTag);
            if (workerGO == null) continue;

            var wa = workerGO.GetComponent<WorkerAssignment>();
            if (wa == null) wa = workerGO.AddComponent<WorkerAssignment>();

            wa.AssignTo(_selectedFieldTick);
            Log($"Assigned '{workerGO.name}' -> '{_selectedFieldTick.name}'");
            return;
        }
    }

    private void CancelOrClear()
    {
        if (_mode == Mode.Build)
        {
            _points.Clear();
            UpdatePreview();
            SetPreview(false);
            _mode = Mode.None;
            Log("BUILD: abgebrochen.");
            return;
        }

        if (_mode == Mode.Assign)
        {
            ClearSelection();
            Log("ASSIGN: Auswahl gelöscht.");
            return;
        }

        ClearSelection();
        SetPreview(false);
        _points.Clear();
        _mode = Mode.None;
    }

    // =======================
    // FIELD CREATION
    // =======================
    private void CreateField(List<Vector3> worldPoints)
    {
        if (worldPoints == null || worldPoints.Count < minPoints) return;

        GameObject fieldGO = fieldPrefab != null ? Instantiate(fieldPrefab) : new GameObject("FieldArea");
        fieldGO.name = "FieldArea";
        fieldGO.tag = fieldTag;

        // keep neutral transform so local/world are simple
        fieldGO.transform.position = Vector3.zero;
        fieldGO.transform.rotation = Quaternion.identity;
        fieldGO.transform.localScale = Vector3.one;

        var fa = fieldGO.GetComponent<FieldArea>();
        if (fa == null) fa = fieldGO.AddComponent<FieldArea>();

        // IMPORTANT: ensure selection box exists for clicking
        var box = fieldGO.GetComponent<BoxCollider>();
        if (box == null) box = fieldGO.AddComponent<BoxCollider>();
        box.isTrigger = true;

        fa.SetPolygonWorldPoints(new List<Vector3>(worldPoints));

        var tick = fieldGO.GetComponent<ResourceTickBehaviour>();
        if (tick == null) tick = fieldGO.AddComponent<ResourceTickBehaviour>();

        SelectField(tick);

        Log($"BUILD: Field created with {worldPoints.Count} points.");
    }

    // =======================
    // PREVIEW
    // =======================
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
            Vector3 p = _points[i];
            _preview.SetPosition(i, new Vector3(p.x, p.y + previewYOffset, p.z));
        }
    }

    // =======================
    // UTILS
    // =======================
    private bool TryGetGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;

        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        var hits = Physics.RaycastAll(ray, 9999f, ~0, QueryTriggerInteraction.Ignore)
                          .OrderBy(h => h.distance)
                          .ToArray();

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (HasTagInParents(h.collider.transform, groundTag))
            {
                point = h.point;
                return true;
            }
        }
        return false;
    }

    private void SelectField(ResourceTickBehaviour field)
    {
        if (_selectedFieldTick != null)
        {
            var oldFA = _selectedFieldTick.GetComponent<FieldArea>();
            if (oldFA != null) oldFA.SetSelected(false);
        }

        _selectedFieldTick = field;

        var fa = _selectedFieldTick.GetComponent<FieldArea>();
        if (fa != null) fa.SetSelected(true);

        Log($"Selected field: {_selectedFieldTick.name}");
    }

    private void ClearSelection()
    {
        if (_selectedFieldTick != null)
        {
            var fa = _selectedFieldTick.GetComponent<FieldArea>();
            if (fa != null) fa.SetSelected(false);
        }
        _selectedFieldTick = null;
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

    private static GameObject FindTaggedObjectInParents(Transform t, string tag)
    {
        while (t != null)
        {
            if (t.CompareTag(tag)) return t.gameObject;
            t = t.parent;
        }
        return null;
    }

    private void Log(string msg)
    {
        if (debugLogs) Debug.Log("[BuildAssign] " + msg);
    }
}
