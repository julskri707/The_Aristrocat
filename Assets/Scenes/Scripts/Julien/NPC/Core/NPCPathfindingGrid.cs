using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class NPCPathfindingGrid : MonoBehaviour
{
    public static NPCPathfindingGrid Instance { get; private set; }

    [Header("Grid Bounds")]
    [SerializeField] private Vector2 worldSize = new Vector2(80f, 80f);
    [SerializeField] private float cellSize = 1f;

    [Header("Walkability")]
    [SerializeField] private float groundRayStartHeight = 50f;
    [SerializeField] private float groundRayDistance = 120f;
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float agentHeight = 1.8f;
    [SerializeField] private float maxStepHeight = 0.75f;

    [Header("Build")]
    [SerializeField] private bool buildOnAwake = true;
    [SerializeField] private bool rebuildOnValidate = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool drawOnlyBlocked = true;

    private Node[,] grid;
    private int gridSizeX;
    private int gridSizeZ;
    private Vector3 originWorld;
    private int searchIdCounter = 0;

    private class Node
    {
        public bool walkable;
        public Vector3 worldPosition;
        public float groundY;
        public int x;
        public int z;

        public Node parent;
        public int gCost;
        public int hCost;
        public int searchId;

        public int FCost => gCost + hCost;

        public void ResetForSearch(int newSearchId)
        {
            if (searchId == newSearchId)
                return;

            searchId = newSearchId;
            parent = null;
            gCost = int.MaxValue;
            hCost = 0;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[NPCPathfindingGrid] Duplicate instance found. Destroying this one.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        if (buildOnAwake)
            BuildGrid();
    }

    private void OnValidate()
    {
        cellSize = Mathf.Max(0.25f, cellSize);
        worldSize.x = Mathf.Max(cellSize, worldSize.x);
        worldSize.y = Mathf.Max(cellSize, worldSize.y);
        groundRayStartHeight = Mathf.Max(1f, groundRayStartHeight);
        groundRayDistance = Mathf.Max(2f, groundRayDistance);
        agentRadius = Mathf.Max(0.05f, agentRadius);
        agentHeight = Mathf.Max(agentRadius * 2f + 0.1f, agentHeight);
        maxStepHeight = Mathf.Max(0.05f, maxStepHeight);

#if UNITY_EDITOR
        if (!Application.isPlaying && rebuildOnValidate)
            BuildGrid();
#endif
    }

    [ContextMenu("Build Grid")]
    public void BuildGrid()
    {
        gridSizeX = Mathf.Max(1, Mathf.RoundToInt(worldSize.x / cellSize));
        gridSizeZ = Mathf.Max(1, Mathf.RoundToInt(worldSize.y / cellSize));

        grid = new Node[gridSizeX, gridSizeZ];
        originWorld = transform.position - new Vector3(worldSize.x * 0.5f, 0f, worldSize.y * 0.5f);

        for (int z = 0; z < gridSizeZ; z++)
        {
            for (int x = 0; x < gridSizeX; x++)
            {
                Vector3 cellCenter = originWorld + new Vector3((x + 0.5f) * cellSize, 0f, (z + 0.5f) * cellSize);

                Node node = new Node
                {
                    x = x,
                    z = z,
                    walkable = false,
                    worldPosition = cellCenter,
                    groundY = 0f
                };

                if (TryGetGroundPoint(cellCenter.x, cellCenter.z, out Vector3 groundPoint))
                {
                    node.groundY = groundPoint.y;
                    node.worldPosition = new Vector3(cellCenter.x, groundPoint.y, cellCenter.z);

                    Vector3 capsuleBottom = new Vector3(cellCenter.x, groundPoint.y + agentRadius + 0.02f, cellCenter.z);
                    Vector3 capsuleTop = capsuleBottom + Vector3.up * Mathf.Max(0f, agentHeight - agentRadius * 2f);

                    node.walkable = !IsBlocked(capsuleBottom, capsuleTop, agentRadius);
                }

                grid[x, z] = node;
            }
        }

        if (debugLogs)
            Debug.Log($"[NPCPathfindingGrid] Grid built: {gridSizeX} x {gridSizeZ}", this);
    }

    public bool TryFindPath(Vector3 startWorld, Vector3 endWorld, List<Vector3> resultBuffer)
    {
        resultBuffer.Clear();

        if (grid == null)
            return false;

        Node startNode = GetClosestWalkableNode(startWorld);
        Node endNode = GetClosestWalkableNode(endWorld);

        if (startNode == null || endNode == null)
            return false;

        searchIdCounter++;
        if (searchIdCounter == int.MaxValue)
            searchIdCounter = 1;

        int searchId = searchIdCounter;

        List<Node> openSet = new List<Node>(128);
        HashSet<Node> closedSet = new HashSet<Node>();

        startNode.ResetForSearch(searchId);
        startNode.gCost = 0;
        startNode.hCost = GetDistanceCost(startNode, endNode);

        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            Node current = openSet[0];

            for (int i = 1; i < openSet.Count; i++)
            {
                Node test = openSet[i];
                if (test.FCost < current.FCost || (test.FCost == current.FCost && test.hCost < current.hCost))
                    current = test;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            if (current == endNode)
            {
                RetraceAndSmoothPath(startNode, endNode, resultBuffer);
                return resultBuffer.Count > 0;
            }

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    int nx = current.x + dx;
                    int nz = current.z + dz;

                    if (!IsInsideGrid(nx, nz))
                        continue;

                    Node neighbour = grid[nx, nz];
                    if (neighbour == null || !neighbour.walkable || closedSet.Contains(neighbour))
                        continue;

                    float yDelta = Mathf.Abs(neighbour.groundY - current.groundY);
                    if (yDelta > maxStepHeight)
                        continue;

                    // Kein diagonales "durch die Ecke schneiden"
                    if (dx != 0 && dz != 0)
                    {
                        Node sideA = grid[current.x + dx, current.z];
                        Node sideB = grid[current.x, current.z + dz];

                        if (sideA == null || sideB == null || !sideA.walkable || !sideB.walkable)
                            continue;

                        if (Mathf.Abs(sideA.groundY - current.groundY) > maxStepHeight ||
                            Mathf.Abs(sideB.groundY - current.groundY) > maxStepHeight)
                            continue;
                    }

                    neighbour.ResetForSearch(searchId);

                    int moveCost = current.gCost + GetDistanceCost(current, neighbour);
                    if (moveCost < neighbour.gCost || !openSet.Contains(neighbour))
                    {
                        neighbour.gCost = moveCost;
                        neighbour.hCost = GetDistanceCost(neighbour, endNode);
                        neighbour.parent = current;

                        if (!openSet.Contains(neighbour))
                            openSet.Add(neighbour);
                    }
                }
            }
        }

        return false;
    }

    private void RetraceAndSmoothPath(Node startNode, Node endNode, List<Vector3> resultBuffer)
    {
        List<Node> rawPath = new List<Node>(128);
        Node current = endNode;

        while (current != null && current != startNode)
        {
            rawPath.Add(current);
            current = current.parent;
        }

        rawPath.Reverse();

        if (rawPath.Count == 0)
            return;

        int anchorIndex = 0;
        resultBuffer.Add(rawPath[0].worldPosition);

        while (anchorIndex < rawPath.Count - 1)
        {
            int furthestVisible = anchorIndex + 1;

            for (int testIndex = rawPath.Count - 1; testIndex > anchorIndex; testIndex--)
            {
                if (IsDirectWalkable(rawPath[anchorIndex].worldPosition, rawPath[testIndex].worldPosition))
                {
                    furthestVisible = testIndex;
                    break;
                }
            }

            if (furthestVisible != anchorIndex)
            {
                resultBuffer.Add(rawPath[furthestVisible].worldPosition);
                anchorIndex = furthestVisible;
            }
            else
            {
                anchorIndex++;
                resultBuffer.Add(rawPath[anchorIndex].worldPosition);
            }
        }
    }

    private Node GetClosestWalkableNode(Vector3 worldPosition)
    {
        WorldToGrid(worldPosition, out int centerX, out int centerZ);

        int maxRadius = Mathf.Max(gridSizeX, gridSizeZ);

        for (int radius = 0; radius <= maxRadius; radius++)
        {
            float bestDist = float.MaxValue;
            Node bestNode = null;

            for (int z = centerZ - radius; z <= centerZ + radius; z++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (!IsInsideGrid(x, z))
                        continue;

                    Node node = grid[x, z];
                    if (node == null || !node.walkable)
                        continue;

                    float dist = (node.worldPosition - worldPosition).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestNode = node;
                    }
                }
            }

            if (bestNode != null)
                return bestNode;
        }

        return null;
    }

    private bool IsInsideGrid(int x, int z)
    {
        return x >= 0 && z >= 0 && x < gridSizeX && z < gridSizeZ;
    }

    private void WorldToGrid(Vector3 worldPosition, out int x, out int z)
    {
        float percentX = Mathf.Clamp01((worldPosition.x - originWorld.x) / worldSize.x);
        float percentZ = Mathf.Clamp01((worldPosition.z - originWorld.z) / worldSize.y);

        x = Mathf.Clamp(Mathf.FloorToInt(percentX * gridSizeX), 0, gridSizeX - 1);
        z = Mathf.Clamp(Mathf.FloorToInt(percentZ * gridSizeZ), 0, gridSizeZ - 1);
    }

    private int GetDistanceCost(Node a, Node b)
    {
        int dstX = Mathf.Abs(a.x - b.x);
        int dstZ = Mathf.Abs(a.z - b.z);

        if (dstX > dstZ)
            return 14 * dstZ + 10 * (dstX - dstZ);

        return 14 * dstX + 10 * (dstZ - dstX);
    }

    private bool TryGetGroundPoint(float x, float z, out Vector3 groundPoint)
    {
        groundPoint = Vector3.zero;

        Vector3 origin = new Vector3(x, transform.position.y + groundRayStartHeight, z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, groundRayDistance, ~0, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            GroundMarker marker = hit.collider.GetComponentInParent<GroundMarker>();
            if (marker == null)
                continue;

            groundPoint = hit.point;
            return true;
        }

        return false;
    }

    private bool IsBlocked(Vector3 capsuleBottom, Vector3 capsuleTop, float radius)
    {
        Collider[] overlaps = Physics.OverlapCapsule(capsuleBottom, capsuleTop, radius, ~0, QueryTriggerInteraction.Ignore);

        if (overlaps == null || overlaps.Length == 0)
            return false;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider c = overlaps[i];
            if (ShouldIgnoreCollider(c))
                continue;

            return true;
        }

        return false;
    }

    private bool IsDirectWalkable(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        dir.y = 0f;

        float dist = dir.magnitude;
        if (dist <= 0.01f)
            return true;

        dir.Normalize();

        Vector3 castOrigin = from + Vector3.up * (agentRadius + 0.2f);
        RaycastHit[] hits = Physics.SphereCastAll(castOrigin, agentRadius * 0.9f, dir, dist, ~0, QueryTriggerInteraction.Ignore);

        if (hits != null && hits.Length > 0)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (ShouldIgnoreCollider(c))
                    continue;

                return false;
            }
        }

        return true;
    }

    private bool ShouldIgnoreCollider(Collider c)
    {
        if (c == null || c.isTrigger)
            return true;

        if (c.GetComponentInParent<GroundMarker>() != null)
            return true;

        if (c.GetComponentInParent<FieldArea>() != null)
            return true;

        if (c.GetComponentInParent<NPCMovementController>() != null)
            return true;

        if (c.GetComponentInParent<NPCDecisionBrain>() != null)
            return true;

        if (c.GetComponentInParent<NPCBrain>() != null)
            return true;

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, new Vector3(worldSize.x, 0.1f, worldSize.y));

        if (!drawGizmos || grid == null)
            return;

        for (int z = 0; z < gridSizeZ; z++)
        {
            for (int x = 0; x < gridSizeX; x++)
            {
                Node node = grid[x, z];
                if (node == null)
                    continue;

                if (drawOnlyBlocked && node.walkable)
                    continue;

                Gizmos.color = node.walkable ? new Color(0f, 1f, 0f, 0.15f) : new Color(1f, 0f, 0f, 0.45f);
                Gizmos.DrawCube(node.worldPosition + Vector3.up * 0.05f, new Vector3(cellSize * 0.8f, 0.1f, cellSize * 0.8f));
            }
        }
    }
}