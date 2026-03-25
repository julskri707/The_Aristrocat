using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class DayNightSystem : MonoBehaviour
{
    public enum TimeSourceMode
    {
        SelfUpdate,
        SyncFromNPCTimeSystem,
        SyncFromTickSystem,
        ManualAdvance
    }

    [Header("Time State")]
    [Range(0f, 24f)] public float timeOfDay = 12f;
    public int dayIndex = 0;
    [Min(1f)] public float secondsPerGameDay = 300f;

    [Header("Time Source")]
    public TimeSourceMode timeSourceMode = TimeSourceMode.SelfUpdate;
    public bool useUnscaledDeltaTime = false;
    public bool pauseTime = false;
    public bool autoFindReferences = true;
    public bool resetDayIndexOnSourceTickReset = true;
    public NPCTimeSystem npcTimeSystem;
    public TickSystem tickSystem;

    [Header("Visual Smoothing")]
    public bool smoothSunAndShadows = true;
    [Min(0.01f)] public float visualSmoothingDuration = 0.35f;
    public bool useSmoothStepForVisuals = true;

    [Header("Sun")]
    public Light sunLight;
    public Gradient sunColorOverDay;
    public AnimationCurve sunIntensityOverDay = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Moon")]
    public Light moonLight;
    public Gradient moonColorOverDay;
    public AnimationCurve moonIntensityOverDay = AnimationCurve.Linear(0f, 0f, 1f, 0f);

    [Header("Stars")]
    [Tooltip("Assign a mesh renderer for your star dome / sky sphere / inside-out sphere.")]
    public Renderer starDomeRenderer;
    public Color starsTint = Color.white;
    [Min(0f)] public float starsMaxEmission = 1.25f;
    public AnimationCurve starsVisibilityOverDay = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    public bool disableStarRendererInDaylight = true;

    [Header("Environment")]
    public Gradient ambientColorOverDay;
    public AnimationCurve fogDensityOverDay;
    [Range(0f, 24f)] public float sunriseTime = 6f;
    [Range(0f, 24f)] public float sunsetTime = 20f;

    [Header("Optional URP Volume Exposure")]
    [Tooltip("Assign a URP Volume component here. Only used if URP is active and the reference is set.")]
    public Component urpVolume;
    public AnimationCurve urpPostExposureOverDay = AnimationCurve.Linear(0f, -0.75f, 1f, 0.35f);

    [Header("Debug")]
    public bool debugLogs = false;

    public event Action<float> OnTimeChanged;
    public event Action<int> OnDayChanged;
    public event Action OnSunrise;
    public event Action OnSunset;

    public float NormalizedTimeOfDay => NormalizeHour(timeOfDay) / HoursPerDay;
    public bool IsDaylightNow => IsWithinForwardInterval(timeOfDay, sunriseTime, sunsetTime);

    private const float HoursPerDay = 24f;
    private const float TimeSnapEpsilon = 0.001f;

    private float _sunBaseYaw;
    private float _sunBaseRoll;
    private float _moonBaseYaw;
    private float _moonBaseRoll;

    private TimeSourceMode _lastTimeSourceMode;

    private bool _npcSyncInitialized;
    private int _lastNpcTickIndex;
    private float _lastNpcTimeOfDay;

    private bool _tickSyncInitialized;
    private int _lastObservedTickSystemIndex;

    private bool _visualStateInitialized;
    private double _targetAbsoluteHours;
    private double _visualAbsoluteHours;
    private double _visualTweenStartAbsoluteHours;
    private double _visualTweenTargetAbsoluteHours;
    private float _visualTweenElapsed;
    private bool _visualTweenActive;

    private bool _warnedMissingSunLight;
    private bool _warnedMissingMoonLight;
    private bool _warnedMissingStarRenderer;
    private bool _warnedMissingSunColor;
    private bool _warnedMissingSunIntensity;
    private bool _warnedMissingMoonColor;
    private bool _warnedMissingMoonIntensity;
    private bool _warnedMissingAmbient;
    private bool _warnedFogCurve;
    private bool _warnedMissingNpcTimeSystem;
    private bool _warnedMissingTickSystem;
    private bool _warnedStarMaterialColor;
    private bool _warnedStarMaterialEmission;

    private bool _warnedMissingUrpVolume;
    private bool _warnedMissingUrpCurve;
    private bool _warnedUrpProfile;
    private bool _warnedUrpColorAdjustments;
    private bool _warnedUrpValueWrite;

    private bool _triedUrpBinding;
    private bool _urpExposureAvailable;
    private object _cachedPostExposureParameter;

    private Material _starMaterialInstance;
    private bool _starMaterialInitialized;
    private Color _cachedStarBaseColor = Color.white;
    private Color _cachedStarEmissionColor = Color.black;
    private bool _starHasBaseColor;
    private bool _starHasLegacyColor;
    private bool _starHasEmissionColor;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private enum ScheduledEventKind
    {
        DayChanged = 0,
        Sunrise = 1,
        Sunset = 2
    }

    private struct ScheduledEvent
    {
        public double absoluteHour;
        public ScheduledEventKind kind;
        public int dayValue;
    }

    private void Reset()
    {
        if (sunColorOverDay == null)
            sunColorOverDay = CreateDefaultSunColorGradient();

        if (moonColorOverDay == null)
            moonColorOverDay = CreateDefaultMoonColorGradient();

        if (ambientColorOverDay == null)
            ambientColorOverDay = CreateDefaultAmbientGradient();

        if (sunIntensityOverDay == null || sunIntensityOverDay.length == 0)
            sunIntensityOverDay = CreateDefaultSunIntensityCurve();

        if (moonIntensityOverDay == null || moonIntensityOverDay.length == 0)
            moonIntensityOverDay = CreateDefaultMoonIntensityCurve();

        if (starsVisibilityOverDay == null || starsVisibilityOverDay.length == 0)
            starsVisibilityOverDay = CreateDefaultStarsVisibilityCurve();

        if (urpPostExposureOverDay == null || urpPostExposureOverDay.length == 0)
            urpPostExposureOverDay = AnimationCurve.Linear(0f, -0.75f, 1f, 0.35f);

        TryAutoFindSun();
        TryAutoFindMoon();
        TryAutoFindTimeSources();
    }

    private void Awake()
    {
        NormalizeInspectorValues();
        EnsureRequiredData(debugLogs);

        if (autoFindReferences)
        {
            TryAutoFindSun();
            TryAutoFindMoon();
            TryAutoFindTimeSources();
        }

        CacheLightBaseRotations();
        CacheStarMaterial(debugLogs);
        TryCacheUrpExposureBinding(debugLogs);
        ResetSourceSyncState();
        InitializeVisualStateFromLogical();
    }

    private void Start()
    {
        if (timeSourceMode == TimeSourceMode.SyncFromNPCTimeSystem || timeSourceMode == TimeSourceMode.SyncFromTickSystem)
            ForceResyncFromCurrentSource();
        else
            SetVisualTargetFromLogical(snapImmediately: true);

        ApplyEnvironment(GetVisualTimeOfDay(), logWarnings: debugLogs);
    }

    private void OnValidate()
    {
        NormalizeInspectorValues();
        EnsureRequiredData(logWarnings: false);

        if (autoFindReferences && !Application.isPlaying)
        {
            TryAutoFindSun();
            TryAutoFindMoon();
            TryAutoFindTimeSources();
        }

        CacheLightBaseRotations();

        if (!Application.isPlaying)
            ApplyEnvironmentPreview(timeOfDay, logWarnings: false);
    }

    private void OnDisable()
    {
        if (starDomeRenderer != null && disableStarRendererInDaylight && Application.isPlaying)
            starDomeRenderer.enabled = true;
    }

    private void Update()
    {
        if (_lastTimeSourceMode != timeSourceMode)
            ResetSourceSyncState();

        if (pauseTime)
            return;

        switch (timeSourceMode)
        {
            case TimeSourceMode.SelfUpdate:
                {
                    float deltaSeconds = useUnscaledDeltaTime ? Time.unscaledDeltaTime : Time.deltaTime;
                    AdvanceByRealSeconds(deltaSeconds);
                    break;
                }
            case TimeSourceMode.SyncFromNPCTimeSystem:
                SyncFromNPCTimeSystem();
                break;
            case TimeSourceMode.SyncFromTickSystem:
                SyncFromTickSystem();
                break;
            case TimeSourceMode.ManualAdvance:
                break;
        }

        UpdateVisualEnvironment();
    }

    public void AdvanceByTickSeconds(float tickSeconds)
    {
        AdvanceByRealSeconds(tickSeconds);
    }

    public void AdvanceByGameHours(float gameHours)
    {
        if (pauseTime)
            return;

        if (gameHours <= 0f)
            return;

        double oldAbsoluteHours = (dayIndex * HoursPerDay) + timeOfDay;
        double newAbsoluteHours = oldAbsoluteHours + gameHours;

        dayIndex = Mathf.FloorToInt((float)(newAbsoluteHours / HoursPerDay));
        timeOfDay = NormalizeHour((float)newAbsoluteHours);

        SetVisualTargetFromLogical(snapImmediately: !ShouldSmoothVisuals());
        OnTimeChanged?.Invoke(timeOfDay);
        FireScheduledEvents(oldAbsoluteHours, newAbsoluteHours);
    }

    public void AdvanceByRealSeconds(float realSeconds)
    {
        if (pauseTime)
            return;

        if (realSeconds <= 0f)
            return;

        if (secondsPerGameDay <= 0f)
        {
            secondsPerGameDay = 1f;
            Log("secondsPerGameDay was <= 0 and got clamped to 1.");
        }

        float gameHoursToAdvance = (realSeconds / secondsPerGameDay) * HoursPerDay;
        AdvanceByGameHours(gameHoursToAdvance);
    }

    public void SetTime(float newTimeOfDay, int newDayIndex, bool fireTimeChanged = true)
    {
        timeOfDay = NormalizeHour(newTimeOfDay);
        dayIndex = Mathf.Max(0, newDayIndex);

        SetVisualTargetFromLogical(snapImmediately: true);

        if (fireTimeChanged)
            OnTimeChanged?.Invoke(timeOfDay);
    }

    public void ForceResyncFromCurrentSource()
    {
        ResetSourceSyncState();

        switch (timeSourceMode)
        {
            case TimeSourceMode.SyncFromNPCTimeSystem:
                SyncFromNPCTimeSystem(forceInitialApply: true);
                break;
            case TimeSourceMode.SyncFromTickSystem:
                SyncFromTickSystem(forceInitialApply: true);
                break;
            default:
                SetVisualTargetFromLogical(snapImmediately: true);
                break;
        }
    }

    private void SyncFromNPCTimeSystem(bool forceInitialApply = false)
    {
        if (npcTimeSystem == null)
        {
            if (autoFindReferences)
                TryAutoFindTimeSources();

            if (npcTimeSystem == null)
            {
                WarnOnce(ref _warnedMissingNpcTimeSystem, "timeSourceMode is SyncFromNPCTimeSystem, but npcTimeSystem is missing.");
                return;
            }
        }

        int sourceTickIndex = npcTimeSystem.TickIndex;
        float sourceTimeOfDay = NormalizeHour(npcTimeSystem.TimeOfDay);

        if (!_npcSyncInitialized || forceInitialApply)
        {
            timeOfDay = sourceTimeOfDay;
            SetVisualTargetFromLogical(snapImmediately: true);
            OnTimeChanged?.Invoke(timeOfDay);

            _lastNpcTickIndex = sourceTickIndex;
            _lastNpcTimeOfDay = sourceTimeOfDay;
            _npcSyncInitialized = true;

            Log($"Initial sync from NPCTimeSystem: timeOfDay={timeOfDay:0.00}, tickIndex={sourceTickIndex}, dayIndex={dayIndex}");
            return;
        }

        if (sourceTickIndex < _lastNpcTickIndex)
        {
            Log($"Detected NPCTimeSystem tick reset ({_lastNpcTickIndex} -> {sourceTickIndex}). Re-syncing local time state.");

            if (resetDayIndexOnSourceTickReset)
            {
                dayIndex = 0;
                Log("resetDayIndexOnSourceTickReset is enabled. dayIndex reset to 0.");
            }

            timeOfDay = sourceTimeOfDay;
            SetVisualTargetFromLogical(snapImmediately: true);
            OnTimeChanged?.Invoke(timeOfDay);

            _lastNpcTickIndex = sourceTickIndex;
            _lastNpcTimeOfDay = sourceTimeOfDay;
            return;
        }

        int tickDelta = sourceTickIndex - _lastNpcTickIndex;
        if (tickDelta > 0)
        {
            float hoursAdvanced = tickDelta * npcTimeSystem.HoursPerTick;
            AdvanceByGameHours(hoursAdvanced);
        }
        else if (!ApproximatelySameHour(sourceTimeOfDay, _lastNpcTimeOfDay))
        {
            Log($"NPCTimeSystem time changed without tick change ({_lastNpcTimeOfDay:0.00} -> {sourceTimeOfDay:0.00}). Snapping DayNightSystem to source time.");
            timeOfDay = sourceTimeOfDay;
            SetVisualTargetFromLogical(snapImmediately: true);
            OnTimeChanged?.Invoke(timeOfDay);
        }

        if (!ApproximatelySameHour(timeOfDay, sourceTimeOfDay))
        {
            Log($"DayNightSystem time mismatch after NPCTimeSystem sync. Local={timeOfDay:0.000}, Source={sourceTimeOfDay:0.000}. Snapping to source.");
            timeOfDay = sourceTimeOfDay;
            SetVisualTargetFromLogical(snapImmediately: false);
            OnTimeChanged?.Invoke(timeOfDay);
        }

        _lastNpcTickIndex = sourceTickIndex;
        _lastNpcTimeOfDay = sourceTimeOfDay;
    }

    private void SyncFromTickSystem(bool forceInitialApply = false)
    {
        if (tickSystem == null)
        {
            if (autoFindReferences)
                TryAutoFindTimeSources();

            if (tickSystem == null)
            {
                WarnOnce(ref _warnedMissingTickSystem, "timeSourceMode is SyncFromTickSystem, but tickSystem is missing.");
                return;
            }
        }

        int currentTickIndex = tickSystem.tickIndex;

        if (!_tickSyncInitialized || forceInitialApply)
        {
            _lastObservedTickSystemIndex = currentTickIndex;
            _tickSyncInitialized = true;
            SetVisualTargetFromLogical(snapImmediately: true);
            Log($"Initial sync from TickSystem: observedTickIndex={currentTickIndex}, timeOfDay={timeOfDay:0.00}, dayIndex={dayIndex}");
            return;
        }

        if (currentTickIndex < _lastObservedTickSystemIndex)
        {
            Log($"Detected TickSystem reset ({_lastObservedTickSystemIndex} -> {currentTickIndex}). Re-syncing tick observation.");

            if (resetDayIndexOnSourceTickReset)
            {
                dayIndex = 0;
                Log("resetDayIndexOnSourceTickReset is enabled. dayIndex reset to 0.");
            }

            _lastObservedTickSystemIndex = currentTickIndex;
            SetVisualTargetFromLogical(snapImmediately: true);
            return;
        }

        int tickDelta = currentTickIndex - _lastObservedTickSystemIndex;
        if (tickDelta <= 0)
            return;

        float realSecondsAdvanced = tickDelta * Mathf.Max(0.01f, tickSystem.secondsPerTick);
        AdvanceByRealSeconds(realSecondsAdvanced);
        _lastObservedTickSystemIndex = currentTickIndex;
    }

    private void ResetSourceSyncState()
    {
        _lastTimeSourceMode = timeSourceMode;
        _npcSyncInitialized = false;
        _lastNpcTickIndex = 0;
        _lastNpcTimeOfDay = 0f;
        _tickSyncInitialized = false;
        _lastObservedTickSystemIndex = 0;
        InitializeVisualStateFromLogical();
    }

    private void InitializeVisualStateFromLogical()
    {
        double absoluteHours = GetAbsoluteHours(dayIndex, timeOfDay);
        _targetAbsoluteHours = absoluteHours;
        _visualAbsoluteHours = absoluteHours;
        _visualTweenStartAbsoluteHours = absoluteHours;
        _visualTweenTargetAbsoluteHours = absoluteHours;
        _visualTweenElapsed = 0f;
        _visualTweenActive = false;
        _visualStateInitialized = true;
    }

    private void SetVisualTargetFromLogical(bool snapImmediately)
    {
        double absoluteHours = GetAbsoluteHours(dayIndex, timeOfDay);
        _targetAbsoluteHours = absoluteHours;

        if (!_visualStateInitialized)
            InitializeVisualStateFromLogical();

        if (snapImmediately || !ShouldSmoothVisuals())
        {
            _visualAbsoluteHours = absoluteHours;
            _visualTweenStartAbsoluteHours = absoluteHours;
            _visualTweenTargetAbsoluteHours = absoluteHours;
            _visualTweenElapsed = 0f;
            _visualTweenActive = false;
            return;
        }

        _visualTweenStartAbsoluteHours = _visualAbsoluteHours;
        _visualTweenTargetAbsoluteHours = absoluteHours;
        _visualTweenElapsed = 0f;
        _visualTweenActive = true;
    }

    private void UpdateVisualEnvironment()
    {
        if (!_visualStateInitialized)
            InitializeVisualStateFromLogical();

        if (!ShouldSmoothVisuals())
        {
            _visualAbsoluteHours = _targetAbsoluteHours;
            _visualTweenActive = false;
        }
        else if (_visualTweenActive)
        {
            float deltaSeconds = useUnscaledDeltaTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _visualTweenElapsed += Mathf.Max(0f, deltaSeconds);

            float duration = Mathf.Max(0.01f, visualSmoothingDuration);
            float t = Mathf.Clamp01(_visualTweenElapsed / duration);
            if (useSmoothStepForVisuals)
                t = t * t * (3f - 2f * t);

            _visualAbsoluteHours = Mathf.Lerp((float)_visualTweenStartAbsoluteHours, (float)_visualTweenTargetAbsoluteHours, t);

            if (_visualTweenElapsed >= duration || Math.Abs(_visualAbsoluteHours - _visualTweenTargetAbsoluteHours) <= 0.0001d)
            {
                _visualAbsoluteHours = _visualTweenTargetAbsoluteHours;
                _visualTweenActive = false;
            }
        }
        else
        {
            _visualAbsoluteHours = _targetAbsoluteHours;
        }

        ApplyEnvironment(GetVisualTimeOfDay(), logWarnings: debugLogs);
    }

    private float GetVisualTimeOfDay()
    {
        return NormalizeHour((float)_visualAbsoluteHours);
    }

    private bool ShouldSmoothVisuals()
    {
        if (!smoothSunAndShadows)
            return false;

        return timeSourceMode == TimeSourceMode.SyncFromNPCTimeSystem || timeSourceMode == TimeSourceMode.SyncFromTickSystem;
    }

    private static double GetAbsoluteHours(int currentDayIndex, float currentTimeOfDay)
    {
        return (currentDayIndex * HoursPerDay) + NormalizeHour(currentTimeOfDay);
    }

    private void FireScheduledEvents(double oldAbsoluteHours, double newAbsoluteHours)
    {
        if (newAbsoluteHours <= oldAbsoluteHours)
            return;

        List<ScheduledEvent> eventsToFire = new List<ScheduledEvent>(8);

        int firstDayBoundary = Mathf.FloorToInt((float)(oldAbsoluteHours / HoursPerDay)) + 1;
        int lastDayBoundary = Mathf.FloorToInt((float)(newAbsoluteHours / HoursPerDay));

        for (int d = firstDayBoundary; d <= lastDayBoundary; d++)
        {
            eventsToFire.Add(new ScheduledEvent
            {
                absoluteHour = d * HoursPerDay,
                kind = ScheduledEventKind.DayChanged,
                dayValue = d
            });
        }

        AppendRecurringEvents(eventsToFire, oldAbsoluteHours, newAbsoluteHours, sunriseTime, ScheduledEventKind.Sunrise);
        AppendRecurringEvents(eventsToFire, oldAbsoluteHours, newAbsoluteHours, sunsetTime, ScheduledEventKind.Sunset);

        eventsToFire.Sort((a, b) =>
        {
            int timeCompare = a.absoluteHour.CompareTo(b.absoluteHour);
            if (timeCompare != 0)
                return timeCompare;

            return a.kind.CompareTo(b.kind);
        });

        for (int i = 0; i < eventsToFire.Count; i++)
            InvokeScheduledEvent(eventsToFire[i]);
    }

    private void AppendRecurringEvents(List<ScheduledEvent> eventsToFire, double oldAbsoluteHours, double newAbsoluteHours, float thresholdHour, ScheduledEventKind kind)
    {
        int startDay = Mathf.FloorToInt((float)(oldAbsoluteHours / HoursPerDay)) - 1;
        int endDay = Mathf.FloorToInt((float)(newAbsoluteHours / HoursPerDay)) + 1;

        for (int d = startDay; d <= endDay; d++)
        {
            double absoluteThreshold = (d * HoursPerDay) + thresholdHour;
            if (absoluteThreshold > oldAbsoluteHours && absoluteThreshold <= newAbsoluteHours)
            {
                eventsToFire.Add(new ScheduledEvent
                {
                    absoluteHour = absoluteThreshold,
                    kind = kind,
                    dayValue = d
                });
            }
        }
    }

    private void InvokeScheduledEvent(ScheduledEvent scheduledEvent)
    {
        switch (scheduledEvent.kind)
        {
            case ScheduledEventKind.DayChanged:
                Log($"Day changed -> {scheduledEvent.dayValue}");
                OnDayChanged?.Invoke(scheduledEvent.dayValue);
                break;

            case ScheduledEventKind.Sunrise:
                Log($"Sunrise crossed (day {scheduledEvent.dayValue}, time {sunriseTime:0.##}).");
                OnSunrise?.Invoke();
                break;

            case ScheduledEventKind.Sunset:
                Log($"Sunset crossed (day {scheduledEvent.dayValue}, time {sunsetTime:0.##}).");
                OnSunset?.Invoke();
                break;
        }
    }

    private void ApplyEnvironment(float currentHour, bool logWarnings)
    {
        float normalized = NormalizeHour(currentHour) / HoursPerDay;

        ApplySunRotation(currentHour, logWarnings);
        ApplyMoonRotation(currentHour, logWarnings);
        ApplySunLight(normalized);
        ApplyMoonLight(normalized);
        ApplyAmbientLight(normalized);
        ApplyFog(normalized, logWarnings);
        ApplyStars(normalized, logWarnings);
        ApplyUrpExposure(normalized, logWarnings);
    }

    private void ApplyEnvironmentPreview(float currentHour, bool logWarnings)
    {
        float normalized = NormalizeHour(currentHour) / HoursPerDay;

        ApplySunRotation(currentHour, logWarnings);
        ApplyMoonRotation(currentHour, logWarnings);
        ApplySunLight(normalized);
        ApplyMoonLight(normalized);
        ApplyAmbientLight(normalized);
        ApplyFog(normalized, logWarnings);
        ApplyStars(normalized, logWarnings);
    }

    private void ApplySunRotation(float currentHour, bool logWarnings)
    {
        if (sunLight == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedMissingSunLight, "sunLight is missing. Time/events still work, but sun visuals are skipped.");
            return;
        }

        float pitch = GetSunPitch(currentHour);
        sunLight.transform.rotation = Quaternion.Euler(pitch, _sunBaseYaw, _sunBaseRoll);
    }

    private void ApplyMoonRotation(float currentHour, bool logWarnings)
    {
        if (moonLight == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedMissingMoonLight, "moonLight is missing. Moon visuals are skipped.");
            return;
        }

        float moonPitch = Mathf.Repeat(GetSunPitch(currentHour) + 180f, 360f);
        moonLight.transform.rotation = Quaternion.Euler(moonPitch, _moonBaseYaw, _moonBaseRoll);
    }

    private void ApplySunLight(float normalized)
    {
        if (sunLight == null)
            return;

        Color sunColor = sunColorOverDay != null ? sunColorOverDay.Evaluate(normalized) : Color.white;
        float intensity = sunIntensityOverDay != null ? Mathf.Max(0f, sunIntensityOverDay.Evaluate(normalized)) : 1f;

        sunLight.color = sunColor;
        sunLight.intensity = intensity;
        sunLight.enabled = intensity > 0.001f;
    }

    private void ApplyMoonLight(float normalized)
    {
        if (moonLight == null)
            return;

        Color moonColor = moonColorOverDay != null ? moonColorOverDay.Evaluate(normalized) : new Color(0.55f, 0.62f, 0.80f);
        float intensity = moonIntensityOverDay != null ? Mathf.Max(0f, moonIntensityOverDay.Evaluate(normalized)) : 0f;

        moonLight.color = moonColor;
        moonLight.intensity = intensity;
        moonLight.enabled = intensity > 0.001f;
    }

    private void ApplyAmbientLight(float normalized)
    {
        if (ambientColorOverDay == null)
            return;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColorOverDay.Evaluate(normalized);
    }

    private void ApplyFog(float normalized, bool logWarnings)
    {
        if (!RenderSettings.fog)
            return;

        if (fogDensityOverDay == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedFogCurve, "RenderSettings.fog is enabled, but fogDensityOverDay is not assigned. Fog density will not be animated.");
            return;
        }

        RenderSettings.fogDensity = Mathf.Max(0f, fogDensityOverDay.Evaluate(normalized));
    }

    private void ApplyStars(float normalized, bool logWarnings)
    {
        if (starDomeRenderer == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedMissingStarRenderer, "starDomeRenderer is missing. Night sky stars are skipped.");
            return;
        }

        if (!_starMaterialInitialized)
            CacheStarMaterial(logWarnings);

        float visibility = starsVisibilityOverDay != null ? Mathf.Clamp01(starsVisibilityOverDay.Evaluate(normalized)) : 0f;
        bool shouldRender = visibility > 0.001f || !disableStarRendererInDaylight;
        starDomeRenderer.enabled = shouldRender;

        if (!shouldRender)
            return;

        if (_starMaterialInstance == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedStarMaterialColor, "Could not get a runtime material instance from starDomeRenderer. Stars can only be toggled via renderer enable/disable.");
            return;
        }

        if (_starHasBaseColor)
        {
            Color c = starsTint;
            c.a = visibility;
            _starMaterialInstance.SetColor(BaseColorId, c);
        }
        else if (_starHasLegacyColor)
        {
            Color c = starsTint;
            c.a = visibility;
            _starMaterialInstance.SetColor(ColorId, c);
        }
        else if (logWarnings)
        {
            WarnOnce(ref _warnedStarMaterialColor, "Star material has neither _BaseColor nor _Color. Alpha fade cannot be applied. Use URP Lit/Simple Lit or another shader exposing one of those properties.");
        }

        if (_starHasEmissionColor)
        {
            float emission = visibility * starsMaxEmission;
            _starMaterialInstance.EnableKeyword("_EMISSION");
            _starMaterialInstance.SetColor(EmissionColorId, starsTint * emission);
        }
        else if (logWarnings)
        {
            WarnOnce(ref _warnedStarMaterialEmission, "Star material has no _EmissionColor. Star glow will be skipped.");
        }
    }

    private void CacheStarMaterial(bool logWarnings)
    {
        _starMaterialInitialized = true;
        _starMaterialInstance = null;
        _starHasBaseColor = false;
        _starHasLegacyColor = false;
        _starHasEmissionColor = false;

        if (starDomeRenderer == null)
            return;

        try
        {
            _starMaterialInstance = starDomeRenderer.material;
        }
        catch (Exception ex)
        {
            if (logWarnings)
                Debug.LogWarning($"[DayNightSystem] Failed to access star dome runtime material. {ex.Message}", this);
            return;
        }

        if (_starMaterialInstance == null)
            return;

        _starHasBaseColor = _starMaterialInstance.HasProperty(BaseColorId);
        _starHasLegacyColor = _starMaterialInstance.HasProperty(ColorId);
        _starHasEmissionColor = _starMaterialInstance.HasProperty(EmissionColorId);

        if (_starHasBaseColor)
            _cachedStarBaseColor = _starMaterialInstance.GetColor(BaseColorId);
        else if (_starHasLegacyColor)
            _cachedStarBaseColor = _starMaterialInstance.GetColor(ColorId);

        if (_starHasEmissionColor)
            _cachedStarEmissionColor = _starMaterialInstance.GetColor(EmissionColorId);
    }

    private void ApplyUrpExposure(float normalized, bool logWarnings)
    {
        if (!IsUrpActive())
            return;

        if (urpVolume == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedMissingUrpVolume, "URP is active, but no urpVolume is assigned. Exposure animation is skipped.");
            return;
        }

        if (urpPostExposureOverDay == null || urpPostExposureOverDay.length == 0)
        {
            if (logWarnings)
                WarnOnce(ref _warnedMissingUrpCurve, "URP is active, but urpPostExposureOverDay is missing. Exposure animation is skipped.");
            return;
        }

        if (!_urpExposureAvailable && !TryCacheUrpExposureBinding(logWarnings))
            return;

        float exposure = urpPostExposureOverDay.Evaluate(normalized);
        SetMemberValue(_cachedPostExposureParameter, "overrideState", true);

        if (!SetMemberValue(_cachedPostExposureParameter, "value", exposure) && logWarnings)
            WarnOnce(ref _warnedUrpValueWrite, "Could not write URP postExposure value via reflection. Exposure animation is skipped.");
    }

    private bool TryCacheUrpExposureBinding(bool logWarnings)
    {
        if (_triedUrpBinding)
            return _urpExposureAvailable;

        _triedUrpBinding = true;
        _urpExposureAvailable = false;
        _cachedPostExposureParameter = null;

        if (!IsUrpActive())
            return false;

        if (urpVolume == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedMissingUrpVolume, "URP is active, but no urpVolume is assigned. Exposure animation is skipped.");
            return false;
        }

        object profile = GetMemberValue(urpVolume, "sharedProfile") ?? GetMemberValue(urpVolume, "profile");
        if (profile == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedUrpProfile, "Assigned URP Volume has no sharedProfile/profile. Exposure animation is skipped.");
            return false;
        }

        IEnumerable components = GetMemberValue(profile, "components") as IEnumerable;
        if (components == null)
        {
            if (logWarnings)
                WarnOnce(ref _warnedUrpProfile, "Could not access VolumeProfile.components via reflection. Exposure animation is skipped.");
            return false;
        }

        foreach (object component in components)
        {
            if (component == null)
                continue;

            string fullName = component.GetType().FullName;
            if (string.IsNullOrEmpty(fullName) || !fullName.Contains("ColorAdjustments"))
                continue;

            object postExposureParameter = GetMemberValue(component, "postExposure");
            if (postExposureParameter == null)
            {
                if (logWarnings)
                    WarnOnce(ref _warnedUrpColorAdjustments, "Found ColorAdjustments, but postExposure was not accessible. Exposure animation is skipped.");
                return false;
            }

            _cachedPostExposureParameter = postExposureParameter;
            _urpExposureAvailable = true;
            return true;
        }

        if (logWarnings)
        {
            WarnOnce(
                ref _warnedUrpColorAdjustments,
                "No ColorAdjustments override found in the assigned URP VolumeProfile. Add 'Color Adjustments' to the Volume to animate postExposure."
            );
        }

        return false;
    }

    private void NormalizeInspectorValues()
    {
        timeOfDay = NormalizeHour(timeOfDay);
        dayIndex = Mathf.Max(0, dayIndex);
        secondsPerGameDay = Mathf.Max(1f, secondsPerGameDay);

        sunriseTime = NormalizeHour(sunriseTime);
        sunsetTime = NormalizeHour(sunsetTime);

        float daylightDuration = GetForwardDuration(sunriseTime, sunsetTime);
        if (Mathf.Approximately(daylightDuration, 0f))
            sunsetTime = NormalizeHour(sunriseTime + 12f);
    }

    private void EnsureRequiredData(bool logWarnings)
    {
        if (sunColorOverDay == null)
        {
            sunColorOverDay = CreateDefaultSunColorGradient();
            if (logWarnings)
                WarnOnce(ref _warnedMissingSunColor, "sunColorOverDay was not assigned. A fallback gradient was created.");
        }

        if (sunIntensityOverDay == null || sunIntensityOverDay.length == 0)
        {
            sunIntensityOverDay = CreateDefaultSunIntensityCurve();
            if (logWarnings)
                WarnOnce(ref _warnedMissingSunIntensity, "sunIntensityOverDay was not assigned. A fallback curve was created.");
        }

        if (moonColorOverDay == null)
        {
            moonColorOverDay = CreateDefaultMoonColorGradient();
            if (logWarnings)
                WarnOnce(ref _warnedMissingMoonColor, "moonColorOverDay was not assigned. A fallback gradient was created.");
        }

        if (moonIntensityOverDay == null || moonIntensityOverDay.length == 0)
        {
            moonIntensityOverDay = CreateDefaultMoonIntensityCurve();
            if (logWarnings)
                WarnOnce(ref _warnedMissingMoonIntensity, "moonIntensityOverDay was not assigned. A fallback curve was created.");
        }

        if (starsVisibilityOverDay == null || starsVisibilityOverDay.length == 0)
            starsVisibilityOverDay = CreateDefaultStarsVisibilityCurve();

        if (ambientColorOverDay == null)
        {
            ambientColorOverDay = CreateDefaultAmbientGradient();
            if (logWarnings)
                WarnOnce(ref _warnedMissingAmbient, "ambientColorOverDay was not assigned. A fallback gradient was created.");
        }
    }

    private void TryAutoFindSun()
    {
        if (sunLight != null)
            return;

        if (RenderSettings.sun != null)
        {
            sunLight = RenderSettings.sun;
            return;
        }

        Light[] allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < allLights.Length; i++)
        {
            if (allLights[i] != null && allLights[i].type == LightType.Directional)
            {
                sunLight = allLights[i];
                return;
            }
        }
    }

    private void TryAutoFindMoon()
    {
        if (moonLight != null)
            return;

        Light[] allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < allLights.Length; i++)
        {
            Light l = allLights[i];
            if (l == null || l.type != LightType.Directional)
                continue;

            if (l == sunLight)
                continue;

            moonLight = l;
            return;
        }
    }

    private void TryAutoFindTimeSources()
    {
        if (npcTimeSystem == null)
            npcTimeSystem = NPCTimeSystem.Instance != null ? NPCTimeSystem.Instance : FindFirstObjectByType<NPCTimeSystem>();

        if (tickSystem == null)
            tickSystem = FindFirstObjectByType<TickSystem>();
    }

    private void CacheLightBaseRotations()
    {
        if (sunLight != null)
        {
            Vector3 euler = sunLight.transform.rotation.eulerAngles;
            _sunBaseYaw = euler.y;
            _sunBaseRoll = euler.z;
        }

        if (moonLight != null)
        {
            Vector3 euler = moonLight.transform.rotation.eulerAngles;
            _moonBaseYaw = euler.y;
            _moonBaseRoll = euler.z;
        }
    }

    private float GetSunPitch(float currentHour)
    {
        currentHour = NormalizeHour(currentHour);

        if (IsWithinForwardInterval(currentHour, sunriseTime, sunsetTime))
        {
            float t = GetForwardProgress(currentHour, sunriseTime, sunsetTime);
            return Mathf.Lerp(0f, 180f, t);
        }

        float nightT = GetForwardProgress(currentHour, sunsetTime, sunriseTime);
        return Mathf.Lerp(180f, 360f, nightT);
    }

    private static float NormalizeHour(float hour)
    {
        return Mathf.Repeat(hour, HoursPerDay);
    }

    private static float GetForwardDuration(float fromHour, float toHour)
    {
        return Mathf.Repeat(toHour - fromHour + HoursPerDay, HoursPerDay);
    }

    private static bool IsWithinForwardInterval(float currentHour, float fromHour, float toHour)
    {
        float duration = GetForwardDuration(fromHour, toHour);
        float elapsed = Mathf.Repeat(currentHour - fromHour + HoursPerDay, HoursPerDay);
        return elapsed <= duration;
    }

    private static float GetForwardProgress(float currentHour, float fromHour, float toHour)
    {
        float duration = GetForwardDuration(fromHour, toHour);
        if (duration <= 0.0001f)
            return 0f;

        float elapsed = Mathf.Repeat(currentHour - fromHour + HoursPerDay, HoursPerDay);
        return Mathf.Clamp01(elapsed / duration);
    }

    private static bool ApproximatelySameHour(float a, float b)
    {
        return Mathf.Abs(Mathf.DeltaAngle(a * 15f, b * 15f)) <= TimeSnapEpsilon * 15f;
    }

    private static Gradient CreateDefaultSunColorGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.08f, 0.10f, 0.20f), 0.00f),
                new GradientColorKey(new Color(0.95f, 0.45f, 0.20f), 0.24f),
                new GradientColorKey(new Color(1.00f, 0.95f, 0.82f), 0.32f),
                new GradientColorKey(new Color(1.00f, 0.98f, 0.92f), 0.50f),
                new GradientColorKey(new Color(1.00f, 0.72f, 0.38f), 0.78f),
                new GradientColorKey(new Color(0.22f, 0.16f, 0.28f), 0.88f),
                new GradientColorKey(new Color(0.08f, 0.10f, 0.20f), 1.00f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }

    private static Gradient CreateDefaultMoonColorGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.58f, 0.66f, 0.84f), 0.00f),
                new GradientColorKey(new Color(0.20f, 0.22f, 0.30f), 0.22f),
                new GradientColorKey(new Color(0.05f, 0.05f, 0.07f), 0.32f),
                new GradientColorKey(new Color(0.05f, 0.05f, 0.07f), 0.70f),
                new GradientColorKey(new Color(0.18f, 0.22f, 0.30f), 0.80f),
                new GradientColorKey(new Color(0.60f, 0.68f, 0.86f), 1.00f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }

    private static Gradient CreateDefaultAmbientGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.03f, 0.04f, 0.08f), 0.00f),
                new GradientColorKey(new Color(0.20f, 0.16f, 0.18f), 0.24f),
                new GradientColorKey(new Color(0.55f, 0.55f, 0.60f), 0.38f),
                new GradientColorKey(new Color(0.72f, 0.72f, 0.75f), 0.50f),
                new GradientColorKey(new Color(0.42f, 0.34f, 0.30f), 0.78f),
                new GradientColorKey(new Color(0.10f, 0.08f, 0.12f), 0.90f),
                new GradientColorKey(new Color(0.03f, 0.04f, 0.08f), 1.00f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }

    private static AnimationCurve CreateDefaultSunIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0.00f, 0.00f),
            new Keyframe(0.20f, 0.00f),
            new Keyframe(0.27f, 0.50f),
            new Keyframe(0.50f, 1.10f),
            new Keyframe(0.75f, 0.55f),
            new Keyframe(0.84f, 0.00f),
            new Keyframe(1.00f, 0.00f)
        );
    }

    private static AnimationCurve CreateDefaultMoonIntensityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0.00f, 0.18f),
            new Keyframe(0.18f, 0.12f),
            new Keyframe(0.24f, 0.04f),
            new Keyframe(0.30f, 0.00f),
            new Keyframe(0.72f, 0.00f),
            new Keyframe(0.80f, 0.05f),
            new Keyframe(0.88f, 0.15f),
            new Keyframe(1.00f, 0.18f)
        );
    }

    private static AnimationCurve CreateDefaultStarsVisibilityCurve()
    {
        return new AnimationCurve(
            new Keyframe(0.00f, 1.00f),
            new Keyframe(0.18f, 1.00f),
            new Keyframe(0.24f, 0.60f),
            new Keyframe(0.30f, 0.00f),
            new Keyframe(0.72f, 0.00f),
            new Keyframe(0.80f, 0.55f),
            new Keyframe(0.88f, 1.00f),
            new Keyframe(1.00f, 1.00f)
        );
    }

    private static bool IsUrpActive()
    {
        RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
            return false;

        string fullName = asset.GetType().FullName;
        return !string.IsNullOrEmpty(fullName) && fullName.Contains("Universal");
    }

    private static object GetMemberValue(object target, string memberName)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = target.GetType();

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null)
            return property.GetValue(target, null);

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
            return field.GetValue(target);

        return null;
    }

    private static bool SetMemberValue(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = target.GetType();

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanWrite)
        {
            object converted = ConvertValueIfNeeded(value, property.PropertyType);
            property.SetValue(target, converted, null);
            return true;
        }

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            object converted = ConvertValueIfNeeded(value, field.FieldType);
            field.SetValue(target, converted);
            return true;
        }

        return false;
    }

    private static object ConvertValueIfNeeded(object value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType.IsInstanceOfType(value))
            return value;

        try
        {
            if (targetType == typeof(float))
                return Convert.ToSingle(value);

            if (targetType == typeof(bool))
                return Convert.ToBoolean(value);

            if (targetType.IsEnum)
                return Enum.ToObject(targetType, value);

            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }

    private void Log(string message)
    {
        if (!debugLogs)
            return;

        Debug.Log($"[DayNightSystem] {message}", this);
    }

    private void WarnOnce(ref bool flag, string message)
    {
        if (flag)
            return;

        flag = true;
        Debug.LogWarning($"[DayNightSystem] {message}", this);
    }
}
