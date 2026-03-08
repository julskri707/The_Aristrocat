// TickSystem.cs
// Unity 2022+
// - Central tick timer calling registered ResourceTickBehaviour listeners
// - No FindObjects per tick
// - No LayerMasks, no SendMessage
// - Registers/unregisters listeners; cleans nulls safely

using System.Collections.Generic;
using UnityEngine;

public class TickSystem : MonoBehaviour
{
    [Header("Tick Settings")]
    public float secondsPerTick = 1f;

    [Header("Runtime State")]
    public int tickIndex = 0;

    [Header("Debug")]
    [SerializeField] private bool debug = false;

    private readonly List<ResourceTickBehaviour> _listeners = new List<ResourceTickBehaviour>();
    private float _timer;

    private void OnValidate()
    {
        if (secondsPerTick < 0.01f)
            secondsPerTick = 0.01f;
    }

    private void Update()
    {
        if (secondsPerTick <= 0f)
        {
            if (debug)
                Debug.LogWarning("[TickSystem] secondsPerTick <= 0. Tick disabled.");
            return;
        }

        _timer += Time.deltaTime;

        // Catch up if frame took longer than one tick.
        while (_timer >= secondsPerTick)
        {
            _timer -= secondsPerTick;
            DoTick();
        }
    }

    public void Register(ResourceTickBehaviour b)
    {
        if (b == null)
        {
            Debug.LogWarning("[TickSystem] Register called with null listener.");
            return;
        }

        CleanNulls();

        if (_listeners.Contains(b))
            return;

        _listeners.Add(b);

        if (debug)
            Debug.Log($"[TickSystem] Registered '{b.name}'. Total listeners: {_listeners.Count}");
    }

    public void Unregister(ResourceTickBehaviour b)
    {
        if (b == null)
        {
            Debug.LogWarning("[TickSystem] Unregister called with null listener.");
            return;
        }

        CleanNulls();

        bool removed = _listeners.Remove(b);

        if (debug)
            Debug.Log($"[TickSystem] Unregistered '{b.name}' removed={removed}. Total listeners: {_listeners.Count}");
    }

    private void DoTick()
    {
        tickIndex++;

        for (int i = _listeners.Count - 1; i >= 0; i--)
        {
            var b = _listeners[i];
            if (b == null)
            {
                _listeners.RemoveAt(i);
                if (debug)
                    Debug.LogWarning("[TickSystem] Removed null listener during tick.");
                continue;
            }

            b.OnTick(tickIndex);
        }

        if (debug)
            Debug.Log($"[TickSystem] Tick {tickIndex} executed. Listeners: {_listeners.Count}");
    }

    private void CleanNulls()
    {
        for (int i = _listeners.Count - 1; i >= 0; i--)
        {
            if (_listeners[i] == null)
                _listeners.RemoveAt(i);
        }
    }
}
