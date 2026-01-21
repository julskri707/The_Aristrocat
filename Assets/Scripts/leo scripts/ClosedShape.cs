using System.Collections.Generic;
using UnityEngine;

public class ClosedShape : MonoBehaviour
{
    public List<Vector3> points = new List<Vector3>();

    void Start()
    {
        // 1) MURS
        WallGenerator wallGen = FindFirstObjectByType<WallGenerator>();
        if (wallGen != null)
            wallGen.GenerateWalls(points);
        else
            Debug.LogWarning("ClosedShape: WallGenerator introuvable dans la scène.");

        // 2) TOURS
        TowerGenerator towers = FindFirstObjectByType<TowerGenerator>();
        if (towers != null)
            towers.GenerateTowers(points);
        else
            Debug.LogWarning("ClosedShape: TowerGenerator introuvable dans la scène.");

        // 3) TOIT (optionnel)
        RoofMeshGenerator roof = FindFirstObjectByType<RoofMeshGenerator>();
        if (roof != null)
            roof.Generate(points);
        // pas de warning ici: tu peux ne pas avoir de toit pour l'instant
    }

    void OnDrawGizmos()
    {
        if (points == null || points.Count < 2) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < points.Count - 1; i++)
            Gizmos.DrawLine(points[i], points[i + 1]);
    }
}
