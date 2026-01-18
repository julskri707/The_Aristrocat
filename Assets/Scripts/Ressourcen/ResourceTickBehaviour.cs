using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ResourceTickBehaviour : MonoBehaviour
{
    [Header("Tick Connection")]
    public TickSystem tickSystem;
    public bool autoFindTickSystem = true;

    [Header("Tick Settings")]
    public bool active = true;
    [Min(1)] public int everyNTicks = 1;

    [Header("Scaling")]
    public float baseMultiplier = 1f;

    [Header("Workers / Assignment (NO position check!)")]
    public bool requireWorkers = true;

    [Tooltip("Set by JobManager based on assignments (Bauer must only be assigned, not standing inside).")]
    public int assignedWorkers = 0;

    [Min(0)] public int maxWorkers = 10;
    public bool scaleByWorkers = true;

    [Header("Field Size Scaling (NEW)")]
    public bool scaleByFieldArea = true;

    [Tooltip("Income scales by (area * areaMultiplier). Example: area=20, multiplier=0.1 => x2.")]
    public float areaMultiplier = 0.1f;

    public bool clampArea = true;
    public float minArea = 1f;
    public float maxArea = 200f;

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        public float amount; // decimals allowed
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

    [Header("Resource Manager (optional, else uses ResourceManager.Instance)")]
    public ResourceManager resourceManager;

    private FieldArea _fieldArea;

    private void Awake()
    {
        _fieldArea = GetComponent<FieldArea>();
    }

    private void OnEnable()
    {
        if (tickSystem == null && autoFindTickSystem)
            tickSystem = UnityEngine.Object.FindFirstObjectByType<TickSystem>(FindObjectsInactive.Include);

        if (tickSystem != null)
            tickSystem.onTick.AddListener(OnTick);
        else
            Debug.LogWarning($"[{name}] ResourceTickBehaviour: No TickSystem found.");
    }

    private void OnDisable()
    {
        if (tickSystem != null)
            tickSystem.onTick.RemoveListener(OnTick);
    }

    private void OnTick(long tickIndex)
    {
        if (!active) return;
        if (everyNTicks > 1 && (tickIndex % everyNTicks) != 0) return;

        int workers = Mathf.Max(0, assignedWorkers);
        if (maxWorkers > 0) workers = Mathf.Min(workers, maxWorkers);

        if (requireWorkers && workers <= 0)
            return;

        float scale = baseMultiplier;

        if (scaleByWorkers)
            scale *= Mathf.Max(1, workers);

        // ✅ NEW: Field area scaling
        if (scaleByFieldArea)
        {
            if (_fieldArea == null) _fieldArea = GetComponent<FieldArea>();

            float area = 0f;
            if (_fieldArea != null)
                area = _fieldArea.GetAreaWorldXZ();

            if (clampArea)
                area = Mathf.Clamp(area, minArea, maxArea);

            // multiply by area*multiplier (so you can tune it)
            scale *= Mathf.Max(0f, area * areaMultiplier);
        }

        var rm = resourceManager != null ? resourceManager : ResourceManager.Instance;
        if (rm == null)
        {
            Debug.LogWarning($"[{name}] No ResourceManager assigned/found.");
            return;
        }

        // Check & consume inputs
        if (inputs != null && inputs.Count > 0)
        {
            bool canPay = true;
            for (int i = 0; i < inputs.Count; i++)
            {
                var req = inputs[i];
                float need = req.amount * scale;
                if (need <= 0f) continue;

                if (!rm.CanAfford(req.type, need))
                {
                    canPay = false;
                    break;
                }
            }

            if (!canPay)
            {
                if (missingInputBehaviour == MissingInputBehaviour.SkipAll)
                    return;
            }
            else
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    var req = inputs[i];
                    float need = req.amount * scale;
                    if (need <= 0f) continue;

                    rm.TryConsume(req.type, need);
                }
            }
        }

        // Produce outputs
        if (outputs != null && outputs.Count > 0)
        {
            for (int i = 0; i < outputs.Count; i++)
            {
                var prod = outputs[i];
                float give = prod.amount * scale;
                if (give <= 0f) continue;

                rm.Add(prod.type, give);
            }
        }
    }
}
