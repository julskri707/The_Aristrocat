
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class WorldTimeWeatherMasterController : MonoBehaviour
{
    [Serializable]
    public class WeatherChanceEntry
    {
        [Tooltip("Nur für dich im Inspector.")]
        public string label = "New Weather";

        [Tooltip("Index in AzureSkyController.weatherProfileList. 0 ist meist Default.")]
        public int weatherProfileIndex = 0;

        [Min(0f)]
        [Tooltip("Gewichtung für die Zufallsauswahl. 0 = wird nie gewählt.")]
        public float weight = 1f;

        [Tooltip("Wenn aktiv, setzt der Controller im NPCTimeSystem coldWeatherActive auf true.")]
        public bool countsAsColdWeather = false;

        [Tooltip("Wenn aktiv, setzt der Controller im NPCTimeSystem settlementDangerActive auf true.")]
        public bool countsAsDangerWeather = false;
    }

    [Header("References")]
    [SerializeField] private MonoBehaviour azureSkyController;
    [SerializeField] private NPCTimeSystem npcTimeSystem;

    [Header("NPC Sync")]
    [SerializeField] private bool syncNpcTimeFromAzure = true;
    [SerializeField] private bool syncNpcWeatherFlags = true;
    [SerializeField] private bool disableNpcAutoTickOnStart = true;
    [SerializeField] private bool writeNpcTickIndexFromExternalTime = true;
    [SerializeField, Min(0.001f)] private float npcHoursPerTick = 0.25f;

    [Header("Weather Randomizer")]
    [SerializeField] private bool enableWeatherRandomizer = true;
    [SerializeField, Min(1f)] private float weatherRollIntervalMinutesRealtime = 3f;
    [SerializeField] private bool rollWeatherImmediatelyOnStart = false;
    [SerializeField] private bool avoidRepeatingSameWeather = true;
    [SerializeField] private bool includeCurrentWeatherAsCandidate = true;
    [SerializeField] private int fallbackWeatherProfileIndex = 0;
    [SerializeField] private List<WeatherChanceEntry> weatherChances = new List<WeatherChanceEntry>();

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private object azureTimeOfDayObject;
    private Type azureControllerType;
    private Type azureTimeOfDayType;

    private float weatherTimer;
    private int currentWeatherProfileIndex = -1;

    private float lastHour = -1f;
    private int lastDay = -1;
    private int lastMonth = -1;
    private int lastYear = -1;

    private bool reflectionReady;

    public float CurrentHour => ReadAzureHour();
    public int CurrentDay => ReadAzureInt("day", 1);
    public int CurrentMonth => ReadAzureInt("month", 1);
    public int CurrentYear => ReadAzureInt("year", 1);
    public int CurrentWeatherProfileIndex => currentWeatherProfileIndex;

    private void Awake()
    {
        CacheReferences();
    }

    private void Start()
    {
        CacheReferences();

        if (npcTimeSystem == null)
            npcTimeSystem = NPCTimeSystem.Instance;

        if (npcTimeSystem != null && disableNpcAutoTickOnStart)
            npcTimeSystem.SetAutoTick(false);

        if (rollWeatherImmediatelyOnStart && enableWeatherRandomizer)
            RollRandomWeather();

        ForceFullSyncToNpc();
    }

    private void Update()
    {
        if (!reflectionReady)
        {
            CacheReferences();
            if (!reflectionReady)
                return;
        }

        if (syncNpcTimeFromAzure)
            SyncNpcFromAzure();

        if (enableWeatherRandomizer)
            UpdateWeatherRandomizer();
    }

    public void ForceFullSyncToNpc()
    {
        SyncNpcFromAzure(forceTickRebuild: true);
    }

    public void RollRandomWeather()
    {
        if (!reflectionReady)
        {
            CacheReferences();
            if (!reflectionReady)
                return;
        }

        int selectedIndex = ChooseRandomWeatherProfileIndex();
        ApplyWeatherProfile(selectedIndex);
    }

    public void ApplyWeatherProfile(int weatherProfileIndex)
    {
        if (!reflectionReady)
        {
            CacheReferences();
            if (!reflectionReady)
                return;
        }

        if (!InvokeAzureSetNewWeatherProfile(weatherProfileIndex))
        {
            Debug.LogWarning("[WorldTimeWeatherMasterController] Konnte SetNewWeatherProfile(int) auf AzureSkyController nicht aufrufen.", this);
            return;
        }

        currentWeatherProfileIndex = weatherProfileIndex;

        if (syncNpcWeatherFlags)
            PushWeatherFlagsToNpc();

        if (verboseLogs)
            Debug.Log($"[WorldTimeWeatherMasterController] Neues Wetter gesetzt: Index {weatherProfileIndex}.", this);
    }

    public void SetAzureDateAndTime(int year, int month, int day, int hour, int minute)
    {
        if (!reflectionReady)
        {
            CacheReferences();
            if (!reflectionReady)
                return;
        }

        WriteAzureInt("year", year);
        WriteAzureInt("month", month);
        WriteAzureInt("day", day);
        WriteAzureFloat("hour", Mathf.Clamp(hour, 0, 23) + Mathf.Clamp(minute, 0, 59) / 60f);

        SyncNpcFromAzure(forceTickRebuild: true);
    }

    public void SetAzureTime(float hour)
    {
        if (!reflectionReady)
        {
            CacheReferences();
            if (!reflectionReady)
                return;
        }

        WriteAzureFloat("hour", NormalizeHour(hour));
        SyncNpcFromAzure(forceTickRebuild: false);
    }

    public void SetNpcHoursPerTick(float value)
    {
        npcHoursPerTick = Mathf.Max(0.001f, value);

        if (npcTimeSystem != null)
            npcTimeSystem.SetHoursPerTickExternal(npcHoursPerTick);

        SyncNpcFromAzure(forceTickRebuild: true);
    }

    private void UpdateWeatherRandomizer()
    {
        if (weatherRollIntervalMinutesRealtime <= 0f)
            return;

        weatherTimer += Time.deltaTime;
        float intervalSeconds = weatherRollIntervalMinutesRealtime * 60f;

        while (weatherTimer >= intervalSeconds)
        {
            weatherTimer -= intervalSeconds;
            RollRandomWeather();
        }
    }

    private void SyncNpcFromAzure(bool forceTickRebuild = false)
    {
        if (npcTimeSystem == null)
            return;

        float hour = ReadAzureHour();
        int day = ReadAzureInt("day", 1);
        int month = ReadAzureInt("month", 1);
        int year = ReadAzureInt("year", 1);

        bool changed =
            forceTickRebuild ||
            !Mathf.Approximately(hour, lastHour) ||
            day != lastDay ||
            month != lastMonth ||
            year != lastYear;

        if (!changed)
            return;

        lastHour = hour;
        lastDay = day;
        lastMonth = month;
        lastYear = year;

        npcTimeSystem.SyncFromExternalClock(
            hour,
            day,
            month,
            year,
            npcHoursPerTick,
            writeNpcTickIndexFromExternalTime);

        if (syncNpcWeatherFlags)
            PushWeatherFlagsToNpc();
    }

    private void PushWeatherFlagsToNpc()
    {
        if (npcTimeSystem == null)
            return;

        WeatherChanceEntry entry = FindWeatherEntryByIndex(currentWeatherProfileIndex);
        bool cold = entry != null && entry.countsAsColdWeather;
        bool danger = entry != null && entry.countsAsDangerWeather;

        npcTimeSystem.SetColdWeatherActive(cold);
        npcTimeSystem.SetSettlementDangerActive(danger);
    }

    private WeatherChanceEntry FindWeatherEntryByIndex(int weatherProfileIndex)
    {
        if (weatherChances == null)
            return null;

        for (int i = 0; i < weatherChances.Count; i++)
        {
            WeatherChanceEntry entry = weatherChances[i];
            if (entry == null)
                continue;

            if (entry.weatherProfileIndex == weatherProfileIndex)
                return entry;
        }

        return null;
    }

    private int ChooseRandomWeatherProfileIndex()
    {
        if (weatherChances == null || weatherChances.Count == 0)
        {
            if (verboseLogs)
                Debug.LogWarning("[WorldTimeWeatherMasterController] Keine WeatherChance-Einträge vorhanden. Fallback wird benutzt.", this);

            return fallbackWeatherProfileIndex;
        }

        float totalWeight = 0f;
        List<WeatherChanceEntry> validEntries = new List<WeatherChanceEntry>(weatherChances.Count);

        for (int i = 0; i < weatherChances.Count; i++)
        {
            WeatherChanceEntry entry = weatherChances[i];
            if (entry == null)
                continue;

            if (entry.weight <= 0f)
                continue;

            if (avoidRepeatingSameWeather && !includeCurrentWeatherAsCandidate && entry.weatherProfileIndex == currentWeatherProfileIndex)
                continue;

            validEntries.Add(entry);
            totalWeight += entry.weight;
        }

        if (validEntries.Count == 0 || totalWeight <= 0f)
            return fallbackWeatherProfileIndex;

        float random = UnityEngine.Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < validEntries.Count; i++)
        {
            cumulative += validEntries[i].weight;
            if (random <= cumulative)
                return validEntries[i].weatherProfileIndex;
        }

        return validEntries[validEntries.Count - 1].weatherProfileIndex;
    }

    private void CacheReferences()
    {
        reflectionReady = false;
        azureTimeOfDayObject = null;
        azureControllerType = null;
        azureTimeOfDayType = null;

        if (azureSkyController == null)
        {
            Debug.LogWarning("[WorldTimeWeatherMasterController] Azure Sky Controller Referenz fehlt.", this);
            return;
        }

        azureControllerType = azureSkyController.GetType();

        if (!TryGetMemberValue(azureSkyController, azureControllerType, "timeOfDay", out azureTimeOfDayObject))
        {
            Debug.LogWarning("[WorldTimeWeatherMasterController] Konnte 'timeOfDay' auf AzureSkyController nicht finden.", this);
            return;
        }

        if (azureTimeOfDayObject == null)
        {
            Debug.LogWarning("[WorldTimeWeatherMasterController] AzureSkyController.timeOfDay ist null.", this);
            return;
        }

        azureTimeOfDayType = azureTimeOfDayObject.GetType();
        reflectionReady = true;
    }

    private float ReadAzureHour()
    {
        float value = ReadAzureFloat("hour", 8f);
        return NormalizeHour(value);
    }

    private float ReadAzureFloat(string memberName, float fallback)
    {
        if (!reflectionReady)
            return fallback;

        if (!TryGetMemberValue(azureTimeOfDayObject, azureTimeOfDayType, memberName, out object boxed) || boxed == null)
            return fallback;

        try
        {
            return Convert.ToSingle(boxed);
        }
        catch
        {
            return fallback;
        }
    }

    private int ReadAzureInt(string memberName, int fallback)
    {
        if (!reflectionReady)
            return fallback;

        if (!TryGetMemberValue(azureTimeOfDayObject, azureTimeOfDayType, memberName, out object boxed) || boxed == null)
            return fallback;

        try
        {
            return Convert.ToInt32(boxed);
        }
        catch
        {
            return fallback;
        }
    }

    private void WriteAzureFloat(string memberName, float value)
    {
        if (!reflectionReady)
            return;

        TrySetMemberValue(azureTimeOfDayObject, azureTimeOfDayType, memberName, value);
    }

    private void WriteAzureInt(string memberName, int value)
    {
        if (!reflectionReady)
            return;

        TrySetMemberValue(azureTimeOfDayObject, azureTimeOfDayType, memberName, value);
    }

    private bool InvokeAzureSetNewWeatherProfile(int profileIndex)
    {
        if (azureSkyController == null || azureControllerType == null)
            return false;

        MethodInfo method = azureControllerType.GetMethod(
            "SetNewWeatherProfile",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int) },
            null);

        if (method == null)
            return false;

        try
        {
            method.Invoke(azureSkyController, new object[] { profileIndex });
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldTimeWeatherMasterController] Fehler bei SetNewWeatherProfile({profileIndex}): {ex.Message}", this);
            return false;
        }
    }

    private static bool TryGetMemberValue(object target, Type type, string memberName, out object value)
    {
        value = null;
        if (target == null || type == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            value = field.GetValue(target);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanRead)
        {
            value = property.GetValue(target);
            return true;
        }

        return false;
    }

    private static bool TrySetMemberValue(object target, Type type, string memberName, object value)
    {
        if (target == null || type == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            field.SetValue(target, value);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            return true;
        }

        return false;
    }

    private static float NormalizeHour(float hour)
    {
        while (hour < 0f)
            hour += 24f;

        while (hour >= 24f)
            hour -= 24f;

        return hour;
    }
}
