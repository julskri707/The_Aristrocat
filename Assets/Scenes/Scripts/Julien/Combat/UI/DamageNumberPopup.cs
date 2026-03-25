using TMPro;
using UnityEngine;

public class DamageNumberPopup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text textMesh;
    [SerializeField] private Transform visualRoot;

    [Header("Lifetime")]
    [SerializeField] private float lifetime = 0.95f;

    [Header("Motion")]
    [SerializeField] private float riseHeight = 1.75f;
    [SerializeField] private float horizontalTravel = 0.45f;
    [SerializeField] private float randomStartOffset = 0.08f;

    [Header("Scale")]
    [SerializeField] private float baseScaleMultiplier = 1f;
    [SerializeField] private float popStrength = 0.28f;
    [SerializeField] private float popDuration = 0.18f;
    [SerializeField] private float critScaleMultiplier = 1.25f;

    [Header("Fade")]
    [SerializeField] [Range(0f, 1f)] private float fadeStartNormalized = 0.55f;

    [Header("Style")]
    [SerializeField] private bool roundToInt = true;
    [SerializeField] private int decimalPlaces = 0;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color criticalColor = new Color(1f, 0.84f, 0.30f, 1f);
    [SerializeField] private bool useBoldForCrit = true;
    [SerializeField] private bool showCritPrefix = true;
    [SerializeField] private string critPrefix = "!";

    [Header("Billboard")]
    [SerializeField] private bool billboardToCamera = true;
    [SerializeField] private bool yawOnlyBillboard = false;
    [SerializeField] private bool useMainCameraFallback = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Camera targetCamera;
    private Vector3 startWorldPosition;
    private Vector3 horizontalDirection;
    private Vector3 baseScale;
    private Color activeColor;
    private bool isCritical;
    private bool initialized;
    private float elapsed;

    private void Awake()
    {
        if (textMesh == null)
        {
            textMesh = GetComponentInChildren<TMP_Text>();
        }

        if (visualRoot == null)
        {
            visualRoot = transform;
        }

        if (textMesh == null)
        {
            Debug.LogWarning($"[{nameof(DamageNumberPopup)}] Missing TMP_Text on '{name}'.", this);
        }

        baseScale = visualRoot != null ? visualRoot.localScale : Vector3.one;
    }

    private void OnEnable()
    {
        elapsed = 0f;
        initialized = false;

        if (textMesh != null)
        {
            textMesh.alpha = 1f;
        }
    }

    private void OnValidate()
    {
        lifetime = Mathf.Max(0.05f, lifetime);
        riseHeight = Mathf.Max(0f, riseHeight);
        horizontalTravel = Mathf.Max(0f, horizontalTravel);
        randomStartOffset = Mathf.Max(0f, randomStartOffset);
        baseScaleMultiplier = Mathf.Max(0.01f, baseScaleMultiplier);
        popStrength = Mathf.Max(0f, popStrength);
        popDuration = Mathf.Max(0.01f, popDuration);
        critScaleMultiplier = Mathf.Max(1f, critScaleMultiplier);
    }

    public void Initialize(float amount, bool criticalHit, Camera cameraOverride = null)
    {
        if (textMesh == null)
        {
            Debug.LogWarning($"[{nameof(DamageNumberPopup)}] Cannot initialize '{name}' because TMP_Text is missing.", this);
            return;
        }

        targetCamera = cameraOverride;

        if (targetCamera == null && useMainCameraFallback)
        {
            targetCamera = Camera.main;
        }

        isCritical = criticalHit;
        activeColor = isCritical ? criticalColor : normalColor;

        startWorldPosition = transform.position + new Vector3(
            Random.Range(-randomStartOffset, randomStartOffset),
            Random.Range(0f, randomStartOffset),
            Random.Range(-randomStartOffset, randomStartOffset)
        );

        horizontalDirection = GetHorizontalDirection();

        string valueText = roundToInt
            ? Mathf.RoundToInt(amount).ToString()
            : amount.ToString($"F{Mathf.Max(0, decimalPlaces)}");

        if (isCritical && showCritPrefix && !string.IsNullOrEmpty(critPrefix))
        {
            valueText = critPrefix + valueText;
        }

        textMesh.text = valueText;
        textMesh.color = activeColor;
        textMesh.fontStyle = isCritical && useBoldForCrit ? FontStyles.Bold : FontStyles.Normal;

        if (visualRoot != null)
        {
            float critScale = isCritical ? critScaleMultiplier : 1f;
            visualRoot.localScale = baseScale * baseScaleMultiplier * critScale;
        }

        initialized = true;

        if (debugLogs)
        {
            Debug.Log($"[{nameof(DamageNumberPopup)}] Initialized popup '{name}' with value '{valueText}'. Crit={isCritical}", this);
        }
    }

    private void Update()
    {
        if (!initialized)
            return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);

        float riseT = EaseOutCubic(t);
        float horizontalT = EaseOutQuad(t);

        transform.position = startWorldPosition
                             + Vector3.up * (riseHeight * riseT)
                             + horizontalDirection * (horizontalTravel * horizontalT);

        UpdateScale(t);
        UpdateFade(t);

        if (billboardToCamera)
        {
            UpdateBillboard();
        }

        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private Vector3 GetHorizontalDirection()
    {
        if (targetCamera != null)
        {
            Vector3 camRight = targetCamera.transform.right;
            camRight.y = 0f;

            if (camRight.sqrMagnitude > 0.0001f)
            {
                camRight.Normalize();

                float sign = Random.value < 0.5f ? -1f : 1f;
                Vector3 dir = camRight * sign;
                return dir;
            }
        }

        Vector2 random2D = Random.insideUnitCircle.normalized;
        if (random2D.sqrMagnitude < 0.0001f)
        {
            random2D = Vector2.right;
        }

        return new Vector3(random2D.x, 0f, random2D.y);
    }

    private void UpdateScale(float normalizedLifetime)
    {
        if (visualRoot == null)
            return;

        float critScale = isCritical ? critScaleMultiplier : 1f;
        float popT = Mathf.Clamp01(normalizedLifetime / Mathf.Max(0.0001f, popDuration / lifetime));
        float punch = Mathf.Sin(popT * Mathf.PI) * popStrength;

        visualRoot.localScale = baseScale * baseScaleMultiplier * critScale * (1f + punch);
    }

    private void UpdateFade(float normalizedLifetime)
    {
        if (textMesh == null)
            return;

        float alpha = 1f;

        if (normalizedLifetime >= fadeStartNormalized)
        {
            float fadeT = Mathf.InverseLerp(fadeStartNormalized, 1f, normalizedLifetime);
            alpha = 1f - fadeT;
        }

        Color c = activeColor;
        c.a *= alpha;
        textMesh.color = c;
    }

    private void UpdateBillboard()
    {
        if (targetCamera == null && useMainCameraFallback)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
            return;

        Vector3 dir = targetCamera.transform.position - transform.position;
        if (dir.sqrMagnitude <= 0.0001f)
            return;

        if (yawOnlyBillboard)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude <= 0.0001f)
                return;
        }

        transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private float EaseOutQuad(float x)
    {
        return 1f - (1f - x) * (1f - x);
    }

    private float EaseOutCubic(float x)
    {
        float inv = 1f - x;
        return 1f - inv * inv * inv;
    }
}
