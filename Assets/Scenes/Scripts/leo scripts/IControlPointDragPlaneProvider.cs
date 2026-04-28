using UnityEngine;

/// <summary>
/// Optionnel : permet de remplacer le plan de drag par défaut (sol Y=0) pour certaines poignées
/// (ex. hauteur de toit sur un plan vertical).
/// </summary>
public interface IControlPointDragPlaneProvider
{
    bool TryGetDragPlane(int index, Camera cam, Vector3 startWorld, out Plane plane);
}
