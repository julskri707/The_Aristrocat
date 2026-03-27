using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SettingsMenuUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    private const string VolumeKey = "settings_master_volume";
    private const string QualityKey = "settings_quality_index";
    private const string FullscreenKey = "settings_fullscreen";

    private void Start()
    {
        SetupQualityDropdown();
        LoadSettings();
        ApplySettingsToUI();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    public void Toggle()
    {
        if (panelRoot != null)
            panelRoot.SetActive(!panelRoot.activeSelf);
    }

    public void SetVolume(float value)
    {
        AudioListener.volume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        PlayerPrefs.Save();
    }

    public void SetQuality(int qualityIndex)
    {
        qualityIndex = Mathf.Clamp(qualityIndex, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(qualityIndex, true);
        PlayerPrefs.SetInt(QualityKey, qualityIndex);
        PlayerPrefs.Save();
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        PlayerPrefs.SetInt(FullscreenKey, isFullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void CloseMenu()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void SetupQualityDropdown()
    {
        if (qualityDropdown == null)
            return;

        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(new System.Collections.Generic.List<string>(QualitySettings.names));
    }

    private void LoadSettings()
    {
        float volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
        int quality = PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel());
        bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;

        AudioListener.volume = Mathf.Clamp01(volume);
        QualitySettings.SetQualityLevel(Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1), true);
        Screen.fullScreen = fullscreen;
    }

    private void ApplySettingsToUI()
    {
        if (volumeSlider != null)
        {
            volumeSlider.SetValueWithoutNotify(AudioListener.volume);
            volumeSlider.onValueChanged.RemoveAllListeners();
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        if (qualityDropdown != null)
        {
            qualityDropdown.SetValueWithoutNotify(QualitySettings.GetQualityLevel());
            qualityDropdown.onValueChanged.RemoveAllListeners();
            qualityDropdown.onValueChanged.AddListener(SetQuality);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            fullscreenToggle.onValueChanged.RemoveAllListeners();
            fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        }
    }
}
