using System.Collections.Generic;
using UnityEngine;

// Optionnel: si un provider sait donner une "courbe" (beaucoup de points)
// l'overlay l'utilise pour afficher une ligne qui suit le mur.
public interface IControlPointPathProvider
{
    // Doit renvoyer une polyline en world space (XZ), idéalement fermée si loop
    List<Vector3> GetPreviewPathWorld();
}
