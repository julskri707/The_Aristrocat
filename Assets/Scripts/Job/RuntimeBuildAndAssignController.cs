using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public class RuntimeBuildAndAssignController : MonoBehaviour
{
    public enum Mode { None, BuildField, AssignWorkers }

    [Header("Camera")]
    public Camera cam;

    [Header("Mode Keys")]
    public KeyCode buildModeKey = KeyCode.B;
    public KeyCode assignModeKey = KeyCode.A;
    public KeyCode cancelKey = KeyCode.Escape;

    [Header("Tags")]
    public string groundTag = "Ground";
    public string fieldTag = "WorkArea";
    public string workerTag = "Bauer";

    [Header("Build Field Settings")]
    [Tooltip("Optional: If set, this prefab will be used as the field root (keeps your ResourceTickBehaviour settings). Otherwise an empty object is created.")]
    public GameObject fieldPrefab;

    [Tooltip("Minimum number of points to create a field.")]
    public int minPoints = 3;

    [Tooltip("Close polygon if click is near the first point.")]
    public float closeDistance = 0.75f;

    [Tooltip("Snap points to grid in world units (0 = off).")]
    public float snap = 0f;

    [Header("Preview Line")]
    public float previewLineWidth = 0.08f;
    public float previewYOffset = 0.03f;

    [Header("Auto Wiring (type-mismatch-proof)")]
    public bool autoFindTickBehaviour = true;
    public bool autoFindResourceManager = true;

    [Header("Debug")]
    public bool debugLogs = true;

    private Mode _mode = Mode.None;

    // Build data
    private readonly List<Vector3> _buildPoints = new();
    private LineRenderer _previewLine;
    private GameObject _previewGO;

    // Assign data
    private ResourceTickBehaviour _selectedField;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        EnsurePreviewLine();
        SetPreviewVisible(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(buildModeKey))
        {
            EnterBuildMode();
        }
        if (Input.GetKeyDown(assignModeKey))
        {
            EnterAssignMode();
        }

        if (Input.GetKeyDown(cancelKey))
        {
            CancelCurrentMode();
        }

        // ignore clicks when on UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (_mode == Mode.BuildField)
            UpdateBuildMode();

        if (_mode == Mode.AssignWorkers)
            UpdateAssignMode();
    }

    // =========================
    // MODE SWITCHING
    // =========================
    private void EnterBuildMode()
    {
        _mode = Mode.BuildField;
        _buildPoints.Clear();
        ClearSelection();
        SetPreviewVisible(true);
        UpdatePreviewLine();
        Log("BUILD: Linksklick Punkte setzen. ENTER = Feld erstellen. BACKSPACE = letzter Punkt. ESC/RightClick = Abbrechen.");
    }

    private void EnterAssignMode()
    {
        _mode = Mode.AssignWorkers;
        _buildPoints.Clear();
        SetPreviewVisible(false);
        Log("ASSIGN: Klick Feld, dann Klick Bauer. ESC = Auswahl löschen.");
    }

    private void CancelCurrentMode()
    {
        if (_mode == Mode.BuildField)
        {
            _buildPoints.Clear();
            UpdatePreviewLine();
            SetPreviewVisible(false);
            Log("BUILD: abgebrochen.");
        }

        if (_mode == Mode.AssignWorkers)
        {
            ClearSelection();
            Log("ASSIGN: Auswahl gelöscht.");
        }

        _mode = Mode.None;
    }

    // =========================
    // BUILD MODE
    // =========================
    private void UpdateBuildMode()
    {
        // right click cancels build
        if (Input.GetMouseButtonDown(1))
        {
            CancelCurrentMode();
            return;
        }

        // remove last point
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (_buildPoints.Count > 0)
                _buildPoints.RemoveAt(_buildPoints.Count - 1);
            UpdatePreviewLine();
        }

        // add point
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetGroundPoint(out Vector3 p))
            {
                p = ApplySnap(p);

                // if close to first point -> close polygon
                if (_buildPoints.Count >= 3 && Vector3.Distance(p, _buildPoints[0]) <= closeDistance)
                {
                    // finalize
                    CreateFieldFromPoints(_buildPoints);
                    _buildPoints.Clear();
                    UpdatePreviewLine();
                    SetPreviewVisible(false);
                    _mode = Mode.None;
                    return;
                }

                _buildPoints.Add(p);
                UpdatePreviewLine();
            }
        }

        // ENTER to finalize
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (_buildPoints.Count >= minPoints)
            {
                CreateFieldFromPoints(_buildPoints);
                _buildPoints.Clear();
                UpdatePreviewLine();
                SetPreviewVisible(false);
                _mode = Mode.None;
            }
            else
            {
                Log("BUILD: Zu wenige Punkte.");
            }
        }
    }

    private void CreateFieldFromPoints(List<Vector3> worldPoints)
    {
        if (worldPoints == null || worldPoints.Count < minPoints) return;

        // Create root
        GameObject fieldGO;
        if (fieldPrefab != null)
        {
            fieldGO = Instantiate(fieldPrefab);
            fieldGO.name = "FieldArea";
        }
        else
        {
            fieldGO = new GameObject("FieldArea");
        }

        // Tag
        fieldGO.tag = fieldTag;

        // Keep transform neutral so local/world don’t drift
        fieldGO.transform.position = Vector3.zero;
        fieldGO.transform.rotation = Quaternion.identity;
        fieldGO.transform.localScale = Vector3.one;

        // Ensure FieldArea
        var fa = fieldGO.GetComponent<FieldArea>();
        if (fa == null) fa = fieldGO.AddComponent<FieldArea>();

        // Make sure selection collider exists + not blocking (trigger)
        // (Your FieldArea script already creates selection box trigger.)

        // Ensure ResourceTickBehaviour
        var tick = fieldGO.GetComponent<ResourceTickBehaviour>();
        if (tick == null) tick = fieldGO.AddComponent<ResourceTickBehaviour>();

        AutoWireRefs(tick);

        // Apply polygon points
        fa.SetPolygonWorldPoints(worldPoints);

        // Optional: select it immediately
        SelectField(tick);

        Log($"BUILD: Feld erstellt. Punkte={worldPoints.Count}");
    }

    // =========================
    // ASSIGN MODE
    // =========================
    private void UpdateAssignMode()
    {
        // right click clears selection
        if (Input.GetMouseButtonDown(1))
        {
            ClearSelection();
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        var hits = Physics.RaycastAll(ray, 9999f, ~0, QueryTriggerInteraction.Collide)
                          .OrderBy(h => h.distance)
                          .ToArray();

        if (hits.Length == 0) return;

        // If no field selected: try select field first
        if (_selectedField == null)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (!HasTagInParents(h.collider.transform, fieldTag)) continue;

                var fa = h.collider.GetComponentInParent<FieldArea>();
                if (fa == null) continue;

                if (!fa.ContainsWorldPoint(h.point)) continue;

                var tick = fa.GetComponent<ResourceTickBehaviour>();
                if (tick == null) tick = fa.gameObject.AddComponent<ResourceTickBehaviour>();
                AutoWireRefs(tick);

                SelectField(tick);
                return;
            }

            Log("ASSIGN: Kein Feld getroffen.");
            return;
        }

        // Field selected -> assign worker
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!HasTagInParents(h.collider.transform, workerTag)) continue;

            var workerGO = FindTaggedObjectInParents(h.collider.transform, workerTag);
            if (workerGO == null) continue;

            var wa = workerGO.GetComponent<WorkerAssignment>();
            if (wa == null) wa = workerGO.AddComponent<WorkerAssignment>();

            wa.AssignTo(_selectedField);

            // immediately update assignedWorkers for this field (no need for JobManager tick)
            _selectedField.assignedWorkers = CountAssignedWorkers(_selectedField);

            Log($"ASSIGN: '{workerGO.name}' -> '{_selectedField.name}' (workers={_selectedField.assignedWorkers})");
            return;
        }

        Log("ASSIGN: Kein Bauer getroffen.");
    }

    private int CountAssignedWorkers(ResourceTickBehaviour field)
    {
        var all = FindObjectsByType<WorkerAssignment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int c = 0;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].assignedField == field) c++;
        return c;
    }

    // =========================
    // PREVIEW LINE
    // =========================
    private void EnsurePreviewLine()
    {
        if (_previewGO != null) return;

        _previewGO = new GameObject("FieldPreviewLine");
        _previewLine = _previewGO.AddComponent<LineRenderer>();
        _previewLine.useWorldSpace = true;
        _previewLine.loop = false;
        _previewLine.alignment = LineAlignment.View;
        _previewLine.startWidth = previewLineWidth;
        _previewLine.endWidth = previewLineWidth;

        var s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        _previewLine.material = new Material(s);
    }

    private void SetPreviewVisible(bool v)
    {
        if (_previewGO != null) _previewGO.SetActive(v);
    }

    private void UpdatePreviewLine()
    {
        if (_previewLine == null) return;

        if (_buildPoints.Count == 0)
        {
            _previewLine.positionCount = 0;
            return;
        }

        _previewLine.positionCount = _buildPoints.Count;
        for (int i = 0; i < _buildPoints.Count; i++)
        {
            Vector3 p = _buildPoints[i];
            _previewLine.SetPosition(i, new Vector3(p.x, p.y + previewYOffset, p.z));
        }
    }

    // =========================
    // HELPERS
    // =========================
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

    private Vector3 ApplySnap(Vector3 p)
    {
        if (snap <= 0f) return p;
        p.x = Mathf.Round(p.x / snap) * snap;
        p.z = Mathf.Round(p.z / snap) * snap;
        return p;
    }

    private void AutoWireRefs(ResourceTickBehaviour tick)
    {
        if (tick == null) return;

        if (tick.tickBehaviour == null && autoFindTickBehaviour)
            tick.tickBehaviour = FindAnyByNames("TickSytem", "TickSystem", "Tick", "TickManager");

        if (tick.resourceManagerBehaviour == null && autoFindResourceManager)
            tick.resourceManagerBehaviour = FindAnyByNames("Resource manager", "ResourceManager", "RessourceManager", "RessourcenManager");

        tick.active = true;
    }

    private MonoBehaviour FindAnyByNames(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            var go = GameObject.Find(names[i]);
            if (go == null) continue;
            var mb = go.GetComponent<MonoBehaviour>();
            if (mb != null) return mb;
        }
        return null;
    }

    private void SelectField(ResourceTickBehaviour field)
    {
        if (_selectedField != null)
        {
            var oldFA = _selectedField.GetComponent<FieldArea>();
            if (oldFA != null) oldFA.SetSelected(false);
        }

        _selectedField = field;

        var fa = _selectedField.GetComponent<FieldArea>();
        if (fa != null) fa.SetSelected(true);

        Log($"Selected field: {_selectedField.name}");
    }

    private void ClearSelection()
    {
        if (_selectedField != null)
        {
            var fa = _selectedField.GetComponent<FieldArea>();
            if (fa != null) fa.SetSelected(false);
        }
        _selectedField = null;
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
