using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using TMPro;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class IntroVideoLoader : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private AudioSource audioSource;

    [Header("Loading UI")]
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private TMP_Text skipText;

    [Header("Fade Overlay")]
    [SerializeField] private Image fadeOverlayImage;
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.5f;

    [Header("Scenes")]
    [SerializeField] private string fallbackGameplaySceneName = "GameScene";

    [Header("Options")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool allowSkip = true;
    [SerializeField] private KeyCode skipKey = KeyCode.Space;
    [SerializeField] private float minimumVideoTimeBeforeSkip = 0f;

    [Header("Loading")]
    [SerializeField] private bool waitForSceneLoadBeforeSwitch = true;
    [SerializeField] private string loadingPrefix = "Laden";
    [SerializeField] private bool animateLoadingDots = true;

    private AsyncOperation loadOperation;
    private bool videoFinished;
    private bool sceneReady;
    private bool switchingScene;
    private float playedTime;
    private float dotsTimer;
    private int dotsCount;

    private void Awake()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        SetFadeOverlayAlphaInstant(1f);
    }

    private void Start()
    {
        if (videoPlayer == null)
        {
            Debug.LogError("[IntroVideoLoader] No VideoPlayer assigned.", this);
            LoadFallbackImmediately();
            return;
        }

        string targetScene = string.IsNullOrWhiteSpace(GameLaunchState.NextGameplaySceneName)
            ? fallbackGameplaySceneName
            : GameLaunchState.NextGameplaySceneName;

        videoPlayer.loopPointReached += HandleVideoFinished;
        videoPlayer.errorReceived += HandleVideoError;

        StartCoroutine(LoadGameplaySceneAsync(targetScene));

        if (playOnStart)
            videoPlayer.Play();

        if (audioSource != null && !audioSource.isPlaying)
            audioSource.Play();

        RefreshSkipText();
        RefreshLoadingText(0f);
        StartCoroutine(FadeOverlayRoutine(1f, 0f, fadeInDuration));
    }

    private void Update()
    {
        if (switchingScene)
            return;

        if (videoPlayer != null && videoPlayer.isPlaying)
            playedTime += Time.deltaTime;

        if (allowSkip && Input.GetKeyDown(skipKey) && playedTime >= minimumVideoTimeBeforeSkip)
        {
            videoFinished = true;
            StartCoroutine(TryEnterGameplayRoutine());
        }

        UpdateLoadingText();
        RefreshSkipText();
    }

    private IEnumerator LoadGameplaySceneAsync(string sceneName)
    {
        loadOperation = SceneManager.LoadSceneAsync(sceneName);
        loadOperation.allowSceneActivation = false;

        while (loadOperation.progress < 0.9f)
        {
            RefreshLoadingText(loadOperation.progress);
            yield return null;
        }

        sceneReady = true;
        RefreshLoadingText(1f);

        if (videoFinished)
            StartCoroutine(TryEnterGameplayRoutine());
    }

    private void HandleVideoFinished(VideoPlayer source)
    {
        videoFinished = true;
        StartCoroutine(TryEnterGameplayRoutine());
    }

    private void HandleVideoError(VideoPlayer source, string message)
    {
        Debug.LogError("[IntroVideoLoader] Video error: " + message, this);
        videoFinished = true;
        StartCoroutine(TryEnterGameplayRoutine());
    }

    private IEnumerator TryEnterGameplayRoutine()
    {
        if (switchingScene)
            yield break;

        if (!videoFinished)
            yield break;

        if (waitForSceneLoadBeforeSwitch && !sceneReady)
            yield break;

        switchingScene = true;

        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Stop();

        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();

        yield return StartCoroutine(FadeOverlayRoutine(GetCurrentFadeAlpha(), 1f, fadeOutDuration));

        if (loadOperation != null)
        {
            loadOperation.allowSceneActivation = true;
            yield break;
        }

        LoadFallbackImmediately();
    }

    private void LoadFallbackImmediately()
    {
        string targetScene = string.IsNullOrWhiteSpace(GameLaunchState.NextGameplaySceneName)
            ? fallbackGameplaySceneName
            : GameLaunchState.NextGameplaySceneName;

        SceneManager.LoadScene(targetScene);
    }

    private void UpdateLoadingText()
    {
        if (loadingText == null)
            return;

        if (!animateLoadingDots)
        {
            RefreshLoadingText(loadOperation != null ? loadOperation.progress : 0f);
            return;
        }

        dotsTimer += Time.deltaTime;
        if (dotsTimer >= 0.4f)
        {
            dotsTimer = 0f;
            dotsCount = (dotsCount + 1) % 4;
            RefreshLoadingText(loadOperation != null ? loadOperation.progress : 0f);
        }
    }

    private void RefreshLoadingText(float progress)
    {
        if (loadingText == null)
            return;

        string dots = "";
        if (animateLoadingDots)
        {
            for (int i = 0; i < dotsCount; i++)
                dots += ".";
        }

        int percent = Mathf.RoundToInt(Mathf.Clamp01(progress / 0.9f) * 100f);
        loadingText.text = loadingPrefix + dots + " " + percent + "%";
    }

    private void RefreshSkipText()
    {
        if (skipText == null)
            return;

        if (!allowSkip)
        {
            skipText.gameObject.SetActive(false);
            return;
        }

        skipText.gameObject.SetActive(true);

        if (playedTime >= minimumVideoTimeBeforeSkip)
            skipText.text = "Leertaste zum Überspringen";
        else
            skipText.text = "";
    }

    private IEnumerator FadeOverlayRoutine(float fromAlpha, float toAlpha, float duration)
    {
        if (fadeOverlayImage == null)
            yield break;

        if (duration <= 0.0001f)
        {
            SetFadeOverlayAlphaInstant(toAlpha);
            yield break;
        }

        float time = 0f;
        Color c = fadeOverlayImage.color;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            c.a = Mathf.Lerp(fromAlpha, toAlpha, t);
            fadeOverlayImage.color = c;
            yield return null;
        }

        c.a = toAlpha;
        fadeOverlayImage.color = c;
    }

    private void SetFadeOverlayAlphaInstant(float alpha)
    {
        if (fadeOverlayImage == null)
            return;

        Color c = fadeOverlayImage.color;
        c.a = alpha;
        fadeOverlayImage.color = c;
    }

    private float GetCurrentFadeAlpha()
    {
        if (fadeOverlayImage == null)
            return 0f;

        return fadeOverlayImage.color.a;
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= HandleVideoFinished;
            videoPlayer.errorReceived -= HandleVideoError;
        }
    }
}
