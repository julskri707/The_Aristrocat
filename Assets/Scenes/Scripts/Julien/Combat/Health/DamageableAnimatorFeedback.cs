using UnityEngine;

public class DamageableAnimatorFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DamageableHealth health;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform fallbackImpactAnchor;

    [Header("Animator Optional")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private string hitTriggerName = "Hit";
    [SerializeField] private string deathTriggerName = "Death";
    [SerializeField] private bool triggerHitOnlyIfStillAlive = true;

    [Header("Audio Optional")]
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip deathClip;
    [SerializeField] [Range(0f, 1f)] private float hitClipVolume = 1f;
    [SerializeField] [Range(0f, 1f)] private float deathClipVolume = 1f;

    [Header("VFX Optional")]
    [SerializeField] private ParticleSystem hitVfx;
    [SerializeField] private ParticleSystem deathVfx;
    [SerializeField] private GameObject hitImpactPrefab;
    [SerializeField] private float hitImpactPrefabLifetime = 3f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<DamageableHealth>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (fallbackImpactAnchor == null)
        {
            fallbackImpactAnchor = transform;
        }

        if (health == null)
        {
            Debug.LogWarning($"[{nameof(DamageableAnimatorFeedback)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }
    }

    private void OnValidate()
    {
        hitImpactPrefabLifetime = Mathf.Max(0f, hitImpactPrefabLifetime);
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
    }

    private void OnDamaged(DamageableHealth damagedHealth, DamageInfo damageInfo)
    {
        bool stillAliveAfterHit = damagedHealth.CurrentHealth > 0f;
        bool shouldTriggerHit = !triggerHitOnlyIfStillAlive || stillAliveAfterHit;

        if (shouldTriggerHit)
        {
            TriggerAnimator(hitTriggerName);
            PlayClip(hitClip, hitClipVolume);
            PlayParticle(hitVfx);
            SpawnImpactPrefab(damageInfo);
        }

        if (debugLogs)
        {
            Debug.Log($"[{nameof(DamageableAnimatorFeedback)}] Hit feedback on '{name}'.", this);
        }
    }

    private void OnDied(DamageableHealth deadHealth, DamageInfo killingDamage)
    {
        TriggerAnimator(deathTriggerName);
        PlayClip(deathClip, deathClipVolume);
        PlayParticle(deathVfx);

        if (debugLogs)
        {
            Debug.Log($"[{nameof(DamageableAnimatorFeedback)}] Death feedback on '{name}'.", this);
        }
    }

    private void TriggerAnimator(string triggerName)
    {
        if (!driveAnimator || animator == null)
            return;

        if (string.IsNullOrWhiteSpace(triggerName))
            return;

        animator.ResetTrigger(triggerName);
        animator.SetTrigger(triggerName);
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (clip == null || audioSource == null)
            return;

        audioSource.PlayOneShot(clip, volume);
    }

    private void PlayParticle(ParticleSystem particleSystem)
    {
        if (particleSystem == null)
            return;

        particleSystem.Play(true);
    }

    private void SpawnImpactPrefab(DamageInfo damageInfo)
    {
        if (hitImpactPrefab == null)
            return;

        Vector3 spawnPosition = damageInfo.hitPoint;
        if (spawnPosition == Vector3.zero)
        {
            spawnPosition = fallbackImpactAnchor != null ? fallbackImpactAnchor.position : transform.position;
        }

        Vector3 forward = damageInfo.hitDirection.sqrMagnitude > 0.0001f
            ? damageInfo.hitDirection.normalized
            : transform.forward;

        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        GameObject instance = Instantiate(hitImpactPrefab, spawnPosition, rotation);
        if (instance != null && hitImpactPrefabLifetime > 0f)
        {
            Destroy(instance, hitImpactPrefabLifetime);
        }
    }
}
