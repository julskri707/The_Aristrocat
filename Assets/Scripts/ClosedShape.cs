using System.Collections.Generic;
using UnityEngine;

public class ClosedShape : MonoBehaviour
{
    public List<Vector3> points = new List<Vector3>();

    void Start()
    {
        WallMeshGenerator gen = FindObjectOfType<WallMeshGenerator>();
        if (gen != null)
        {
            gen.Generate(points);
        }
    }

    void OnDrawGizmos()
    {
        if (points == null || points.Count < 2) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Gizmos.DrawLine(points[i], points[i + 1]);
        }
    }
}
