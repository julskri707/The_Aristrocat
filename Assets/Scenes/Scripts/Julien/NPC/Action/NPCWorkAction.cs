using UnityEngine;

public class NPCWorkAction : NPCAction
{
    public override NPCActionType ActionType => NPCActionType.Work;
    public override int MinDurationTicks => 4;
    public override float ContinueBonus => 12f;

    private NPCActionAnimationBridge bridge;
    private bool poseStarted;

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

        if (brain.Needs.hunger < 35f) return 0f;
        if (brain.Needs.energy < 30f) return 0f;
        if (brain.Needs.safety < 25f) return 0f;
        if (brain.IsNightTime(timeOfDay)) return 0f;

        float score = 5f;

        if (brain.IsWorkTime(timeOfDay))
            score += 30f;
        else if (brain.IsEveningTime(timeOfDay))
            score += 4f;

        score += Remap01(brain.Needs.energy, 35f, 100f) * 10f;
        score += Remap01(brain.Needs.hunger, 35f, 100f) * 6f;
        score += Remap01(brain.Needs.safety, 30f, 100f) * 6f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        poseStarted = false;
        GetBridge(brain)?.ClearPose();
        brain.SetCurrentTarget(brain.WorkPoint);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        if (!brain.IsAtCurrentTarget())
        {
            if (brain.CurrentTarget != brain.WorkPoint)
                brain.SetCurrentTarget(brain.WorkPoint);
            return;
        }

        NPCActionAnimationBridge actionBridge = GetBridge(brain);
        if (!poseStarted && actionBridge != null)
        {
            actionBridge.BeginPose(ActionType, brain.WorkPoint);
            poseStarted = true;
        }

        brain.Needs.ModifyNeedPerTick(NPCNeedType.Energy, -0.6f);
        brain.Needs.ModifyNeedPerTick(NPCNeedType.Social, -0.1f);
        brain.Needs.ModifyNeedPerTick(NPCNeedType.Hunger, -0.25f);
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        GetBridge(brain)?.EndPose(ActionType);
        poseStarted = false;
    }
}
