using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class StylizedValleyFog : MonoBehaviour
{
    public enum FogQuality
    {
        Subtle,
        Medium,
        Strong
    }

    [Header("Auto Apply")]
    [SerializeField] private bool applyContinuously = true;
    [SerializeField] private bool applyOnEnable = true;

    [Header("Scene References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Light mainDirectionalLight;

    [Header("Look")]
    [SerializeField] private FogQuality fogQuality = FogQuality.Medium;
    [SerializeField] private Color fogColor = new Color(0.68f, 0.77f, 0.88f, 1f);
    [SerializeField] private Color horizonFogColor = new Color(0.77f, 0.82f, 0.88f, 1f);
    [SerializeField] private bool blendWithLightColor = true;
    [SerializeField, Range(0f, 1f)] private float lightColorInfluence = 0.18f;

    [Header("Distance Fog")]
    [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;
    [SerializeField, Min(0f)] private float fogDensity = 0.0085f;
    [SerializeField, Min(0f)] private float linearStart = 65f;
    [SerializeField, Min(0f)] private float linearEnd = 260f;

    [Header("Height Blend")]
    [SerializeField] private bool useHeightCompensation = true;
    [SerializeField] private float fogBaseHeight = 2f;
    [SerializeField] private float fogTopHeight = 40f;
    [SerializeField, Range(0f, 2f)] private float lowCameraBoost = 0.55f;

    [Header("Water / Valley Boost")]
    [SerializeField] private bool boostNearWaterLevel = true;
    [SerializeField] private float waterLevel = 0f;
    [SerializeField] private float waterBoostRange = 14f;
    [SerializeField, Range(0f, 2f)] private float waterBoost = 0.4f;

    [Header("Skybox Blend")]
    [SerializeField] private bool tintSkyboxExposure = true;
    [SerializeField] private string skyboxTintProperty = "_Tint";
    [SerializeField] private string skyboxExposureProperty = "_Exposure";
    [SerializeField, Range(0.1f, 2f)] private float skyboxExposure = 0.95f;
    [SerializeField, Range(0f, 1f)] private float skyboxTintStrength = 0.2f;

    private float _baseDensity;

    private void Reset()
    {
        targetCamera = Camera.main;
        mainDirectionalLight = FindMainDirectionalLight();

        fogMode = FogMode.ExponentialSquared;
        fogDensity = 0.0085f;
        linearStart = 65f;
        linearEnd = 260f;

        fogColor = new Color(0.68f, 0.77f, 0.88f, 1f);
        horizonFogColor = new Color(0.77f, 0.82f, 0.88f, 1f);
        fogBaseHeight = 2f;
        fogTopHeight = 40f;
        waterLevel = 0f;
    }

    private void OnEnable()
    {
        CacheDefaults();

        if (applyOnEnable)
            ApplyFog();
    }

    private void Update()
    {
        if (!applyContinuously)
            return;

        ApplyFog();
    }

    [ContextMenu("Apply Fog Now")]
    public void ApplyFog()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (mainDirectionalLight == null)
            mainDirectionalLight = FindMainDirectionalLight();

        RenderSettings.fog = true;
        RenderSettings.fogMode = fogMode;

        Color finalFogColor = Color.Lerp(fogColor, horizonFogColor, 0.35f);

        if (blendWithLightColor && mainDirectionalLight != null)
        {
            finalFogColor = Color.Lerp(finalFogColor, mainDirectionalLight.color, lightColorInfluence);
        }

        RenderSettings.fogColor = finalFogColor;

        float densityMultiplier = GetQualityMultiplier();
        float finalDensity = fogDensity * densityMultiplier;

        if (targetCamera != null && useHeightCompensation)
        {
            float camY = targetCamera.transform.position.y;
            float height01 = Mathf.InverseLerp(fogTopHeight, fogBaseHeight, camY);
            finalDensity *= 1f + (height01 * lowCameraBoost);
        }

        if (targetCamera != null && boostNearWaterLevel)
        {
            float distanceToWater = Mathf.Abs(targetCamera.transform.position.y - waterLevel);
            float water01 = 1f - Mathf.Clamp01(distanceToWater / Mathf.Max(0.01f, waterBoostRange));
            finalDensity *= 1f + (water01 * waterBoost);
        }

        switch (fogMode)
        {
            case FogMode.Linear:
                RenderSettings.fogStartDistance = linearStart;
                RenderSettings.fogEndDistance = linearEnd;
                break;

            case FogMode.Exponential:
            case FogMode.ExponentialSquared:
                RenderSettings.fogDensity = finalDensity;
                break;
        }

        ApplySkyboxTint(finalFogColor);
    }

    private void CacheDefaults()
    {
        _baseDensity = fogDensity;
    }

    private float GetQualityMultiplier()
    {
        switch (fogQuality)
        {
            case FogQuality.Subtle:
                return 0.7f;
            case FogQuality.Strong:
                return 1.25f;
            default:
                return 1f;
        }
    }

    private void ApplySkyboxTint(Color finalFogColor)
    {
        if (!tintSkyboxExposure)
            return;

        Material sky = RenderSettings.skybox;
        if (sky == null)
            return;

        if (sky.HasProperty(skyboxTintProperty))
        {
            Color currentTint = sky.GetColor(skyboxTintProperty);
            Color targetTint = Color.Lerp(currentTint, finalFogColor, skyboxTintStrength);
            sky.SetColor(skyboxTintProperty, targetTint);
        }

        if (sky.HasProperty(skyboxExposureProperty))
        {
            sky.SetFloat(skyboxExposureProperty, skyboxExposure);
        }
    }

    private static Light FindMainDirectionalLight()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }
}
