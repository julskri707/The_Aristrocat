using System;
using UnityEngine;
using TMPro;
using UnityEngine.Events;

// If you already have ResourceType elsewhere, delete this enum block.
public enum ResourceType
{
    Gold,
    Wood,
    Stone,
    Iron,
    Food,
    Clothing,
    Loyalty
}

[DisallowMultipleComponent]
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("Resources (floats allowed)")]
    public float Gold = 100f;
    public float Wood = 100f;
    public float Stone = 100f;
    public float Iron = 100f;
    public float Food = 100f;
    public float Clothing = 100f;

    [Tooltip("Often 0..100.")]
    public float Loyalty = 100f;

    [Header("Clamping")]
    public bool clampToZero = true;
    public bool clampLoyaltyTo0_100 = true;

    [Header("Tick (optional: for per-tick deltas in UI)")]
    public TickSystem tickSystem;
    public bool autoFindTickSystem = true;

    [Serializable]
    public class ResourceChangedEvent : UnityEvent<ResourceType, float> { }

    [Header("Events")]
    public ResourceChangedEvent OnResourceChanged;

    // ---------- UI Bindings ----------
    public enum BindingMode
    {
        ResourceValue,
        AllResourcesList
    }

    [Serializable]
    public class UIBinding
    {
        public string name = "Binding";
        public BindingMode mode = BindingMode.ResourceValue;

        [Header("ResourceValue Mode")]
        public ResourceType resource = ResourceType.Gold;

        [Header("Target")]
        public TextMeshProUGUI targetText;

        [Header("Formatting")]
        public bool showPerTick = false;

        // Example: "{label}: {value} (+{perTick}/t)"
        public string template = "{label}: {value}";
        public string labelOverride = "";
        [Range(0, 6)] public int decimals = 0;

        [Header("AllResourcesList Mode")]
        public string lineTemplate = "{label}: {value}";
        public string separator = "\n";
    }

    [Header("UI Bindings (YOU decide what shows where)")]
    public UIBinding[] bindings;

    // ---------- Per-tick delta storage ----------
    private float[] _tickStart;
    private float[] _tickDelta;
    private bool _tickInit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureTickArrays();
    }

    private void OnEnable()
    {
        if (tickSystem == null && autoFindTickSystem)
            tickSystem = UnityEngine.Object.FindFirstObjectByType<TickSystem>(FindObjectsInactive.Include);

        // If your TickSystem exposes onPreTick/onPostTick, we use it.
        // If it doesn't, no problem: TickSytem.cs can call ResourceManager.OnTick() directly.
        if (tickSystem != null)
        {
            // These events may or may not exist in your TickSystem.
            // If they don't exist in your TickSystem, just comment these two lines out.
            tickSystem.onPreTick?.AddListener(OnPreTick);
            tickSystem.onPostTick?.AddListener(OnPostTick);
        }

        RefreshUI();
    }

    private void OnDisable()
    {
        if (tickSystem != null)
        {
            tickSystem.onPreTick?.RemoveListener(OnPreTick);
            tickSystem.onPostTick?.RemoveListener(OnPostTick);
        }
    }

    private void EnsureTickArrays()
    {
        int n = Enum.GetValues(typeof(ResourceType)).Length;

        if (_tickStart == null || _tickStart.Length != n) _tickStart = new float[n];
        if (_tickDelta == null || _tickDelta.Length != n) _tickDelta = new float[n];

        if (!_tickInit)
        {
            for (int i = 0; i < n; i++)
            {
                var rt = (ResourceType)i;
                _tickStart[i] = Get(rt);
                _tickDelta[i] = 0f;
            }
            _tickInit = true;
        }
    }

    // ---------- Compatibility: TickSytem.cs calls this with no args ----------
    public void OnTick()
    {
        OnTick(0);
    }

    // ---------- Compatibility: some systems call with tickIndex ----------
    public void OnTick(long tickIndex)
    {
        EnsureTickArrays();

        for (int i = 0; i < _tickStart.Length; i++)
        {
            var rt = (ResourceType)i;
            float now = Get(rt);
            _tickDelta[i] = now - _tickStart[i];
            _tickStart[i] = now;
        }

        RefreshUI();
    }

    // If your TickSystem supports pre/post, these give exact tick deltas
    private void OnPreTick(long tickIndex)
    {
        EnsureTickArrays();
        for (int i = 0; i < _tickStart.Length; i++)
        {
            var rt = (ResourceType)i;
            _tickStart[i] = Get(rt);
            _tickDelta[i] = 0f;
        }
    }

    private void OnPostTick(long tickIndex)
    {
        EnsureTickArrays();
        for (int i = 0; i < _tickStart.Length; i++)
        {
            var rt = (ResourceType)i;
            _tickDelta[i] = Get(rt) - _tickStart[i];
        }
        RefreshUI();
    }

    // ---------- Public API ----------
    public float Get(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Gold: return Gold;
            case ResourceType.Wood: return Wood;
            case ResourceType.Stone: return Stone;
            case ResourceType.Iron: return Iron;
            case ResourceType.Food: return Food;
            case ResourceType.Clothing: return Clothing;
            case ResourceType.Loyalty: return Loyalty;
            default: return 0f;
        }
    }

    public void Set(ResourceType type, float value)
    {
        value = ApplyClamps(type, value);

        switch (type)
        {
            case ResourceType.Gold: Gold = value; break;
            case ResourceType.Wood: Wood = value; break;
            case ResourceType.Stone: Stone = value; break;
            case ResourceType.Iron: Iron = value; break;
            case ResourceType.Food: Food = value; break;
            case ResourceType.Clothing: Clothing = value; break;
            case ResourceType.Loyalty: Loyalty = value; break;
        }

        OnResourceChanged?.Invoke(type, value);
        RefreshUI();
    }

    public void Add(ResourceType type, float amount)
    {
        Set(type, Get(type) + amount);
    }

    public bool CanAfford(ResourceType type, float amount)
    {
        return Get(type) >= amount;
    }

    public bool TryConsume(ResourceType type, float amount)
    {
        if (!CanAfford(type, amount)) return false;
        Add(type, -amount);
        return true;
    }

    // ---------- UI ----------
    public void RefreshUI()
    {
        if (bindings == null) return;

        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];
            if (b == null || b.targetText == null) continue;

            if (b.mode == BindingMode.ResourceValue)
            {
                string label = string.IsNullOrWhiteSpace(b.labelOverride) ? GetLabel(b.resource) : b.labelOverride;
                string value = FormatValue(Get(b.resource), b.decimals);

                string perTick = "";
                if (b.showPerTick)
                    perTick = FormatValue(GetPerTickDelta(b.resource), b.decimals);

                b.targetText.text = ApplyTemplate(b.template, label, value, perTick);
            }
            else // AllResourcesList
            {
                EnsureTickArrays();
                int n = _tickStart.Length;
                string s = "";

                for (int r = 0; r < n; r++)
                {
                    var rt = (ResourceType)r;
                    string label = GetLabel(rt);
                    string value = FormatValue(Get(rt), b.decimals);

                    string perTick = "";
                    if (b.showPerTick)
                        perTick = FormatValue(GetPerTickDelta(rt), b.decimals);

                    string line = ApplyTemplate(b.lineTemplate, label, value, perTick);

                    if (r > 0) s += b.separator;
                    s += line;
                }

                b.targetText.text = s;
            }
        }
    }

    private float GetPerTickDelta(ResourceType type)
    {
        EnsureTickArrays();
        int i = (int)type;
        if (_tickDelta == null || i < 0 || i >= _tickDelta.Length) return 0f;
        return _tickDelta[i];
    }

    // ---------- Helpers ----------
    private float ApplyClamps(ResourceType type, float v)
    {
        if (clampToZero && v < 0f) v = 0f;

        if (type == ResourceType.Loyalty && clampLoyaltyTo0_100)
            v = Mathf.Clamp(v, 0f, 100f);

        return v;
    }

    private static string GetLabel(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Gold: return "Gold";
            case ResourceType.Wood: return "Wood";
            case ResourceType.Stone: return "Stone";
            case ResourceType.Iron: return "Iron";
            case ResourceType.Food: return "Food";
            case ResourceType.Clothing: return "Clothing";
            case ResourceType.Loyalty: return "Loyalty";
            default: return type.ToString();
        }
    }

    private static string FormatValue(float v, int decimals)
    {
        decimals = Mathf.Clamp(decimals, 0, 6);
        return v.ToString("F" + decimals);
    }

    private static string ApplyTemplate(string template, string label, string value, string perTick)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = "{label}: {value}";

        return template
            .Replace("{label}", label)
            .Replace("{value}", value)
            .Replace("{perTick}", perTick);
    }
}
