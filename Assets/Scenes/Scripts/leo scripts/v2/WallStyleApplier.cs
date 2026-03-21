using UnityEngine;

public static class WallStyleApplier
{
    public static bool Apply(WallObject wall, WallStyleDefinition style)
    {
        if (wall == null || style == null)
            return false;

        wall.height = Mathf.Max(0.1f, style.height);

        // Temporaire : tant que WallObject n'a qu'un seul material,
        // on utilise le matériau latéral comme matériau principal.
        wall.wallMaterial = style.sideMaterial != null ? style.sideMaterial : wall.wallMaterial;

        // Temporaire : on utilise les UV latéraux pour le mur entier.
        wall.uvMetersPerU = Mathf.Max(0.01f, style.sideUvMetersPerU);
        wall.uvMetersPerV = Mathf.Max(0.01f, style.sideUvMetersPerV);

        // SetThickness déclenche le rebuild du mesh.
        wall.SetThickness(Mathf.Max(0.01f, style.thickness));

        WallStyleInstance instance = wall.GetComponent<WallStyleInstance>();
        if (instance == null)
            instance = wall.gameObject.AddComponent<WallStyleInstance>();

        instance.SetCurrentStyle(style);
        return true;
    }
}