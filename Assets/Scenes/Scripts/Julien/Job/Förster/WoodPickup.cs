using UnityEngine;

[DisallowMultipleComponent]
public class WoodPickup : MonoBehaviour
{
    [Header("Pickup")]
    [Min(1)]
    [SerializeField] private int woodAmount = 1;

    [Header("References")]
    [SerializeField] private TreeResourceNode sourceTree;
    [SerializeField] private WoodcutterWorkArea ownerArea;

    [Header("Reservation Debug")]
    [SerializeField] private bool isReserved = false;
    [SerializeField] private string reservedByDebugName = "";
    [SerializeField] private bool isCollected = false;

    private object reservedBy;

    public int WoodAmount => woodAmount;
    public TreeResourceNode SourceTree => sourceTree;
    public WoodcutterWorkArea OwnerArea => ownerArea;
    public bool IsReserved => isReserved;
    public bool IsCollected => isCollected;

    private void OnEnable()
    {
        if (ownerArea != null)
            ownerArea.RegisterPickup(this);
    }

    private void OnDisable()
    {
        if (ownerArea != null)
            ownerArea.UnregisterPickup(this);
    }

    public void Initialize(TreeResourceNode tree, int amount, WoodcutterWorkArea area)
    {
        sourceTree = tree;
        woodAmount = Mathf.Max(1, amount);
        ownerArea = area;
        reservedBy = null;
        isReserved = false;
        reservedByDebugName = string.Empty;
        isCollected = false;

        if (ownerArea != null)
            ownerArea.RegisterPickup(this);
    }

    public bool CanBeAssigned()
    {
        if (isCollected)
            return false;

        if (isReserved)
            return false;

        return true;
    }

    public bool CanBeAssigned(object worker)
    {
        if (isCollected)
            return false;

        if (!isReserved)
            return true;

        return worker != null && ReferenceEquals(reservedBy, worker);
    }

    public bool Reserve(object worker)
    {
        if (worker == null)
            return false;

        if (!CanBeAssigned(worker))
            return false;

        if (ReferenceEquals(reservedBy, worker))
            return true;

        reservedBy = worker;
        isReserved = true;
        reservedByDebugName = worker.ToString();
        return true;
    }

    public bool Release(object worker)
    {
        if (!isReserved)
            return true;

        if (worker == null)
            return false;

        if (!ReferenceEquals(reservedBy, worker))
            return false;

        reservedBy = null;
        isReserved = false;
        reservedByDebugName = string.Empty;
        return true;
    }

    public bool TryCollect(object worker, out int amount)
    {
        amount = 0;

        if (worker == null)
            return false;

        if (!CanBeAssigned(worker))
            return false;

        reservedBy = worker;
        isReserved = true;
        reservedByDebugName = worker.ToString();

        amount = woodAmount;
        isCollected = true;

        if (ownerArea != null)
            ownerArea.UnregisterPickup(this);

        Destroy(gameObject);
        return true;
    }

    public void SetOwnerArea(WoodcutterWorkArea area)
    {
        if (ownerArea == area)
            return;

        if (ownerArea != null)
            ownerArea.UnregisterPickup(this);

        ownerArea = area;

        if (ownerArea != null && isActiveAndEnabled)
            ownerArea.RegisterPickup(this);
    }

    private void OnValidate()
    {
        woodAmount = Mathf.Max(1, woodAmount);
    }
}
