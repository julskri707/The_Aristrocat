using UnityEngine;

public class RuntimeBuildAssignController : MonoBehaviour
{
    public enum Mode { None, Build, Assign }

    [Header("Tags")]
    public string groundTag = "Ground";
    public string fieldTag = "WorkArea";
    public string workerTag = "Bauer";

    [Header("Prefabs")]
    public GameObject fieldPrefab;

    [Header("Build")]
    public float snapSize = 1f;     // 0 = no snap
    public float placeYOffset = 0f;

    [Header("Keys")]
    public KeyCode buildKey = KeyCode.B;
    public KeyCode assignKey = KeyCode.A;

    [Header("Debug")]
    public bool debugLogs = true;

    public Mode mode = Mode.Build;

    private ResourceTickBehaviour _selectedField;

    private void Update()
    {
        if (Input.GetKeyDown(buildKey))
        {
            mode = Mode.Build;
            ClearSelection();
            Log("Mode: BUILD (click ground to place fields)");
        }

        if (Input.GetKeyDown(assignKey))
        {
            mode = Mode.Assign;
            ClearSelection();
            Log("Mode: ASSIGN (click a field, then click farmers)");
        }

        if (Input.GetMouseButtonDown(1))
            ClearSelection();

        if (Input.GetMouseButtonDown(0))
        {
            if (mode == Mode.Build) TryBuild();
            else if (mode == Mode.Assign) TryAssign();
        }
    }

    private void TryBuild()
    {
        if (fieldPrefab == null) { Log("No fieldPrefab assigned."); return; }
        if (!RayFromMouse(out var hit)) return;

        // must click ground-tagged collider
        if (!HasTagInParents(hit.collider.transform, groundTag)) return;

        Vector3 pos = hit.point;
        pos.y += placeYOffset;

        if (snapSize > 0.0001f)
        {
            pos.x = Mathf.Round(pos.x / snapSize) * snapSize;
            pos.z = Mathf.Round(pos.z / snapSize) * snapSize;
        }

        var go = Instantiate(fieldPrefab, pos, Quaternion.identity);

        // Ensure tag on root (or on collider object)
        if (!go.CompareTag(fieldTag)) go.tag = fieldTag;

        // Ensure outline exists
        if (go.GetComponent<FieldOutline>() == null) go.AddComponent<FieldOutline>();

        // Ensure resource tick behaviour exists
        if (go.GetComponent<ResourceTickBehaviour>() == null)
            Log("WARNING: FieldPrefab has no ResourceTickBehaviour (field won't produce).");

        Log($"Field placed: {go.name}");
    }

    private void TryAssign()
    {
        if (!RayFromMouse(out var hit)) return;

        // 1) click field to select
        var site = FindFieldSite(hit.collider);
        if (site != null)
        {
            SelectField(site);
            return;
        }

        // 2) with field selected, click farmer
        if (_selectedField != null)
        {
            var workerRoot = FindWorkerRoot(hit.collider);
            if (workerRoot != null)
            {
                var wa = workerRoot.GetComponent<WorkerAssignment>();
                if (wa == null) wa = workerRoot.AddComponent<WorkerAssignment>();

                wa.AssignTo(_selectedField);
                Log($"Assigned '{workerRoot.name}' -> '{_selectedField.name}'");
            }
        }
    }

    private ResourceTickBehaviour FindFieldSite(Collider col)
    {
        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag(fieldTag))
                return t.GetComponent<ResourceTickBehaviour>() ?? t.GetComponentInParent<ResourceTickBehaviour>();
            t = t.parent;
        }
        return null;
    }

    private GameObject FindWorkerRoot(Collider col)
    {
        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag(workerTag))
                return t.root.gameObject;
            t = t.parent;
        }
        return null;
    }

    private void SelectField(ResourceTickBehaviour field)
    {
        // unselect old
        if (_selectedField != null)
        {
            var oldO = _selectedField.GetComponent<FieldOutline>();
            if (oldO != null) oldO.SetSelected(false);
        }

        _selectedField = field;

        var o = _selectedField.GetComponent<FieldOutline>();
        if (o != null) o.SetSelected(true);

        Log($"Selected field: {_selectedField.name} (now click farmers)");
    }

    private void ClearSelection()
    {
        if (_selectedField != null)
        {
            var o = _selectedField.GetComponent<FieldOutline>();
            if (o != null) o.SetSelected(false);
        }
        _selectedField = null;
    }

    private bool RayFromMouse(out RaycastHit hit)
    {
        hit = default;
        var cam = Camera.main;
        if (cam == null) return false;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out hit, 9999f, ~0, QueryTriggerInteraction.Collide);
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

    private void Log(string msg)
    {
        if (debugLogs) Debug.Log("[BuildAssign] " + msg);
    }
}
