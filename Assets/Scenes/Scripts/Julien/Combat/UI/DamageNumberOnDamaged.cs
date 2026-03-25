using UnityEngine;

public class DamageNumberOnDamaged : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DamageableHealth health;
    [SerializeField] private Transform spawnAnchor;
    [SerializeField] private DamageNumberPopup popupPrefab;
    [SerializeField] private Camera cameraOverride;

    [Header("Filter")]
    [SerializeField] private bool onlyShowIfSourceTeamIsPlayer = true;
    [SerializeField] private bool ignoreSelfInflictedDamage = true;
    [SerializeField] private bool ignoreZeroOrNegativeDamage = true;

    [Header("Spawn Position")]
    [SerializeField] private bool useHitPointIfAvailable = true;
    [SerializeField] private float randomHorizontalOffset = 0.20f;
    [SerializeField] private float randomVerticalOffset = 0.20f;
    [SerializeField] private float baseVerticalOffset = 1.45f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Camera cachedCamera;
    private bool warnedMissingPrefab;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<DamageableHealth>();
        }

        if (spawnAnchor == null)
        {
            spawnAnchor = transform;
        }

        if (cameraOverride != null)
        {
            cachedCamera = cameraOverride;
        }
        else
        {
            cachedCamera = Camera.main;
        }

        if (health == null)
        {
            Debug.LogWarning($"[{nameof(DamageNumberOnDamaged)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
        }
    }

    private void OnDamaged(DamageableHealth damagedHealth, DamageInfo damageInfo)
    {
        if (popupPrefab == null)
        {
            WarnMissingPrefabOnce();
            return;
        }

        if (ignoreZeroOrNegativeDamage && damageInfo.amount <= 0f)
            return;

        if (onlyShowIfSourceTeamIsPlayer && damageInfo.sourceTeam != CombatTeam.Player)
            return;

        if (ignoreSelfInflictedDamage && damageInfo.sourceTransform != null)
        {
            if (damageInfo.sourceTransform == transform || damageInfo.sourceTransform.IsChildOf(transform))
                return;
        }

        Vector3 anchorPosition = spawnAnchor != null ? spawnAnchor.position : transform.position;
        Vector3 basePosition = anchorPosition;

        if (useHitPointIfAvailable && damageInfo.hitPoint != Vector3.zero)
        {
            basePosition = damageInfo.hitPoint;
        }

        basePosition.y = anchorPosition.y + baseVerticalOffset;

        Vector2 random2D = Random.insideUnitCircle * randomHorizontalOffset;
        basePosition += new Vector3(
            random2D.x,
            Random.Range(0f, randomVerticalOffset),
            random2D.y
        );

        Camera cam = cameraOverride != null ? cameraOverride : cachedCamera;
        if (cam == null)
        {
            cachedCamera = Camera.main;
            cam = cachedCamera;
        }

        DamageNumberPopup instance = Instantiate(popupPrefab, basePosition, Quaternion.identity);
        instance.Initialize(damageInfo.amount, damageInfo.isCriticalHit, cam);

        if (debugLogs)
        {
            Debug.Log($"[{nameof(DamageNumberOnDamaged)}] Spawned damage number for '{name}': {damageInfo.amount} Crit={damageInfo.isCriticalHit}", this);
        }
    }

    private void WarnMissingPrefabOnce()
    {
        if (warnedMissingPrefab)
            return;

        warnedMissingPrefab = true;
        Debug.LogWarning($"[{nameof(DamageNumberOnDamaged)}] No popupPrefab assigned on '{name}'.", this);
    }
}
