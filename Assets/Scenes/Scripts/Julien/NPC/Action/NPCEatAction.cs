using UnityEngine;

public class NPCEatAction : NPCAction
{
    private enum EatStage
    {
        None,
        MovingToStandPoint,
        Eating,
        StandingBeforeLeave
    }

    private const float EatUntilHungerValue = 95f;
    private const float EatStartThreshold = 75f;

    public override NPCActionType ActionType => NPCActionType.Eat;
    public override int MinDurationTicks => 2;
    public override float ContinueBonus => 30f;

    private int lastMealTick = int.MinValue;
    private int standStageStartTick = int.MinValue;

    private EatStage stage = EatStage.None;
    private NPCActionAnimationBridge bridge;

    private Transform assignedStandPoint;
    private Transform assignedSeatPoint;
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
               && brain.CurrentFoodSite != null
               && brain.CurrentFoodSite.Capacity > 0
               && brain.Needs.hunger < EatUntilHungerValue;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null || foodSite.Capacity <= 0)
            return 0f;

        float hunger = brain.Needs.hunger;

        if (hunger >= EatUntilHungerValue)
            return 0f;

        if (hunger > EatStartThreshold && brain.CurrentActionType != NPCActionType.Eat)
            return 0f;

        float hungerUrgency = NeedUrgency(hunger);
        float score = hungerUrgency * 100f;

        if ((timeOfDay >= 11f && timeOfDay <= 14f) || (timeOfDay >= 18f && timeOfDay <= 21f))
            score += 10f;

        if (brain.CurrentActionType == NPCActionType.Eat && hunger < EatUntilHungerValue)
            score += 40f;

        if (foodSite.RequiresStoredFood && !foodSite.HasFoodAvailable())
            score *= 0.2f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        FoodSite foodSite = brain.CurrentFoodSite;

        stage = EatStage.None;
        lastMealTick = int.MinValue;
        standStageStartTick = int.MinValue;
        poseStarted = false;

        assignedStandPoint = null;
        assignedSeatPoint = null;

        GetBridge(brain)?.ClearPose();

        if (foodSite == null)
        {
            brain.SetCurrentTarget(brain.FoodPoint);
            return;
        }

        if (!foodSite.EnsureSeatAssignment(brain.gameObject))
        {
            brain.SetCurrentTarget(brain.FoodPoint);
            return;
        }

        assignedStandPoint = foodSite.GetAssignedStandPoint(brain.gameObject);
        assignedSeatPoint = foodSite.GetAssignedSeatPoint(brain.gameObject);

        Transform moveTarget = assignedStandPoint != null ? assignedStandPoint : brain.FoodPoint;

        brain.SetCurrentTarget(moveTarget);
        stage = EatStage.MovingToStandPoint;
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null)
            return;

        if (assignedStandPoint == null)
            assignedStandPoint = foodSite.GetAssignedStandPoint(brain.gameObject);

        if (assignedSeatPoint == null)
            assignedSeatPoint = foodSite.GetAssignedSeatPoint(brain.gameObject);

        switch (stage)
        {
            case EatStage.MovingToStandPoint:
                {
                    Transform moveTarget = assignedStandPoint != null ? assignedStandPoint : brain.FoodPoint;
                    if (moveTarget == null)
                        return;

                    if (!brain.IsAtTarget(moveTarget))
                    {
                        if (brain.CurrentTarget != moveTarget)
                            brain.SetCurrentTarget(moveTarget);

                        if (poseStarted)
                        {
                            GetBridge(brain)?.EndPose(ActionType);
                            poseStarted = false;
                        }

                        return;
                    }

                    if (assignedSeatPoint == null)
                        assignedSeatPoint = moveTarget;

                    brain.SetCurrentTarget(null);
                    GetBridge(brain)?.BeginPose(ActionType, assignedSeatPoint);
                    poseStarted = true;
                    stage = EatStage.Eating;
                    return;
                }

            case EatStage.Eating:
                {
                    if (brain.Needs.hunger >= EatUntilHungerValue)
                    {
                        GetBridge(brain)?.EndPose(ActionType);
                        poseStarted = false;

                        if (assignedStandPoint != null)
                            GetBridge(brain)?.TeleportToAnchor(assignedStandPoint);

                        standStageStartTick = tickIndex;
                        stage = EatStage.StandingBeforeLeave;
                        return;
                    }

                    if (tickIndex == lastMealTick)
                        return;

                    lastMealTick = tickIndex;

                    bool consumedStoredFood;
                    bool success = foodSite.TryConsumeMeal(out consumedStoredFood);

                    if (success)
                    {
                        if (consumedStoredFood || !foodSite.RequiresStoredFood)
                        {
                            brain.Needs.ModifyNeed(NPCNeedType.Hunger, foodSite.HungerRestorePerMeal);
                            brain.Needs.ModifyNeed(NPCNeedType.Energy, foodSite.EnergyRestorePerMeal);
                            brain.Needs.ModifyNeed(NPCNeedType.Safety, foodSite.SafetyRestorePerMeal);

                            if (brain.Needs.hunger > 100f)
                                brain.Needs.hunger = 100f;
                        }
                    }
                    else
                    {
                        brain.Needs.ModifyNeed(NPCNeedType.Hunger, foodSite.FallbackHungerRestore);
                        brain.Needs.ModifyNeed(NPCNeedType.Safety, -1f);
                    }

                    return;
                }

            case EatStage.StandingBeforeLeave:
                {
                    int delayTicks = foodSite.StandBeforeLeavingDelayTicks;
                    if (tickIndex - standStageStartTick < delayTicks)
                    {
                        brain.SetCurrentTarget(null);
                        return;
                    }

                    stage = EatStage.None;
                    return;
                }
        }
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        if (poseStarted)
            GetBridge(brain)?.EndPose(ActionType);

        poseStarted = false;
        stage = EatStage.None;
        assignedStandPoint = null;
        assignedSeatPoint = null;
        standStageStartTick = int.MinValue;
    }
}