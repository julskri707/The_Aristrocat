using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class FoodSite : MonoBehaviour
{
    private sealed class SeatSlot
    {
        public CampfireBench bench;
        public int benchSeatIndex;
        public Transform standTransform;
        public Transform seatTransform;
    }

    [Header("Setup")]
    [SerializeField] private Transform servicePoint;
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField, Min(1)] private int maxSeats = 10;

    [Header("Food Logic")]
    [SerializeField, Min(1)] private int mealFoodCost = 1;
    [SerializeField] private bool requiresStoredFood = true;

    [Tooltip("Muss exakt zum Namen in deinem ResourceManager passen, z. B. Essen / Food / Nahrung")]
    [SerializeField] private string resourceKeyName = "Essen";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugWarnings = true;

    private readonly List<CampfireBench> benches = new List<CampfireBench>();
    private readonly List<SeatSlot> seatSlots = new List<SeatSlot>();
    private readonly Dictionary<int, SeatSlot> npcSeatAssignments = new Dictionary<int, SeatSlot>();
    private readonly HashSet<int> reservedNpcIds = new HashSet<int>();

    private bool layoutDirty = true;
    private FoodSiteEatSettings cachedEatSettings;

    public Transform ServicePoint => servicePoint != null ? servicePoint : transform;
    public ResourceManager ResourceManager => resourceManager;

    public FoodSiteEatSettings EatSettings
    {
        get
        {
            if (cachedEatSettings == null)
                cachedEatSettings = GetComponent<FoodSiteEatSettings>();
            return cachedEatSettings;
        }
    }

    public int Capacity
    {
        get
        {
            EnsureSeatLayout();
            return seatSlots.Count;
        }
    }

    public int ReservedCount => reservedNpcIds.Count;
    public int MealFoodCost => Mathf.Max(1, mealFoodCost);
    public bool RequiresStoredFood => requiresStoredFood;
    public string ResourceKeyName => resourceKeyName;

    public int StandBeforeLeavingDelayTicks => 0;
    public float FallbackHungerRestore => 0f;
    public float HungerRestorePerMeal => 0f;
    public float EnergyRestorePerMeal => 0f;
    public float SafetyRestorePerMeal => 0f;

    private void OnEnable()
    {
        cachedEatSettings = GetComponent<FoodSiteEatSettings>();
        RegisterToRegistry();
        layoutDirty = true;
    }

    private void Start()
    {
        cachedEatSettings = GetComponent<FoodSiteEatSettings>();
        RegisterToRegistry();
        layoutDirty = true;
        EnsureSeatLayout();
    }

    private void OnDisable()
    {
        SiteRegistry.Instance?.UnregisterFoodSite(this);

        benches.Clear();
        seatSlots.Clear();
        npcSeatAssignments.Clear();
        reservedNpcIds.Clear();
    }

    private void OnValidate()
    {
        maxSeats = Mathf.Max(1, maxSeats);
        mealFoodCost = Mathf.Max(1, mealFoodCost);

        if (string.IsNullOrWhiteSpace(resourceKeyName))
            resourceKeyName = "Essen";

        cachedEatSettings = GetComponent<FoodSiteEatSettings>();
        layoutDirty = true;
    }

    private void RegisterToRegistry()
    {
        if (SiteRegistry.Instance == null)
        {
            if (debugWarnings)
                Debug.LogWarning($"[FoodSite] No SiteRegistry.Instance yet for '{name}'.", this);
            return;
        }

        SiteRegistry.Instance.RegisterFoodSite(this);

        if (debugLogs)
            Debug.Log($"[FoodSite] Registered '{name}' to SiteRegistry.", this);
    }

    public void RegisterBench(CampfireBench bench)
    {
        if (bench == null)
            return;

        if (!benches.Contains(bench))
        {
            benches.Add(bench);
            layoutDirty = true;
        }
    }

    public void UnregisterBench(CampfireBench bench)
    {
        if (bench == null)
            return;

        if (benches.Remove(bench))
        {
            layoutDirty = true;

            List<int> invalidNpcIds = new List<int>();

            foreach (var pair in npcSeatAssignments)
            {
                if (pair.Value != null && pair.Value.bench == bench)
                    invalidNpcIds.Add(pair.Key);
            }

            for (int i = 0; i < invalidNpcIds.Count; i++)
            {
                int npcId = invalidNpcIds[i];
                npcSeatAssignments.Remove(npcId);
                reservedNpcIds.Remove(npcId);
            }
        }
    }

    public void NotifyBenchChanged(CampfireBench bench)
    {
        if (bench == null)
            return;

        layoutDirty = true;
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

        EnsureSeatLayout();

        if (IsReservedBy(npc))
            return true;

        return FindFreeSeat() != null;
    }

    public bool TryReserve(GameObject npc)
    {
        if (npc == null)
            return false;

        EnsureSeatLayout();

        int npcId = npc.GetInstanceID();

        if (npcSeatAssignments.ContainsKey(npcId))
        {
            reservedNpcIds.Add(npcId);
            return true;
        }

        SeatSlot freeSeat = FindFreeSeat();
        if (freeSeat == null)
            return false;

        npcSeatAssignments[npcId] = freeSeat;
        reservedNpcIds.Add(npcId);

        if (debugLogs)
            Debug.Log($"[FoodSite] '{name}' assigned seat '{freeSeat.seatTransform.name}' to '{npc.name}'.", this);

        return true;
    }

    public bool EnsureSeatAssignment(GameObject npc)
    {
        if (npc == null)
            return false;

        EnsureSeatLayout();

        int npcId = npc.GetInstanceID();

        if (npcSeatAssignments.TryGetValue(npcId, out SeatSlot slot))
            return slot != null && slot.seatTransform != null && slot.standTransform != null;

        return TryReserve(npc);
    }

    public Transform GetAssignedSeatPoint(GameObject npc)
    {
        if (npc == null)
            return ServicePoint;

        EnsureSeatLayout();

        int npcId = npc.GetInstanceID();

        if (npcSeatAssignments.TryGetValue(npcId, out SeatSlot slot) && slot != null && slot.seatTransform != null)
            return slot.seatTransform;

        return ServicePoint;
    }

    public Transform GetAssignedStandPoint(GameObject npc)
    {
        if (npc == null)
            return ServicePoint;

        EnsureSeatLayout();

        int npcId = npc.GetInstanceID();

        if (npcSeatAssignments.TryGetValue(npcId, out SeatSlot slot) && slot != null && slot.standTransform != null)
            return slot.standTransform;

        return ServicePoint;
    }

    public void Release(GameObject npc)
    {
        if (npc == null)
            return;

        int npcId = npc.GetInstanceID();

        npcSeatAssignments.Remove(npcId);
        reservedNpcIds.Remove(npcId);
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

        return CanAffordFoodInternal(resourceManager, MealFoodCost, resourceKeyName);
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

        bool success = TryConsumeFoodInternal(resourceManager, MealFoodCost, resourceKeyName);
        consumedStoredFood = success;
        return success;
    }

    private void EnsureSeatLayout()
    {
        if (!layoutDirty)
            return;

        layoutDirty = false;
        RebuildSeatLayout();
    }

    private void RebuildSeatLayout()
    {
        seatSlots.Clear();

        for (int i = benches.Count - 1; i >= 0; i--)
        {
            if (benches[i] == null)
                benches.RemoveAt(i);
        }

        int count = 0;

        for (int i = 0; i < benches.Count; i++)
        {
            CampfireBench bench = benches[i];
            if (bench == null || !bench.isActiveAndEnabled)
                continue;

            for (int seatIndex = 0; seatIndex < bench.SeatCount; seatIndex++)
            {
                if (count >= maxSeats)
                    break;

                if (!bench.HasSeatPair(seatIndex))
                    continue;

                Transform stand = bench.GetStandTransform(seatIndex);
                Transform seat = bench.GetSeatTransform(seatIndex);

                if (stand == null || seat == null)
                    continue;

                seatSlots.Add(new SeatSlot
                {
                    bench = bench,
                    benchSeatIndex = seatIndex,
                    standTransform = stand,
                    seatTransform = seat
                });

                count++;
            }

            if (count >= maxSeats)
                break;
        }

        List<int> invalidNpcIds = new List<int>();

        foreach (var pair in npcSeatAssignments)
        {
            if (pair.Value == null || pair.Value.seatTransform == null || pair.Value.standTransform == null || !seatSlots.Contains(pair.Value))
                invalidNpcIds.Add(pair.Key);
        }

        for (int i = 0; i < invalidNpcIds.Count; i++)
        {
            int npcId = invalidNpcIds[i];
            npcSeatAssignments.Remove(npcId);
            reservedNpcIds.Remove(npcId);
        }
    }

    private SeatSlot FindFreeSeat()
    {
        for (int i = 0; i < seatSlots.Count; i++)
        {
            SeatSlot slot = seatSlots[i];
            if (slot == null || slot.seatTransform == null || slot.standTransform == null)
                continue;

            bool used = false;

            foreach (var pair in npcSeatAssignments)
            {
                if (pair.Value == slot)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
                return slot;
        }

        return null;
    }

    private static bool CanAffordFoodInternal(ResourceManager manager, int amount, string resourceKeyName)
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

            object[] args = BuildFoodArguments(parameters[0].ParameterType, parameters[1].ParameterType, amount, resourceKeyName);
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

    private static bool TryConsumeFoodInternal(ResourceManager manager, int amount, string resourceKeyName)
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

            object[] args = BuildFoodArguments(parameters[0].ParameterType, parameters[1].ParameterType, amount, resourceKeyName);
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

    private static object[] BuildFoodArguments(Type resourceParamType, Type amountParamType, int amount, string resourceKeyName)
    {
        object resourceArg;

        if (resourceParamType == typeof(string))
        {
            resourceArg = resourceKeyName;
        }
        else if (resourceParamType.IsEnum)
        {
            try
            {
                resourceArg = Enum.Parse(resourceParamType, resourceKeyName);
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
