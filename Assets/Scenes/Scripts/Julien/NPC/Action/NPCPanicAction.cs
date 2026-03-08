using UnityEngine;

public class NPCPanicAction : NPCAction
{
    public override NPCActionType ActionType => NPCActionType.Panic;
    public override int MinDurationTicks => 4;
    public override float ContinueBonus => 25f;

    public override bool CanRun(NPCDecisionBrain brain)
    {
        if (!base.CanRun(brain))
            return false;

        return brain.EventContext != null && brain.EventContext.PanicRecommended;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        if (brain.EventContext == null)
            return 0f;

        return 200f + brain.EventContext.PanicScoreBonus;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        if (TownCenterSite.Instance != null && TownCenterSite.Instance.safePoint != null)
        {
            brain.SetCurrentTarget(TownCenterSite.Instance.safePoint);
        }
        else if (brain.BedPoint != null)
        {
            brain.SetCurrentTarget(brain.BedPoint);
        }
        else
        {
            brain.SetCurrentTarget(null);
            Debug.LogWarning($"[NPCPanicAction] No safePoint or BedPoint found for {brain.name}.", brain);
        }
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        if (!brain.IsAtCurrentTarget())
            return;

        brain.Needs.ModifyNeed(NPCNeedType.Safety, 3f);
        brain.Needs.ModifyNeed(NPCNeedType.Energy, -0.2f);
    }
}