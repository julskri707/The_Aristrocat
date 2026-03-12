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

    [Header("Global Flags")]
    [SerializeField] private bool settlementDangerActive = false;
    [SerializeField] private bool coldWeatherActive = false;

    private readonly List<NPCDecisionBrain> brains = new List<NPCDecisionBrain>(256);
    private float tickTimer = 0f;

    public float TimeOfDay => timeOfDay;
    public float HoursPerTick => hoursPerTick;
    public int TickIndex => tickIndex;
    public bool SettlementDangerActive => settlementDangerActive;
    public bool ColdWeatherActive => coldWeatherActive;

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
            timeOfDay -= 24f;

        for (int i = 0; i < brains.Count; i++)
        {
            NPCDecisionBrain brain = brains[i];
            if (brain == null)
                continue;

            brain.OnNPCTick(tickIndex, timeOfDay, settlementDangerActive, coldWeatherActive);
        }
    }

    public bool IsNight(float time) => time >= 22f || time < 6f;
    public bool IsWorkTime(float time) => time >= 8f && time < 17f;
    public bool IsEvening(float time) => time >= 18f && time < 22f;

    public void SetSettlementDangerActive(bool value)
    {
        settlementDangerActive = value;
    }

    public void SetColdWeatherActive(bool value)
    {
        coldWeatherActive = value;
    }
}