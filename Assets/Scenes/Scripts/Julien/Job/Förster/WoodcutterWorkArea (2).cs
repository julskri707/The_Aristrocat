using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WoodcutterWorkArea : MonoBehaviour
{
    [Header("Storage Center")]
    [SerializeField] private WoodStorage storage;

    [Header("Tree Search")]
    [Min(0.5f)]
    [SerializeField] private float treeSearchRadius = 25f;
    [SerializeField] private bool autoScanTreesOnStart = true;

    [Header("Runtime Debug")]
    [SerializeField] private List<TreeResourceNode> trees = new List<TreeResourceNode>();
    [SerializeField] private List<WoodPickup> activePickups = new List<WoodPickup>();
    [SerializeField] private List<WorkerAssignment> assignedWorkers = new List<WorkerAssignment>();

    public WoodStorage Storage => storage;
    public IReadOnlyList<TreeResourceNode> Trees => trees;
    public IReadOnlyList<WoodPickup> ActivePickups => activePickups;
    public float TreeSearchRadius => treeSearchRadius;

    private void Awake()
    {
        AutoAssignReferences();
        BindStorage();
    }

    private void Start()
    {
        if (autoScanTreesOnStart)
            RefreshTreesInRadius();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        BindStorage();
        treeSearchRadius = Mathf.Max(0.5f, treeSearchRadius);
    }

    public bool AssignWorker(WorkerAssignment worker)
    {
        if (worker == null)
            return false;

        ResourceTickBehaviour assignmentField = GetAssignmentField();
        if (assignmentField == null)
            return false;

        worker.AssignTo(assignmentField);

        if (!assignedWorkers.Contains(worker))
            assignedWorkers.Add(worker);

        return true;
    }

    public void RemoveWorker(WorkerAssignment worker)
    {
        if (worker == null)
            return;

        ResourceTickBehaviour assignmentField = GetAssignmentField();
        if (assignmentField != null && worker.assignedField == assignmentField)
            worker.AssignTo(null);

        assignedWorkers.Remove(worker);
    }

    public bool IsAssignedWorker(WorkerAssignment worker)
    {
        if (worker == null)
            return false;

        ResourceTickBehaviour assignmentField = GetAssignmentField();
        if (assignmentField == null)
            return false;

        return worker.assignedField == assignmentField;
    }

    public TreeResourceNode GetAvailableTree()
    {
        return GetAvailableTree(null, GetReferencePosition());
    }

    public TreeResourceNode GetAvailableTree(object worker)
    {
        return GetAvailableTree(worker, GetReferencePosition());
    }

    public TreeResourceNode GetAvailableTree(object worker, Vector3 fromPosition)
    {
        CleanNullTrees();

        TreeResourceNode best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < trees.Count; i++)
        {
            TreeResourceNode tree = trees[i];
            if (tree == null)
                continue;

            if (!IsInsideTreeRadius(tree.transform.position))
                continue;

            bool canUse = worker == null ? tree.CanBeAssigned() : tree.CanBeAssigned(worker);
            if (!canUse)
                continue;

            float sqr = (tree.transform.position - fromPosition).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = tree;
            }
        }

        return best;
    }

    public WoodPickup GetAvailablePickup(object worker, Vector3 fromPosition, TreeResourceNode preferredSourceTree = null)
    {
        CleanNullPickups();

        WoodPickup best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < activePickups.Count; i++)
        {
            WoodPickup pickup = activePickups[i];
            if (pickup == null)
                continue;

            if (!IsInsideTreeRadius(pickup.transform.position))
                continue;

            if (!pickup.CanBeAssigned(worker))
                continue;

            if (preferredSourceTree != null && pickup.SourceTree != preferredSourceTree)
                continue;

            float sqr = (pickup.transform.position - fromPosition).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = pickup;
            }
        }

        return best;
    }

    public WoodStorage GetStorage()
    {
        return storage;
    }

    public ResourceTickBehaviour GetAssignmentField()
    {
        return storage != null ? storage.GetAssignmentField() : null;
    }

    public void SetStorage(WoodStorage newStorage)
    {
        storage = newStorage;
        BindStorage();
    }

    public bool IsInsideTreeRadius(Vector3 worldPosition)
    {
        Vector3 center = GetReferencePosition();
        Vector3 flatDelta = worldPosition - center;
        flatDelta.y = 0f;
        return flatDelta.sqrMagnitude <= treeSearchRadius * treeSearchRadius;
    }

    public void RegisterTree(TreeResourceNode tree)
    {
        if (tree == null)
            return;

        if (!IsInsideTreeRadius(tree.transform.position))
            return;

        if (!trees.Contains(tree))
            trees.Add(tree);

        if (tree.OwningWorkArea != this)
            tree.SetOwningWorkArea(this);
    }

    public void UnregisterTree(TreeResourceNode tree)
    {
        if (tree == null)
            return;

        trees.Remove(tree);
    }

    public void RegisterPickup(WoodPickup pickup)
    {
        if (pickup == null)
            return;

        if (!IsInsideTreeRadius(pickup.transform.position))
            return;

        if (!activePickups.Contains(pickup))
            activePickups.Add(pickup);

        if (pickup.OwnerArea != this)
            pickup.SetOwnerArea(this);
    }

    public void UnregisterPickup(WoodPickup pickup)
    {
        if (pickup == null)
            return;

        activePickups.Remove(pickup);
    }

    public void RefreshTreesInRadius()
    {
        for (int i = 0; i < trees.Count; i++)
        {
            TreeResourceNode oldTree = trees[i];
            if (oldTree != null && oldTree.OwningWorkArea == this)
                oldTree.SetOwningWorkArea(null);
        }

        trees.Clear();

        Vector3 center = GetReferencePosition();
        Collider[] hits = Physics.OverlapSphere(center, treeSearchRadius);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            TreeResourceNode tree = hit.GetComponentInParent<TreeResourceNode>();
            if (tree == null)
                continue;

            RegisterTree(tree);
        }
    }

    public void RefreshAssignedWorkerList()
    {
        ResourceTickBehaviour assignmentField = GetAssignmentField();

        for (int i = assignedWorkers.Count - 1; i >= 0; i--)
        {
            WorkerAssignment worker = assignedWorkers[i];
            if (worker == null)
            {
                assignedWorkers.RemoveAt(i);
                continue;
            }

            if (assignmentField == null || worker.assignedField != assignmentField)
                assignedWorkers.RemoveAt(i);
        }
    }

    public int GetAssignedWorkerCount()
    {
        RefreshAssignedWorkerList();
        return assignedWorkers.Count;
    }

    public Vector3 GetReferencePosition()
    {
        if (storage != null)
            return storage.transform.position;

        return transform.position;
    }

    private void CleanNullTrees()
    {
        for (int i = trees.Count - 1; i >= 0; i--)
        {
            if (trees[i] == null)
                trees.RemoveAt(i);
        }
    }

    private void CleanNullPickups()
    {
        for (int i = activePickups.Count - 1; i >= 0; i--)
        {
            if (activePickups[i] == null)
                activePickups.RemoveAt(i);
        }
    }

    private void AutoAssignReferences()
    {
        if (storage == null)
        {
            storage = GetComponent<WoodStorage>();
            if (storage == null)
                storage = GetComponentInParent<WoodStorage>();
            if (storage == null)
                storage = GetComponentInChildren<WoodStorage>();
        }
    }

    private void BindStorage()
    {
        if (storage != null)
            storage.SetOwnerArea(this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.85f, 0f, 0.2f);
        Gizmos.DrawSphere(GetReferencePosition(), treeSearchRadius);
    }
}
