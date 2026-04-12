using UnityEngine;

public class NPCSocialAction : NPCAction
{
    private const float SocialStartThreshold = 70f;
    private const float SocialStopThreshold = 92f;

    public override NPCActionType ActionType => NPCActionType.Socialize;
    public override int MinDurationTicks => 2;
    public override float ContinueBonus => 24f;

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
               && brain.SocialPoint != null
               && brain.Needs.social < SocialStopThreshold;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        float social = brain.Needs.social;

        if (brain.CurrentActionType != NPCActionType.Socialize && social >= SocialStartThreshold)
            return 0f;

        if (social >= SocialStopThreshold)
            return 0f;

        float score = NeedUrgency(social) * 75f;

        if (brain.IsEveningTime(timeOfDay))
            score += 18f;

        if (brain.IsWorkTime(timeOfDay) && social > 25f)
            score *= 0.45f;

        if (brain.IsNightTime(timeOfDay))
            score *= 0.15f;

        if (brain.CurrentActionType == NPCActionType.Socialize)
            score += 35f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        poseStarted = false;
        GetBridge(brain)?.ClearPose();
        brain.SetCurrentTarget(brain.SocialPoint);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        if (!brain.IsAtCurrentTarget())
        {
            if (brain.CurrentTarget != brain.SocialPoint)
                brain.SetCurrentTarget(brain.SocialPoint);
            return;
        }

        NPCActionAnimationBridge actionBridge = GetBridge(brain);
        if (!poseStarted && actionBridge != null)
        {
            actionBridge.BeginPose(ActionType, brain.SocialPoint);
            poseStarted = true;
        }

        if (brain.Needs.social >= SocialStopThreshold)
            return;

        LeisureSite leisureSite = brain.CurrentLeisureSite;
        float socialGain = leisureSite != null ? leisureSite.SocialBonusPerTick : 6f;
        float safetyGain = leisureSite != null ? leisureSite.SafetyBonusPerTick : 1f;

        brain.Needs.ModifyNeedPerTick(NPCNeedType.Social, socialGain);
        brain.Needs.ModifyNeedPerTick(NPCNeedType.Safety, safetyGain);
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        GetBridge(brain)?.EndPose(ActionType);
        poseStarted = false;
    }
}
