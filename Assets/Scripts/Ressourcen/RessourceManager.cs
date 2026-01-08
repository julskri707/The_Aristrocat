// ResourceManager.cs
// One big script: resources + tick logic + UI binding.
// IMPORTANT CHANGE: In bindings you can now drag the whole Text GameObject.
// The script will automatically find Text or TMP_Text on that object.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ResourceManager : MonoBehaviour
{
    // ---------- TYPES ----------
    public enum ResourceType { Gold, Wood, Stone, Iron, Food, Clothing, Loyalty }

    [Serializable]
    public struct ResourceStart
    {
        public int gold, wood, stone, iron, food, clothing;
        [Range(0, 100)] public int loyalty;
    }

    [Serializable]
    public struct TickSettings
    {
        [Header("Optional internal tick (for testing)")]
        public bool autoTick;
        public float secondsPerTick;

        [Header("Population & Upkeep (per tick)")]
        [Min(0)] public int population;
        [Min(0)] public float foodPerPerson;
        [Min(0)] public float clothingPerPerson;

        [Header("Loyalty effects (per tick)")]
        [Min(0)] public int loyaltyLossIfFoodMissing;
        [Min(0)] public int loyaltyLossIfClothingMissing;
        [Min(0)] public int loyaltyGainIfSupplied;
    }

    [Serializable]
    public class ResourceChangedEvent : UnityEvent<ResourceType, int> { }

    public enum BindingMode
    {
        ResourceValue,
        AllResourcesMultiline
    }

    [Serializable]
    public class UITextBinding
    {
        public string name = "Binding";
        public BindingMode mode = BindingMode.ResourceValue;

        public ResourceType resourceType = ResourceType.Gold;

        [Tooltip("Drag the WHOLE Text GameObject here (e.g. 'Gold'). " +
                 "The script will auto-find Text or TMP_Text on it. You can also drag a Text/TMP component directly.")]
        public UnityEngine.Object target; // GameObject, Component, Transform, etc.

        [Header("Formatting")]
        public bool showPerTickInfo = true;

        [Tooltip("Tokens: {label} {value} {income} {upkeep} {perTick} {pop}")]
        public string template = "{label}: {value} ({perTick}/tick)";

        public string labelOverride = "";

        [Tooltip("Only for AllResourcesMultiline: line template per resource.")]
        public string lineTemplate = "{label}: {value}";
    }

    // ---------- INSPECTOR ----------
    [Header("Start Values")]
    [SerializeField]
    private ResourceStart startValues = new ResourceStart
    {
        gold = 300,
        wood = 80,
        stone = 40,
        iron = 10,
        food = 120,
        clothing = 25,
        loyalty = 70
    };

    [Header("Tick")]
    [SerializeField]
    public TickSettings tick = new TickSettings
    {
        autoTick = false,
        secondsPerTick = 1.0f,
        population = 10,
        foodPerPerson = 0.5f,
        clothingPerPerson = 0.05f,
        loyaltyLossIfFoodMissing = 2,
        loyaltyLossIfClothingMissing = 1,
        loyaltyGainIfSupplied = 1
    };

    [Header("Per-Tick Income (Production)")]
    [SerializeField] private float incomeGold;
    [SerializeField] private float incomeWood;
    [SerializeField] private float incomeStone;
    [SerializeField] private float incomeIron;
    [SerializeField] private float incomeFood;
    [SerializeField] private float incomeClothing;

    [Header("UI Bindings (YOU control what shows where)")]
    [SerializeField] private List<UITextBinding> bindings = new List<UITextBinding>();

    [Header("Events")]
    public ResourceChangedEvent onResourceChanged;

    // ---------- RUNTIME ----------
    public static ResourceManager Instance { get; private set; }

    private readonly Dictionary<ResourceType, int> _values = new();
    private readonly Dictionary<ResourceType, float> _floatAccu = new();
    private float _tickTimer;

    private readonly List<(UITextBinding binding, ITextAdapter adapter)> _cached =
        new List<(UITextBinding binding, ITextAdapter adapter)>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        InitDictionaries();
        SetStartValues();

        CacheBindings();
        RefreshAllUI();
    }

    private void OnValidate()
    {
        if (tick.secondsPerTick < 0.01f) tick.secondsPerTick = 0.01f;
        if (tick.population < 0) tick.population = 0;
    }

    private void Update()
    {
        if (!tick.autoTick) return;

        _tickTimer += Time.deltaTime;
        while (_tickTimer >= tick.secondsPerTick)
        {
            _tickTimer -= tick.secondsPerTick;
            OnTick();
        }
    }

    // ---------- PUBLIC API ----------
    public int Get(ResourceType type) => _values[type];

    public void Set(ResourceType type, int value)
    {
        int clamped = Clamp(type, value);
        if (_values[type] == clamped) return;

        _values[type] = clamped;
        onResourceChanged?.Invoke(type, clamped);
        RefreshAllUI();
    }

    public void Add(ResourceType type, int amount)
    {
        if (amount == 0) return;
        Set(type, _values[type] + amount);
    }

    public bool TrySpend(ResourceType type, int amount)
    {
        if (amount <= 0) return true;
        if (_values[type] < amount) return false;
        Set(type, _values[type] - amount);
        return true;
    }

    public void OnTick()
    {
        // Income
        AddFloat(ResourceType.Gold, incomeGold);
        AddFloat(ResourceType.Wood, incomeWood);
        AddFloat(ResourceType.Stone, incomeStone);
        AddFloat(ResourceType.Iron, incomeIron);
        AddFloat(ResourceType.Food, incomeFood);
        AddFloat(ResourceType.Clothing, incomeClothing);

        // Upkeep
        bool foodOk = ConsumeFloat(ResourceType.Food, tick.population * tick.foodPerPerson);
        bool clothingOk = ConsumeFloat(ResourceType.Clothing, tick.population * tick.clothingPerPerson);

        // Loyalty
        int loyalty = Get(ResourceType.Loyalty);
        if (!foodOk) loyalty -= tick.loyaltyLossIfFoodMissing;
        if (!clothingOk) loyalty -= tick.loyaltyLossIfClothingMissing;
        if (foodOk && clothingOk) loyalty += tick.loyaltyGainIfSupplied;
        Set(ResourceType.Loyalty, loyalty);

        RefreshAllUI();
    }

    public void SetPopulation(int pop) { tick.population = Mathf.Max(0, pop); RefreshAllUI(); }

    public void SetIncome(ResourceType type, float perTick)
    {
        switch (type)
        {
            case ResourceType.Gold: incomeGold = perTick; break;
            case ResourceType.Wood: incomeWood = perTick; break;
            case ResourceType.Stone: incomeStone = perTick; break;
            case ResourceType.Iron: incomeIron = perTick; break;
            case ResourceType.Food: incomeFood = perTick; break;
            case ResourceType.Clothing: incomeClothing = perTick; break;
        }
        RefreshAllUI();
    }

    public float GetIncome(ResourceType type) => type switch
    {
        ResourceType.Gold => incomeGold,
        ResourceType.Wood => incomeWood,
        ResourceType.Stone => incomeStone,
        ResourceType.Iron => incomeIron,
        ResourceType.Food => incomeFood,
        ResourceType.Clothing => incomeClothing,
        _ => 0f
    };

    // ---------- INTERNAL ----------
    private void InitDictionaries()
    {
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
        {
            _values[t] = 0;
            _floatAccu[t] = 0f;
        }
    }

    private void SetStartValues()
    {
        _values[ResourceType.Gold] = Clamp(ResourceType.Gold, startValues.gold);
        _values[ResourceType.Wood] = Clamp(ResourceType.Wood, startValues.wood);
        _values[ResourceType.Stone] = Clamp(ResourceType.Stone, startValues.stone);
        _values[ResourceType.Iron] = Clamp(ResourceType.Iron, startValues.iron);
        _values[ResourceType.Food] = Clamp(ResourceType.Food, startValues.food);
        _values[ResourceType.Clothing] = Clamp(ResourceType.Clothing, startValues.clothing);
        _values[ResourceType.Loyalty] = Clamp(ResourceType.Loyalty, startValues.loyalty);
    }

    private int Clamp(ResourceType type, int v)
    {
        if (type == ResourceType.Loyalty) return Mathf.Clamp(v, 0, 100);
        return Mathf.Max(0, v);
    }

    private void AddFloat(ResourceType type, float amount)
    {
        if (Mathf.Approximately(amount, 0f)) return;

        _floatAccu[type] += amount;
        int delta = Mathf.FloorToInt(_floatAccu[type]);
        if (delta != 0)
        {
            _floatAccu[type] -= delta;
            _values[type] = Clamp(type, _values[type] + delta);
            onResourceChanged?.Invoke(type, _values[type]);
        }
    }

    private bool ConsumeFloat(ResourceType type, float amount)
    {
        if (amount <= 0f) return true;

        _floatAccu[type] -= amount;
        int need = Mathf.CeilToInt(-_floatAccu[type]);
        if (need <= 0) return true;

        int have = _values[type];
        int take = Mathf.Min(have, need);
        if (take > 0)
        {
            _values[type] = Clamp(type, have - take);
            _floatAccu[type] += take;
            onResourceChanged?.Invoke(type, _values[type]);
        }
        return _floatAccu[type] >= 0f;
    }
    // Add this inside ResourceManager class (below your existing private AddFloat/ConsumeFloat)

    /// <summary>
    /// Add a float amount to a resource (e.g. +0.5 Food).
    /// Uses internal fractional accumulator.
    /// </summary>
    public void AddAmount(ResourceType type, float amount)
    {
        if (type == ResourceType.Loyalty)
        {
            // loyalty is int-based (0..100)
            int delta = Mathf.RoundToInt(amount);
            if (delta != 0) Add(type, delta);
            return;
        }

        AddFloat(type, amount); // uses your existing private method
    }

    /// <summary>
    /// Consume a float amount from a resource (e.g. 0.25 Clothing).
    /// Returns true if fully paid, false if not enough.
    /// </summary>
    public bool TryConsumeAmount(ResourceType type, float amount)
    {
        if (type == ResourceType.Loyalty)
        {
            int need = Mathf.CeilToInt(amount);
            return TrySpend(type, need);
        }

        return ConsumeFloat(type, amount); // uses your existing private method
    }

    // ---------- UI ----------
    private void CacheBindings()
    {
        _cached.Clear();
        if (bindings == null) return;

        foreach (var b in bindings)
        {
            if (b == null || b.target == null) continue;

            var adapter = TextAdapterFactory.TryCreateFromObject(b.target);
            if (adapter == null)
            {
                Debug.LogWarning($"ResourceManager: Binding '{b.name}' target has no Text/TMP text property. " +
                                 $"Drag the Text GameObject or the Text/TMP component itself.");
                continue;
            }
            _cached.Add((b, adapter));
        }
    }

    public void RefreshAllUI()
    {
        if (_cached.Count == 0 && bindings != null && bindings.Count > 0)
            CacheBindings();

        foreach (var (b, adapter) in _cached)
        {
            if (b.mode == BindingMode.ResourceValue)
            {
                string label = string.IsNullOrWhiteSpace(b.labelOverride) ? b.resourceType.ToString() : b.labelOverride;
                int value = Get(b.resourceType);

                float income = GetIncome(b.resourceType);
                float upkeep = 0f;
                if (b.resourceType == ResourceType.Food) upkeep = tick.population * tick.foodPerPerson;
                if (b.resourceType == ResourceType.Clothing) upkeep = tick.population * tick.clothingPerPerson;

                string perTick = "";
                if (b.showPerTickInfo)
                {
                    if (b.resourceType == ResourceType.Loyalty) perTick = "0";
                    else
                    {
                        string incS = income >= 0 ? $"+{income:0.##}" : $"{income:0.##}";
                        string upS = upkeep > 0 ? $" -{upkeep:0.##}" : "";
                        perTick = $"{incS}{upS}";
                    }
                }

                string tpl = string.IsNullOrEmpty(b.template) ? "{label}: {value}" : b.template;
                adapter.SetText(ApplyTokens(tpl, label, value, income, upkeep, perTick, tick.population));
            }
            else
            {
                string lineTpl = string.IsNullOrEmpty(b.lineTemplate) ? "{label}: {value}" : b.lineTemplate;
                var sb = new System.Text.StringBuilder(256);

                foreach (ResourceType rt in Enum.GetValues(typeof(ResourceType)))
                {
                    int value = Get(rt);
                    float income = GetIncome(rt);
                    float upkeep = 0f;
                    if (rt == ResourceType.Food) upkeep = tick.population * tick.foodPerPerson;
                    if (rt == ResourceType.Clothing) upkeep = tick.population * tick.clothingPerPerson;

                    string perTick = "";
                    if (b.showPerTickInfo)
                    {
                        if (rt == ResourceType.Loyalty) perTick = "0";
                        else
                        {
                            string incS = income >= 0 ? $"+{income:0.##}" : $"{income:0.##}";
                            string upS = upkeep > 0 ? $" -{upkeep:0.##}" : "";
                            perTick = $"{incS}{upS}";
                        }
                    }

                    sb.AppendLine(ApplyTokens(lineTpl, rt.ToString(), value, income, upkeep, perTick, tick.population));
                }

                adapter.SetText(sb.ToString().TrimEnd());
            }
        }
    }

    private static string ApplyTokens(string tpl, string label, int value, float income, float upkeep, string perTick, int pop)
    {
        return tpl
            .Replace("{label}", label)
            .Replace("{value}", value.ToString())
            .Replace("{income}", income.ToString("0.##"))
            .Replace("{upkeep}", upkeep.ToString("0.##"))
            .Replace("{perTick}", perTick)
            .Replace("{pop}", pop.ToString());
    }

    // ---------- Text adapter (supports UI.Text and TMP_Text via reflection) ----------
    private interface ITextAdapter { void SetText(string value); }

    private class LegacyTextAdapter : ITextAdapter
    {
        private readonly Text _t;
        public LegacyTextAdapter(Text t) { _t = t; }
        public void SetText(string value) { if (_t) _t.text = value; }
    }

    private class ReflectionTextAdapter : ITextAdapter
    {
        private readonly Component _c;
        private readonly PropertyInfo _textProp;
        public ReflectionTextAdapter(Component c, PropertyInfo textProp) { _c = c; _textProp = textProp; }
        public void SetText(string value)
        {
            if (_c == null || _textProp == null) return;
            _textProp.SetValue(_c, value, null);
        }
    }

    private static class TextAdapterFactory
    {
        public static ITextAdapter TryCreateFromObject(UnityEngine.Object obj)
        {
            if (obj == null) return null;

            // If user drags a component directly (Text/TMP/whatever)
            if (obj is Component comp)
            {
                // Legacy UI Text
                if (comp is Text ut) return new LegacyTextAdapter(ut);

                // TMP_Text (or any component with a public string 'text' property)
                var prop = comp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
                    return new ReflectionTextAdapter(comp, prop);

                // If it's a Transform/RectTransform, try its GameObject
                var goFromComp = comp.gameObject;
                return TryCreateFromGameObject(goFromComp);
            }

            // If user drags a GameObject
            if (obj is GameObject go)
                return TryCreateFromGameObject(go);

            return null;
        }

        private static ITextAdapter TryCreateFromGameObject(GameObject go)
        {
            if (!go) return null;

            // Prefer legacy Text first
            var t = go.GetComponent<Text>();
            if (t) return new LegacyTextAdapter(t);

            // Otherwise find any component with 'text' property (TMP_Text, etc.)
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                var prop = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
                    return new ReflectionTextAdapter(c, prop);
            }

            return null;
        }
    }

    // ---------- Debug ----------
    [ContextMenu("Debug: Tick Once")]
    private void DebugTickOnce() => OnTick();
}
