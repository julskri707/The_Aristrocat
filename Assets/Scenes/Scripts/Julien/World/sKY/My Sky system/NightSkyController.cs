using UnityEngine;

[DisallowMultipleComponent]
public class NightSkyController : MonoBehaviour
{
    public enum TimeSourceMode
    {
        DayNightSystem,
        NPCTimeSystem,
        Manual
    }

    [Header("Time Source")]
    public TimeSourceMode timeSourceMode = TimeSourceMode.DayNightSystem;
    public DayNightSystem dayNightSystem;
    public NPCTimeSystem npcTimeSystem;
    [Range(0f, 24f)] public float manualTimeOfDay = 0f;
    public bool autoFindReferences = true;

    [Header("Star Dome")]
    public Renderer starDomeRenderer;
    public bool disableRendererInDaylight = true;
    public Color starsTint = Color.white;
    [Min(0f)] public float starsMaxEmission = 1.25f;
    public AnimationCurve starsVisibilityOverDay = new AnimationCurve(
        new Keyframe(0.00f, 1.00f),
        new Keyframe(0.20f, 1.00f),
        new Keyframe(0.28f, 0.25f),
        new Keyframe(0.32f, 0.00f),
        new Keyframe(0.70f, 0.00f),
        new Keyframe(0.80f, 0.25f),
        new Keyframe(0.88f, 1.00f),
        new Keyframe(1.00f, 1.00f)
    );

    [Header("Rotation")]
    public bool rotateStars = true;
    public bool rotateOnlyWhenVisible = true;
    public Vector3 rotationAxis = Vector3.up;
    public float rotationSpeed = 0.2f;

    [Header("Twinkle")]
    public bool enableTwinkle = true;
    [Range(0f, 2f)] public float twinkleStrength = 0.15f;
    public float twinkleSpeed = 1.2f;
    public float noiseOffset = 7.13f;

    [Header("Follow Target")]
    public bool followTarget = true;
    public Transform followTransform;
    public bool keepOwnRotation = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private Material _runtimeStarMaterial;
    private Material _cachedStarMaterial;

    private bool _warnedMissingRenderer;
    private bool _warnedMissingMaterial;
    private bool _warnedMissingDayNightSystem;
    private bool _warnedMissingNPCTimeSystem;
    private bool _warnedMissingColorProperty;
    private bool _warnedMissingEmissionProperty;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Reset()
    {
        if (starDomeRenderer == null)
            starDomeRenderer = GetComponent<Renderer>();

        if (followTransform == null && Camera.main != null)
            followTransform = Camera.main.transform;

        TryAutoFindTimeSource();
    }

    private void Awake()
    {
        if (starDomeRenderer == null)
            starDomeRenderer = GetComponent<Renderer>();

        if (autoFindReferences)
            TryAutoFindTimeSource();

        CacheStarMaterial(logWarnings: debugLogs);
        ApplyStars(GetNormalizedTimeOfDay(), logWarnings: debugLogs);
    }

    private void OnValidate()
    {
        if (starDomeRenderer == null)
            starDomeRenderer = GetComponent<Renderer>();

        if (!Application.isPlaying)
        {
            _runtimeStarMaterial = null;
            CacheStarMaterial(logWarnings: false);
            ApplyStars(GetNormalizedTimeOfDay(), logWarnings: false);
        }
    }

    private void Update()
    {
        if (autoFindReferences)
            TryAutoFindTimeSource();

        FollowIfNeeded();

        float normalized = GetNormalizedTimeOfDay();
        float visibility = ApplyStars(normalized, logWarnings: debugLogs);

        if (rotateStars)
            RotateStars(visibility);
    }

    private void TryAutoFindTimeSource()
    {
        if (dayNightSystem == null)
            dayNightSystem = FindFirstObjectByType<DayNightSystem>();

        if (npcTimeSystem == null)
            npcTimeSystem = NPCTimeSystem.Instance != null ? NPCTimeSystem.Instance : FindFirstObjectByType<NPCTimeSystem>();

        if (followTransform == null && Camera.main != null)
            followTransform = Camera.main.transform;
    }

    private float GetNormalizedTimeOfDay()
    {
        float hour = 0f;

        switch (timeSourceMode)
        {
            case TimeSourceMode.DayNightSystem:
                if (dayNightSystem != null)
                {
                    hour = dayNightSystem.timeOfDay;
                }
                else
                {
                    WarnOnce(ref _warnedMissingDayNightSystem, "timeSourceMode is DayNightSystem, but no DayNightSystem reference is assigned.");
                    hour = manualTimeOfDay;
                }
                break;

            case TimeSourceMode.NPCTimeSystem:
                if (npcTimeSystem != null)
                {
                    hour = npcTimeSystem.TimeOfDay;
                }
                else
                {
                    WarnOnce(ref _warnedMissingNPCTimeSystem, "timeSourceMode is NPCTimeSystem, but no NPCTimeSystem reference is assigned.");
                    hour = manualTimeOfDay;
                }
                break;

            case TimeSourceMode.Manual:
                hour = manualTimeOfDay;
                break;
        }

        return Mathf.Repeat(hour, 24f) / 24f;
    }

    private void FollowIfNeeded()
    {
        if (!followTarget || followTransform == null)
            return;

        transform.position = followTransform.position;

        if (!keepOwnRotation)
            transform.rotation = followTransform.rotation;
    }

    private void RotateStars(float visibility)
    {
        if (rotateOnlyWhenVisible && visibility <= 0.001f)
            return;

        Vector3 axis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
        transform.Rotate(axis, rotationSpeed * Time.deltaTime, Space.World);
    }

    private float ApplyStars(float normalized, bool logWarnings)
    {
        if (starDomeRenderer == null)
        {
            WarnOnce(ref _warnedMissingRenderer, "starDomeRenderer is missing.");
            return 0f;
        }

        float starVisibility = 0f;
        if (starsVisibilityOverDay != null && starsVisibilityOverDay.length > 0)
            starVisibility = Mathf.Clamp01(starsVisibilityOverDay.Evaluate(normalized));

        if (disableRendererInDaylight)
            starDomeRenderer.enabled = starVisibility > 0.001f;
        else
            starDomeRenderer.enabled = true;

        CacheStarMaterial(logWarnings);

        if (_cachedStarMaterial == null)
            return starVisibility;

        Color baseColor = starsTint;
        baseColor.a *= starVisibility;

        if (_cachedStarMaterial.HasProperty(BaseColorId))
            _cachedStarMaterial.SetColor(BaseColorId, baseColor);
        else if (_cachedStarMaterial.HasProperty(ColorId))
            _cachedStarMaterial.SetColor(ColorId, baseColor);
        else
            WarnOnce(ref _warnedMissingColorProperty, "Star material has neither _BaseColor nor _Color.");

        if (_cachedStarMaterial.HasProperty(EmissionColorId))
        {
            float twinkle = 1f;
            if (enableTwinkle && Application.isPlaying)
            {
                float t = Time.time * twinkleSpeed + noiseOffset;
                float n1 = Mathf.PerlinNoise(t, 0.19f);
                float n2 = Mathf.PerlinNoise(0.73f, t * 0.67f);
                float combined = ((n1 + n2) * 0.5f * 2f) - 1f;
                twinkle = 1f + (combined * twinkleStrength);
            }

            Color emission = starsTint * (starsMaxEmission * starVisibility * twinkle);
            _cachedStarMaterial.SetColor(EmissionColorId, emission);
        }
        else
        {
            WarnOnce(ref _warnedMissingEmissionProperty, "Star material has no _EmissionColor property. Twinkle glow will not be visible.");
        }

        return starVisibility;
    }

    private void CacheStarMaterial(bool logWarnings)
    {
        _cachedStarMaterial = null;

        if (starDomeRenderer == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            _cachedStarMaterial = starDomeRenderer.sharedMaterial;

            if (_cachedStarMaterial == null && logWarnings)
                WarnOnce(ref _warnedMissingMaterial, "starDomeRenderer has no sharedMaterial assigned.");

            return;
        }
#endif

        if (_runtimeStarMaterial == null)
            _runtimeStarMaterial = starDomeRenderer.material;

        _cachedStarMaterial = _runtimeStarMaterial;

        if (_cachedStarMaterial == null && logWarnings)
            WarnOnce(ref _warnedMissingMaterial, "starDomeRenderer has no material assigned.");
    }

    private void OnDestroy()
    {
        if (_runtimeStarMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeStarMaterial);
            else
                DestroyImmediate(_runtimeStarMaterial);

            _runtimeStarMaterial = null;
        }
    }

    private void WarnOnce(ref bool flag, string message)
    {
        if (flag)
            return;

        flag = true;

        if (debugLogs)
            Debug.LogWarning("[NightSkyController] " + message, this);
    }
}
