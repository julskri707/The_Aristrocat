// ResourceTickBehaviour.cs
// Unity 2022+
// Produces resources on Tick AFTER workers are assigned.
// - Works with ResourceManager, TickSystem, optional FieldArea
// - No LayerMasks, no SendMessage
// - Registers/unregisters itself with TickSystem
// - Scales by workers and/or by field area
// - Clear warnings when references are missing or production is blocked

using System.Collections.Generic;
using UnityEngine;

// Aliases so we can use the requested names "ResourceAmount" / "ResourceType"
// while staying compatible with the ResourceManager implementation you use.
using ResourceAmount = ResourceManager.ResourceAmount;
using ResourceType = ResourceManager.ResourceType;

public class ResourceTickBehaviour : MonoBehaviour
{
    [Header("References (auto-find if null)")]
    public ResourceManager resourceManager;
    public TickSystem tickSystem;
    public FieldArea fieldArea;

    [Header("Tick IO")]
    public List<ResourceAmount> inputs = new List<ResourceAmount>();
    public List<ResourceAmount> outputs = new List<ResourceAmount>();

    [Header("Worker Requirement")]
    public bool requireWorkers = true;
    public int minWorkers = 1;

    [Header("Scaling")]
    public bool scaleByWorkers = false;
    public bool scaleByArea = false;

    [Tooltip("If scaleByArea is enabled, multiplier *= (fieldArea.GetAreaWorldXZ() * areaMultiplier).")]
    public float areaMultiplier = 1f;

    [Header("State / Debug")]
    public bool isActive = true;
    public bool debugLogs = false;

    // Worker tracking (no assumptions about WorkerAssignment internals)
    private readonly List<WorkerAssignment> _workers = new List<WorkerAssignment>();

    // Reusable buffers to avoid allocations every tick
    private readonly List<ResourceAmount> _scaledInputs = new List<ResourceAmount>(8);
    private readonly List<ResourceAmount> _scaledOutputs = new List<ResourceAmount>(8);

    private void Awake()
    {
        AutoFindReferences();
    }

    private void OnEnable()
    {
        AutoFindReferences();

        if (tickSystem == null)
        {
            Debug.LogWarning($"[ResourceTickBehaviour] TickSystem reference missing on '{name}'. Production will not run.");
            return;
        }

        tickSystem.Register(this);

        if (debugLogs)
            Debug.Log($"[ResourceTickBehaviour] Registered with TickSystem on '{name}'.");
    }

    private void OnDisable()
    {
        if (tickSystem != null)
        {
            tickSystem.Unregister(this);

            if (debugLogs)
                Debug.Log($"[ResourceTickBehaviour] Unregistered from TickSystem on '{name}'.");
        }
    }

    private void OnDestroy()
    {
        if (tickSystem != null)
            tickSystem.Unregister(this);
    }

    private void AutoFindReferences()
    {
        if (resourceManager == null)
        {
            resourceManager = FindFirstObjectByType<ResourceManager>();
            if (resourceManager == null)
                Debug.LogWarning($"[ResourceTickBehaviour] No ResourceManager found in scene for '{name}'.");
            else if (debugLogs)
                Debug.Log($"[ResourceTickBehaviour] Auto-found ResourceManager for '{name}'.");
        }

        if (tickSystem == null)
        {
            tickSystem = FindFirstObjectByType<TickSystem>();
            if (tickSystem == null)
                Debug.LogWarning($"[ResourceTickBehaviour] No TickSystem found in scene for '{name}'.");
            else if (debugLogs)
                Debug.Log($"[ResourceTickBehaviour] Auto-found TickSystem for '{name}'.");
        }

        if (fieldArea == null)
        {
            // Prefer local / parent FieldArea (common for fields/buildings placed on/inside areas)
            fieldArea = GetComponent<FieldArea>();
            if (fieldArea == null)
                fieldArea = GetComponentInParent<FieldArea>();

            if (fieldArea != null && debugLogs)
                Debug.Log($"[ResourceTickBehaviour] Auto-found FieldArea '{fieldArea.name}' for '{name}'.");
        }

        if (minWorkers < 1) minWorkers = 1;
        if (areaMultiplier < 0f) areaMultiplier = 0f;
    }

    // -----------------------------
    // Worker API
    // -----------------------------

    public void RegisterWorker(WorkerAssignment w)
    {
        if (w == null)
        {
            Debug.LogWarning($"[ResourceTickBehaviour] RegisterWorker called with null on '{name}'.");
            return;
        }

        CleanNullWorkers();

        if (_workers.Contains(w))
            return;

        _workers.Add(w);

        if (debugLogs)
            Debug.Log($"[ResourceTickBehaviour] Worker registered on '{name}'. WorkerCount={_workers.Count}");
    }

    public void UnregisterWorker(WorkerAssignment w)
    {
        if (w == null)
        {
            Debug.LogWarning($"[ResourceTickBehaviour] UnregisterWorker called with null on '{name}'.");
            return;
        }

        CleanNullWorkers();

        bool removed = _workers.Remove(w);

        if (debugLogs)
            Debug.Log($"[ResourceTickBehaviour] Worker unregistered on '{name}'. removed={removed} WorkerCount={_workers.Count}");
    }

    public int GetWorkerCount()
    {
        CleanNullWorkers();
        return _workers.Count;
    }

    private void CleanNullWorkers()
    {
        for (int i = _workers.Count - 1; i >= 0; i--)
        {
            if (_workers[i] == null)
                _workers.RemoveAt(i);
        }
    }

    // -----------------------------
    // Tick callback (called by TickSystem)
    // -----------------------------

    public void OnTick(int tickIndex)
    {
        if (!isActive || !enabled || !gameObject.activeInHierarchy)
            return;

        if (resourceManager == null)
        {
            Debug.LogWarning($"[ResourceTickBehaviour] Missing ResourceManager on '{name}'. Tick {tickIndex} skipped.");
            return;
        }

        // Worker gating
        int workerCount = GetWorkerCount();
        if (requireWorkers && workerCount < minWorkers)
        {
            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[ResourceTickBehaviour] '{name}' blocked on Tick {tickIndex}: " +
                    $"requireWorkers=true but WorkerCount={workerCount} < minWorkers={minWorkers}."
                );
            }
            return;
        }

        // Multiplier
        float multiplier = 1f;

        if (scaleByWorkers)
        {
            // Scale linearly with worker count
            multiplier *= Mathf.Max(0, workerCount);
        }

        if (scaleByArea)
        {
            if (fieldArea == null)
            {
                Debug.LogWarning($"[ResourceTickBehaviour] '{name}' scaleByArea=true but FieldArea is null. Area scaling ignored on Tick {tickIndex}.");
            }
            else
            {
                float area = fieldArea.GetAreaWorldXZ();
                float areaScale = area * areaMultiplier;

                if (areaScale < 0f) areaScale = 0f;
                multiplier *= areaScale;
            }
        }

        if (multiplier <= 0f)
        {
            if (debugLogs)
                Debug.LogWarning($"[ResourceTickBehaviour] '{name}' multiplier <= 0 on Tick {tickIndex}. Nothing to do.");
            return;
        }

        // Prepare scaled costs/outputs
        BuildScaledList(inputs, _scaledInputs, multiplier);
        BuildScaledList(outputs, _scaledOutputs, multiplier);

        // If there are inputs, check affordability
        if (_scaledInputs.Count > 0 && !resourceManager.CanAffordAll(_scaledInputs))
        {
            if (debugLogs)
                Debug.LogWarning($"[ResourceTickBehaviour] '{name}' cannot afford inputs on Tick {tickIndex}. Production skipped.");
            return;
        }

        // Consume inputs
        if (_scaledInputs.Count > 0)
        {
            bool consumed = resourceManager.TryConsumeAll(_scaledInputs);
            if (!consumed)
            {
                Debug.LogWarning($"[ResourceTickBehaviour] '{name}' TryConsumeAll failed unexpectedly on Tick {tickIndex}. Production aborted.");
                return;
            }
        }

        // Produce outputs
        for (int i = 0; i < _scaledOutputs.Count; i++)
        {
            var o = _scaledOutputs[i];
            if (o.amount <= 0f) continue;

            resourceManager.Add(o.type, o.amount);
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[ResourceTickBehaviour] '{name}' produced on Tick {tickIndex}. " +
                $"multiplier={multiplier:0.###}, workers={workerCount}, inputs={_scaledInputs.Count}, outputs={_scaledOutputs.Count}"
            );
        }
    }

    private static void BuildScaledList(List<ResourceAmount> src, List<ResourceAmount> dst, float multiplier)
    {
        dst.Clear();
        if (src == null || src.Count == 0)
            return;

        for (int i = 0; i < src.Count; i++)
        {
            var ra = src[i];
            if (ra.amount <= 0f)
                continue;

            float scaled = ra.amount * multiplier;
            if (scaled <= 0f)
                continue;

            dst.Add(new ResourceAmount(ra.type, scaled));
        }
    }
}
