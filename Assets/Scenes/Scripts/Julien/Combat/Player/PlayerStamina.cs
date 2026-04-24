using UnityEngine;
using UnityEngine.Events;

public class PlayerStamina : MonoBehaviour
{
    [SerializeField] private DamageableHealth ownerHealth;
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina = 100f;
    [SerializeField] private bool resetToMaxOnAwake = true;
    [SerializeField] private float staminaRegenPerSecond = 18f;
    [SerializeField] private float regenDelayAfterSpend = 1f;

    public UnityEvent<float> onStaminaChanged;
    public UnityEvent onStaminaEmptied;
    public UnityEvent onStaminaFull;

    private float regenCooldownTimer;

    public float MaxStamina => maxStamina;
    public float CurrentStamina => currentStamina;

    private void Awake()
    {
        if (ownerHealth == null)
            ownerHealth = GetComponent<DamageableHealth>();

        if (resetToMaxOnAwake)
            currentStamina = maxStamina;
    }

    private void Update()
    {
        if (ownerHealth != null && ownerHealth.IsDead)
            return;

        if (regenCooldownTimer > 0f)
            regenCooldownTimer -= Time.deltaTime;
        else if (currentStamina < maxStamina)
        {
            float next = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(next, currentStamina))
            {
                currentStamina = next;
                onStaminaChanged?.Invoke(currentStamina);
                if (currentStamina >= maxStamina)
                    onStaminaFull?.Invoke();
            }
        }
    }

    public bool HasEnough(float amount)
    {
        return currentStamina >= amount;
    }

    public bool TrySpend(float amount, string reason = "")
    {
        if (amount <= 0f)
            return true;

        if (currentStamina < amount)
            return false;

        currentStamina -= amount;
        regenCooldownTimer = regenDelayAfterSpend;
        onStaminaChanged?.Invoke(currentStamina);

        if (currentStamina <= 0f)
            onStaminaEmptied?.Invoke();

        return true;
    }
}
