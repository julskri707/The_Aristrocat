using System.Linq;
using UnityEngine;

public class RuntimeAssignController_Tags_Scan : MonoBehaviour
{
    [Header("Camera (optional)")]
    public Camera raycastCamera;

    [Header("Tags")]
    public string fieldTag = "WorkArea";
    public string workerTag = "Bauer";

    [Header("Key")]
    public KeyCode assignKey = KeyCode.A;

    [Header("Worker Scan (über & drunter)")]
    public float scanUp = 3f;
    public float scanDown = 3f;
    public float scanRadius = 0.6f;

    [Header("Debug")]
    public bool debugLogs = true;

    private ResourceTickBehaviour _selectedField;

    private void Awake()
    {
        if (raycastCamera == null) raycastCamera = Camera.main;
    }

    private void Update()
    {
        if (Input.GetKeyDown(assignKey))
        {
            ClearSelection();
            Log("ASSIGN: Klick Feld, dann Klick Bauer.");
        }

        if (Input.GetMouseButtonDown(1)) ClearSelection();

        if (Input.GetMouseButtonDown(0))
            HandleClick();
    }

    private void HandleClick()
    {
        if (!RaycastAllSorted(out var hits))
        {
            Log("Raycast hit nothing.");
            return;
        }

        // 1) Field selection
        var fieldHit = hits.FirstOrDefault(h => HasTagInParents(h.collider.transform, fieldTag));
        if (fieldHit.collider != null)
        {
            var site = fieldHit.collider.GetComponentInParent<ResourceTickBehaviour>();
            if (site != null)
            {
                SelectField(site);
                return;
            }
        }

        // 2) Assign if field selected
        if (_selectedField == null) return;

        // 2a) direct worker hit (PRIORITY: tagged worker object, not root)
        var workerHit = hits.FirstOrDefault(h => HasTagInParents(h.collider.transform, workerTag));
        if (workerHit.collider != null)
        {
            var workerGO = FindTaggedObjectInParents(workerHit.collider.transform, workerTag);
            if (workerGO != null)
            {
                AssignWorker(workerGO);
                return;
            }
        }

        // 2b) scan around click point (if you didn't hit the capsule exactly)
        var p = hits[0].point;
        var found = ScanForWorkerAtPoint(p);
        if (found != null) AssignWorker(found);
        else Log("Kein Bauer im Scan-Bereich gefunden.");
    }

    private GameObject ScanForWorkerAtPoint(Vector3 point)
    {
        Vector3 top = point + Vector3.up * scanUp;
        Vector3 bottom = point - Vector3.up * scanDown;

        var cols = Physics.OverlapCapsule(top, bottom, scanRadius, ~0, QueryTriggerInteraction.Collide);
        if (cols == null || cols.Length == 0) return null;

        GameObject best = null;
        float bestD = float.MaxValue;

        foreach (var c in cols)
        {
            if (c == null) continue;

            var workerGO = FindTaggedObjectInParents(c.transform, workerTag);
            if (workerGO == null) continue;

            float d = Vector3.Distance(workerGO.transform.position, point);
            if (d < bestD) { bestD = d; best = workerGO; }
        }

        return best;
    }

    private void AssignWorker(GameObject workerGO)
    {
        if (workerGO == null) return;

        var wa = workerGO.GetComponent<WorkerAssignment>();
        if (wa == null) wa = workerGO.AddComponent<WorkerAssignment>();

        wa.AssignTo(_selectedField);
        Log($"Assigned '{workerGO.name}' -> '{_selectedField.name}'");
    }

    private void SelectField(ResourceTickBehaviour field)
    {
        if (_selectedField != null)
        {
            var oldFa = _selectedField.GetComponent<FieldArea>();
            if (oldFa != null) oldFa.SetSelected(false);
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

    private bool RaycastAllSorted(out RaycastHit[] hits)
    {
        hits = null;
        if (raycastCamera == null) raycastCamera = Camera.main;
        if (raycastCamera == null) { Log("No camera (Camera.main missing)."); return false; }

        var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);

        hits = Physics.RaycastAll(ray, 9999f, ~0, QueryTriggerInteraction.Collide)
                      .OrderBy(h => h.distance)
                      .ToArray();

        if (debugLogs && hits.Length > 0)
        {
            var first = hits[0].collider;
            Debug.Log($"[AssignScan] First hit: {first.name} tag={first.tag} layer={LayerMask.LayerToName(first.gameObject.layer)}");
        }

        return hits.Length > 0;
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
        if (debugLogs) Debug.Log("[AssignScan] " + msg);
    }
}
