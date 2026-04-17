using UnityEngine;

public enum NPCNeedType
{
    Hunger,
    Energy,
    Warmth,
    Safety,
    Social
}

[DisallowMultipleComponent]
public class NPCNeeds : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NPCTimeSystem timeSystem;
    [SerializeField] private NPCNeedsProfileSO profile;
    [SerializeField] private bool autoFindTimeSystem = true;
    [SerializeField] private bool applyProfileOnAwake = true;

    [Header("Current Needs")]
    [Range(0f, 100f)] public float hunger = 100f;
    [Range(0f, 100f)] public float energy = 100f;
    [Range(0f, 100f)] public float warmth = 100f;
    [Range(0f, 100f)] public float safety = 100f;
    [Range(0f, 100f)] public float social = 100f;

    [Header("Decay Values")]
    public float hungerDecay = 0.75f;
    public float energyDecay = 0.45f;
    public float warmthDecay = 0.20f;
    public float safetyDecay = 0.05f;
    public float socialDecay = 0.15f;

    [Header("Tick Scaling")]
    [SerializeField, Min(0.01f)] private float referenceHoursPerTick = 0.25f;

    public NPCTimeSystem TimeSystem => timeSystem;
    public NPCNeedsProfileSO Profile => profile;

    private void Awake()
    {
        ResolveTimeSystem();

        if (applyProfileOnAwake && profile != null)
            ApplyProfile(profile);
    }

    private void OnValidate()
    {
        referenceHoursPerTick = Mathf.Max(0.01f, referenceHoursPerTick);
    }

    public void ApplyProfile(NPCNeedsProfileSO newProfile)
    {
        if (newProfile == null)
            return;

        profile = newProfile;
        hungerDecay = Mathf.Max(0f, newProfile.hungerDecay);
        energyDecay = Mathf.Max(0f, newProfile.energyDecay);
        warmthDecay = Mathf.Max(0f, newProfile.warmthDecay);
        safetyDecay = Mathf.Max(0f, newProfile.safetyDecay);
        socialDecay = Mathf.Max(0f, newProfile.socialDecay);
    }

    public void SetTimeSystem(NPCTimeSystem newTimeSystem)
    {
        timeSystem = newTimeSystem;
    }

    public void TickNeeds(bool danger, bool cold)
    {
        float scale = GetTickScale();

        hunger = ClampNeed(hunger - hungerDecay * scale);
        energy = ClampNeed(energy - energyDecay * scale);
        warmth = ClampNeed(warmth - (cold ? warmthDecay * 2f : warmthDecay) * scale);
        social = ClampNeed(social - socialDecay * scale);
        safety = ClampNeed(safety - (danger ? safetyDecay * 8f : safetyDecay) * scale);
    }

    public void ModifyNeed(NPCNeedType type, float delta)
    {
        switch (type)
        {
            case NPCNeedType.Hunger:
                hunger = ClampNeed(hunger + delta);
                break;
            case NPCNeedType.Energy:
                energy = ClampNeed(energy + delta);
                break;
            case NPCNeedType.Warmth:
                warmth = ClampNeed(warmth + delta);
                break;
            case NPCNeedType.Safety:
                safety = ClampNeed(safety + delta);
                break;
            case NPCNeedType.Social:
                social = ClampNeed(social + delta);
                break;
        }
    }

    public void ModifyNeedPerTick(NPCNeedType type, float deltaPerReferenceTick)
    {
        ModifyNeed(type, deltaPerReferenceTick * GetTickScale());
    }

    public float GetNeedValue(NPCNeedType type)
    {
        switch (type)
        {
            case NPCNeedType.Hunger: return hunger;
            case NPCNeedType.Energy: return energy;
            case NPCNeedType.Warmth: return warmth;
            case NPCNeedType.Safety: return safety;
            case NPCNeedType.Social: return social;
            default: return 0f;
        }
    }

    public bool IsCritical(NPCNeedType type)
    {
        return GetNeedValue(type) <= 20f;
    }

    public float GetTickScale()
    {
        ResolveTimeSystem();

        float hoursPerTick = referenceHoursPerTick;
        if (timeSystem != null)
            hoursPerTick = Mathf.Max(0.01f, timeSystem.HoursPerTick);

        return hoursPerTick / Mathf.Max(0.01f, referenceHoursPerTick);
    }

    private void ResolveTimeSystem()
    {
        if (timeSystem == null && autoFindTimeSystem)
            timeSystem = NPCTimeSystem.Instance;
    }

    private static float ClampNeed(float value)
    {
        return Mathf.Clamp(value, 0f, 100f);
    }
}
