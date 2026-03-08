using UnityEngine;

public class NPCEatAction : NPCAction
{
    private const float EatUntilHungerValue = 95f;
    private const float EatStartThreshold = 75f;

    public override NPCActionType ActionType => NPCActionType.Eat;
    public override int MinDurationTicks => 1;
    public override float ContinueBonus => 30f;

    private int lastMealTick = int.MinValue;

    public override bool CanRun(NPCDecisionBrain brain)
    {
        return base.CanRun(brain)
               && brain.FoodPoint != null
               && brain.CurrentFoodSite != null
               && brain.Needs.hunger < EatUntilHungerValue;
    }

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null)
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
        brain.SetCurrentTarget(brain.FoodPoint);
    }

    public override void OnTick(NPCDecisionBrain brain, int tickIndex, float timeOfDay)
    {
        FoodSite foodSite = brain.CurrentFoodSite;
        if (foodSite == null)
            return;

        if (!brain.IsAtCurrentTarget())
            return;

        if (brain.Needs.hunger >= EatUntilHungerValue)
            return;

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

            Debug.Log($"[NPCEatAction] {brain.name} found no food at '{foodSite.name}'.", brain);
        }
    }
}