using UnityEngine;

/// <summary>
/// Passive Heilung für NPCs mit <see cref="DamageableHealth"/>:
/// Nach <see cref="regenDelaySeconds"/> ohne neuen Schaden (siehe <see cref="DamageableHealth.LastDamageTime"/>)
/// steigt die Gesundheit um <see cref="regenPerSecond"/> pro Sekunde bis <see cref="DamageableHealth.MaxHealth"/>.
/// </summary>
/// <remarks>
/// Prefab: Auf denselben Root wie <see cref="DamageableHealth"/> setzen; Referenz zuweisen oder leer lassen für Auto-Find.
/// Zusätzlich Collider/Hitbox für Schwert-Melee wie in eurer Szene üblich.
/// </remarks>
[DisallowMultipleComponent]
public class NPCHealthRegeneration : MonoBehaviour
{
    [SerializeField] private DamageableHealth health;

    [Header("Regeneration")]
    [SerializeField] [Min(0f)] private float regenDelaySeconds = 60f;
    [SerializeField] [Min(0f)] private float regenPerSecond = 10f;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<DamageableHealth>();
    }

    private void Update()
    {
        if (health == null || health.IsDead)
            return;

        if (health.CurrentHealth >= health.MaxHealth - 0.0001f)
            return;

        if (Time.time - health.LastDamageTime < regenDelaySeconds)
            return;

        health.ApplyHeal(regenPerSecond * Time.deltaTime);
    }

    private void OnValidate()
    {
        regenDelaySeconds = Mathf.Max(0f, regenDelaySeconds);
        regenPerSecond = Mathf.Max(0f, regenPerSecond);
    }
}
