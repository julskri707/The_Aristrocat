using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optionnel : second poly pour les fils gris (ex. cadre intérieur toit en plus du contour extérieur).
/// </summary>
public interface ISecondaryControlPointPathProvider
{
    List<Vector3> GetSecondaryPreviewPathWorld();
}
