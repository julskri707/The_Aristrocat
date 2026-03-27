using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int currentHp = 100;
    [SerializeField] private bool destroyOnDeath = false;

    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public bool IsDead => currentHp <= 0;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDied;

    private void Awake()
    {
        if (maxHp < 1)
            maxHp = 1;

        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
    }

    private void Start()
    {
        NotifyHealthChanged();
    }

    public void SetMaxHp(int value, bool refillToFull = true)
    {
        maxHp = Mathf.Max(1, value);

        if (refillToFull)
            currentHp = maxHp;
        else
            currentHp = Mathf.Clamp(currentHp, 0, maxHp);

        NotifyHealthChanged();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        int oldHp = currentHp;
        currentHp = Mathf.Clamp(currentHp + amount, 0, maxHp);

        if (currentHp != oldHp)
            NotifyHealthChanged();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        int oldHp = currentHp;
        currentHp = Mathf.Clamp(currentHp - amount, 0, maxHp);

        if (currentHp != oldHp)
            NotifyHealthChanged();

        if (currentHp <= 0)
        {
            OnDied?.Invoke();

            if (destroyOnDeath)
                Destroy(gameObject);
        }
    }

    public void RestoreFull()
    {
        currentHp = maxHp;
        NotifyHealthChanged();
    }

    private void NotifyHealthChanged()
    {
        OnHealthChanged?.Invoke(currentHp, maxHp);
    }
}
