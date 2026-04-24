using UnityEngine;
using UnityEngine.Events;

public class DamageableHealth : MonoBehaviour
{
    [SerializeField] private CombatTeam team = CombatTeam.Enemy;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;
    [SerializeField] private bool resetToMaxHealthOnAwake = true;
    [SerializeField] private bool canBeHealed = true;
    [SerializeField] private bool invincible = false;
    [SerializeField] private bool allowFriendlyFire = false;

    public UnityEvent<float> onDamaged;
    public UnityEvent<float> onHealed;
    public UnityEvent onDeath;

    public CombatTeam Team => team;
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0f;

    /// <summary>Zeitpunkt des letzten erfolgreichen Schadens (<see cref="ApplyDamage"/>). Sonst sehr klein.</summary>
    public float LastDamageTime { get; private set; } = float.NegativeInfinity;

    private void Awake()
    {
        if (resetToMaxHealthOnAwake)
            currentHealth = maxHealth;
    }

    public bool ApplyDamage(DamageInfo info)
    {
        if (invincible || IsDead)
            return false;

        if (!info.IgnoresFriendlyFire && !allowFriendlyFire && info.SourceTeam == team)
            return false;

        float amount = Mathf.Max(0f, info.Amount);
        if (amount <= 0f)
            return false;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        LastDamageTime = Time.time;
        onDamaged?.Invoke(amount);

        if (currentHealth <= 0f)
            onDeath?.Invoke();

        return true;
    }

    public bool ApplyHeal(float amount)
    {
        if (!canBeHealed || invincible || IsDead)
            return false;

        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
            return false;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        onHealed?.Invoke(amount);
        return true;
    }
}
