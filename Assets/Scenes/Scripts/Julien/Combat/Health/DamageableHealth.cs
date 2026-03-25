using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class DamageableHealth : MonoBehaviour, IDamageable
{
    [Header("Identity")]
    [SerializeField] private CombatTeam team = CombatTeam.Neutral;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;
    [SerializeField] private bool resetToMaxHealthOnAwake = true;
    [SerializeField] private bool canBeHealed = true;
    [SerializeField] private bool invincible = false;
    [SerializeField] private bool allowFriendlyFire = false;

    [Header("Inspector Events")]
    [SerializeField] private UnityEvent onDamaged;
    [SerializeField] private UnityEvent onHealed;
    [SerializeField] private UnityEvent onDeath;

    private bool isDead;
    private DamageInfo lastDamageInfo;

    private readonly List<MonoBehaviour> incomingModifierBehaviours = new List<MonoBehaviour>();
    private readonly List<IIncomingDamageModifier> incomingModifiers = new List<IIncomingDamageModifier>();

    public CombatTeam Team => team;
    public bool IsDead => isDead;
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float NormalizedHealth => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public DamageInfo LastDamageInfo => lastDamageInfo;

    public event Action<DamageableHealth, DamageInfo> Damaged;
    public event Action<DamageableHealth> Healed;
    public event Action<DamageableHealth, DamageInfo> Died;

    private void Awake()
    {
        maxHealth = Mathf.Max(1f, maxHealth);

        if (resetToMaxHealthOnAwake)
        {
            currentHealth = maxHealth;
        }
        else
        {
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        isDead = currentHealth <= 0f;

        CacheIncomingDamageModifiers();
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);

        if (!IsFinite(currentHealth))
        {
            currentHealth = maxHealth;
        }

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    public bool CanReceiveDamage(DamageInfo damageInfo)
    {
        if (isDead)
            return false;

        if (invincible)
            return false;

        if (!IsFinite(damageInfo.amount))
        {
            Debug.LogWarning($"[{nameof(DamageableHealth)}] Invalid damage amount received on '{name}'.", this);
            return false;
        }

        if (damageInfo.amount <= 0f)
            return false;

        if (!allowFriendlyFire && !damageInfo.ignoresFriendlyFire)
        {
            bool sameNonNeutralTeam =
                team != CombatTeam.Neutral &&
                damageInfo.sourceTeam != CombatTeam.Neutral &&
                damageInfo.sourceTeam == team;

            if (sameNonNeutralTeam)
                return false;
        }

        return true;
    }

    public bool ApplyDamage(DamageInfo damageInfo)
    {
        if (!CanReceiveDamage(damageInfo))
            return false;

        IncomingDamageContext context = new IncomingDamageContext(damageInfo);
        ApplyIncomingDamageModifiers(context);

        if (context.CancelDamage)
            return false;

        damageInfo = context.DamageInfo;

        if (!CanReceiveDamage(damageInfo))
            return false;

        lastDamageInfo = damageInfo;

        currentHealth -= damageInfo.amount;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        Damaged?.Invoke(this, damageInfo);
        onDamaged?.Invoke();

        if (currentHealth <= 0f)
        {
            HandleDeath(damageInfo);
        }

        return true;
    }

    public bool Heal(float amount)
    {
        if (isDead)
            return false;

        if (!canBeHealed)
            return false;

        if (!IsFinite(amount))
        {
            Debug.LogWarning($"[{nameof(DamageableHealth)}] Invalid heal amount on '{name}'.", this);
            return false;
        }

        if (amount <= 0f)
            return false;

        float before = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);

        if (currentHealth <= before)
            return false;

        Healed?.Invoke(this);
        onHealed?.Invoke();
        return true;
    }

    public void RestoreFullHealth()
    {
        isDead = false;
        currentHealth = maxHealth;
    }

    public void SetInvincible(bool value)
    {
        invincible = value;
    }

    public void SetCurrentHealth(float value)
    {
        if (!IsFinite(value))
        {
            Debug.LogWarning($"[{nameof(DamageableHealth)}] Tried to set invalid currentHealth on '{name}'.", this);
            return;
        }

        currentHealth = Mathf.Clamp(value, 0f, maxHealth);

        if (currentHealth <= 0f)
        {
            if (!isDead)
            {
                DamageInfo forcedDeath = new DamageInfo(
                    amount: 0f,
                    hitPoint: transform.position,
                    hitDirection: Vector3.zero,
                    source: null,
                    sourceTransform: null,
                    sourceTeam: CombatTeam.Neutral,
                    ignoresFriendlyFire: true,
                    damageId: "ForcedHealthToZero"
                );

                lastDamageInfo = forcedDeath;
                HandleDeath(forcedDeath);
            }
        }
        else
        {
            isDead = false;
        }
    }

    public void Kill(GameObject source = null, Transform sourceTransform = null, CombatTeam sourceTeam = CombatTeam.Neutral)
    {
        if (isDead)
            return;

        DamageInfo killingDamage = new DamageInfo(
            amount: Mathf.Max(currentHealth, 1f),
            hitPoint: transform.position,
            hitDirection: Vector3.zero,
            source: source,
            sourceTransform: sourceTransform,
            sourceTeam: sourceTeam,
            ignoresFriendlyFire: true,
            damageId: "Kill"
        );

        ApplyDamage(killingDamage);
    }

    private void HandleDeath(DamageInfo killingDamage)
    {
        if (isDead)
            return;

        isDead = true;
        currentHealth = 0f;
        lastDamageInfo = killingDamage;

        Died?.Invoke(this, killingDamage);
        onDeath?.Invoke();
    }

    private void CacheIncomingDamageModifiers()
    {
        incomingModifierBehaviours.Clear();
        incomingModifiers.Clear();

        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (behaviour is IIncomingDamageModifier modifier)
            {
                incomingModifierBehaviours.Add(behaviour);
                incomingModifiers.Add(modifier);
            }
        }

        SortIncomingDamageModifiersByPriorityDescending();
    }

    private void SortIncomingDamageModifiersByPriorityDescending()
    {
        for (int i = 0; i < incomingModifiers.Count - 1; i++)
        {
            for (int j = i + 1; j < incomingModifiers.Count; j++)
            {
                if (incomingModifiers[j].Priority > incomingModifiers[i].Priority)
                {
                    IIncomingDamageModifier modifierTmp = incomingModifiers[i];
                    incomingModifiers[i] = incomingModifiers[j];
                    incomingModifiers[j] = modifierTmp;

                    MonoBehaviour behaviourTmp = incomingModifierBehaviours[i];
                    incomingModifierBehaviours[i] = incomingModifierBehaviours[j];
                    incomingModifierBehaviours[j] = behaviourTmp;
                }
            }
        }
    }

    private void ApplyIncomingDamageModifiers(IncomingDamageContext context)
    {
        for (int i = 0; i < incomingModifiers.Count; i++)
        {
            MonoBehaviour behaviour = incomingModifierBehaviours[i];
            IIncomingDamageModifier modifier = incomingModifiers[i];

            if (behaviour == null || modifier == null)
                continue;

            if (!behaviour.enabled)
                continue;

            modifier.ModifyIncomingDamage(this, context);

            if (context.CancelDamage)
                return;
        }
    }

    private bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [ContextMenu("Debug Take 10 Damage")]
    private void DebugTake10Damage()
    {
        ApplyDamage(new DamageInfo(
            amount: 10f,
            hitPoint: transform.position,
            hitDirection: transform.forward,
            source: null,
            sourceTransform: null,
            sourceTeam: CombatTeam.Neutral,
            ignoresFriendlyFire: true,
            damageId: "Debug10"
        ));
    }

    [ContextMenu("Debug Kill")]
    private void DebugKill()
    {
        Kill();
    }

    [ContextMenu("Debug Full Heal")]
    private void DebugFullHeal()
    {
        RestoreFullHealth();
    }
}
