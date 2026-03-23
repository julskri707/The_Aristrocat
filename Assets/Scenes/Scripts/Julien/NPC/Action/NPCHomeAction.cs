using UnityEngine;

public class NPCHomeAction : NPCAction
{
    public override NPCActionType ActionType => NPCActionType.Sleep;
    public override int MinDurationTicks => 6;
    public override float ContinueBonus => 18f;

    private NPCActionAnimationBridge bridge;

    private bool waitingAtBedside;
    private bool poseStarted;
    private int bedsideArrivalTick = int.MinValue;

    private NPCActionAnimationBridge GetBridge(NPCDecisionBrain brain)
    {
        if (bridge == null && brain != null)
            bridge = brain.GetComponent<NPCActionAnimationBridge>();

        return bridge;
    }

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
        waitingAtBedside = false;
        poseStarted = false;
        bedsideArrivalTick = int.MinValue;

        GetBridge(brain)?.ClearPose();

        HomeSite homeSite = brain.CurrentHomeSite;
        Transform standTarget = homeSite != null ? homeSite.BedStandPoint : brain.BedPoint;

        brain.SetCurrentTarget(standTarget);

        if (brain.TimeSystem != null && brain.IsNightTime(brain.TimeSystem.TimeOfDay))
            brain.StartNightSleepBlock(brain.TimeSystem.TickIndex);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        HomeSite homeSite = brain.CurrentHomeSite;
        Transform standTarget = homeSite != null ? homeSite.BedStandPoint : brain.BedPoint;
        Transform bedTarget = brain.BedPoint;

        if (bedTarget == null)
            return;

        if (poseStarted)
        {
            TickSleepNeeds(brain, homeSite, timeOfDay);
            return;
        }

        if (!waitingAtBedside)
        {
            if (!brain.IsAtTarget(standTarget))
            {
                GetBridge(brain)?.EndPose(ActionType);
                return;
            }

            waitingAtBedside = true;
            bedsideArrivalTick = tickIndex;

            brain.SetCurrentTarget(null);
            return;
        }

        int delayTicks = homeSite != null ? homeSite.TeleportIntoBedDelayTicks : 1;
        if (tickIndex - bedsideArrivalTick < delayTicks)
            return;

        brain.SetCurrentTarget(null);
        GetBridge(brain)?.BeginPose(ActionType, bedTarget);

        poseStarted = true;

        TickSleepNeeds(brain, homeSite, timeOfDay);
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        HomeSite homeSite = brain.CurrentHomeSite;
        Transform standTarget = homeSite != null ? homeSite.BedStandPoint : null;
        NPCActionAnimationBridge actionBridge = GetBridge(brain);

        if (poseStarted)
        {
            actionBridge?.ClearPose();
            actionBridge?.TeleportToAnchor(standTarget);
        }
        else
        {
            actionBridge?.EndPose(ActionType);
        }

        waitingAtBedside = false;
        poseStarted = false;
        bedsideArrivalTick = int.MinValue;
    }

    private void TickSleepNeeds(NPCDecisionBrain brain, HomeSite homeSite, float timeOfDay)
    {
        float energyGain;
        float warmthGain;
        float safetyGain;
        float hungerDelta;

        if (homeSite != null)
        {
            energyGain = homeSite.EnergyRestorePerTickInBed;
            warmthGain = homeSite.WarmthRestorePerTickInBed;
            safetyGain = homeSite.SafetyRestorePerTickInBed;
            hungerDelta = homeSite.HungerDeltaPerTickInBed;
        }
        else
        {
            energyGain = brain.IsNightTime(timeOfDay) ? 16f : 10f;
            warmthGain = 3f;
            safetyGain = 1f;
            hungerDelta = -0.35f;
        }

        brain.Needs.ModifyNeed(NPCNeedType.Energy, energyGain);
        brain.Needs.ModifyNeed(NPCNeedType.Warmth, warmthGain);
        brain.Needs.ModifyNeed(NPCNeedType.Safety, safetyGain);
        brain.Needs.ModifyNeed(NPCNeedType.Hunger, hungerDelta);
    }
}