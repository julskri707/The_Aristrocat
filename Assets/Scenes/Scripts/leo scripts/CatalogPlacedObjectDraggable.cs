using UnityEngine;

/// <summary>
/// Objet placé depuis le catalogue (lit, pot, prefab sol) : une poignée overlay au centre pour le déplacer sur le plan horizontal à sa hauteur.
/// </summary>
[DisallowMultipleComponent]
public class CatalogPlacedObjectDraggable : MonoBehaviour, IControlPointProvider, IControlPointDragPlaneProvider, IControlPointWallShapeBinding
{
    public bool ControlPointsBelongToWallShape => false;

    public int ControlPointCount => 1;

    public Vector3 GetControlPointWorld(int index)
    {
        return transform.position;
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        worldPos.y = transform.position.y;
        worldPos = SnapWorldXZIfInHouseLot(worldPos);
        transform.position = worldPos;
    }

    public bool IsControlPointEditable(int index) => index == 0;

    public bool TryGetDragPlane(int index, Camera cam, Vector3 startWorld, out Plane plane)
    {
        float y = transform.position.y;
        plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        return true;
    }

    Vector3 SnapWorldXZIfInHouseLot(Vector3 world)
    {
        if (!RuntimeAssetStoreUI.IsWorldPointInsideAnyDesignatedHouseLotXZ(world))
            return world;

        WallDrawInput di = FindFirstObjectByType<WallDrawInput>();
        if (di == null || !di.TryGetMainGridLatticeStepXZ(out float st, out Vector2 o))
            return world;

        float fine = st / Mathf.Max(1.1f, di.interiorFineGridFinenessMul);
        world.x = Mathf.Round((world.x - o.x) / fine) * fine + o.x;
        world.z = Mathf.Round((world.z - o.y) / fine) * fine + o.y;
        return world;
    }
}
