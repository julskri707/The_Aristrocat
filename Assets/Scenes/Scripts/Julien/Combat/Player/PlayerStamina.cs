using System;
using UnityEngine;
using UnityEngine.Events;

public class PlayerStamina : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DamageableHealth ownerHealth;

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina = 100f;
    [SerializeField] private bool resetToMaxOnAwake = true;

    [Header("Regeneration")]
    [SerializeField] private float staminaRegenPerSecond = 18f;
    [SerializeField] private float regenDelayAfterSpend = 1.0f;

    [Header("Inspector Events")]
    [SerializeField] private UnityEvent onStaminaChanged;
    [SerializeField] private UnityEvent onStaminaEmptied;
    [SerializeField] private UnityEvent onStaminaFull;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private float nextRegenAllowedTime;
    private bool wasEmptyLastFrame;
    private bool wasFullLastFrame;

    public float MaxStamina => maxStamina;
    public float CurrentStamina => currentStamina;
    public float NormalizedStamina => maxStamina > 0f ? currentStamina / maxStamina : 0f;

    public event Action<PlayerStamina> StaminaChanged;
    public event Action<PlayerStamina> StaminaEmptied;
    public event Action<PlayerStamina> StaminaFull;

    private void Awake()
    {
        if (ownerHealth == null)
        {
            ownerHealth = GetComponent<DamageableHealth>();
        }

        maxStamina = Mathf.Max(1f, maxStamina);

        if (resetToMaxOnAwake)
        {
            currentStamina = maxStamina;
        }
        else
        {
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        }

        wasEmptyLastFrame = currentStamina <= 0.0001f;
        wasFullLastFrame = currentStamina >= maxStamina - 0.0001f;
    }

    private void OnValidate()
    {
        maxStamina = Mathf.Max(1f, maxStamina);
        staminaRegenPerSecond = Mathf.Max(0f, staminaRegenPerSecond);
        regenDelayAfterSpend = Mathf.Max(0f, regenDelayAfterSpend);

        if (!IsFinite(currentStamina))
        {
            currentStamina = maxStamina;
        }

        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
    }

    private void Update()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
            return;

        if (currentStamina >= maxStamina)
        {
            NotifyThresholdEventsIfNeeded();
            return;
        }

        if (Time.time < nextRegenAllowedTime)
        {
            NotifyThresholdEventsIfNeeded();
            return;
        }

        if (staminaRegenPerSecond <= 0f)
        {
            NotifyThresholdEventsIfNeeded();
            return;
        }

        float oldStamina = currentStamina;
        currentStamina = Mathf.Clamp(currentStamina + staminaRegenPerSecond * Time.deltaTime, 0f, maxStamina);

        if (!Mathf.Approximately(oldStamina, currentStamina))
        {
            RaiseStaminaChanged();
        }

        NotifyThresholdEventsIfNeeded();
    }

    public bool HasEnough(float amount)
    {
        amount = Mathf.Max(0f, amount);
        return currentStamina >= amount;
    }

    public bool TrySpend(float amount, string reason = "")
    {
        if (!IsFinite(amount))
        {
            Debug.LogWarning($"[{nameof(PlayerStamina)}] Invalid stamina spend requested on '{name}'.", this);
            return false;
        }

        amount = Mathf.Max(0f, amount);

        if (amount <= 0f)
            return true;

        if (currentStamina < amount)
            return false;

        currentStamina -= amount;
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        nextRegenAllowedTime = Time.time + regenDelayAfterSpend;

        RaiseStaminaChanged();
        NotifyThresholdEventsIfNeeded();

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerStamina)}] Spent {amount:0.##} stamina on '{name}' ({reason}). Remaining: {currentStamina:0.##}", this);
        }

        return true;
    }

    public void AddStamina(float amount)
    {
        if (!IsFinite(amount))
        {
            Debug.LogWarning($"[{nameof(PlayerStamina)}] Invalid stamina add requested on '{name}'.", this);
            return;
        }

        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
            return;

        float oldStamina = currentStamina;
        currentStamina = Mathf.Clamp(currentStamina + amount, 0f, maxStamina);

        if (!Mathf.Approximately(oldStamina, currentStamina))
        {
            RaiseStaminaChanged();
        }

        NotifyThresholdEventsIfNeeded();
    }

    public void RestoreFull()
    {
        currentStamina = maxStamina;
        RaiseStaminaChanged();
        NotifyThresholdEventsIfNeeded();
    }

    public void SetCurrentStamina(float value)
    {
        if (!IsFinite(value))
        {
            Debug.LogWarning($"[{nameof(PlayerStamina)}] Invalid stamina value set on '{name}'.", this);
            return;
        }

        currentStamina = Mathf.Clamp(value, 0f, maxStamina);
        RaiseStaminaChanged();
        NotifyThresholdEventsIfNeeded();
    }

    public void PauseRegeneration(float duration)
    {
        duration = Mathf.Max(0f, duration);
        nextRegenAllowedTime = Mathf.Max(nextRegenAllowedTime, Time.time + duration);
    }

    private void RaiseStaminaChanged()
    {
        StaminaChanged?.Invoke(this);
        onStaminaChanged?.Invoke();
    }

    private void NotifyThresholdEventsIfNeeded()
    {
        bool isEmptyNow = currentStamina <= 0.0001f;
        bool isFullNow = currentStamina >= maxStamina - 0.0001f;

        if (isEmptyNow && !wasEmptyLastFrame)
        {
            StaminaEmptied?.Invoke(this);
            onStaminaEmptied?.Invoke();
        }

        if (isFullNow && !wasFullLastFrame)
        {
            StaminaFull?.Invoke(this);
            onStaminaFull?.Invoke();
        }

        wasEmptyLastFrame = isEmptyNow;
        wasFullLastFrame = isFullNow;
    }

    private bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
