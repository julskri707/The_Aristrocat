using UnityEngine;

public class NPCSocialAction : NPCAction
{
    private const float SocialStartThreshold = 70f;
    private const float SocialStopThreshold = 92f;

    public override NPCActionType ActionType => NPCActionType.Socialize;
    public override int MinDurationTicks => 2;
    public override float ContinueBonus => 24f;

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
               && brain.SocialPoint != null
               && brain.CurrentLeisureSite != null
               && brain.Needs.social < SocialStopThreshold;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        LeisureSite leisureSite = brain.CurrentLeisureSite;
        if (leisureSite == null)
            return 0f;

        float social = brain.Needs.social;

        if (brain.CurrentActionType != NPCActionType.Socialize && social >= SocialStartThreshold)
            return 0f;

        if (social >= SocialStopThreshold)
            return 0f;

        float socialUrgency = NeedUrgency(social);
        float score = socialUrgency * 75f;

        if (brain.IsEveningTime(timeOfDay))
            score += 18f;

        if (brain.IsWorkTime(timeOfDay) && social > 25f)
            score *= 0.45f;

        if (brain.IsNightTime(timeOfDay))
            score *= 0.15f;

        if (brain.CurrentActionType == NPCActionType.Socialize && social < SocialStopThreshold)
            score += 35f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        GetBridge(brain)?.ClearPose();
        brain.SetCurrentTarget(brain.SocialPoint);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        LeisureSite leisureSite = brain.CurrentLeisureSite;
        if (leisureSite == null)
            return;

        if (!brain.IsAtCurrentTarget())
        {
            GetBridge(brain)?.EndPose(ActionType);
            return;
        }

        GetBridge(brain)?.BeginPose(ActionType, brain.SocialPoint);

        if (brain.Needs.social >= SocialStopThreshold)
            return;

        brain.Needs.ModifyNeed(NPCNeedType.Social, leisureSite.SocialBonusPerTick);
        brain.Needs.ModifyNeed(NPCNeedType.Safety, leisureSite.SafetyBonusPerTick);

        if (brain.Needs.social > 100f)
            brain.Needs.social = 100f;
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        GetBridge(brain)?.EndPose(ActionType);
    }
}