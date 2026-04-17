using UnityEngine;

public class NPCEatAction : NPCAction
{
    private enum EatStage
    {
        None,
        MovingToStandPoint,
        WaitingSeated,
        Finished
    }

    private const float EatStartThreshold = 75f;
    private const float EatStopThreshold = 95f;
    private const float ActiveActionScore = 10000f;

    public override NPCActionType ActionType => NPCActionType.Eat;
    public override int MinDurationTicks => 1;
    public override float ContinueBonus => 1000f;

    private EatStage stage = EatStage.None;
    private NPCActionAnimationBridge bridge;

    private Transform assignedStandPoint;
    private Transform assignedSeatPoint;

    private bool poseStarted;
    private bool seatReleased;
    private float waitEndTime = -1f;

    private NPCActionAnimationBridge GetBridge(NPCDecisionBrain brain)
    {
        if (bridge == null && brain != null)
            bridge = brain.GetComponent<NPCActionAnimationBridge>();

        return bridge;
    }

    private static FoodSiteEatSettings GetSettings(FoodSite foodSite)
    {
        return foodSite != null ? foodSite.EatSettings : null;
    }

    public override bool CanRun(NPCDecisionBrain brain)
    {
        if (!base.CanRun(brain))
            return false;

        if (stage == EatStage.MovingToStandPoint || stage == EatStage.WaitingSeated)
            return true;

        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null || foodSite.Capacity <= 0)
            return false;

        return brain.Needs.hunger < EatStopThreshold;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        if (stage == EatStage.MovingToStandPoint || stage == EatStage.WaitingSeated)
            return ActiveActionScore;

        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null || foodSite.Capacity <= 0)
            return 0f;

        float hunger = brain.Needs.hunger;

        if (brain.CurrentActionType != NPCActionType.Eat && hunger >= EatStartThreshold)
            return 0f;

        if (hunger >= EatStopThreshold)
            return 0f;

        float score = NeedUrgency(hunger) * 100f;

        if ((timeOfDay >= 11f && timeOfDay <= 14f) || (timeOfDay >= 18f && timeOfDay <= 21f))
            score += 10f;

        return score;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        stage = EatStage.None;
        assignedStandPoint = null;
        assignedSeatPoint = null;
        poseStarted = false;
        seatReleased = false;
        waitEndTime = -1f;

        GetBridge(brain)?.ClearPose();

        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null)
        {
            brain.SetCurrentTarget(brain.FoodPoint);
            return;
        }

        if (!foodSite.TryReserve(brain.gameObject))
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

        FoodSiteEatSettings settings = GetSettings(foodSite);

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

                        return;
                    }

                    brain.SetCurrentTarget(null);

                    Transform poseTarget = assignedSeatPoint != null ? assignedSeatPoint : moveTarget;

                    if (assignedSeatPoint != null)
                        GetBridge(brain)?.TeleportToAnchor(assignedSeatPoint);

                    GetBridge(brain)?.BeginPose(ActionType, poseTarget);
                    poseStarted = true;

                    bool canStartEating = true;

                    if (settings == null || settings.consumeMealOnEatStart)
                    {
                        if (foodSite.RequiresStoredFood)
                        {
                            bool consumedStoredFood;
                            bool consumeOk = foodSite.TryConsumeMeal(out consumedStoredFood);
                            canStartEating = consumeOk && consumedStoredFood;
                        }
                    }

                    if (settings != null && settings.requireSuccessfulMealConsumption && !canStartEating)
                    {
                        ForceStandUp(brain, foodSite);
                        stage = EatStage.Finished;
                        return;
                    }

                    ApplyEatResult(brain, settings);

                    float waitSeconds = settings != null ? settings.waitSecondsAfterEating : 30f;
                    waitEndTime = Time.time + Mathf.Max(0f, waitSeconds);
                    stage = EatStage.WaitingSeated;
                    return;
                }

            case EatStage.WaitingSeated:
                {
                    brain.SetCurrentTarget(null);

                    if (Time.time < waitEndTime)
                        return;

                    ForceStandUp(brain, foodSite);
                    stage = EatStage.Finished;
                    return;
                }

            case EatStage.Finished:
                {
                    brain.SetCurrentTarget(null);
                    return;
                }
        }
    }

    public override void OnExit(NPCDecisionBrain brain)
    {
        FoodSite foodSite = brain.CurrentFoodSite;
        ForceStandUp(brain, foodSite);

        stage = EatStage.None;
        assignedStandPoint = null;
        assignedSeatPoint = null;
        poseStarted = false;
        seatReleased = false;
        waitEndTime = -1f;
    }

    private void ApplyEatResult(NPCDecisionBrain brain, FoodSiteEatSettings settings)
    {
        if (brain == null || brain.Needs == null)
            return;

        if (settings == null)
        {
            brain.Needs.hunger = 100f;
            return;
        }

        if (settings.setHungerToFullOnEat)
            brain.Needs.hunger = 100f;
        else
            brain.Needs.hunger = Mathf.Clamp(settings.hungerValueOnEat, 0f, 100f);

        if (settings.setEnergyOnEat)
            brain.Needs.energy = Mathf.Clamp(settings.energyValueOnEat, 0f, 100f);
    }

    private void ForceStandUp(NPCDecisionBrain brain, FoodSite foodSite)
    {
        if (brain == null)
            return;

        if (poseStarted)
        {
            GetBridge(brain)?.ClearPose();

            if (assignedStandPoint != null)
                GetBridge(brain)?.TeleportToAnchor(assignedStandPoint);

            poseStarted = false;
        }
        else
        {
            GetBridge(brain)?.EndPose(ActionType);
        }

        if (!seatReleased && foodSite != null)
        {
            foodSite.Release(brain.gameObject);
            seatReleased = true;
        }
    }
}
