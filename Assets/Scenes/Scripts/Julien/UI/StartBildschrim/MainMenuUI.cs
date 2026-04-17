using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField] private string introVideoSceneName = "IntroVideoScene";
    [SerializeField] private bool useIntroVideoScene = true;

    [Header("Fade")]
    [SerializeField] private Image fadeImage;
    [SerializeField] private float fadeDuration = 0.35f;

    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject optionsPanel;

    [Header("Options UI")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private Slider fovSlider;
    [SerializeField] private Toggle vSyncToggle;
    [SerializeField] private Toggle subtitlesToggle;

    [Header("Optional Value Labels")]
    [SerializeField] private TMP_Text masterVolumeValueText;
    [SerializeField] private TMP_Text musicVolumeValueText;
    [SerializeField] private TMP_Text sfxVolumeValueText;
    [SerializeField] private TMP_Text brightnessValueText;
    [SerializeField] private TMP_Text fovValueText;

    private bool isLoading;
    private bool isApplyingUI;

    private void Start()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (optionsPanel != null) optionsPanel.SetActive(false);

        SetupSliderRanges();
        LoadSettingsIntoUI();

        if (GameSettingsRuntime.Instance != null)
            GameSettingsRuntime.Instance.ApplySavedSettings();
        else
            GameSettingsData.Apply(GameSettingsData.Load());

        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 1f;
            fadeImage.color = c;
            StartCoroutine(FadeIn());
        }
    }

    private void SetupSliderRanges()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.minValue = 0f;
            masterVolumeSlider.maxValue = 1f;
            masterVolumeSlider.wholeNumbers = false;
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.minValue = 0f;
            musicVolumeSlider.maxValue = 1f;
            musicVolumeSlider.wholeNumbers = false;
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.minValue = 0f;
            sfxVolumeSlider.maxValue = 1f;
            sfxVolumeSlider.wholeNumbers = false;
        }

        if (brightnessSlider != null)
        {
            brightnessSlider.minValue = 0.25f;
            brightnessSlider.maxValue = 1.5f;
            brightnessSlider.wholeNumbers = false;
        }

        if (fovSlider != null)
        {
            fovSlider.minValue = 40f;
            fovSlider.maxValue = 100f;
            fovSlider.wholeNumbers = true;
        }
    }

    public void PlayGame()
    {
        if (isLoading) return;

        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogError("MainMenuUI: gameplaySceneName ist leer.");
            return;
        }

        if (useIntroVideoScene && string.IsNullOrWhiteSpace(introVideoSceneName))
        {
            Debug.LogError("MainMenuUI: introVideoSceneName ist leer.");
            return;
        }

        StartCoroutine(LoadGameRoutine());
    }

    public void OpenOptions()
    {
        SetupSliderRanges();
        LoadSettingsIntoUI();

        if (mainPanel != null) mainPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(true);
    }

    public void SaveAndCloseOptions()
    {
        GameSettingsData.SettingsValues values = ReadSettingsFromUI();
        GameSettingsData.Save(values);

        if (GameSettingsRuntime.Instance != null)
            GameSettingsRuntime.Instance.ApplySettings(values);
        else
            GameSettingsData.Apply(values);

        CloseOptionsToMainMenu();
        RefreshValueLabels();
    }

    public void CancelAndCloseOptions()
    {
        GameSettingsData.SettingsValues savedValues = GameSettingsData.Load();

        ApplySettingsToUI(savedValues);

        if (GameSettingsRuntime.Instance != null)
            GameSettingsRuntime.Instance.ApplySettings(savedValues);
        else
            GameSettingsData.Apply(savedValues);

        CloseOptionsToMainMenu();
        RefreshValueLabels();
    }

    public void ResetToDefaultSettings()
    {
        ApplySettingsToUI(GameSettingsData.GetDefaults());
    }

    public void OnAnySettingChanged()
    {
        if (isApplyingUI)
            return;

        RefreshValueLabels();
        ApplyPreviewFromUI();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void LoadSettingsIntoUI()
    {
        ApplySettingsToUI(GameSettingsData.Load());
    }

    private void ApplySettingsToUI(GameSettingsData.SettingsValues values)
    {
        isApplyingUI = true;

        if (masterVolumeSlider != null) masterVolumeSlider.SetValueWithoutNotify(values.masterVolume);
        if (musicVolumeSlider != null) musicVolumeSlider.SetValueWithoutNotify(values.musicVolume);
        if (sfxVolumeSlider != null) sfxVolumeSlider.SetValueWithoutNotify(values.sfxVolume);
        if (brightnessSlider != null) brightnessSlider.SetValueWithoutNotify(values.brightness);
        if (fovSlider != null) fovSlider.SetValueWithoutNotify(values.fov);
        if (vSyncToggle != null) vSyncToggle.SetIsOnWithoutNotify(values.vSync);
        if (subtitlesToggle != null) subtitlesToggle.SetIsOnWithoutNotify(values.subtitles);

        isApplyingUI = false;
        RefreshValueLabels();
    }

    private GameSettingsData.SettingsValues ReadSettingsFromUI()
    {
        GameSettingsData.SettingsValues values = GameSettingsData.GetDefaults();

        if (masterVolumeSlider != null) values.masterVolume = masterVolumeSlider.value;
        if (musicVolumeSlider != null) values.musicVolume = musicVolumeSlider.value;
        if (sfxVolumeSlider != null) values.sfxVolume = sfxVolumeSlider.value;
        if (brightnessSlider != null) values.brightness = brightnessSlider.value;
        if (fovSlider != null) values.fov = fovSlider.value;
        if (vSyncToggle != null) values.vSync = vSyncToggle.isOn;
        if (subtitlesToggle != null) values.subtitles = subtitlesToggle.isOn;

        return values;
    }

    private void ApplyPreviewFromUI()
    {
        GameSettingsData.SettingsValues previewValues = ReadSettingsFromUI();

        if (GameSettingsRuntime.Instance != null)
            GameSettingsRuntime.Instance.ApplySettings(previewValues);
        else
            GameSettingsData.Apply(previewValues);
    }

    private void RefreshValueLabels()
    {
        if (masterVolumeValueText != null && masterVolumeSlider != null)
            masterVolumeValueText.text = Mathf.RoundToInt(masterVolumeSlider.value * 100f) + "%";

        if (musicVolumeValueText != null && musicVolumeSlider != null)
            musicVolumeValueText.text = Mathf.RoundToInt(musicVolumeSlider.value * 100f) + "%";

        if (sfxVolumeValueText != null && sfxVolumeSlider != null)
            sfxVolumeValueText.text = Mathf.RoundToInt(sfxVolumeSlider.value * 100f) + "%";

        if (brightnessValueText != null && brightnessSlider != null)
            brightnessValueText.text = Mathf.RoundToInt(brightnessSlider.value * 100f) + "%";

        if (fovValueText != null && fovSlider != null)
            fovValueText.text = Mathf.RoundToInt(fovSlider.value).ToString();
    }

    private void CloseOptionsToMainMenu()
    {
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    private IEnumerator LoadGameRoutine()
    {
        isLoading = true;
        yield return StartCoroutine(FadeOut());

        GameLaunchState.NextGameplaySceneName = gameplaySceneName;

        if (useIntroVideoScene)
            SceneManager.LoadScene(introVideoSceneName);
        else
            SceneManager.LoadScene(gameplaySceneName);
    }

    private IEnumerator FadeIn()
    {
        if (fadeImage == null)
            yield break;

        float time = 0f;
        Color c = fadeImage.color;

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / fadeDuration);
            c.a = 1f - t;
            fadeImage.color = c;
            yield return null;
        }

        c.a = 0f;
        fadeImage.color = c;
    }

    private IEnumerator FadeOut()
    {
        if (fadeImage == null)
            yield break;

        float time = 0f;
        Color c = fadeImage.color;

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / fadeDuration);
            c.a = t;
            fadeImage.color = c;
            yield return null;
        }

        c.a = 1f;
        fadeImage.color = c;
    }
}
