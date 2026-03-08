
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
    [Range(0,100)] public float hunger = 100;
    [Range(0,100)] public float energy = 100;
    [Range(0,100)] public float warmth = 100;
    [Range(0,100)] public float safety = 100;
    [Range(0,100)] public float social = 100;

    public float hungerDecay = 0.75f;
    public float energyDecay = 0.45f;
    public float warmthDecay = 0.2f;
    public float safetyDecay = 0.05f;
    public float socialDecay = 0.15f;

    public void TickNeeds(bool danger,bool cold)
    {
        hunger = Mathf.Clamp(hunger - hungerDecay,0,100);
        energy = Mathf.Clamp(energy - energyDecay,0,100);
        warmth = Mathf.Clamp(warmth - (cold?warmthDecay*2:warmthDecay),0,100);
        social = Mathf.Clamp(social - socialDecay,0,100);

        float decay = danger ? safetyDecay*8 : safetyDecay;
        safety = Mathf.Clamp(safety - decay,0,100);
    }

    public void ModifyNeed(NPCNeedType type,float delta)
    {
        switch(type)
        {
            case NPCNeedType.Hunger: hunger = Mathf.Clamp(hunger+delta,0,100); break;
            case NPCNeedType.Energy: energy = Mathf.Clamp(energy+delta,0,100); break;
            case NPCNeedType.Warmth: warmth = Mathf.Clamp(warmth+delta,0,100); break;
            case NPCNeedType.Safety: safety = Mathf.Clamp(safety+delta,0,100); break;
            case NPCNeedType.Social: social = Mathf.Clamp(social+delta,0,100); break;
        }
    }

    public bool IsCritical(NPCNeedType type)
    {
        float v=0;
        switch(type)
        {
            case NPCNeedType.Hunger:v=hunger;break;
            case NPCNeedType.Energy:v=energy;break;
            case NPCNeedType.Warmth:v=warmth;break;
            case NPCNeedType.Safety:v=safety;break;
            case NPCNeedType.Social:v=social;break;
        }
        return v<=20;
    }
}
