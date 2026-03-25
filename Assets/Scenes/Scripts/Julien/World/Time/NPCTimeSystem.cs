
using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCTimeSystem : MonoBehaviour
{
    public static NPCTimeSystem Instance { get; private set; }

    [Header("Tick Mode")]
    [SerializeField] private bool autoTick = true;
    [SerializeField] private float secondsPerTick = 0.5f;

    [Header("World Time")]
    [SerializeField, Range(0f, 24f)] private float timeOfDay = 8f;
    [SerializeField] private float hoursPerTick = 0.25f; // 0.25h = 15 Minuten pro Tick
    [SerializeField] private int tickIndex = 0;

    [Header("Calendar")]
    [SerializeField] private int day = 1;
    [SerializeField] private int month = 1;
    [SerializeField] private int year = 1;

    [Header("External Authority")]
    [SerializeField] private bool useExternalClock = false;

    [Header("Global Flags")]
    [SerializeField] private bool settlementDangerActive = false;
    [SerializeField] private bool coldWeatherActive = false;

    private readonly List<NPCDecisionBrain> brains = new List<NPCDecisionBrain>(256);
    private float tickTimer = 0f;

    public float TimeOfDay => timeOfDay;
    public float HoursPerTick => hoursPerTick;
    public int TickIndex => tickIndex;
    public int Day => day;
    public int Month => month;
    public int Year => year;
    public bool SettlementDangerActive => settlementDangerActive;
    public bool ColdWeatherActive => coldWeatherActive;
    public bool UseExternalClock => useExternalClock;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[NPCTimeSystem] Duplicate instance found. Destroying this one.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        if (useExternalClock)
            return;

        if (!autoTick)
            return;

        if (secondsPerTick <= 0f)
            return;

        tickTimer += Time.deltaTime;

        while (tickTimer >= secondsPerTick)
        {
            tickTimer -= secondsPerTick;
            ManualTick();
        }
    }

    public void RegisterBrain(NPCDecisionBrain brain)
    {
        if (brain == null)
            return;

        if (!brains.Contains(brain))
            brains.Add(brain);
    }

    public void UnregisterBrain(NPCDecisionBrain brain)
    {
        if (brain == null)
            return;

        brains.Remove(brain);
    }

    public void ManualTick()
    {
        tickIndex++;
        timeOfDay += hoursPerTick;

        while (timeOfDay >= 24f)
        {
            timeOfDay -= 24f;
            IncrementCalendarByOneDay();
        }

        BroadcastCurrentState();
    }

    public void SyncFromExternalClock(float externalHour, int externalDay, int externalMonth, int externalYear, float externalHoursPerTick, bool rebuildTickIndex)
    {
        useExternalClock = true;

        timeOfDay = NormalizeHour(externalHour);
        day = Mathf.Max(1, externalDay);
        month = Mathf.Clamp(externalMonth, 1, 12);
        year = Mathf.Max(1, externalYear);
        hoursPerTick = Mathf.Max(0.001f, externalHoursPerTick);

        if (rebuildTickIndex)
            tickIndex = BuildTickIndexFromDateTime(year, month, day, timeOfDay, hoursPerTick);

        BroadcastCurrentState();
    }

    public void ReleaseExternalClock()
    {
        useExternalClock = false;
    }

    public void SetAutoTick(bool value)
    {
        autoTick = value;
    }

    public void SetHoursPerTickExternal(float value)
    {
        hoursPerTick = Mathf.Max(0.001f, value);
    }

    public bool IsNight(float time) => time >= 22f || time < 6f;
    public bool IsWorkTime(float time) => time >= 8f && time < 17f;
    public bool IsEvening(float time) => time >= 18f && time < 22f;

    public bool IsNight() => IsNight(timeOfDay);
    public bool IsWorkTime() => IsWorkTime(timeOfDay);
    public bool IsEvening() => IsEvening(timeOfDay);

    public void SetSettlementDangerActive(bool value)
    {
        settlementDangerActive = value;
    }

    public void SetColdWeatherActive(bool value)
    {
        coldWeatherActive = value;
    }

    private void BroadcastCurrentState()
    {
        for (int i = 0; i < brains.Count; i++)
        {
            NPCDecisionBrain brain = brains[i];
            if (brain == null)
                continue;

            brain.OnNPCTick(tickIndex, timeOfDay, settlementDangerActive, coldWeatherActive);
        }
    }

    private void IncrementCalendarByOneDay()
    {
        try
        {
            DateTime current = new DateTime(Mathf.Max(1, year), Mathf.Clamp(month, 1, 12), Mathf.Clamp(day, 1, DateTime.DaysInMonth(Mathf.Max(1, year), Mathf.Clamp(month, 1, 12))));
            current = current.AddDays(1);
            year = current.Year;
            month = current.Month;
            day = current.Day;
        }
        catch
        {
            day++;
            if (day > 30)
            {
                day = 1;
                month++;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }
            }
        }
    }

    private static int BuildTickIndexFromDateTime(int year, int month, int day, float hour, float hoursPerTick)
    {
        hoursPerTick = Mathf.Max(0.001f, hoursPerTick);

        try
        {
            DateTime start = new DateTime(1, 1, 1, 0, 0, 0);
            int clampedYear = Mathf.Max(1, year);
            int clampedMonth = Mathf.Clamp(month, 1, 12);
            int maxDay = DateTime.DaysInMonth(clampedYear, clampedMonth);
            int clampedDay = Mathf.Clamp(day, 1, maxDay);

            int wholeHours = Mathf.FloorToInt(NormalizeHour(hour));
            int minutes = Mathf.Clamp(Mathf.RoundToInt((NormalizeHour(hour) - wholeHours) * 60f), 0, 59);

            DateTime current = new DateTime(clampedYear, clampedMonth, clampedDay, wholeHours, minutes, 0);
            double totalHours = (current - start).TotalHours;
            return Mathf.Max(0, Mathf.FloorToInt((float)(totalHours / hoursPerTick)));
        }
        catch
        {
            int daysApprox = ((Mathf.Max(1, year) - 1) * 365) + ((Mathf.Clamp(month, 1, 12) - 1) * 30) + Mathf.Max(0, day - 1);
            float totalHours = daysApprox * 24f + NormalizeHour(hour);
            return Mathf.Max(0, Mathf.FloorToInt(totalHours / hoursPerTick));
        }
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
