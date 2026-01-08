using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class ResourceTickBehaviour : MonoBehaviour
{
    [Serializable]
    public struct ResourceAmount
    {
        public ResourceManager.ResourceType type;
        [Min(0f)] public float amount; // float => comma values
    }

    public enum MissingInputBehaviour
    {
        SkipAll,          // If any input missing -> do nothing
        PartialProduction // Produce proportionally to available inputs
    }

    public enum WorkAreaMode
    {
        None,
        RequireInsideTrigger,
        RequireWithinRadius
    }

    [Header("Tick Connection")]
    [Tooltip("Optional: assign your TickSystem. If empty, it will auto-find one in the scene.")]
    public TickSystem tickSystem;

    [Tooltip("If true, this component listens to ticks automatically.")]
    public bool active = true;

    [Tooltip("Execute only every N ticks (1 = every tick, 2 = every 2nd tick, etc.)")]
    [Min(1)] public int everyNTicks = 1;

    [Header("Base Scaling")]
    [Tooltip("Everything (inputs + outputs) gets multiplied by this base multiplier.")]
    [Min(0.1f)] public float baseMultiplier = 1f;

    // ---------------- Workers ----------------
    [Header("Workers / Assignment")]
    [Tooltip("If true, production requires assignedWorkers > 0.")]
    public bool requireWorkers = false;

    [Tooltip("How many workers are assigned to this producer (set by JobManager/UI).")]
    [Min(0)] public int assignedWorkers = 0;

    [Tooltip("Optional cap.")]
    [Min(0)] public int maxWorkers = 10;

    [Tooltip("If true, inputs/outputs are multiplied by assignedWorkers (in addition to baseMultiplier).")]
    public bool scaleByWorkers = true;

    // ---------------- Work Area ----------------
    [Header("Work Area Gating")]
    public WorkAreaMode workAreaMode = WorkAreaMode.None;

    [Tooltip("Trigger mode: the worker must be inside a trigger zone with this tag.")]
    public string workAreaTag = "WorkArea";

    [Tooltip("Optional: Use layer mask instead of tag (or as additional filter).")]
    public bool useLayerMask = false;

    public LayerMask workAreaLayerMask = ~0;

    [Tooltip("Radius mode: center transform (e.g. Farm building). If null, it can be found by tag below.")]
    public Transform radiusCenter;

    [Tooltip("Radius mode fallback: find center by tag once (if radiusCenter is null).")]
    public string radiusCenterTag = "";

    [Min(0f)] public float radius = 5f;

    [Tooltip("If true and radiusCenter is missing, production is allowed (not recommended).")]
    public bool allowIfCenterMissing = false;

    // ---------------- Production ----------------
    [Header("Inputs (consumed)")]
    public List<ResourceAmount> inputs = new List<ResourceAmount>();

    [Header("Outputs (produced)")]
    public List<ResourceAmount> outputs = new List<ResourceAmount>();

    [Header("If inputs missing...")]
    public MissingInputBehaviour missingInputBehaviour = MissingInputBehaviour.SkipAll;

    [Tooltip("If true and there are NO inputs, outputs will always be produced (if other gates allow).")]
    public bool allowFreeProductionIfNoInputs = true;

    [Tooltip("Log to console when skipped (debug)")]
    public bool debugLogSkips = false;

    // Robust trigger tracking (start-inside + avoid double counts)
    private readonly HashSet<int> _insideWorkAreas = new HashSet<int>();

    private UnityAction<long> _tickListener;

    private void OnEnable()
    {
        if (!active) return;

        if (tickSystem == null)
            tickSystem = UnityEngine.Object.FindFirstObjectByType<TickSystem>(FindObjectsInactive.Include);

        if (tickSystem == null)
        {
            Debug.LogWarning($"[{name}] ResourceTickBehaviour: No TickSystem found in scene.");
            return;
        }

        _tickListener = OnTick;
        tickSystem.onTick.AddListener(_tickListener);

        // If worker starts already inside the trigger zone, seed state
        SeedInitialTriggerState();
    }

    private void OnDisable()
    {
        if (tickSystem != null && _tickListener != null)
            tickSystem.onTick.RemoveListener(_tickListener);

        _tickListener = null;
        _insideWorkAreas.Clear();
    }

    // ---------------- Trigger detection (3D + 2D) ----------------
    private void AddWorkAreaContact(Component other)
    {
        if (workAreaMode != WorkAreaMode.RequireInsideTrigger) return;
        if (other == null) return;

        var go = other.gameObject;
        if (!IsWorkArea(go)) return;

        _insideWorkAreas.Add(other.GetInstanceID());
    }

    private void RemoveWorkAreaContact(Component other)
    {
        if (workAreaMode != WorkAreaMode.RequireInsideTrigger) return;
        if (other == null) return;

        _insideWorkAreas.Remove(other.GetInstanceID());
    }

    private void OnTriggerEnter(Collider other) => AddWorkAreaContact(other);
    private void OnTriggerExit(Collider other) => RemoveWorkAreaContact(other);
    private void OnTriggerStay(Collider other) => AddWorkAreaContact(other);

    private void OnTriggerEnter2D(Collider2D other) => AddWorkAreaContact(other);
    private void OnTriggerExit2D(Collider2D other) => RemoveWorkAreaContact(other);
    private void OnTriggerStay2D(Collider2D other) => AddWorkAreaContact(other);

    private bool IsWorkArea(GameObject go)
    {
        bool tagOk = !string.IsNullOrEmpty(workAreaTag) && go.CompareTag(workAreaTag);
        bool layerOk = !useLayerMask || ((workAreaLayerMask.value & (1 << go.layer)) != 0);

        if (useLayerMask) return layerOk && (string.IsNullOrEmpty(workAreaTag) || tagOk);
        return tagOk;
    }

    private void SeedInitialTriggerState()
    {
        if (workAreaMode != WorkAreaMode.RequireInsideTrigger) return;

        // Detect trigger zones around current position (handles "start inside")
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.25f, workAreaLayerMask, QueryTriggerInteraction.Collide);
        foreach (var h in hits)
        {
            if (h == null) continue;
            if (IsWorkArea(h.gameObject))
                _insideWorkAreas.Add(h.GetInstanceID());
        }
    }

    // ---------------- Main Tick ----------------
    private void OnTick(long tickIndex)
    {
        if (!active) return;
        if (everyNTicks < 1) everyNTicks = 1;
        if ((tickIndex % everyNTicks) != 0) return;

        var rm = ResourceManager.Instance;
        if (rm == null) return;

        // --- Gate 1: workers ---
        int workers = Mathf.Clamp(assignedWorkers, 0, maxWorkers <= 0 ? int.MaxValue : maxWorkers);
        if (requireWorkers && workers <= 0)
        {
            if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: no workers assigned.");
            return;
        }

        // --- Gate 2: work area ---
        if (workAreaMode == WorkAreaMode.RequireInsideTrigger)
        {
            if (_insideWorkAreas.Count <= 0)
            {
                if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: not inside work area trigger.");
                return;
            }
        }
        else if (workAreaMode == WorkAreaMode.RequireWithinRadius)
        {
            if (radiusCenter == null && !string.IsNullOrEmpty(radiusCenterTag))
            {
                var go = GameObject.FindGameObjectWithTag(radiusCenterTag);
                if (go != null) radiusCenter = go.transform;
            }

            if (radiusCenter == null)
            {
                if (!allowIfCenterMissing)
                {
                    if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: radiusCenter missing.");
                    return;
                }
            }
            else
            {
                float d = Vector3.Distance(transform.position, radiusCenter.position);
                if (d > radius)
                {
                    if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: outside radius ({d:0.00} > {radius}).");
                    return;
                }
            }
        }

        // --- Effective multiplier ---
        float m = Mathf.Max(0.1f, baseMultiplier);
        if (scaleByWorkers) m *= Mathf.Max(0, workers);
        if (m <= 0f)
        {
            if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: effective multiplier is 0.");
            return;
        }

        bool hasInputs = inputs != null && inputs.Count > 0;

        // No inputs defined -> free production (if allowed)
        if (!hasInputs)
        {
            if (!allowFreeProductionIfNoInputs)
            {
                if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: no inputs and free production disabled.");
                return;
            }
            ApplyOutputs(rm, m, 1f);
            return;
        }

        // Inputs exist:
        if (missingInputBehaviour == MissingInputBehaviour.SkipAll)
        {
            if (!CanPayAllInputs(rm, m))
            {
                if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: not enough inputs.");
                return;
            }

            PayAllInputs(rm, m);
            ApplyOutputs(rm, m, 1f);
            return;
        }

        // PartialProduction
        float ratio = ComputeInputRatio(rm, m);
        if (ratio <= 0f)
        {
            if (debugLogSkips) Debug.Log($"[{name}] Tick skipped: ratio=0 (no inputs available).");
            return;
        }

        PayInputsProportional(rm, m, ratio);
        ApplyOutputs(rm, m, ratio);
    }

    // ---------- Input checks ----------
    private bool CanPayAllInputs(ResourceManager rm, float m)
    {
        foreach (var inp in inputs)
        {
            float need = inp.amount * m;
            if (need <= 0f) continue;

            int have = rm.Get(inp.type);
            if (have < Mathf.CeilToInt(need)) return false;
        }
        return true;
    }

    private void PayAllInputs(ResourceManager rm, float m)
    {
        foreach (var inp in inputs)
        {
            float need = inp.amount * m;
            if (need <= 0f) continue;

            // Requires ResourceManager.TryConsumeAmount(type, float)
            rm.TryConsumeAmount(inp.type, need);
        }
    }

    private float ComputeInputRatio(ResourceManager rm, float m)
    {
        float ratio = 1f;

        foreach (var inp in inputs)
        {
            float need = inp.amount * m;
            if (need <= 0f) continue;

            int have = rm.Get(inp.type);
            float r = Mathf.Clamp01(have / need);
            ratio = Mathf.Min(ratio, r);
        }

        return ratio;
    }

    private void PayInputsProportional(ResourceManager rm, float m, float ratio)
    {
        foreach (var inp in inputs)
        {
            float need = inp.amount * m * ratio;
            if (need <= 0f) continue;

            rm.TryConsumeAmount(inp.type, need);
        }
    }

    // ---------- Outputs ----------
    private void ApplyOutputs(ResourceManager rm, float m, float ratio)
    {
        if (outputs == null) return;

        foreach (var outp in outputs)
        {
            float amount = outp.amount * m * ratio;
            if (amount <= 0f) continue;

            // Requires ResourceManager.AddAmount(type, float)
            rm.AddAmount(outp.type, amount);
        }
    }

    // ---------- Worker helpers ----------
    public void SetWorkers(int value) => assignedWorkers = Mathf.Max(0, value);

    [ContextMenu("Debug: Apply Once (as if one tick happened)")]
    private void DebugApplyOnce() => OnTick(1);
}
