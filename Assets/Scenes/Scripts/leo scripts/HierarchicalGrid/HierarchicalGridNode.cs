using UnityEngine;

public sealed class HierarchicalGridNode
{
    public readonly Vector2 center;
    public readonly float size;
    public readonly int depth;
    public HierarchicalGridNode[] children;

    public bool IsLeaf => children == null || children.Length == 0;
    public float HalfSize => size * 0.5f;
    public Vector2 Min => center - Vector2.one * HalfSize;
    public Vector2 Max => center + Vector2.one * HalfSize;

    public HierarchicalGridNode(Vector2 center, float size, int depth)
    {
        this.center = center;
        this.size = size;
        this.depth = depth;
    }

    public void Subdivide()
    {
        if (!IsLeaf)
            return;

        float childSize = size / 3f;
        float step = childSize;

        children = new HierarchicalGridNode[9];
        int k = 0;
        for (int row = 1; row >= -1; row--)
        {
            for (int col = -1; col <= 1; col++)
            {
                Vector2 childCenter = center + new Vector2(col * step, row * step);
                children[k++] = new HierarchicalGridNode(childCenter, childSize, depth + 1);
            }
        }
    }

    public bool ContainsXZ(Vector2 point)
    {
        Vector2 min = Min;
        Vector2 max = Max;
        return point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;
    }

    public float DistanceToBoundsXZ(Vector2 point)
    {
        Vector2 min = Min;
        Vector2 max = Max;
        float dx = Mathf.Max(min.x - point.x, 0f, point.x - max.x);
        float dz = Mathf.Max(min.y - point.y, 0f, point.y - max.y);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}

