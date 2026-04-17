using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TreeResourceNode : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private TreeType treeType = TreeType.Oak;

    [Header("Wood")]
    [Min(0)]
    [SerializeField] private int maxWoodYield = 10;
    [Min(0)]
    [SerializeField] private int currentWood = 10;

    [Header("Health")]
    [Min(1)]
    [SerializeField] private int maxHealth = 5;
    [Min(0)]
    [SerializeField] private int currentHealth = 5;

    [Header("State")]
    [SerializeField] private bool isFelled = false;

    [Header("Drops")]
    [SerializeField] private bool spawnWoodDropsOnFell = true;
    [SerializeField] private WoodPickup woodPickupPrefab;
    [SerializeField] private Transform woodDropOrigin;
    [Min(1)]
    [SerializeField] private int woodPerPickup = 1;
    [Min(0f)]
    [SerializeField] private float woodDropRadius = 1.25f;
    [Min(0f)]
    [SerializeField] private float woodDropHeightOffset = 0.1f;

    [Header("Drop Overlap Prevention")]
    [SerializeField] private bool preventWoodDropOverlap = true;
    [Min(0.05f)]
    [SerializeField] private float woodDropMinSpacing = 0.65f;
    [Min(1)]
    [SerializeField] private int woodDropMaxPlacementAttemptsPerPickup = 20;

    [Header("Worker Points")]
    [SerializeField] private Transform interactionPoint;
    [SerializeField] private Transform workerSnapPoint;
    [SerializeField] private Transform workerLookAtPoint;
    [Min(0.25f)]
    [SerializeField] private float fallbackWorkerSnapDistance = 1.35f;

    [Header("Optional References")]
    [SerializeField] private Collider treeCollider;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private WoodcutterWorkArea owningWorkArea;

    [Header("Fall Animation")]
    [SerializeField] private bool destroyAfterFell = true;
    [SerializeField] private bool randomFallDirection = true;
    [SerializeField] private Vector3 manualFallDirection = new Vector3(1f, 0f, 0f);
    [SerializeField] private float fallAngle = 90f;
    [SerializeField] private float fallDuration = 0.6f;
    [SerializeField] private float stayOnGroundTime = 1.5f;

    [Header("Fade")]
    [SerializeField] private bool fadeOutAfterFall = true;
    [SerializeField] private float fadeDuration = 1.25f;

    [Header("Reservation Debug")]
    [SerializeField] private bool isReserved = false;
    [SerializeField] private string reservedByDebugName = "";
    [SerializeField] private bool hasSpawnedWoodDrops = false;

    private object reservedBy;
    private bool isFallingOrRemoving = false;

    private Collider[] cachedColliders;
    private Renderer[] cachedRenderers;
    private Material[][] runtimeMaterials;

    public TreeType TreeType => treeType;
    public int MaxWoodYield => maxWoodYield;
    public int CurrentWood => currentWood;
    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsFelled => isFelled;
    public bool IsReserved => isReserved;
    public bool HasWoodRemaining => currentWood > 0;
    public bool HasSpawnedWoodDrops => hasSpawnedWoodDrops;
    public Collider TreeCollider => treeCollider;
    public WoodcutterWorkArea OwningWorkArea => owningWorkArea;

    public event Action<TreeResourceNode, object> OnReserved;
    public event Action<TreeResourceNode, object> OnReleased;
    public event Action<TreeResourceNode, int> OnDamaged;
    public event Action<TreeResourceNode> OnFelled;
    public event Action<TreeResourceNode, int> OnWoodHarvested;
    public event Action<TreeResourceNode, int> OnWoodDropsSpawned;

    private void Reset()
    {
        AutoAssignReferences();
        currentWood = maxWoodYield;
        currentHealth = maxHealth;
    }

    private void Awake()
    {
        AutoAssignReferences();

        if (!isFelled)
        {
            if (currentWood <= 0)
                currentWood = maxWoodYield;

            if (currentHealth <= 0)
                currentHealth = maxHealth;
        }

        ClampValues();
        CacheComponents();
        PrepareRuntimeMaterials();
        ApplyInitialState();
    }

    private void OnEnable()
    {
        if (owningWorkArea != null)
            owningWorkArea.RegisterTree(this);
    }

    private void OnDisable()
    {
        if (owningWorkArea != null)
            owningWorkArea.UnregisterTree(this);
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        ClampValues();
    }

    public bool CanBeAssigned()
    {
        if (isFelled) return false;
        if (isFallingOrRemoving) return false;
        if (currentWood <= 0) return false;
        if (currentHealth <= 0) return false;
        if (isReserved) return false;
        return true;
    }

    public bool CanBeAssigned(object worker)
    {
        if (worker == null) return CanBeAssigned();
        if (isFelled) return false;
        if (isFallingOrRemoving) return false;
        if (currentWood <= 0) return false;
        if (currentHealth <= 0) return false;
        if (!isReserved) return true;
        return ReferenceEquals(reservedBy, worker);
    }

    public bool Reserve(object worker)
    {
        if (worker == null) return false;
        if (!CanBeAssigned(worker)) return false;
        if (ReferenceEquals(reservedBy, worker)) return true;

        reservedBy = worker;
        isReserved = true;
        reservedByDebugName = worker.ToString();

        OnReserved?.Invoke(this, worker);
        return true;
    }

    public bool Release(object worker)
    {
        if (!isReserved) return true;
        if (worker == null) return false;
        if (!ReferenceEquals(reservedBy, worker)) return false;

        object previousWorker = reservedBy;
        reservedBy = null;
        isReserved = false;
        reservedByDebugName = string.Empty;

        OnReleased?.Invoke(this, previousWorker);
        return true;
    }

    public bool IsReservedBy(object worker)
    {
        if (!isReserved || worker == null) return false;
        return ReferenceEquals(reservedBy, worker);
    }

    public object GetReservedWorker()
    {
        return reservedBy;
    }

    public bool ApplyChopDamage(int amount)
    {
        if (amount <= 0) return false;
        if (isFelled) return false;
        if (isFallingOrRemoving) return false;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        OnDamaged?.Invoke(this, amount);

        if (currentHealth <= 0)
            Fell();

        return true;
    }

    public void Fell()
    {
        if (isFelled) return;
        if (isFallingOrRemoving) return;

        isFelled = true;
        currentHealth = 0;
        ClearReservationInternal();
        DisableAllColliders();

        if (spawnWoodDropsOnFell)
            SpawnWoodDrops();

        OnFelled?.Invoke(this);
        StartCoroutine(FallAndRemoveRoutine());
    }

    public void SpawnWoodDrops()
    {
        if (hasSpawnedWoodDrops) return;
        if (woodPickupPrefab == null) return;
        if (currentWood <= 0) return;

        int totalWoodToDrop = currentWood;
        int droppedAmount = totalWoodToDrop;
        int safeWoodPerPickup = Mathf.Max(1, woodPerPickup);

        Vector3 center = woodDropOrigin != null ? woodDropOrigin.position : GetInteractionPosition();
        List<Vector3> usedPositions = new List<Vector3>();

        while (totalWoodToDrop > 0)
        {
            int amountForThisPickup = Mathf.Min(safeWoodPerPickup, totalWoodToDrop);
            Vector3 spawnPosition = FindWoodDropSpawnPosition(center, usedPositions);

            WoodPickup pickup = Instantiate(woodPickupPrefab, spawnPosition, Quaternion.identity);
            pickup.Initialize(this, amountForThisPickup, owningWorkArea);

            usedPositions.Add(spawnPosition);
            totalWoodToDrop -= amountForThisPickup;
        }

        currentWood = 0;
        hasSpawnedWoodDrops = true;
        OnWoodDropsSpawned?.Invoke(this, droppedAmount);
    }

    private Vector3 FindWoodDropSpawnPosition(Vector3 center, List<Vector3> usedPositions)
    {
        if (!preventWoodDropOverlap)
            return GetRandomDropPosition(center);

        int attempts = Mathf.Max(1, woodDropMaxPlacementAttemptsPerPickup);
        float minSpacing = Mathf.Max(0.05f, woodDropMinSpacing);
        float minSpacingSqr = minSpacing * minSpacing;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = GetRandomDropPosition(center);
            if (IsDropPositionFree(candidate, usedPositions, minSpacingSqr))
                return candidate;
        }

        float angleStep = 360f / Mathf.Max(6, attempts);
        float startAngle = UnityEngine.Random.Range(0f, 360f);

        for (int i = 0; i < attempts; i++)
        {
            float angle = (startAngle + angleStep * i) * Mathf.Deg2Rad;
            float radius = Mathf.Min(woodDropRadius, minSpacing * (1f + (i * 0.35f)));
            Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * radius, woodDropHeightOffset, Mathf.Sin(angle) * radius);
            if (IsDropPositionFree(candidate, usedPositions, minSpacingSqr))
                return candidate;
        }

        int fallbackIndex = usedPositions != null ? usedPositions.Count : 0;
        float fallbackAngle = (fallbackIndex * 47.5f) * Mathf.Deg2Rad;
        float fallbackRadius = Mathf.Max(woodDropRadius, minSpacing) + (fallbackIndex * minSpacing);
        return center + new Vector3(Mathf.Cos(fallbackAngle) * fallbackRadius, woodDropHeightOffset, Mathf.Sin(fallbackAngle) * fallbackRadius);
    }

    private Vector3 GetRandomDropPosition(Vector3 center)
    {
        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * woodDropRadius;
        return center + new Vector3(randomCircle.x, woodDropHeightOffset, randomCircle.y);
    }

    private bool IsDropPositionFree(Vector3 candidate, List<Vector3> usedPositions, float minSpacingSqr)
    {
        if (usedPositions == null || usedPositions.Count == 0)
            return true;

        Vector2 candidateXZ = new Vector2(candidate.x, candidate.z);

        for (int i = 0; i < usedPositions.Count; i++)
        {
            Vector3 existing = usedPositions[i];
            Vector2 existingXZ = new Vector2(existing.x, existing.z);
            if ((candidateXZ - existingXZ).sqrMagnitude < minSpacingSqr)
                return false;
        }

        return true;
    }

    public int HarvestWood(int amount, object worker = null)
    {
        if (amount <= 0) return 0;
        if (currentWood <= 0) return 0;
        if (isReserved && worker != null && !ReferenceEquals(reservedBy, worker)) return 0;

        int harvested = Mathf.Min(amount, currentWood);
        currentWood -= harvested;
        OnWoodHarvested?.Invoke(this, harvested);
        return harvested;
    }

    public int GetWoodYield()
    {
        return currentWood;
    }

    public Vector3 GetInteractionPosition()
    {
        if (interactionPoint != null) return interactionPoint.position;
        if (treeCollider != null) return treeCollider.bounds.center;
        return transform.position;
    }

    public Vector3 GetWorkerLookAtPosition()
    {
        if (workerLookAtPoint != null) return workerLookAtPoint.position;
        return GetInteractionPosition();
    }

    public Vector3 GetWorkerSnapPosition(Vector3 workerPosition)
    {
        if (workerSnapPoint != null)
            return workerSnapPoint.position;

        Vector3 center = GetInteractionPosition();
        Vector3 dir = workerPosition - center;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
            dir = -transform.forward;

        dir.Normalize();
        return center + dir * fallbackWorkerSnapDistance;
    }

    public void SetOwningWorkArea(WoodcutterWorkArea workArea)
    {
        if (owningWorkArea == workArea)
            return;

        if (owningWorkArea != null)
            owningWorkArea.UnregisterTree(this);

        owningWorkArea = workArea;

        if (owningWorkArea != null && isActiveAndEnabled)
            owningWorkArea.RegisterTree(this);
    }

    public void RestoreTree()
    {
        StopAllCoroutines();

        isFelled = false;
        isFallingOrRemoving = false;
        hasSpawnedWoodDrops = false;
        currentHealth = maxHealth;
        currentWood = maxWoodYield;

        ClearReservationInternal();
        ResetVisualAlpha();
        EnableAllColliders();
    }

    public void ClearReservationForce()
    {
        ClearReservationInternal();
    }

    private IEnumerator FallAndRemoveRoutine()
    {
        isFallingOrRemoving = true;

        Transform targetRoot = visualRoot != null ? visualRoot : transform;

        Vector3 pivot = GetFallPivot();
        Vector3 fallDirection = GetFallDirection();
        Vector3 fallAxis = Vector3.Cross(Vector3.up, fallDirection.normalized);

        if (fallAxis.sqrMagnitude < 0.0001f)
            fallAxis = Vector3.forward;

        float elapsed = 0f;
        float previousAngle = 0f;

        while (elapsed < fallDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fallDuration);
            float currentAngle = Mathf.Lerp(0f, fallAngle, t);
            float deltaAngle = currentAngle - previousAngle;

            targetRoot.RotateAround(pivot, fallAxis, deltaAngle);
            previousAngle = currentAngle;
            yield return null;
        }

        yield return new WaitForSeconds(stayOnGroundTime);

        if (fadeOutAfterFall)
            yield return StartCoroutine(FadeOutRoutine());

        if (destroyAfterFell)
            Destroy(gameObject);
        else
            isFallingOrRemoving = false;
    }

    private IEnumerator FadeOutRoutine()
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
            yield break;

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float alpha = 1f - t;

            SetAllRendererAlpha(alpha);
            yield return null;
        }

        SetAllRendererAlpha(0f);
    }

    private void SetAllRendererAlpha(float alpha)
    {
        if (runtimeMaterials == null)
            return;

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            Material[] mats = runtimeMaterials[i];
            if (mats == null)
                continue;

            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }

                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
            }
        }
    }

    private void ResetVisualAlpha()
    {
        if (runtimeMaterials == null)
            return;

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            Material[] mats = runtimeMaterials[i];
            if (mats == null)
                continue;

            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = 1f;
                    mat.SetColor("_BaseColor", c);
                }

                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = 1f;
                    mat.SetColor("_Color", c);
                }
            }
        }
    }

    private void PrepareRuntimeMaterials()
    {
        if (cachedRenderers == null)
            return;

        runtimeMaterials = new Material[cachedRenderers.Length][];

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] == null)
                continue;

            Material[] mats = cachedRenderers[i].materials;
            runtimeMaterials[i] = mats;

            for (int j = 0; j < mats.Length; j++)
                PrepareMaterialForFade(mats[j]);
        }
    }

    private void PrepareMaterialForFade(Material mat)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);

        if (mat.HasProperty("_SrcBlend"))
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

        if (mat.HasProperty("_DstBlend"))
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);

        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
    }

    private Vector3 GetFallPivot()
    {
        if (treeCollider != null)
        {
            Bounds b = treeCollider.bounds;
            return new Vector3(b.center.x, b.min.y, b.center.z);
        }

        return transform.position;
    }

    private Vector3 GetFallDirection()
    {
        if (!randomFallDirection)
        {
            Vector3 manual = manualFallDirection;
            manual.y = 0f;

            if (manual.sqrMagnitude < 0.001f)
                manual = Vector3.right;

            return manual.normalized;
        }

        Vector2 circle = UnityEngine.Random.insideUnitCircle.normalized;
        if (circle.sqrMagnitude < 0.001f)
            circle = Vector2.right;

        return new Vector3(circle.x, 0f, circle.y).normalized;
    }

    private void DisableAllColliders()
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            CacheComponents();

        for (int i = 0; i < cachedColliders.Length; i++)
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = false;
    }

    private void EnableAllColliders()
    {
        if (cachedColliders == null || cachedColliders.Length == 0)
            CacheComponents();

        for (int i = 0; i < cachedColliders.Length; i++)
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = true;
    }

    private void ApplyInitialState()
    {
        if (isFelled)
        {
            DisableAllColliders();
            SetAllRendererAlpha(0f);

            if (destroyAfterFell && Application.isPlaying)
                Destroy(gameObject);
        }
        else
        {
            EnableAllColliders();
            ResetVisualAlpha();
        }
    }

    private void ClearReservationInternal()
    {
        object previousWorker = reservedBy;

        reservedBy = null;
        isReserved = false;
        reservedByDebugName = string.Empty;

        if (previousWorker != null)
            OnReleased?.Invoke(this, previousWorker);
    }

    private void ClampValues()
    {
        maxWoodYield = Mathf.Max(0, maxWoodYield);
        currentWood = Mathf.Clamp(currentWood, 0, maxWoodYield);

        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = isFelled ? 0 : Mathf.Clamp(currentHealth <= 0 ? maxHealth : currentHealth, 0, maxHealth);

        woodPerPickup = Mathf.Max(1, woodPerPickup);
        woodDropRadius = Mathf.Max(0f, woodDropRadius);
        woodDropHeightOffset = Mathf.Max(0f, woodDropHeightOffset);
        woodDropMinSpacing = Mathf.Max(0.05f, woodDropMinSpacing);
        woodDropMaxPlacementAttemptsPerPickup = Mathf.Max(1, woodDropMaxPlacementAttemptsPerPickup);
        fallbackWorkerSnapDistance = Mathf.Max(0.25f, fallbackWorkerSnapDistance);

        if (fallDuration < 0.01f)
            fallDuration = 0.01f;

        if (fadeDuration < 0.01f)
            fadeDuration = 0.01f;

        if (stayOnGroundTime < 0f)
            stayOnGroundTime = 0f;
    }

    private void CacheComponents()
    {
        cachedColliders = GetComponentsInChildren<Collider>(true);
        Transform searchRoot = visualRoot != null ? visualRoot : transform;
        cachedRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);
    }

    private void AutoAssignReferences()
    {
        if (treeCollider == null)
        {
            treeCollider = GetComponent<Collider>();
            if (treeCollider == null)
                treeCollider = GetComponentInChildren<Collider>();
        }

        if (visualRoot == null)
            visualRoot = transform;

        if (owningWorkArea == null)
        {
            owningWorkArea = GetComponentInParent<WoodcutterWorkArea>();
            if (owningWorkArea == null)
                owningWorkArea = GetComponentInChildren<WoodcutterWorkArea>();
        }
    }
}
