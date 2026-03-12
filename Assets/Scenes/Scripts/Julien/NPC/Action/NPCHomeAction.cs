using UnityEngine;

public class NPCHomeAction : NPCAction
{
    public override NPCActionType ActionType => NPCActionType.Sleep;
    public override int MinDurationTicks => 6;
    public override float ContinueBonus => 18f;

    public override bool CanRun(NPCDecisionBrain brain)
    {
        return base.CanRun(brain)
               && brain.BedPoint != null
               && brain.CanSleepNow(brain.TimeSystem != null ? brain.TimeSystem.TimeOfDay : 0f);
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        float energyUrgency = NeedUrgency(brain.Needs.energy);
        float score = energyUrgency * 100f;

        if (brain.IsNightTime(timeOfDay))
            score += 35f;

        if (brain.IsNightSleepLocked(brain.TimeSystem != null ? brain.TimeSystem.TickIndex : 0))
            score += 200f;

        if (!brain.IsNightTime(timeOfDay) && brain.Needs.energy > 30f)
            score *= 0.35f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        brain.SetCurrentTarget(brain.BedPoint);

        if (brain.TimeSystem != null && brain.IsNightTime(brain.TimeSystem.TimeOfDay))
        {
            brain.StartNightSleepBlock(brain.TimeSystem.TickIndex);
        }
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        if (!brain.IsAtCurrentTarget())
            return;

        float energyGain = brain.IsNightTime(timeOfDay) ? 16f : 10f;

        brain.Needs.ModifyNeed(NPCNeedType.Energy, energyGain);
        brain.Needs.ModifyNeed(NPCNeedType.Warmth, 3f);
        brain.Needs.ModifyNeed(NPCNeedType.Safety, 1f);
        brain.Needs.ModifyNeed(NPCNeedType.Hunger, -0.35f);
    }
}