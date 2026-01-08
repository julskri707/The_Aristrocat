// TickSystem.cs (Unity 6 compatible)
// Adds onPreTick and onPostTick so systems can run in order.
// Order: onPreTick -> onTick -> (optional) ResourceManager.OnTick -> onPostTick

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TickSystem : MonoBehaviour
{
    [Serializable]
    public class TickEvent : UnityEvent<long> { } // tickIndex

    [Header("Tick Settings")]
    [Min(0.01f)] public float secondsPerTick = 1.0f;
    public bool autoRun = true;
    public bool useUnscaledTime = false;

    [Header("Speed")]
    [Min(0.1f)] public float speedMultiplier = 1f;

    [Header("Integration")]
    public bool tickResourceManager = true;
    public ResourceManager resourceManager;

    [Header("Optional UI")]
    public Component tickCounterText;
    public Component speedText;

    [Header("Events")]
    public TickEvent onPreTick;   // ✅ new
    public TickEvent onTick;      // existing
    public TickEvent onPostTick;  // ✅ new

    public bool IsPaused => !_running;
    public long TickIndex => _tickIndex;

    private bool _running;
    private float _accumulator;
    private long _tickIndex;

    private ITextAdapter _tickUi;
    private ITextAdapter _speedUi;

    private void Awake()
    {
        _running = autoRun;
        _tickUi = TextAdapterFactory.TryCreate(tickCounterText);
        _speedUi = TextAdapterFactory.TryCreate(speedText);
        RefreshUI();
    }

    private void Update()
    {
        if (!_running) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float interval = GetTickInterval();
        if (interval <= 0.0001f) interval = 0.0001f;

        _accumulator += dt;

        while (_accumulator >= interval)
        {
            _accumulator -= interval;
            DoTick();
        }
    }

    public void StartTicks() { _running = true; }
    public void PauseTicks() { _running = false; }
    public void TogglePause() { _running = !_running; }

    public void SetSpeed(float multiplier)
    {
        speedMultiplier = Mathf.Max(0.1f, multiplier);
        RefreshUI();
    }

    public void Speed1x() => SetSpeed(1f);
    public void Speed2x() => SetSpeed(2f);
    public void Speed4x() => SetSpeed(4f);

    public void TickOnce() => DoTick();

    private float GetTickInterval() => secondsPerTick / Mathf.Max(0.1f, speedMultiplier);

    private void DoTick()
    {
        _tickIndex++;

        // ✅ PRE
        onPreTick?.Invoke(_tickIndex);

        // ✅ MAIN
        onTick?.Invoke(_tickIndex);

        // ResourceManager upkeep etc.
        if (tickResourceManager)
        {
            var rm = resourceManager != null ? resourceManager : ResourceManager.Instance;
            if (rm != null) rm.OnTick();
        }

        // ✅ POST
        onPostTick?.Invoke(_tickIndex);

        RefreshUI();
    }

    private void RefreshUI()
    {
        _tickUi?.SetText($"Tick: {_tickIndex}");
        _speedUi?.SetText($"Speed: x{speedMultiplier:0.#}");
    }

    // ---------- Text Adapter ----------
    private interface ITextAdapter { void SetText(string v); }

    private class LegacyTextAdapter : ITextAdapter
    {
        private readonly Text _t;
        public LegacyTextAdapter(Text t) { _t = t; }
        public void SetText(string v) { if (_t) _t.text = v; }
    }

    private class ReflectionTextAdapter : ITextAdapter
    {
        private readonly Component _c;
        private readonly System.Reflection.PropertyInfo _p;
        public ReflectionTextAdapter(Component c, System.Reflection.PropertyInfo p) { _c = c; _p = p; }
        public void SetText(string v)
        {
            if (_c == null || _p == null) return;
            _p.SetValue(_c, v, null);
        }
    }

    private static class TextAdapterFactory
    {
        public static ITextAdapter TryCreate(Component c)
        {
            if (c == null) return null;
            if (c is Text t) return new LegacyTextAdapter(t);

            var prop = c.GetType().GetProperty("text",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            if (prop != null && prop.PropertyType == typeof(string) && prop.CanWrite)
                return new ReflectionTextAdapter(c, prop);

            return null;
        }
    }
}
