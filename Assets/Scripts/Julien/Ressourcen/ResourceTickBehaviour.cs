using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ResourceTickBehaviour : MonoBehaviour
{
    [Header("Run")]
    public bool active = true;

    [Tooltip("If true, production only happens when at least 1 worker assigned.")]
    public bool requireWorkers = true;

    [Header("Workers")]
    [Tooltip("If true, counts WorkerAssignment components assigned to THIS field each tick.")]
    public bool autoCountAssignedWorkers = true;

    [Min(0)] public int assignedWorkers = 0;
    [Min(0)] public int maxWorkers = 10;

    [Header("Tick Frequency")]
    [Min(1)] public int everyNTicks = 1;

    [Header("Scaling")]
    public float baseMultiplier = 1f;

    [Tooltip("Multiply by number of workers (min 1).")]
    public bool scaleByWorkers = true;

    [Tooltip("Multiply by field area.")]
    public bool scaleByFieldArea = true;

    public float areaMultiplier = 0.1f;

    [Tooltip("Clamp area used for scaling.")]
    public bool clampArea = true;
    public float minArea = 1f;
    public float maxArea = 200f;

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        public float amount;
    }

    [Header("Inputs (consumed)")]
    public List<ResourceAmount> inputs = new();

    [Header("Outputs (produced)")]
    public List<ResourceAmount> outputs = new();

    public enum MissingInputBehaviour
    {
        SkipAll,
        AllowFreeProduction
    }

    [Header("If inputs missing...")]
    public MissingInputBehaviour missingInputBehaviour = MissingInputBehaviour.SkipAll;

    [Header("References (auto)")]
    [Tooltip("Optional: drag your ResourceManager here. If empty it will auto-find.")]
    public ResourceManager resourceManager;

    [Tooltip("Optional: drag your TickSystem here. If empty it will auto-find.")]
    public MonoBehaviour tickSystemLike; // supports TickSystem or TickSytem without type mismatch

    [Header("Debug")]
    public bool debugLogs = false;

    [Tooltip("If true, it will tick itself (for testing).")]
    public bool debugAutoTick = false;

    public float debugSecondsPerTick = 1f;

    private float _acc;
    private long _localTick;

    private FieldArea _fieldArea;

    private void Awake()
    {
        _fieldArea = GetComponent<FieldArea>();
        AutoFindRefs();
        TryRegisterToTickSystem();
    }

    private void OnEnable()
    {
        AutoFindRefs();
        TryRegisterToTickSystem();
    }

    private void AutoFindRefs()
    {
        if (resourceManager == null)
        {
            resourceManager = ResourceManager.Instance != null
                ? ResourceManager.Instance
                : FindFirstObjectByType<ResourceManager>(FindObjectsInactive.Include);
        }

        if (tickSystemLike == null)
        {
            // Find any MonoBehaviour on an object named TickSystem/TickSytem
            var go = GameObject.Find("TickSystem") ?? GameObject.Find("TickSytem") ?? GameObject.Find("Tick");
            if (go != null) tickSystemLike = go.GetComponent<MonoBehaviour>();
        }
    }

    /// <summary>
    /// Optional: if your TickSystem has a method "Register(ResourceTickBehaviour)" we use it.
    /// If not, it's fine: TickSystem may already SendMessage("OnTick", ...) globally.
    /// </summary>
    private void TryRegisterToTickSystem()
    {
        if (tickSystemLike == null) return;

        var m = tickSystemLike.GetType().GetMethod("Register");
        if (m != null)
        {
            try
            {
                m.Invoke(tickSystemLike, new object[] { this });
                if (debugLogs) Debug.Log($"[ResourceTick] Registered to {tickSystemLike.GetType().Name}");
            }
            catch { /* ignore */ }
        }
    }

    private void Update()
    {
        if (!debugAutoTick) return;

        _acc += Time.deltaTime;
        if (_acc >= Mathf.Max(0.05f, debugSecondsPerTick))
        {
            _acc = 0f;
            OnTick(_localTick++);
        }
    }

    // Called by TickSystem (preferred)
    public void OnTick(long tickIndex)
    {
        DoTick(tickIndex);
    }

    // Called by TickSystem if it uses SendMessage without args
    public void OnTick()
    {
        DoTick(_localTick++);
    }

    private void DoTick(long tickIndex)
    {
        if (!active) return;
        if (everyNTicks > 1 && (tickIndex % everyNTicks) != 0) return;

        AutoFindRefs();
        if (resourceManager == null)
        {
            if (debugLogs) Debug.LogWarning($"[ResourceTick] No ResourceManager found on {name}");
            return;
        }

        if (autoCountAssignedWorkers)
            assignedWorkers = CountAssignedWorkers();

        int workers = Mathf.Max(0, assignedWorkers);
        if (maxWorkers > 0) workers = Mathf.Min(workers, maxWorkers);

        if (requireWorkers && workers <= 0)
        {
            if (debugLogs) Debug.Log($"[ResourceTick] {name} skipped: no workers");
            return;
        }

        float scale = baseMultiplier;

        if (scaleByWorkers)
            scale *= Mathf.Max(1, workers);

        if (scaleByFieldArea)
        {
            if (_fieldArea == null) _fieldArea = GetComponent<FieldArea>();
            float area = _fieldArea != null ? _fieldArea.GetAreaWorldXZ() : 0f;
            if (clampArea) area = Mathf.Clamp(area, minArea, maxArea);
            scale *= Mathf.Max(0f, area * areaMultiplier);
        }

        // Inputs
        bool canPay = true;
        if (inputs != null && inputs.Count > 0)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                float need = inputs[i].amount * scale;
                if (need <= 0f) continue;

                if (!resourceManager.CanAfford(inputs[i].type, need))
                {
                    canPay = false;
                    break;
                }
            }

            if (!canPay && missingInputBehaviour == MissingInputBehaviour.SkipAll)
            {
                if (debugLogs) Debug.Log($"[ResourceTick] {name} skipped: missing inputs");
                return;
            }

            if (canPay)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    float need = inputs[i].amount * scale;
                    if (need <= 0f) continue;
                    resourceManager.TryConsume(inputs[i].type, need);
                }
            }
        }

        // Outputs
        if (outputs != null && outputs.Count > 0)
        {
            for (int i = 0; i < outputs.Count; i++)
            {
                float give = outputs[i].amount * scale;
                if (give <= 0f) continue;

                resourceManager.Add(outputs[i].type, give);
            }
        }

        if (debugLogs)
            Debug.Log($"[ResourceTick] {name} tick {tickIndex} workers={workers} scale={scale:0.###}");
    }

    private int CountAssignedWorkers()
    {
        // Count ALL WorkerAssignment in scene assigned to THIS field’s ResourceTickBehaviour
        var all = FindObjectsByType<WorkerAssignment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int c = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var wa = all[i];
            if (wa != null && wa.assignedField == this) c++;
        }
        return c;
    }
}
