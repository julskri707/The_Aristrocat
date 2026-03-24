using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class GameSettingsRuntime : MonoBehaviour
{
    public static GameSettingsRuntime Instance { get; private set; }

    [Header("Optional Audio Mixers")]
    [SerializeField] private AudioMixer masterMixer;
    [SerializeField] private string masterVolumeParameter = "MasterVolume";

    [SerializeField] private AudioMixer musicMixer;
    [SerializeField] private string musicVolumeParameter = "MusicVolume";

    [SerializeField] private AudioMixer sfxMixer;
    [SerializeField] private string sfxVolumeParameter = "SfxVolume";

    [Header("Optional Brightness Overlay")]
    [SerializeField] private Image brightnessOverlayImage;

    [Header("Brightness Settings")]
    [SerializeField] private float minOverlayAlphaAtLowestBrightness = 0.55f;

    [Header("Optional Camera FOV")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool autoUseMainCameraIfMissing = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ApplySavedSettings();
    }

    public void ApplySavedSettings()
    {
        ApplySettings(GameSettingsData.Load());
    }

    public void ApplySettings(GameSettingsData.SettingsValues values)
    {
        GameSettingsData.Apply(
            values,
            masterMixer,
            masterVolumeParameter,
            musicMixer,
            musicVolumeParameter,
            sfxMixer,
            sfxVolumeParameter);

        ApplyBrightnessOverlay(values.brightness);
        ApplyCameraFov(values.fov);
    }

    public void RegisterBrightnessOverlay(Image overlayImage)
    {
        brightnessOverlayImage = overlayImage;
        ApplyBrightnessOverlay(GameSettingsData.Load().brightness);
    }

    public void RegisterTargetCamera(Camera cam)
    {
        targetCamera = cam;
        ApplyCameraFov(GameSettingsData.Load().fov);
    }

    private void ApplyBrightnessOverlay(float brightness)
    {
        if (brightnessOverlayImage == null)
            return;

        brightness = Mathf.Clamp(brightness, 0.25f, 1.5f);

        Color c = brightnessOverlayImage.color;

        if (brightness >= 1f)
        {
            c.a = 0f;
        }
        else
        {
            float t = Mathf.InverseLerp(1f, 0.25f, brightness);
            c.a = Mathf.Lerp(0f, minOverlayAlphaAtLowestBrightness, t);
        }

        brightnessOverlayImage.color = c;
    }

    private void ApplyCameraFov(float fov)
    {
        fov = Mathf.Clamp(fov, 40f, 100f);

        if (targetCamera == null && autoUseMainCameraIfMissing)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        if (targetCamera.orthographic)
            return;

        targetCamera.fieldOfView = fov;
    }
}