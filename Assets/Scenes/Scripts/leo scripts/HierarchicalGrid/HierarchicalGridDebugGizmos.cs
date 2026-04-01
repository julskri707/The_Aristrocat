using UnityEngine;

[DisallowMultipleComponent]
public class HierarchicalGridDebugGizmos : MonoBehaviour
{
    public HierarchicalGridManager manager;
    public bool drawLeafBounds = true;
    public bool drawHoverCell = true;
    public Color leafColor = new Color(0.2f, 0.9f, 0.5f, 0.5f);
    public Color hoverColor = new Color(1f, 0.55f, 0.2f, 0.95f);

    void OnDrawGizmosSelected()
    {
        if (manager == null)
            manager = GetComponent<HierarchicalGridManager>();
        if (manager == null)
            return;

        if (drawLeafBounds)
            DrawLeaves();

        if (drawHoverCell)
            DrawHover();
    }

    void DrawLeaves()
    {
        var leaves = manager.LeafNodes;
        if (leaves == null)
            return;

        Gizmos.color = leafColor;
        for (int i = 0; i < leaves.Count; i++)
        {
            HierarchicalGridNode n = leaves[i];
            if (n == null)
                continue;
            DrawNodeRect(n, 0f);
        }
    }

    void DrawHover()
    {
        HierarchicalGridNode n = manager.HoverCell;
        if (n == null)
            return;

        Gizmos.color = hoverColor;
        DrawNodeRect(n, 0.02f);
    }

    static void DrawNodeRect(HierarchicalGridNode node, float yOffset)
    {
        Vector2 min = node.Min;
        Vector2 max = node.Max;
        Vector3 a = new Vector3(min.x, yOffset, min.y);
        Vector3 b = new Vector3(max.x, yOffset, min.y);
        Vector3 c = new Vector3(max.x, yOffset, max.y);
        Vector3 d = new Vector3(min.x, yOffset, max.y);
        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }
}

