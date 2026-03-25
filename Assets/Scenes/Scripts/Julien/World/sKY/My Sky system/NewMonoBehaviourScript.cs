using UnityEngine;

[DisallowMultipleComponent]
public class NightDomeLightController : MonoBehaviour
{
    [Header("References")]
    public Light domeLight;
    public DayNightSystem dayNightSystem;

    [Header("Night Settings")]
    public float nightIntensity = 0.4f;
    public float fadeSpeed = 1.5f;

    [Header("Night Range")]
    public float nightStart = 20f;
    public float nightEnd = 6f;

    private float targetIntensity;

    void Awake()
    {
        if (domeLight == null)
            domeLight = GetComponent<Light>();

        if (domeLight != null)
            domeLight.intensity = 0f;
    }

    void Update()
    {
        if (dayNightSystem == null || domeLight == null)
            return;

        float time = dayNightSystem.timeOfDay;

        bool isNight =
            (time >= nightStart) ||
            (time <= nightEnd);

        targetIntensity = isNight ? nightIntensity : 0f;

        domeLight.intensity = Mathf.Lerp(
            domeLight.intensity,
            targetIntensity,
            Time.deltaTime * fadeSpeed
        );
    }
}