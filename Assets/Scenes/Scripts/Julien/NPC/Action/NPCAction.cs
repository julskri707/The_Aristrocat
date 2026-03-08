using UnityEngine;

public enum NPCActionType
{
    None,
    Idle,
    Work,
    Eat,
    Sleep,
    WarmUp,
    Socialize,
    Panic
}

public abstract class NPCAction
{
    public abstract NPCActionType ActionType { get; }

    // Mindestdauer, bevor die Action freiwillig gewechselt werden darf.
    public virtual int MinDurationTicks => 2;

    // Bonus, damit der NPC nicht wegen minimal besserem Score ständig umspringt.
    public virtual float ContinueBonus => 0f;

    public virtual bool CanRun(NPCDecisionBrain brain)
    {
        return brain != null && brain.Needs != null;
    }

    public abstract float CalculateUtility(NPCDecisionBrain brain, float timeOfDay);

    public virtual void OnEnter(NPCDecisionBrain brain) { }
    public virtual void OnExit(NPCDecisionBrain brain) { }
    public virtual void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay) { }

    protected static float NeedUrgency(float needValue)
    {
        return 1f - Mathf.Clamp01(needValue / 100f);
    }

    protected static float Remap01(float value, float min, float max)
    {
        if (Mathf.Approximately(min, max))
            return 0f;

        return Mathf.Clamp01((value - min) / (max - min));
    }
}