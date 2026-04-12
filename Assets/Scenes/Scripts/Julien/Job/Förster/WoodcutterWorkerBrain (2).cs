using UnityEngine;

[DisallowMultipleComponent]
public class WoodcutterWorkerBrain : MonoBehaviour
{
    public enum WorkerState
    {
        Idle,
        MovingToTree,
        Chopping,
        MovingToPickup,
        PickingUp,
        MovingToStorage,
        Depositing
    }

    [Header("References")]
    [SerializeField] private WorkerAssignment workerAssignment;
    [SerializeField] private NPCDecisionBrain decisionBrain;
    [SerializeField] private Animator animator;
    [SerializeField] private GameObject axeInHandObject;

    [Header("Decision Integration")]
    [SerializeField] private bool obeyDecisionBrain = true;
    [SerializeField] private bool releaseTargetsWhenNotWorking = true;

    [Header("Animator")]
    [SerializeField] private bool useAnimator = true;
    [SerializeField] private string isMovingParameter = "IsMoving";
    [SerializeField] private string isChoppingParameter = "IsChopping";
    [SerializeField] private bool setCarriedWoodInt = false;
    [SerializeField] private string carriedWoodParameter = "CarriedWood";

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float stopDistance = 0.2f;
    [SerializeField] private float rotationSpeed = 8f;
    [SerializeField] private bool hardSnapToTreePoint = true;

    [Header("Work")]
    [SerializeField] private int carryCapacity = 3;
    [SerializeField] private int chopDamage = 1;
    [SerializeField] private float chopInterval = 0.8f;
    [SerializeField] private float pickupInterval = 0.2f;
    [SerializeField] private float depositInterval = 0.35f;

    [Header("Pickup Radius")]
    [SerializeField] private bool usePickupCollectRadius = true;
    [SerializeField] private float pickupCollectRadius = 1.5f;
    [SerializeField] private bool facePickupWhileCollecting = true;

    [Header("Runtime Debug")]
    [SerializeField] private WorkerState currentState = WorkerState.Idle;
    [SerializeField] private int carriedWood = 0;
    [SerializeField] private WoodcutterWorkArea currentArea;
    [SerializeField] private TreeResourceNode currentTree;
    [SerializeField] private WoodPickup currentPickup;
    [SerializeField] private WoodStorage currentStorage;
    [SerializeField] private Vector3 currentTreeSnapPosition;
    [SerializeField] private bool pausedByDecision = false;

    private ResourceTickBehaviour lastAssignedField;
    private float actionTimer = 0f;

    private int isMovingHash;
    private int isChoppingHash;
    private int carriedWoodHash;
    private bool animatorHashesReady = false;
    private bool lastMovingValue = false;
    private bool lastChoppingValue = false;
    private int lastCarriedWoodValue = int.MinValue;
    private bool animatorInitialized = false;

    public WorkerState CurrentState => currentState;
    public int CarriedWood => carriedWood;

    private void Awake()
    {
        if (workerAssignment == null)
            workerAssignment = GetComponent<WorkerAssignment>();

        if (decisionBrain == null)
            decisionBrain = GetComponent<NPCDecisionBrain>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        CacheAnimatorHashes();
        UpdateVisualWorkState(true);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        carryCapacity = Mathf.Max(1, carryCapacity);
        chopDamage = Mathf.Max(1, chopDamage);
        chopInterval = Mathf.Max(0.01f, chopInterval);
        pickupInterval = Mathf.Max(0.01f, pickupInterval);
        depositInterval = Mathf.Max(0.01f, depositInterval);
        pickupCollectRadius = Mathf.Max(0.05f, pickupCollectRadius);
        CacheAnimatorHashes();
    }

    private void Update()
    {
        RefreshAssignedAreaIfNeeded();

        if (currentArea == null)
        {
            pausedByDecision = false;
            SetState(WorkerState.Idle);
            UpdateVisualWorkState(false);
            return;
        }

        if (!CanWorkRightNow())
        {
            PauseWorkBehaviour();
            UpdateVisualWorkState(false);
            return;
        }

        pausedByDecision = false;
        actionTimer -= Time.deltaTime;

        switch (currentState)
        {
            case WorkerState.Idle:
                TickIdle();
                break;

            case WorkerState.MovingToTree:
                TickMoveToTree();
                break;

            case WorkerState.Chopping:
                TickChopping();
                break;

            case WorkerState.MovingToPickup:
                TickMoveToPickup();
                break;

            case WorkerState.PickingUp:
                TickPickingUp();
                break;

            case WorkerState.MovingToStorage:
                TickMoveToStorage();
                break;

            case WorkerState.Depositing:
                TickDepositing();
                break;
        }

        UpdateVisualWorkState(false);
    }

    public void ShowAssignedWorkArea()
    {
    }

    private bool CanWorkRightNow()
    {
        if (!obeyDecisionBrain)
            return true;

        if (decisionBrain == null)
            return true;

        return decisionBrain.CurrentActionType == NPCActionType.Work;
    }

    private void PauseWorkBehaviour()
    {
        pausedByDecision = true;
        actionTimer = 0f;

        if (releaseTargetsWhenNotWorking)
            ReleaseTargets();

        SetState(WorkerState.Idle);
    }

    private void RefreshAssignedAreaIfNeeded()
    {
        ResourceTickBehaviour assignedField = workerAssignment != null ? workerAssignment.assignedField : null;
        if (assignedField == lastAssignedField)
            return;

        ReleaseTargets();

        lastAssignedField = assignedField;
        currentArea = null;
        currentStorage = null;

        if (assignedField == null)
            return;

        currentArea = assignedField.GetComponent<WoodcutterWorkArea>();
        if (currentArea == null)
            currentArea = assignedField.GetComponentInParent<WoodcutterWorkArea>();
        if (currentArea == null)
            currentArea = assignedField.GetComponentInChildren<WoodcutterWorkArea>();

        if (currentArea == null)
        {
            currentStorage = assignedField.GetComponent<WoodStorage>();
            if (currentStorage == null)
                currentStorage = assignedField.GetComponentInParent<WoodStorage>();
            if (currentStorage == null)
                currentStorage = assignedField.GetComponentInChildren<WoodStorage>();

            if (currentStorage != null)
                currentArea = currentStorage.OwnerArea;
        }
        else
        {
            currentStorage = currentArea.GetStorage();
        }
    }

    private void TickIdle()
    {
        if (carriedWood > 0)
        {
            if (TryFindStorage())
            {
                SetState(WorkerState.MovingToStorage);
                return;
            }
        }

        if (carriedWood >= carryCapacity)
        {
            SetState(WorkerState.Idle);
            return;
        }

        if (TryFindPickupFromCurrentTree())
            return;

        if (TryFindAnyPickup())
            return;

        if (TryFindTree())
            return;
    }

    private void TickMoveToTree()
    {
        if (currentTree == null)
        {
            SetState(WorkerState.Idle);
            return;
        }

        currentTreeSnapPosition = currentTree.GetWorkerSnapPosition(transform.position);
        MoveTowards(currentTreeSnapPosition);

        if (IsAt(currentTreeSnapPosition))
        {
            if (hardSnapToTreePoint)
                SnapToXZ(currentTreeSnapPosition);

            FaceTowards(currentTree.GetWorkerLookAtPosition());
            actionTimer = 0f;
            SetState(WorkerState.Chopping);
        }
    }

    private void TickChopping()
    {
        if (currentTree == null)
        {
            SetState(WorkerState.Idle);
            return;
        }

        currentTreeSnapPosition = currentTree.GetWorkerSnapPosition(transform.position);

        if (hardSnapToTreePoint)
            SnapToXZ(currentTreeSnapPosition);

        FaceTowards(currentTree.GetWorkerLookAtPosition());

        if (currentTree.IsFelled)
        {
            if (TryFindPickupFromCurrentTree())
                return;

            if (TryFindAnyPickup())
                return;

            if (carriedWood > 0 && TryFindStorage())
            {
                SetState(WorkerState.MovingToStorage);
                return;
            }

            SetState(WorkerState.Idle);
            return;
        }

        if (actionTimer > 0f)
            return;

        currentTree.ApplyChopDamage(chopDamage);
        actionTimer = chopInterval;
    }

    private void TickMoveToPickup()
    {
        if (currentPickup == null)
        {
            DecideNextAfterMissingPickup();
            return;
        }

        if (carriedWood >= carryCapacity)
        {
            ReleaseCurrentPickup();
            if (TryFindStorage())
                SetState(WorkerState.MovingToStorage);
            else
                SetState(WorkerState.Idle);
            return;
        }

        Vector3 pickupPosition = GetPickupApproachPosition(currentPickup);

        if (IsInPickupRange(currentPickup))
        {
            if (facePickupWhileCollecting)
                FaceTowards(currentPickup.transform.position);

            actionTimer = 0f;
            SetState(WorkerState.PickingUp);
            return;
        }

        MoveTowards(pickupPosition);

        if (IsAt(pickupPosition) || IsInPickupRange(currentPickup))
        {
            if (facePickupWhileCollecting)
                FaceTowards(currentPickup.transform.position);

            actionTimer = 0f;
            SetState(WorkerState.PickingUp);
        }
    }

    private void TickPickingUp()
    {
        if (currentPickup == null)
        {
            DecideNextAfterMissingPickup();
            return;
        }

        if (facePickupWhileCollecting)
            FaceTowards(currentPickup.transform.position);

        if (actionTimer > 0f)
            return;

        if (!usePickupCollectRadius || IsInPickupRange(currentPickup))
        {
            if (!currentPickup.TryCollect(this, out int amount))
            {
                DecideNextAfterMissingPickup();
                return;
            }

            carriedWood += amount;
            carriedWood = Mathf.Clamp(carriedWood, 0, carryCapacity);
            currentPickup = null;

            if (carriedWood >= carryCapacity)
            {
                if (TryFindStorage())
                    SetState(WorkerState.MovingToStorage);
                else
                    SetState(WorkerState.Idle);
                return;
            }

            if (TryFindPickupFromCurrentTree())
                return;

            if (TryFindAnyPickup())
                return;

            if (carriedWood > 0 && TryFindStorage())
            {
                SetState(WorkerState.MovingToStorage);
                return;
            }

            if (TryFindTree())
                return;

            actionTimer = pickupInterval;
            SetState(WorkerState.Idle);
            return;
        }

        SetState(WorkerState.MovingToPickup);
    }

    private void TickMoveToStorage()
    {
        if (currentStorage == null)
        {
            SetState(WorkerState.Idle);
            return;
        }

        if (carriedWood <= 0)
        {
            SetState(WorkerState.Idle);
            return;
        }

        MoveTowards(currentStorage.transform.position);

        if (IsAt(currentStorage.transform.position))
        {
            actionTimer = 0f;
            SetState(WorkerState.Depositing);
        }
    }

    private void TickDepositing()
    {
        if (currentStorage == null)
        {
            SetState(WorkerState.Idle);
            return;
        }

        if (carriedWood <= 0)
        {
            SetState(WorkerState.Idle);
            return;
        }

        if (actionTimer > 0f)
            return;

        int stored = currentStorage.StoreWood(carriedWood);
        carriedWood -= stored;
        actionTimer = depositInterval;

        if (carriedWood > 0)
        {
            if (TryFindStorage())
                SetState(WorkerState.MovingToStorage);
            else
                SetState(WorkerState.Idle);
            return;
        }

        if (TryFindPickupFromCurrentTree())
            return;

        if (TryFindAnyPickup())
            return;

        if (TryFindTree())
            return;

        SetState(WorkerState.Idle);
    }

    private bool TryFindTree()
    {
        if (currentArea == null)
            return false;

        ReleaseCurrentTree();

        TreeResourceNode tree = currentArea.GetAvailableTree(this, transform.position);
        if (tree == null)
            return false;

        if (!tree.Reserve(this))
            return false;

        currentTree = tree;
        currentTreeSnapPosition = currentTree.GetWorkerSnapPosition(transform.position);
        SetState(WorkerState.MovingToTree);
        return true;
    }

    private bool TryFindPickupFromCurrentTree()
    {
        if (currentArea == null || currentTree == null)
            return false;

        ReleaseCurrentPickup();

        WoodPickup pickup = currentArea.GetAvailablePickup(this, transform.position, currentTree);
        if (pickup == null)
            return false;

        if (!pickup.Reserve(this))
            return false;

        currentPickup = pickup;
        SetState(WorkerState.MovingToPickup);
        return true;
    }

    private bool TryFindAnyPickup()
    {
        if (currentArea == null)
            return false;

        ReleaseCurrentPickup();

        WoodPickup pickup = currentArea.GetAvailablePickup(this, transform.position, null);
        if (pickup == null)
            return false;

        if (!pickup.Reserve(this))
            return false;

        currentPickup = pickup;
        SetState(WorkerState.MovingToPickup);
        return true;
    }

    private bool TryFindStorage()
    {
        if (currentArea == null)
            return false;

        currentStorage = currentArea.GetStorage();
        if (currentStorage == null)
            return false;

        if (currentStorage.GetFreeCapacity() <= 0)
            return false;

        return true;
    }

    private void DecideNextAfterMissingPickup()
    {
        ReleaseCurrentPickup();

        if (carriedWood > 0 && TryFindStorage())
        {
            SetState(WorkerState.MovingToStorage);
            return;
        }

        if (TryFindPickupFromCurrentTree())
            return;

        if (TryFindAnyPickup())
            return;

        if (TryFindTree())
            return;

        SetState(WorkerState.Idle);
    }

    private Vector3 GetPickupApproachPosition(WoodPickup pickup)
    {
        if (pickup == null)
            return transform.position;

        if (!usePickupCollectRadius)
            return pickup.transform.position;

        Vector3 from = transform.position;
        Vector3 target = pickup.transform.position;
        Vector3 flatDir = target - from;
        flatDir.y = 0f;

        if (flatDir.sqrMagnitude <= 0.0001f)
            return target;

        flatDir.Normalize();
        return target - flatDir * Mathf.Max(stopDistance, pickupCollectRadius * 0.8f);
    }

    private bool IsInPickupRange(WoodPickup pickup)
    {
        if (pickup == null)
            return false;

        float allowedRange = usePickupCollectRadius ? Mathf.Max(stopDistance, pickupCollectRadius) : stopDistance;

        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(pickup.transform.position.x, 0f, pickup.transform.position.z);
        return Vector3.Distance(a, b) <= allowedRange;
    }

    private void MoveTowards(Vector3 target)
    {
        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        Vector3 delta = flatTarget - transform.position;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
            return;

        Vector3 direction = delta / distance;
        transform.position += direction * moveSpeed * Time.deltaTime;

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        Vector3 delta = flatTarget - transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude <= 0.0001f)
            return;

        Quaternion lookRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, rotationSpeed * Time.deltaTime);
    }

    private void SnapToXZ(Vector3 target)
    {
        transform.position = new Vector3(target.x, transform.position.y, target.z);
    }

    private bool IsAt(Vector3 target)
    {
        Vector3 a = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 b = new Vector3(target.x, 0f, target.z);
        return Vector3.Distance(a, b) <= stopDistance;
    }

    private void ReleaseCurrentTree()
    {
        if (currentTree != null)
        {
            currentTree.Release(this);
            currentTree = null;
        }
    }

    private void ReleaseCurrentPickup()
    {
        if (currentPickup != null)
        {
            currentPickup.Release(this);
            currentPickup = null;
        }
    }

    private void ReleaseTargets()
    {
        ReleaseCurrentTree();
        ReleaseCurrentPickup();
        currentStorage = null;
    }

    private void SetState(WorkerState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;
        UpdateVisualWorkState(false);
    }

    private void UpdateVisualWorkState(bool force)
    {
        bool isMoving = !pausedByDecision &&
                        (currentState == WorkerState.MovingToTree ||
                         currentState == WorkerState.MovingToPickup ||
                         currentState == WorkerState.MovingToStorage);

        bool isChopping = !pausedByDecision && currentState == WorkerState.Chopping;

        if (axeInHandObject != null)
            axeInHandObject.SetActive(isChopping);

        if (!useAnimator || animator == null || !animatorHashesReady)
            return;

        if (force || !animatorInitialized || lastMovingValue != isMoving)
        {
            animator.SetBool(isMovingHash, isMoving);
            lastMovingValue = isMoving;
        }

        if (force || !animatorInitialized || lastChoppingValue != isChopping)
        {
            animator.SetBool(isChoppingHash, isChopping);
            lastChoppingValue = isChopping;
        }

        if (setCarriedWoodInt && (force || !animatorInitialized || lastCarriedWoodValue != carriedWood))
        {
            animator.SetInteger(carriedWoodHash, carriedWood);
            lastCarriedWoodValue = carriedWood;
        }

        animatorInitialized = true;
    }

    private void CacheAnimatorHashes()
    {
        isMovingHash = Animator.StringToHash(string.IsNullOrWhiteSpace(isMovingParameter) ? "IsMoving" : isMovingParameter);
        isChoppingHash = Animator.StringToHash(string.IsNullOrWhiteSpace(isChoppingParameter) ? "IsChopping" : isChoppingParameter);
        carriedWoodHash = Animator.StringToHash(string.IsNullOrWhiteSpace(carriedWoodParameter) ? "CarriedWood" : carriedWoodParameter);
        animatorHashesReady = true;
    }

    private void OnDisable()
    {
        ReleaseTargets();
        if (axeInHandObject != null)
            axeInHandObject.SetActive(false);
        UpdateVisualWorkState(true);
    }

    private void OnDestroy()
    {
        ReleaseTargets();
    }
}
