// ResourceManager.cs
// Unity 2022+
// - Singleton Instance
// - Manages float resources
// - Supports Add / TryConsume / CanAfford / Get
// - Supports CanAffordAll / TryConsumeAll with IReadOnlyList<ResourceAmount>
// - UI bindings for TMP_Text (and optional legacy UnityEngine.UI.Text)
// - Refresh UI per-resource on change, RefreshAllUI on start
// - Precise warnings when active binding has no text assigned

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("Resources (floats)")]
    public float Gold;
    public float Holz;
    public float Stein;
    public float Eisen;
    public float Essen;
    public float Kleidung;
    public float Loyalität;

    [Header("UI Bindings")]
    public List<UIBinding> uiBindings = new List<UIBinding>();

    /// <summary>Fires when a specific resource changes (type, newValue).</summary>
    public event Action<ResourceType, float> OnResourceChanged;

    /// <summary>Fires when any resource changes (type, newValue).</summary>
    public event Action<ResourceType, float> OnAnyResourceChanged;

    private readonly Dictionary<ResourceType, List<UIBinding>> _bindingsByType = new Dictionary<ResourceType, List<UIBinding>>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ResourceManager] Duplicate instance detected on '{name}'. Destroying this component to keep singleton.");
            Destroy(this);
            return;
        }
        Instance = this;

        BuildBindingCache();
    }

    private void Start()
    {
        RefreshAllUI();
    }

    private void OnValidate()
    {
        // Keep cache up-to-date in editor when bindings change.
        if (!Application.isPlaying)
        {
            BuildBindingCache();
        }
    }

    private void BuildBindingCache()
    {
        _bindingsByType.Clear();

        if (uiBindings == null)
            return;

        for (int i = 0; i < uiBindings.Count; i++)
        {
            var b = uiBindings[i];
            if (b == null) continue;

            if (!_bindingsByType.TryGetValue(b.type, out var list))
            {
                list = new List<UIBinding>();
                _bindingsByType.Add(b.type, list);
            }

            list.Add(b);
        }
    }

    // -----------------------------
    // Public API
    // -----------------------------

    public float Get(ResourceType type)
    {
        return type switch
        {
            ResourceType.Gold => Gold,
            ResourceType.Holz => Holz,
            ResourceType.Stein => Stein,
            ResourceType.Eisen => Eisen,
            ResourceType.Essen => Essen,
            ResourceType.Kleidung => Kleidung,
            ResourceType.Loyalität => Loyalität,
            _ => 0f
        };
    }

    public void Add(ResourceType type, float amount)
    {
        if (Mathf.Approximately(amount, 0f))
            return;

        SetInternal(type, Get(type) + amount, invokeEvents: true);
    }

    public bool CanAfford(ResourceType type, float amount)
    {
        if (amount <= 0f) return true;
        return Get(type) >= amount;
    }

    public bool TryConsume(ResourceType type, float amount)
    {
        if (amount <= 0f) return true;

        float current = Get(type);
        if (current < amount) return false;

        SetInternal(type, current - amount, invokeEvents: true);
        return true;
    }

    public bool CanAffordAll(IReadOnlyList<ResourceAmount> costs)
    {
        if (costs == null) return true;

        for (int i = 0; i < costs.Count; i++)
        {
            var c = costs[i];
            if (c.amount <= 0f) continue;

            if (Get(c.type) < c.amount)
                return false;
        }

        return true;
    }

    public bool TryConsumeAll(IReadOnlyList<ResourceAmount> costs)
    {
        if (!CanAffordAll(costs))
            return false;

        if (costs == null) return true;

        // Apply (we can afford all, so this won't go negative)
        for (int i = 0; i < costs.Count; i++)
        {
            var c = costs[i];
            if (c.amount <= 0f) continue;

            // Uses the same internal pathway with per-resource UI refresh and events.
            TryConsume(c.type, c.amount);
        }

        return true;
    }

    // Optional convenience: set absolute value
    public void Set(ResourceType type, float value)
    {
        SetInternal(type, value, invokeEvents: true);
    }

    // -----------------------------
    // UI
    // -----------------------------

    public void RefreshAllUI()
    {
        // If bindings list changed at runtime, keep cache accurate.
        if (_bindingsByType.Count == 0 && uiBindings != null && uiBindings.Count > 0)
            BuildBindingCache();

        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
        {
            RefreshUIFor(t);
        }
    }

    public void RefreshUIFor(ResourceType type)
    {
        if (!_bindingsByType.TryGetValue(type, out var list) || list == null)
            return;

        float value = Get(type);

        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (b == null) continue;
            if (!b.active) continue;

            // Warning if active but no text assigned
            if (b.tmpText == null && b.uiText == null)
            {
                Debug.LogWarning($"[ResourceManager] UI binding active but no text assigned. Binding index={GetBindingIndex(b)} type={b.type} on ResourceManager '{name}'.");
                continue;
            }

            string label = b.showName ? (type.ToString() + " ") : string.Empty;
            string formattedValue = FormatValue(value, b.format);
            string final = $"{b.prefix}{label}{formattedValue}";

            if (b.tmpText != null)
                b.tmpText.text = final;

            if (b.uiText != null)
                b.uiText.text = final;
        }
    }

    private string FormatValue(float value, string format)
    {
        if (string.IsNullOrEmpty(format))
            return value.ToString();

        // Common case: numeric formats like "0", "0.0", "0.##"
        // If someone puts a composite like "{0:0.0}", support that too.
        if (format.Contains("{0"))
        {
            try { return string.Format(format, value); }
            catch { return value.ToString(); }
        }

        try { return value.ToString(format); }
        catch { return value.ToString(); }
    }

    private int GetBindingIndex(UIBinding binding)
    {
        if (uiBindings == null) return -1;
        return uiBindings.IndexOf(binding);
    }

    // -----------------------------
    // Internals
    // -----------------------------

    private void SetInternal(ResourceType type, float newValue, bool invokeEvents)
    {
        newValue = Mathf.Max(0f, newValue);

        bool changed;
        switch (type)
        {
            case ResourceType.Gold:
                changed = !Mathf.Approximately(Gold, newValue);
                Gold = newValue;
                break;

            case ResourceType.Holz:
                changed = !Mathf.Approximately(Holz, newValue);
                Holz = newValue;
                break;

            case ResourceType.Stein:
                changed = !Mathf.Approximately(Stein, newValue);
                Stein = newValue;
                break;

            case ResourceType.Eisen:
                changed = !Mathf.Approximately(Eisen, newValue);
                Eisen = newValue;
                break;

            case ResourceType.Essen:
                changed = !Mathf.Approximately(Essen, newValue);
                Essen = newValue;
                break;

            case ResourceType.Kleidung:
                changed = !Mathf.Approximately(Kleidung, newValue);
                Kleidung = newValue;
                break;

            case ResourceType.Loyalität:
                changed = !Mathf.Approximately(Loyalität, newValue);
                Loyalität = newValue;
                break;

            default:
                changed = false;
                break;
        }

        if (!changed)
            return;

        RefreshUIFor(type);

        if (invokeEvents)
        {
            float v = Get(type);
            OnResourceChanged?.Invoke(type, v);
            OnAnyResourceChanged?.Invoke(type, v);
        }
    }

    // -----------------------------
    // Types
    // -----------------------------

    public enum ResourceType
    {
        Gold,
        Holz,
        Stein,
        Eisen,
        Essen,
        Kleidung,
        Loyalität
    }

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        public float amount;

        public ResourceAmount(ResourceType type, float amount)
        {
            this.type = type;
            this.amount = amount;
        }
    }

    [Serializable]
    public class UIBinding
    {
        public ResourceType type;

        [Header("Text Targets")]
        public TMP_Text tmpText;
        public Text uiText;

        [Header("Options")]
        public bool active = true;
        public bool showName = false;

        [Tooltip("Text placed before name/value (e.g. \"+\" or \"Gold: \")")]
        public string prefix = "";

        [Tooltip("Numeric format. Examples: \"0\", \"0.0\", \"0.##\" or composite like \"{0:0.0}\"")]
        public string format = "0";
    }
}
