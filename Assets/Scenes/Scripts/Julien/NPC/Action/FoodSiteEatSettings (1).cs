using UnityEngine;

[DisallowMultipleComponent]
public class FoodSiteEatSettings : MonoBehaviour
{
    [Header("Simple Eat Flow")]
    [Min(0f)] public float waitSecondsAfterEating = 30f;

    [Header("Applied When Sitting Down")]
    public bool setHungerToFullOnEat = true;
    [Range(0f, 100f)] public float hungerValueOnEat = 100f;

    public bool setEnergyOnEat = false;
    [Range(0f, 100f)] public float energyValueOnEat = 100f;

    [Header("Meal Consumption")]
    public bool consumeMealOnEatStart = true;
    public bool requireSuccessfulMealConsumption = true;

    private void OnValidate()
    {
        waitSecondsAfterEating = Mathf.Max(0f, waitSecondsAfterEating);
        hungerValueOnEat = Mathf.Clamp(hungerValueOnEat, 0f, 100f);
        energyValueOnEat = Mathf.Clamp(energyValueOnEat, 0f, 100f);
    }
}
