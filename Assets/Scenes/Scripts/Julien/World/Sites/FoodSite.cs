using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class FoodSite : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform servicePoint;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField, Min(1)] private int capacity = 2;

    [Header("Food Logic")]
    [SerializeField, Min(1)] private int mealFoodCost = 1;
    [SerializeField] private bool requiresStoredFood = true;
    [SerializeField, Range(0f, 100f)] private float fallbackHungerRestore = 4f;

    [Header("Need Restore Per Meal")]
    [SerializeField, Range(0f, 100f)] private float hungerRestorePerMeal = 18f;
    [SerializeField, Range(0f, 100f)] private float energyRestorePerMeal = 2f;
    [SerializeField, Range(0f, 100f)] private float safetyRestorePerMeal = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugWarnings = true;

    private readonly HashSet<int> reservedNpcIds = new HashSet<int>();

    public Transform ServicePoint => servicePoint != null ? servicePoint : transform;
    public ResourceManager ResourceManager => resourceManager;
    public int Capacity => Mathf.Max(1, capacity);
    public int ReservedCount => reservedNpcIds.Count;

    public int MealFoodCost => Mathf.Max(1, mealFoodCost);
    public bool RequiresStoredFood => requiresStoredFood;
    public float FallbackHungerRestore => fallbackHungerRestore;

    public float HungerRestorePerMeal => hungerRestorePerMeal;
    public float EnergyRestorePerMeal => energyRestorePerMeal;
    public float SafetyRestorePerMeal => safetyRestorePerMeal;

    private void OnEnable()
    {
        SiteRegistry.Instance?.RegisterFoodSite(this);
    }

    private void OnDisable()
    {
        SiteRegistry.Instance?.UnregisterFoodSite(this);
        reservedNpcIds.Clear();
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        mealFoodCost = Mathf.Max(1, mealFoodCost);
        fallbackHungerRestore = Mathf.Clamp(fallbackHungerRestore, 0f, 100f);
        hungerRestorePerMeal = Mathf.Clamp(hungerRestorePerMeal, 0f, 100f);
        energyRestorePerMeal = Mathf.Clamp(energyRestorePerMeal, 0f, 100f);
        safetyRestorePerMeal = Mathf.Clamp(safetyRestorePerMeal, 0f, 100f);
    }

    public bool IsReservedBy(GameObject npc)
    {
        if (npc == null)
            return false;

        return reservedNpcIds.Contains(npc.GetInstanceID());
    }

    public bool CanReserve(GameObject npc)
    {
        if (npc == null)
            return false;

        if (IsReservedBy(npc))
            return true;

        return reservedNpcIds.Count < Capacity;
    }

    public bool TryReserve(GameObject npc)
    {
        if (!CanReserve(npc))
            return false;

        reservedNpcIds.Add(npc.GetInstanceID());
        return true;
    }

    public void Release(GameObject npc)
    {
        if (npc == null)
            return;

        reservedNpcIds.Remove(npc.GetInstanceID());
    }

    public Vector3 GetUsePosition()
    {
        return ServicePoint.position;
    }

    public bool HasFoodAvailable()
    {
        if (!requiresStoredFood)
            return true;

        if (resourceManager == null)
        {
            if (debugWarnings)
                Debug.LogWarning($"[FoodSite] '{name}' has no ResourceManager assigned.", this);

            return false;
        }

        return CanAffordFoodInternal(resourceManager, MealFoodCost);
    }

    public bool TryConsumeMeal(out bool consumedStoredFood)
    {
        consumedStoredFood = false;

        if (!requiresStoredFood)
            return true;

        if (resourceManager == null)
        {
            if (debugWarnings)
                Debug.LogWarning($"[FoodSite] '{name}' has no ResourceManager assigned. Cannot consume food.", this);

            return false;
        }

        bool success = TryConsumeFoodInternal(resourceManager, MealFoodCost);
        consumedStoredFood = success;

        if (debugLogs)
            Debug.Log($"[FoodSite] {name} TryConsumeMeal => {success}", this);

        return success;
    }

    private static bool CanAffordFoodInternal(ResourceManager manager, int amount)
    {
        if (manager == null)
            return false;

        Type managerType = manager.GetType();
        MethodInfo[] methods = managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != "CanAfford")
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2)
                continue;

            object[] args = BuildFoodArguments(parameters[0].ParameterType, parameters[1].ParameterType, amount);
            if (args == null)
                continue;

            try
            {
                object result = method.Invoke(manager, args);
                if (result is bool boolResult)
                    return boolResult;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryConsumeFoodInternal(ResourceManager manager, int amount)
    {
        if (manager == null)
            return false;

        Type managerType = manager.GetType();
        MethodInfo[] methods = managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != "TryConsume")
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2)
                continue;

            object[] args = BuildFoodArguments(parameters[0].ParameterType, parameters[1].ParameterType, amount);
            if (args == null)
                continue;

            try
            {
                object result = method.Invoke(manager, args);
                if (result is bool boolResult)
                    return boolResult;
            }
            catch
            {
            }
        }

        return false;
    }

    private static object[] BuildFoodArguments(Type resourceParamType, Type amountParamType, int amount)
    {
        object resourceArg;

        if (resourceParamType == typeof(string))
        {
            resourceArg = "Essen";
        }
        else if (resourceParamType.IsEnum)
        {
            try
            {
                resourceArg = Enum.Parse(resourceParamType, "Essen");
            }
            catch
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        object amountArg;

        if (amountParamType == typeof(int))
            amountArg = amount;
        else if (amountParamType == typeof(float))
            amountArg = (float)amount;
        else if (amountParamType == typeof(double))
            amountArg = (double)amount;
        else
            return null;

        return new[] { resourceArg, amountArg };
    }
}