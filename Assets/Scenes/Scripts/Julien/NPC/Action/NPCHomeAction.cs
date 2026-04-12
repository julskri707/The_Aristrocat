using UnityEngine;

public class NPCHomeAction : NPCAction
{
    private const float DaySleepStartEnergyThreshold = 18f;
    private const float DaySleepWakeEnergyThreshold = 90f;
    private const float NightSleepWakeEnergyThreshold = 98f;

    public override NPCActionType ActionType => NPCActionType.Sleep;
    public override int MinDurationTicks => 2;
    public override float ContinueBonus => 55f;

    private NPCActionAnimationBridge bridge;
    private bool poseStarted;
    private Transform lastBedTarget;
    private Transform lastStandTarget;

    private NPCActionAnimationBridge GetBridge(NPCDecisionBrain brain)
    {
        if (bridge == null && brain != null)
            bridge = brain.GetComponent<NPCActionAnimationBridge>();

        return bridge;
    }

    public override bool CanRun(NPCDecisionBrain brain)
    {
        if (!base.CanRun(brain) || brain.BedPoint == null)
            return false;

        float timeOfDay = brain.TimeSystem != null ? brain.TimeSystem.TimeOfDay : 0f;

        if (brain.CurrentActionType == NPCActionType.Sleep)
            return ShouldContinueSleeping(brain, timeOfDay);

        return brain.IsNightTime(timeOfDay) || brain.Needs.energy <= DaySleepStartEnergyThreshold;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        float energyUrgency = NeedUrgency(brain.Needs.energy);
        float score = energyUrgency * 130f;

        if (brain.IsNightTime(timeOfDay))
            score += 45f;

        if (brain.IsNightSleepLocked(brain.TimeSystem != null ? brain.TimeSystem.TickIndex : 0))
            score += 160f;

        if (brain.CurrentActionType == NPCActionType.Sleep && ShouldContinueSleeping(brain, timeOfDay))
            score += 120f;

        if (!brain.IsNightTime(timeOfDay) && brain.CurrentActionType != NPCActionType.Sleep && brain.Needs.energy > DaySleepStartEnergyThreshold)
            score *= 0.2f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        poseStarted = false;

        HomeSite homeSite = brain.CurrentHomeSite;
        lastBedTarget = brain.BedPoint;
        lastStandTarget = homeSite != null && homeSite.BedStandPoint != null ? homeSite.BedStandPoint : lastBedTarget;

        GetBridge(brain)?.ClearPose();

        if (brain.TimeSystem != null && brain.IsNightTime(brain.TimeSystem.TimeOfDay))
            brain.StartNightSleepBlock(brain.TimeSystem.TickIndex);

        brain.SetCurrentTarget(lastStandTarget);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        HomeSite homeSite = brain.CurrentHomeSite;
        Transform bedTarget = brain.BedPoint;
        Transform standTarget = homeSite != null && homeSite.BedStandPoint != null ? homeSite.BedStandPoint : bedTarget;

        lastBedTarget = bedTarget;
        lastStandTarget = standTarget;

        if (bedTarget == null)
            return;

        if (!poseStarted)
        {
            Transform moveTarget = standTarget != null ? standTarget : bedTarget;

            if (!brain.IsAtTarget(moveTarget))
            {
                if (brain.CurrentTarget != moveTarget)
                    brain.SetCurrentTarget(moveTarget);

                return;
            }

            brain.SetCurrentTarget(null);
            StartSleepPose(brain, bedTarget);
        }

        brain.SetCurrentTarget(null);
        TickSleepNeeds(brain, homeSite, timeOfDay);
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        NPCActionAnimationBridge actionBridge = GetBridge(brain);

        if (poseStarted)
        {
            actionBridge?.ClearPose();

            if (lastStandTarget != null)
                actionBridge?.TeleportToAnchor(lastStandTarget);
        }
        else
        {
            actionBridge?.EndPose(ActionType);
        }

        poseStarted = false;
        lastBedTarget = null;
        lastStandTarget = null;
    }

    private void StartSleepPose(NPCDecisionBrain brain, Transform bedTarget)
    {
        NPCActionAnimationBridge actionBridge = GetBridge(brain);

        if (actionBridge != null)
        {
            actionBridge.TeleportToAnchor(bedTarget);
            actionBridge.BeginPose(ActionType, bedTarget);
        }

        poseStarted = true;
    }

    private bool ShouldContinueSleeping(NPCDecisionBrain brain, float timeOfDay)
    {
        if (brain == null || brain.Needs == null || brain.BedPoint == null)
            return false;

        int tickIndex = brain.TimeSystem != null ? brain.TimeSystem.TickIndex : 0;
        bool lockedNightSleep = brain.IsNightSleepLocked(tickIndex);
        bool isNightTime = brain.IsNightTime(timeOfDay);

        if (lockedNightSleep)
            return true;

        if (isNightTime)
            return brain.Needs.energy < NightSleepWakeEnergyThreshold;

        return brain.Needs.energy < DaySleepWakeEnergyThreshold;
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

        brain.Needs.ModifyNeedPerTick(NPCNeedType.Energy, energyGain);
        brain.Needs.ModifyNeedPerTick(NPCNeedType.Warmth, warmthGain);
        brain.Needs.ModifyNeedPerTick(NPCNeedType.Safety, safetyGain);
        brain.Needs.ModifyNeedPerTick(NPCNeedType.Hunger, hungerDelta);
    }
}
