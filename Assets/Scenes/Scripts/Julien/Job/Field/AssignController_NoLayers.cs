// AssignController_NoLayers.cs
// ONLY change: ensure old field is unselected and new selected on field click (already mostly present),
// kept here for clarity. If your current file already does this, you can keep it as-is.

using UnityEngine;
using UnityEngine.EventSystems;

public class AssignController_NoLayers : MonoBehaviour
{
    [Header("Mode")]
    public bool assignMode = false;

    [Tooltip("If true, after a successful assignment the mode is turned off. If false, mode stays on and only selection is cleared.")]
    public bool exitModeAfterAssign = false;

    [Header("UI Click Ignore")]
    public bool ignoreClicksOverUI = true;

    [Header("Field Visuals")]
    public bool useFieldSelectedVisual = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private FieldArea _selectedField;
    private WorkerAssignment _selectedWorker;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            assignMode = !assignMode;

            if (debugLogs)
                Debug.Log($"[AssignController_NoLayers] AssignMode toggled => {assignMode}");

            if (!assignMode)
                ClearSelection();
        }

        if (!assignMode)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (debugLogs)
                Debug.Log("[AssignController_NoLayers] ESC -> cancel current selection.");
            ClearSelection();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (debugLogs)
                Debug.Log($"[AssignController_NoLayers] Enter pressed. Field={(_selectedField ? _selectedField.name : "NULL")} Worker={(_selectedWorker ? _selectedWorker.name : "NULL")}");
            TryCompleteAssignment("Enter");
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Debug.LogWarning("[AssignController_NoLayers] Click ignored because pointer is over UI (EventSystem.IsPointerOverGameObject == true).");
                return;
            }

            HandleClick();
        }
    }

    private void HandleClick()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[AssignController_NoLayers] Camera.main is null (tag a camera as MainCamera).");
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);

        if (hits == null || hits.Length == 0)
        {
            Debug.LogWarning("[AssignController_NoLayers] Click found no colliders (Physics.RaycastAll returned 0 hits).");
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (_selectedField == null)
        {
            if (TryPickField(hits, out FieldArea field))
            {
                SelectField(field); // ensures old false, new true
                return;
            }

            Debug.LogWarning("[AssignController_NoLayers] No valid FieldArea found under click.");
            return;
        }

        if (_selectedWorker == null)
        {
            if (TryPickWorker(hits, out WorkerAssignment worker))
            {
                _selectedWorker = worker;

                if (debugLogs)
                    Debug.Log($"[AssignController_NoLayers] Selected Worker '{worker.name}'. Attempting assignment immediately.");

                TryCompleteAssignment("Click");
                return;
            }

            // Allow switching field with a click (also updates highlights)
            if (TryPickField(hits, out FieldArea newField))
            {
                SelectField(newField); // ensures old false, new true
                return;
            }

            Debug.LogWarning("[AssignController_NoLayers] No valid WorkerAssignment found under click (and no alternative FieldArea).");
            return;
        }

        if (TryPickWorker(hits, out WorkerAssignment w2))
        {
            _selectedWorker = w2;
            if (debugLogs)
                Debug.Log($"[AssignController_NoLayers] Re-selected Worker '{w2.name}'.");
            TryCompleteAssignment("Click");
            return;
        }

        if (TryPickField(hits, out FieldArea f2))
        {
            SelectField(f2); // ensures old false, new true
            return;
        }

        Debug.LogWarning("[AssignController_NoLayers] Click did not hit a selectable FieldArea or WorkerAssignment.");
    }

    private bool TryPickField(RaycastHit[] hits, out FieldArea field)
    {
        field = null;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;

            FieldArea fa = h.collider.GetComponentInParent<FieldArea>();
            if (fa == null) continue;

            Vector3 projected = new Vector3(h.point.x, 0f, h.point.z);

            if (!fa.ContainsWorldPoint(projected))
            {
                if (debugLogs)
                    Debug.Log($"[AssignController_NoLayers] Hit FieldArea '{fa.name}' but projected point is outside polygon. (Hit collider='{h.collider.name}', point={h.point})");
                continue;
            }

            field = fa;
            if (debugLogs)
                Debug.Log($"[AssignController_NoLayers] Picked FieldArea '{fa.name}' via collider '{h.collider.name}', dist={h.distance:0.###}");
            return true;
        }

        return false;
    }

    private bool TryPickWorker(RaycastHit[] hits, out WorkerAssignment worker)
    {
        worker = null;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;

            WorkerAssignment wa = h.collider.GetComponentInParent<WorkerAssignment>();
            if (wa == null) continue;

            worker = wa;

            if (debugLogs)
                Debug.Log($"[AssignController_NoLayers] Picked WorkerAssignment '{wa.name}' via collider '{h.collider.name}', dist={h.distance:0.###}");
            return true;
        }

        return false;
    }

    private void SelectField(FieldArea field)
    {
        if (field == _selectedField)
            return;

        if (_selectedField != null && useFieldSelectedVisual)
            _selectedField.SetSelected(false);

        _selectedField = field;

        if (_selectedField != null && useFieldSelectedVisual)
            _selectedField.SetSelected(true);

        _selectedWorker = null;

        if (debugLogs)
            Debug.Log($"[AssignController_NoLayers] Selected Field '{_selectedField.name}'. Worker selection cleared.");
    }

    private void TryCompleteAssignment(string source)
    {
        if (_selectedField == null || _selectedWorker == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[AssignController_NoLayers] Cannot confirm assignment from {source}: Field={(_selectedField ? _selectedField.name : "NULL")} Worker={(_selectedWorker ? _selectedWorker.name : "NULL")}.");
            return;
        }

        var rtb = _selectedField.GetComponent<ResourceTickBehaviour>();
        if (rtb == null)
        {
            Debug.LogWarning($"[AssignController_NoLayers] Field '{_selectedField.name}' has no ResourceTickBehaviour. Assignment aborted.");
            return;
        }

        _selectedWorker.AssignTo(rtb);

        if (debugLogs)
            Debug.Log($"[AssignController_NoLayers] Assignment completed ({source}): Worker '{_selectedWorker.name}' -> Field '{_selectedField.name}'.");

        if (exitModeAfterAssign)
        {
            assignMode = false;
            ClearSelection();
            if (debugLogs)
                Debug.Log("[AssignController_NoLayers] exitModeAfterAssign=true -> AssignMode turned OFF.");
        }
        else
        {
            ClearSelection(keepMode: true);
        }
    }

    private void ClearSelection(bool keepMode = false)
    {
        if (_selectedField != null && useFieldSelectedVisual)
            _selectedField.SetSelected(false);

        _selectedField = null;
        _selectedWorker = null;

        if (debugLogs)
            Debug.Log($"[AssignController_NoLayers] Selection cleared. AssignMode={assignMode}");
    }
}
