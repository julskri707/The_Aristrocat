using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class JobManager : MonoBehaviour
{
    [Header("Tick Connection")]
    public TickSystem tickSystem;
    public bool autoFindTickSystem = true;

    [Header("Optional: only count workers with this tag (leave empty to count all)")]
    public string workerTag = "Bauer";

    private void OnEnable()
    {
        if (tickSystem == null && autoFindTickSystem)
            tickSystem = UnityEngine.Object.FindFirstObjectByType<TickSystem>(FindObjectsInactive.Include);

        if (tickSystem != null)
            tickSystem.onTick.AddListener(OnTick);
        else
            Debug.LogWarning("[JobManager] No TickSystem found.");
    }

    private void OnDisable()
    {
        if (tickSystem != null)
            tickSystem.onTick.RemoveListener(OnTick);
    }

    private void OnTick(long tickIndex)
    {
        UpdateAssignments();
    }

    [ContextMenu("Update Assignments Now")]
    public void UpdateAssignments()
    {
        // 1) reset all fields
        var fields = UnityEngine.Object.FindObjectsByType<ResourceTickBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < fields.Length; i++)
            fields[i].assignedWorkers = 0;

        // 2) count workers and apply to their assigned field
        var workers = UnityEngine.Object.FindObjectsByType<WorkerAssignment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int assignedCount = 0;

        for (int i = 0; i < workers.Length; i++)
        {
            var w = workers[i];
            if (w == null || w.assignedField == null) continue;

            // optional tag filter
            if (!string.IsNullOrEmpty(workerTag))
            {
                // workerTag should be on the same GameObject as WorkerAssignment
                if (!w.gameObject.CompareTag(workerTag)) continue;
            }

            w.assignedField.assignedWorkers += 1;
            assignedCount++;
        }

        // Debug once in a while (you can remove)
        // Debug.Log($"[JobManager] Assigned workers counted: {assignedCount}");
    }
}
