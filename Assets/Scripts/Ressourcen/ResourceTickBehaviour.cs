using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ResourceTickBehaviour : MonoBehaviour
{
    [Header("Tick Connection (NO hard type, avoids Type mismatch)")]
    [Tooltip("Drag ANY Tick object here (TickSytem or TickSystem). If empty it will auto-find by name.")]
    public MonoBehaviour tickBehaviour; // <- any MonoBehaviour, no type mismatch

    [Tooltip("If true, we try to auto-find a tick object in scene by common names.")]
    public bool autoFindTick = true;

    [Header("Tick Settings")]
    public bool active = true;
    [Min(1)] public int everyNTicks = 1;

    [Header("Scaling")]
    public float baseMultiplier = 1f;

    [Header("Workers / Assignment (NO position check!)")]
    public bool requireWorkers = true;
    public int assignedWorkers = 0;
    [Min(0)] public int maxWorkers = 10;
    public bool scaleByWorkers = true;

    [Header("Field Size Scaling")]
    public bool scaleByFieldArea = true;
    public float areaMultiplier = 0.1f;
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

    [Header("Resource Manager (NO hard type, avoids Type mismatch)")]
    [Tooltip("Drag your ResourceManager/RessourceManager object here if you want. If empty it will auto-find.")]
    public MonoBehaviour resourceManagerBehaviour;

    private FieldArea _fieldArea;

    // tick counter if your tick system doesn't call us
    private long _localTickIndex;

    private void Awake()
    {
        _fieldArea = GetComponent<FieldArea>();
    }

    private void OnEnable()
    {
        if (autoFindTick && tickBehaviour == null)
            tickBehaviour = FindTickBehaviour();

        if (resourceManagerBehaviour == null)
            resourceManagerBehaviour = FindResourceManagerBehaviour();

        // If your TickSytem uses SendMessage("OnTick", long) or calls components directly,
        // this script works either way. We also support receiving OnTick via Unity SendMessage.
    }

    // ✅ This method can be called by ANY tick system (even via SendMessage)
    public void OnTick(long tickIndex)
    {
        DoTick(tickIndex);
    }

    // ✅ If your tick system calls OnTick() without parameter
    public void OnTick()
    {
        DoTick(_localTickIndex++);
    }

    // Optional: if nothing calls us, you can test by enabling this:
    [Header("Debug")]
    public bool debugAutoTick = false;
    public float debugSecondsPerTick = 1f;
    private float _acc;

    private void Update()
    {
        if (!debugAutoTick) return;
        _acc += Time.deltaTime;
        if (_acc >= Mathf.Max(0.05f, debugSecondsPerTick))
        {
            _acc = 0f;
            OnTick();
        }
    }

    private void DoTick(long tickIndex)
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

        if (scaleByFieldArea)
        {
            if (_fieldArea == null) _fieldArea = GetComponent<FieldArea>();
            float area = _fieldArea != null ? _fieldArea.GetAreaWorldXZ() : 0f;

            if (clampArea)
                area = Mathf.Clamp(area, minArea, maxArea);

            scale *= Mathf.Max(0f, area * areaMultiplier);
        }

        // Resource manager must have: Get(ResourceType), Add(ResourceType,float), CanAfford(ResourceType,float), TryConsume(ResourceType,float)
        // We call them using SendMessage to avoid type mismatch.
        var rm = resourceManagerBehaviour != null ? resourceManagerBehaviour : FindResourceManagerBehaviour();
        if (rm == null)
        {
            Debug.LogWarning($"[{name}] No ResourceManager/RessourceManager found.");
            return;
        }

        // Check affordability
        if (inputs != null && inputs.Count > 0)
        {
            bool canPay = true;
            for (int i = 0; i < inputs.Count; i++)
            {
                float need = inputs[i].amount * scale;
                if (need <= 0f) continue;

                bool afford = InvokeBool(rm, "CanAfford", inputs[i].type, need);
                if (!afford) { canPay = false; break; }
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
                    float need = inputs[i].amount * scale;
                    if (need <= 0f) continue;

                    InvokeBool(rm, "TryConsume", inputs[i].type, need);
                }
            }
        }

        // Produce
        if (outputs != null && outputs.Count > 0)
        {
            for (int i = 0; i < outputs.Count; i++)
            {
                float give = outputs[i].amount * scale;
                if (give <= 0f) continue;

                InvokeVoid(rm, "Add", outputs[i].type, give);
            }
        }
    }

    // ------------------ Finders ------------------

    private MonoBehaviour FindTickBehaviour()
    {
        // Try common object names
        string[] names = { "TickSytem", "TickSystem", "Tick", "Ticks", "TickManager" };
        for (int i = 0; i < names.Length; i++)
        {
            var go = GameObject.Find(names[i]);
            if (go == null) continue;
            var mb = go.GetComponent<MonoBehaviour>();
            if (mb != null) return mb;
        }

        // fallback: any behaviour in scene with a method named "Register" or "onTick" isn't reliable,
        // so we just return null and assume tick will call OnTick on us.
        return null;
    }

    private MonoBehaviour FindResourceManagerBehaviour()
    {
        // Try common object names
        string[] names = { "Resource manager", "ResourceManager", "RessourceManager", "RessourcenManager" };
        for (int i = 0; i < names.Length; i++)
        {
            var go = GameObject.Find(names[i]);
            if (go == null) continue;
            var mb = go.GetComponent<MonoBehaviour>();
            if (mb != null) return mb;
        }

        // fallback: first MonoBehaviour in scene that has method "Add"
        var all = GameObject.FindObjectsOfType<MonoBehaviour>(true);
        foreach (var mb in all)
        {
            if (mb == null) continue;
            var t = mb.GetType();
            if (t.GetMethod("Add") != null && t.GetMethod("TryConsume") != null)
                return mb;
        }

        return null;
    }

    // ------------------ Reflection calls ------------------

    private static void InvokeVoid(MonoBehaviour target, string method, ResourceType type, float amount)
    {
        var m = target.GetType().GetMethod(method);
        if (m == null) return;
        m.Invoke(target, new object[] { type, amount });
    }

    private static bool InvokeBool(MonoBehaviour target, string method, ResourceType type, float amount)
    {
        var m = target.GetType().GetMethod(method);
        if (m == null) return false;
        object r = m.Invoke(target, new object[] { type, amount });
        return r is bool b && b;
    }
}
