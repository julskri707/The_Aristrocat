using UnityEngine;
using UnityEngine.Audio;

public static class GameSettingsData
{
    public const string MasterVolumeKey = "Settings_MasterVolume";
    public const string MusicVolumeKey = "Settings_MusicVolume";
    public const string SfxVolumeKey = "Settings_SfxVolume";
    public const string BrightnessKey = "Settings_Brightness";
    public const string VSyncKey = "Settings_VSync";
    public const string SubtitlesKey = "Settings_Subtitles";
    public const string FovKey = "Settings_Fov";

    public static float DefaultMasterVolume => 1f;
    public static float DefaultMusicVolume => 1f;
    public static float DefaultSfxVolume => 1f;
    public static float DefaultBrightness => 1f;
    public static bool DefaultVSync => true;
    public static bool DefaultSubtitles => true;
    public static float DefaultFov => 60f;

    public struct SettingsValues
    {
        public float masterVolume;
        public float musicVolume;
        public float sfxVolume;
        public float brightness;
        public bool vSync;
        public bool subtitles;
        public float fov;
    }

    public static SettingsValues Load()
    {
        SettingsValues values = new SettingsValues
        {
            masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume),
            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume),
            sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume),
            brightness = PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness),
            vSync = PlayerPrefs.GetInt(VSyncKey, DefaultVSync ? 1 : 0) == 1,
            subtitles = PlayerPrefs.GetInt(SubtitlesKey, DefaultSubtitles ? 1 : 0) == 1,
            fov = PlayerPrefs.GetFloat(FovKey, DefaultFov)
        };

        values.masterVolume = Mathf.Clamp01(values.masterVolume);
        values.musicVolume = Mathf.Clamp01(values.musicVolume);
        values.sfxVolume = Mathf.Clamp01(values.sfxVolume);
        values.brightness = Mathf.Clamp(values.brightness, 0.25f, 1.5f);
        values.fov = Mathf.Clamp(values.fov, 40f, 100f);

        return values;
    }

    public static void Save(SettingsValues values)
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, Mathf.Clamp01(values.masterVolume));
        PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(values.musicVolume));
        PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(values.sfxVolume));
        PlayerPrefs.SetFloat(BrightnessKey, Mathf.Clamp(values.brightness, 0.25f, 1.5f));
        PlayerPrefs.SetInt(VSyncKey, values.vSync ? 1 : 0);
        PlayerPrefs.SetInt(SubtitlesKey, values.subtitles ? 1 : 0);
        PlayerPrefs.SetFloat(FovKey, Mathf.Clamp(values.fov, 40f, 100f));
        PlayerPrefs.Save();
    }

    public static SettingsValues GetDefaults()
    {
        return new SettingsValues
        {
            masterVolume = DefaultMasterVolume,
            musicVolume = DefaultMusicVolume,
            sfxVolume = DefaultSfxVolume,
            brightness = DefaultBrightness,
            vSync = DefaultVSync,
            subtitles = DefaultSubtitles,
            fov = DefaultFov
        };
    }

    public static void Apply(
        SettingsValues values,
        AudioMixer masterMixer = null,
        string masterVolumeParameter = "MasterVolume",
        AudioMixer musicMixer = null,
        string musicVolumeParameter = "MusicVolume",
        AudioMixer sfxMixer = null,
        string sfxVolumeParameter = "SfxVolume")
    {
        QualitySettings.vSyncCount = values.vSync ? 1 : 0;

        AudioListener.volume = Mathf.Clamp01(values.masterVolume);

        if (masterMixer != null)
            masterMixer.SetFloat(masterVolumeParameter, LinearToDecibel(values.masterVolume));

        if (musicMixer != null)
            musicMixer.SetFloat(musicVolumeParameter, LinearToDecibel(values.musicVolume));

        if (sfxMixer != null)
            sfxMixer.SetFloat(sfxVolumeParameter, LinearToDecibel(values.sfxVolume));

        Shader.SetGlobalFloat("_GameBrightness", Mathf.Clamp(values.brightness, 0.25f, 1.5f));
    }

    public static float LinearToDecibel(float value)
    {
        value = Mathf.Clamp(value, 0.0001f, 1f);
        return Mathf.Log10(value) * 20f;
    }
}