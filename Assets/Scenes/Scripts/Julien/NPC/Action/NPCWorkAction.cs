using UnityEngine;

public class NPCWorkAction : NPCAction
{
    public override NPCActionType ActionType => NPCActionType.Work;
    public override int MinDurationTicks => 8;
    public override float ContinueBonus => 16f;

    private NPCActionAnimationBridge bridge;

    private NPCActionAnimationBridge GetBridge(NPCDecisionBrain brain)
    {
        if (bridge == null && brain != null)
            bridge = brain.GetComponent<NPCActionAnimationBridge>();

        return bridge;
    }

    public override bool CanRun(NPCDecisionBrain brain)
    {
        return base.CanRun(brain)
               && brain.HasJobAssigned
               && brain.WorkPoint != null;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        if (!CanRun(brain))
            return 0f;

        if (brain.Needs.hunger < 25f) return 0f;
        if (brain.Needs.energy < 22f) return 0f;
        if (brain.Needs.safety < 20f) return 0f;
        if (brain.IsNightTime(timeOfDay)) return 0f;

        float score = 8f;

        if (brain.IsWorkTime(timeOfDay))
            score += 35f;
        else if (brain.IsEveningTime(timeOfDay))
            score += 6f;

        score += Remap01(brain.Needs.energy, 30f, 100f) * 12f;
        score += Remap01(brain.Needs.hunger, 30f, 100f) * 8f;
        score += Remap01(brain.Needs.safety, 30f, 100f) * 8f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        GetBridge(brain)?.ClearPose();
        brain.SetCurrentTarget(brain.WorkPoint);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        if (!brain.IsAtCurrentTarget())
        {
            GetBridge(brain)?.EndPose(ActionType);
            return;
        }

        GetBridge(brain)?.BeginPose(ActionType, brain.WorkPoint);

        brain.Needs.ModifyNeed(NPCNeedType.Energy, -0.6f);
        brain.Needs.ModifyNeed(NPCNeedType.Social, -0.1f);
        brain.Needs.ModifyNeed(NPCNeedType.Hunger, -0.25f);
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        GetBridge(brain)?.EndPose(ActionType);
    }
}