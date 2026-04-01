using UnityEngine;

/// <summary>
/// Provider UI pour éditer les points d'un WallObject.
/// - Compte les points (sans le dernier duplicata si closedLoop)
/// - Get/Set passent par WallObject.SetPoint(...)
/// - Si closedLoop: quand tu bouges le point 0, on met aussi à jour le dernier point
/// </summary>
[DisallowMultipleComponent]
public class WallControlPointProvider_WallObject : MonoBehaviour, IControlPointProvider
{
    [Header("Target")]
    public WallObject wall;

    void Awake()
    {
        if (wall == null)
            wall = GetComponent<WallObject>();
    }

    public int ControlPointCount
    {
        get
        {
            if (wall == null || wall.Points == null) return 0;

            int count = wall.Points.Count;
            bool hasDuplicateClosurePoint =
                wall.closedLoop &&
                count >= 2 &&
                Vector3.Distance(wall.Points[0], wall.Points[count - 1]) < 0.001f;

            // Si boucle fermée (Tiny Glade style), on ne veut pas un handle doublon
            if (hasDuplicateClosurePoint && count > 0)
                count -= 1;

            return Mathf.Max(0, count);
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        if (wall == null || wall.Points == null) return Vector3.zero;

        int count = ControlPointCount;
        if (index < 0 || index >= count) return Vector3.zero;

        return wall.Points[index];
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        if (wall == null) return;

        int count = ControlPointCount;
        if (index < 0 || index >= count) return;

        // Met à jour le point demandé
        wall.SetPoint(index, worldPos);

        // Si boucle fermée: le dernier point doit rester égal au premier
        if (wall.closedLoop && wall.Points != null && wall.Points.Count >= 2)
        {
            int last = wall.Points.Count - 1;
            if (index == 0)
                wall.SetPoint(last, worldPos);
        }
    }

    public bool IsControlPointEditable(int index)
    {
        int count = ControlPointCount;
        return index >= 0 && index < count;
    }
}
