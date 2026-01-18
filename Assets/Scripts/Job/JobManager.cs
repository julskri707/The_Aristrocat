using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class JobManager : MonoBehaviour
{
    public static JobManager Instance { get; private set; }

    public TickSystem tickSystem;

    private readonly List<WorkerAssignment> _workers = new();
    private readonly Dictionary<ResourceTickBehaviour, int> _lastApplied = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (tickSystem == null)
            tickSystem = UnityEngine.Object.FindFirstObjectByType<TickSystem>(FindObjectsInactive.Include);

        if (tickSystem == null)
        {
            Debug.LogWarning("JobManager: No TickSystem found.");
            return;
        }

        tickSystem.onPreTick.AddListener(OnPreTick);
    }

    private void OnDisable()
    {
        if (tickSystem != null)
            tickSystem.onPreTick.RemoveListener(OnPreTick);
    }

    public void RegisterWorker(WorkerAssignment w)
    {
        if (w == null) return;
        if (_workers.Contains(w)) return;
        _workers.Add(w);
    }

    public void UnregisterWorker(WorkerAssignment w)
    {
        if (w == null) return;
        _workers.Remove(w);
    }

    private void OnPreTick(long tickIndex)
    {
        // reset previous
        foreach (var kv in _lastApplied)
            if (kv.Key != null) kv.Key.assignedWorkers = 0;

        _lastApplied.Clear();

        // count
        var counts = new Dictionary<ResourceTickBehaviour, int>(64);
        foreach (var w in _workers)
        {
            if (w == null || !w.enabled) continue;
            if (!w.gameObject.activeInHierarchy) continue;

            var site = w.assignedSite;
            if (site == null || !site.enabled) continue;

            counts.TryGetValue(site, out int c);
            counts[site] = c + 1;
        }

        // apply
        foreach (var kv in counts)
        {
            var site = kv.Key;
            if (site == null) continue;

            int desired = kv.Value;
            if (site.maxWorkers > 0) desired = Mathf.Min(desired, site.maxWorkers);

            site.assignedWorkers = desired;
            _lastApplied[site] = desired;
        }
    }
}
