using UnityEngine;

[DisallowMultipleComponent]
public class WoodStorage : MonoBehaviour
{
    [Header("Storage")]
    [Min(1)]
    [SerializeField] private int maxCapacity = 300;
    [Min(0)]
    [SerializeField] private int currentWood = 0;

    [Header("Assignment")]
    [SerializeField] private ResourceTickBehaviour assignmentField;

    [Header("Optional Global Sync")]
    [SerializeField] private bool addToGlobalResourceManagerOnStore = false;
    [SerializeField] private ResourceManager resourceManager;

    [Header("Worker Snap")]
    [SerializeField] private Transform workerSnapPoint;
    [SerializeField] private Transform idleStandPoint;
    [SerializeField] private Transform lookAtPoint;
    [SerializeField] private Vector3 fallbackSnapLocalOffset = new Vector3(0f, 0f, -1.2f);
    [SerializeField] private Vector3 fallbackIdleLocalOffset = new Vector3(0f, 0f, -1.8f);

    [Header("Fill Models")]
    [SerializeField] private GameObject modelBelow25;
    [SerializeField] private GameObject model25;
    [SerializeField] private GameObject model50;
    [SerializeField] private GameObject model75;
    [SerializeField] private GameObject model100;

    [Header("Runtime Debug")]
    [SerializeField] private WoodcutterWorkArea ownerArea;

    public int MaxCapacity => maxCapacity;
    public int CurrentWood => currentWood;
    public WoodcutterWorkArea OwnerArea => ownerArea;

    private void Awake()
    {
        AutoAssignReferences();
        ClampValues();
        RefreshVisualModel();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        ClampValues();
        RefreshVisualModel();
    }

    public bool CanStore(int amount)
    {
        if (amount <= 0)
            return false;

        return currentWood + amount <= maxCapacity;
    }

    public int StoreWood(int amount)
    {
        if (amount <= 0)
            return 0;

        int accepted = Mathf.Min(amount, GetFreeCapacity());
        if (accepted <= 0)
            return 0;

        currentWood += accepted;

        if (addToGlobalResourceManagerOnStore)
        {
            if (resourceManager == null)
                resourceManager = ResourceManager.Instance;

            if (resourceManager != null)
                resourceManager.Add(ResourceManager.ResourceType.Holz, accepted);
        }

        RefreshVisualModel();
        return accepted;
    }

    public int TakeWood(int amount)
    {
        if (amount <= 0)
            return 0;

        int taken = Mathf.Min(amount, currentWood);
        currentWood -= taken;
        RefreshVisualModel();
        return taken;
    }

    public int GetFreeCapacity()
    {
        return Mathf.Max(0, maxCapacity - currentWood);
    }

    public int GetCurrentWood()
    {
        return currentWood;
    }

    public bool IsFull()
    {
        return currentWood >= maxCapacity;
    }

    public void SetCurrentWood(int amount)
    {
        currentWood = Mathf.Clamp(amount, 0, maxCapacity);
        RefreshVisualModel();
    }

    public void ClearStorage()
    {
        currentWood = 0;
        RefreshVisualModel();
    }

    public ResourceTickBehaviour GetAssignmentField()
    {
        return assignmentField;
    }

    public void SetOwnerArea(WoodcutterWorkArea area)
    {
        ownerArea = area;
    }

    public Vector3 GetWorkerSnapPosition(Vector3 fromPosition)
    {
        if (workerSnapPoint != null)
            return workerSnapPoint.position;

        return transform.TransformPoint(fallbackSnapLocalOffset);
    }

    public Vector3 GetIdleStandPosition(Vector3 fromPosition)
    {
        if (idleStandPoint != null)
            return idleStandPoint.position;

        if (workerSnapPoint != null)
            return workerSnapPoint.position;

        return transform.TransformPoint(fallbackIdleLocalOffset);
    }

    public Vector3 GetLookAtPosition()
    {
        if (lookAtPoint != null)
            return lookAtPoint.position;

        return transform.position;
    }

    public float GetFill01()
    {
        if (maxCapacity <= 0)
            return 0f;

        return Mathf.Clamp01((float)currentWood / maxCapacity);
    }

    private void RefreshVisualModel()
    {
        float fill = GetFill01();

        GameObject active = modelBelow25;

        if (fill >= 1f)
            active = model100 != null ? model100 : active;
        else if (fill >= 0.75f)
            active = model75 != null ? model75 : active;
        else if (fill >= 0.5f)
            active = model50 != null ? model50 : active;
        else if (fill >= 0.25f)
            active = model25 != null ? model25 : active;
        else
            active = modelBelow25 != null ? modelBelow25 : active;

        SetModelActive(modelBelow25, active == modelBelow25);
        SetModelActive(model25, active == model25);
        SetModelActive(model50, active == model50);
        SetModelActive(model75, active == model75);
        SetModelActive(model100, active == model100);
    }

    private void SetModelActive(GameObject go, bool state)
    {
        if (go != null)
            go.SetActive(state);
    }

    private void AutoAssignReferences()
    {
        if (assignmentField == null)
        {
            assignmentField = GetComponent<ResourceTickBehaviour>();
            if (assignmentField == null)
                assignmentField = GetComponentInParent<ResourceTickBehaviour>();
        }

        if (resourceManager == null)
            resourceManager = ResourceManager.Instance;
    }

    private void ClampValues()
    {
        maxCapacity = Mathf.Max(1, maxCapacity);
        currentWood = Mathf.Clamp(currentWood, 0, maxCapacity);
    }
}
