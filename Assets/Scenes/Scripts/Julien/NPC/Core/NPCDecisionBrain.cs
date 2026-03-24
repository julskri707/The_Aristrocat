using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCDecisionBrain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NPCNeeds needs;
    [SerializeField] private NPCTimeSystem timeSystem;
    [SerializeField] private NPCEventContext eventContext;
    [SerializeField] private WorkerAssignment workerAssignment;
    [SerializeField] private NPCNavMeshMovementController navMeshMovement;

    [Header("Auto Site Assignment")]
    [SerializeField] private bool autoAssignNearbySites = true;
    [SerializeField, Min(1)] private int siteRefreshIntervalTicks = 20;

    [Header("Decision")]
    [SerializeField, Min(1)] private int decisionIntervalTicks = 2;
    [SerializeField] private bool evaluateOnFirstTick = true;
    [SerializeField, Min(0f)] private float minScoreAdvantageToSwitch = 8f;

    [Header("Sleep Schedule")]
    [SerializeField] private float nightStartHour = 22f;
    [SerializeField] private float nightEndHour = 6f;
    [SerializeField] private float workStartHour = 8f;
    [SerializeField] private float workEndHour = 17f;
    [SerializeField] private float eveningStartHour = 18f;
    [SerializeField] private float eveningEndHour = 22f;
    [SerializeField] private float forcedNightSleepHours = 8f;
    [SerializeField] private float emergencyDaySleepEnergyThreshold = 18f;

    [Header("Debug")]
    [SerializeField] private bool debugDecisionLogs = false;
    [SerializeField] private bool debugWarnings = true;

    [Header("Runtime Targets")]
    [SerializeField] private Transform currentTarget;
    [SerializeField] private Transform bedPoint;
    [SerializeField] private Transform foodPoint;
    [SerializeField] private Transform socialPoint;
    [SerializeField] private Transform workPoint;

    [Header("Runtime Sites")]
    [SerializeField] private HomeSite homeSite;
    [SerializeField] private FoodSite foodSite;
    [SerializeField] private LeisureSite leisureSite;
    [SerializeField] private JobSite jobSite;

    [Header("Runtime")]
    [SerializeField] private NPCActionType currentActionType = NPCActionType.None;
    [SerializeField] private int currentActionStartTick = int.MinValue;
    [SerializeField] private int forcedSleepUntilTick = int.MinValue;

    private readonly List<NPCAction> actions = new List<NPCAction>(8);
    private NPCAction currentAction;
    private int lastDecisionTick = int.MinValue;
    private int lastSiteRefreshTick = int.MinValue;

    public NPCNeeds Needs => needs;
    public NPCTimeSystem TimeSystem => timeSystem;
    public NPCEventContext EventContext => eventContext;
    public WorkerAssignment WorkerAssignment => workerAssignment;
    public NPCNavMeshMovementController NavMeshMovement => navMeshMovement;

    public Transform CurrentTarget => currentTarget;
    public Transform BedPoint => bedPoint;
    public Transform FoodPoint => foodPoint;
    public Transform SocialPoint => socialPoint;
    public Transform WorkPoint => workPoint;

    public HomeSite CurrentHomeSite => homeSite;
    public FoodSite CurrentFoodSite => foodSite;
    public LeisureSite CurrentLeisureSite => leisureSite;
    public JobSite CurrentJobSite => jobSite;

    public NPCActionType CurrentActionType => currentActionType;
    public bool HasJobAssigned => workerAssignment != null && workerAssignment.assignedField != null;

    private void Awake()
    {
        if (needs == null) needs = GetComponent<NPCNeeds>();
        if (timeSystem == null) timeSystem = NPCTimeSystem.Instance;
        if (eventContext == null) eventContext = GetComponent<NPCEventContext>();
        if (workerAssignment == null) workerAssignment = GetComponent<WorkerAssignment>();
        if (navMeshMovement == null) navMeshMovement = GetComponent<NPCNavMeshMovementController>();

        if (needs == null && debugWarnings)
            Debug.LogWarning($"[NPCDecisionBrain] Missing NPCNeeds on {name}.", this);

        BuildActions();
    }

    private void Start()
    {
        ForceRefreshSites();
        UpdateWorkTargetFromAssignment();
    }

    private void OnEnable()
    {
        if (timeSystem == null)
            timeSystem = NPCTimeSystem.Instance;

        if (timeSystem != null)
            timeSystem.RegisterBrain(this);
        else if (debugWarnings)
            Debug.LogWarning($"[NPCDecisionBrain] Missing NPCTimeSystem on {name}.", this);
    }

    private void OnDisable()
    {
        if (timeSystem != null)
            timeSystem.UnregisterBrain(this);

        SiteRegistry.Instance?.ReleaseReservations(gameObject);
    }

    private void OnDestroy()
    {
        SiteRegistry.Instance?.ReleaseReservations(gameObject);
    }

    private void BuildActions()
    {
        actions.Clear();
        actions.Add(new NPCPanicAction());
        actions.Add(new NPCEatAction());
        actions.Add(new NPCHomeAction());
        actions.Add(new NPCSocialAction());
        actions.Add(new NPCWorkAction());
        actions.Add(new IdleAction());
    }

    public void OnNPCTick(int tickIndex, float timeOfDay, bool dangerActive, bool coldActive)
    {
        if (needs == null)
            return;

        bool needSiteRefresh =
            autoAssignNearbySites &&
            (lastSiteRefreshTick == int.MinValue ||
             tickIndex - lastSiteRefreshTick >= siteRefreshIntervalTicks ||
             homeSite == null ||
             foodSite == null ||
             leisureSite == null);

        if (needSiteRefresh)
        {
            RefreshPreferredSites();
            lastSiteRefreshTick = tickIndex;
        }

        UpdateWorkTargetFromAssignment();
        needs.TickNeeds(dangerActive, coldActive);

        if (currentActionType == NPCActionType.Sleep && IsNightSleepLocked(tickIndex) && !IsEmergencyState())
        {
            currentAction?.OnTick(this, tickIndex, timeOfDay);
            return;
        }

        bool shouldEvaluate = false;

        if (evaluateOnFirstTick && lastDecisionTick == int.MinValue)
            shouldEvaluate = true;

        if (!shouldEvaluate && tickIndex - lastDecisionTick >= decisionIntervalTicks)
            shouldEvaluate = true;

        if (!shouldEvaluate && currentAction == null)
            shouldEvaluate = true;

        if (!shouldEvaluate && HasCriticalNeed())
            shouldEvaluate = true;

        if (shouldEvaluate)
        {
            EvaluateAndSwitchAction(tickIndex, timeOfDay);
            lastDecisionTick = tickIndex;
        }

        currentAction?.OnTick(this, tickIndex, timeOfDay);
    }

    [ContextMenu("Force Refresh Sites")]
    public void ForceRefreshSites()
    {
        if (!autoAssignNearbySites)
            return;

        if (SiteRegistry.Instance == null)
            return;

        RefreshPreferredSites();
    }

    private void RefreshPreferredSites()
    {
        SiteRegistry registry = SiteRegistry.Instance;
        if (registry == null)
        {
            if (debugWarnings)
                Debug.LogWarning($"[NPCDecisionBrain] Missing SiteRegistry on {name}.", this);
            return;
        }

        if (homeSite == null || !homeSite.IsReservedBy(gameObject))
        {
            if (registry.TryClaimNearestHomeSite(transform.position, gameObject, out HomeSite nearestHome))
            {
                homeSite = nearestHome;
                bedPoint = nearestHome.BedPoint;
            }
        }

        if (foodSite == null || !foodSite.IsReservedBy(gameObject))
        {
            if (registry.TryClaimNearestFoodSite(transform.position, gameObject, out FoodSite nearestFood))
            {
                foodSite = nearestFood;
                foodPoint = nearestFood.ServicePoint;
            }
        }

        if (leisureSite == null || !leisureSite.IsReservedBy(gameObject))
        {
            if (registry.TryClaimNearestLeisureSite(transform.position, gameObject, out LeisureSite nearestLeisure))
            {
                leisureSite = nearestLeisure;
                socialPoint = nearestLeisure.InteractionPoint;
            }
        }
    }

    private void UpdateWorkTargetFromAssignment()
    {
        if (workerAssignment == null || workerAssignment.assignedField == null)
        {
            jobSite = null;
            workPoint = null;
            return;
        }

        ResourceTickBehaviour assignedField = workerAssignment.assignedField;

        if (jobSite == null || jobSite.ResourceBehaviour != assignedField)
        {
            SiteRegistry registry = SiteRegistry.Instance;
            jobSite = registry != null ? registry.FindJobSiteByResourceBehaviour(assignedField) : null;
        }

        workPoint = jobSite != null ? jobSite.WorkPoint : assignedField.transform;
    }

    private void EvaluateAndSwitchAction(int tickIndex, float timeOfDay)
    {
        NPCAction bestAction = null;
        float bestScore = float.MinValue;

        NPCAction currentScoredAction = null;
        float currentScore = float.MinValue;

        NPCAction secondAction = null;
        float secondScore = float.MinValue;

        NPCAction thirdAction = null;
        float thirdScore = float.MinValue;

        for (int i = 0; i < actions.Count; i++)
        {
            NPCAction action = actions[i];
            if (action == null || !action.CanRun(this))
                continue;

            float score = Mathf.Max(0f, action.CalculateUtility(this, timeOfDay));

            if (action == currentAction)
                score += action.ContinueBonus;

            if (action == currentAction)
            {
                currentScoredAction = action;
                currentScore = score;
            }

            if (score > bestScore)
            {
                thirdAction = secondAction;
                thirdScore = secondScore;

                secondAction = bestAction;
                secondScore = bestScore;

                bestAction = action;
                bestScore = score;
            }
            else if (score > secondScore)
            {
                thirdAction = secondAction;
                thirdScore = secondScore;

                secondAction = action;
                secondScore = score;
            }
            else if (score > thirdScore)
            {
                thirdAction = action;
                thirdScore = score;
            }
        }

        if (debugDecisionLogs)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("[NPCDecisionBrain] ").Append(name).Append(" -> ");
            sb.Append("#1 ").Append(bestAction != null ? bestAction.ActionType.ToString() : "NULL").Append("=").Append(bestScore.ToString("0.00"));
            sb.Append(" | #2 ").Append(secondAction != null ? secondAction.ActionType.ToString() : "NULL").Append("=").Append(secondScore.ToString("0.00"));
            sb.Append(" | #3 ").Append(thirdAction != null ? thirdAction.ActionType.ToString() : "NULL").Append("=").Append(thirdScore.ToString("0.00"));
            Debug.Log(sb.ToString(), this);
        }

        if (bestAction == null)
            return;

        if (currentAction != null && bestAction != currentAction)
        {
            bool emergencyInterrupt = bestAction.ActionType == NPCActionType.Panic && IsEmergencyState();
            bool minDurationReached = tickIndex - currentActionStartTick >= currentAction.MinDurationTicks;
            bool enoughScoreGain = bestScore >= currentScore + minScoreAdvantageToSwitch;

            if (!emergencyInterrupt)
            {
                if (!minDurationReached || !enoughScoreGain)
                {
                    bestAction = currentAction;
                    bestScore = currentScore;
                }
            }
        }

        if (currentAction == bestAction)
            return;

        currentAction?.OnExit(this);

        if (bestAction.ActionType != NPCActionType.Sleep)
            forcedSleepUntilTick = int.MinValue;

        currentAction = bestAction;
        currentActionType = currentAction.ActionType;
        currentActionStartTick = tickIndex;

        if (currentActionType == NPCActionType.Sleep && IsNightTime(timeOfDay))
        {
            StartNightSleepBlock(tickIndex);
        }

        currentAction.OnEnter(this);
    }

    private bool HasCriticalNeed()
    {
        return needs.IsCritical(NPCNeedType.Hunger)
               || needs.IsCritical(NPCNeedType.Energy)
               || needs.IsCritical(NPCNeedType.Warmth)
               || needs.IsCritical(NPCNeedType.Safety)
               || needs.IsCritical(NPCNeedType.Social);
    }

    public bool IsEmergencyState()
    {
        return (eventContext != null && eventContext.PanicRecommended)
               || (needs != null && needs.IsCritical(NPCNeedType.Safety));
    }

    public bool IsNightTime(float timeOfDay)
    {
        return timeOfDay >= nightStartHour || timeOfDay < nightEndHour;
    }

    public bool IsWorkTime(float timeOfDay)
    {
        return timeOfDay >= workStartHour && timeOfDay < workEndHour;
    }

    public bool IsEveningTime(float timeOfDay)
    {
        return timeOfDay >= eveningStartHour && timeOfDay < eveningEndHour;
    }

    public bool CanSleepNow(float timeOfDay)
    {
        return IsNightTime(timeOfDay) || needs.energy <= emergencyDaySleepEnergyThreshold;
    }

    public int GetTicksForHours(float hours)
    {
        float hoursPerTick = timeSystem != null ? timeSystem.HoursPerTick : 0.25f;
        hoursPerTick = Mathf.Max(0.01f, hoursPerTick);
        return Mathf.CeilToInt(hours / hoursPerTick);
    }

    public void StartNightSleepBlock(int tickIndex)
    {
        forcedSleepUntilTick = tickIndex + GetTicksForHours(forcedNightSleepHours);
    }

    public bool IsNightSleepLocked(int tickIndex)
    {
        return forcedSleepUntilTick != int.MinValue && tickIndex < forcedSleepUntilTick;
    }

    public bool IsAtTarget(Transform target, float distance = 0.6f)
    {
        if (target == null)
            return false;

        if (navMeshMovement != null)
            return navMeshMovement.ReachedTarget(target);

        Vector3 a = transform.position;
        Vector3 b = target.position;
        a.y = 0f;
        b.y = 0f;

        return (a - b).sqrMagnitude <= distance * distance;
    }

    public bool IsAtCurrentTarget(float distance = 0.6f)
    {
        return IsAtTarget(currentTarget, distance);
    }

    public void SetCurrentTarget(Transform target)
    {
        currentTarget = target;
    }

    public void SetBedPoint(Transform target)
    {
        bedPoint = target;
    }

    public void SetFoodPoint(Transform target)
    {
        foodPoint = target;
    }

    public void SetSocialPoint(Transform target)
    {
        socialPoint = target;
    }

    public void SetWorkPoint(Transform target)
    {
        workPoint = target;
    }
}

public class IdleAction : NPCAction
{
    public override NPCActionType ActionType => NPCActionType.Idle;
    public override int MinDurationTicks => 1;
    public override float ContinueBonus => 1f;

    public override float CalculateUtility(NPCDecisionBrain brain, float timeOfDay)
    {
        return 1f;
    }

    public override void OnEnter(NPCDecisionBrain brain)
    {
        brain.SetCurrentTarget(null);
    }
}