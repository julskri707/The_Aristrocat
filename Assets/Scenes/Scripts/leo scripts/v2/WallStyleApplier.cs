using UnityEngine;

public static class WallStyleApplier
{
    public static bool Apply(WallObject wall, WallStyleDefinition style)
    {
        if (wall == null || style == null)
            return false;

        wall.height = Mathf.Max(0.1f, style.height);
        wall.wallMaterial = style.wallMaterial != null ? style.wallMaterial : wall.wallMaterial;
        wall.uvMetersPerU = Mathf.Max(0.01f, style.uvMetersPerU);
        wall.uvMetersPerV = Mathf.Max(0.01f, style.uvMetersPerV);

        // SetThickness triggers the rebuild and re-applies the material.
        wall.SetThickness(Mathf.Max(0.01f, style.thickness));

        WallStyleInstance instance = wall.GetComponent<WallStyleInstance>();
        if (instance == null)
            instance = wall.gameObject.AddComponent<WallStyleInstance>();

        instance.SetCurrentStyle(style);
        return true;
    }
}
