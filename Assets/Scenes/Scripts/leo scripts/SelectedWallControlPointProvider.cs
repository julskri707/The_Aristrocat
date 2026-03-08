using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SelectedWallControlPointProvider : MonoBehaviour, IControlPointProvider, IControlPointPathProvider
{
    public WallBuildController buildController;

    void Awake()
    {
        if (buildController == null)
            buildController = FindObjectOfType<WallBuildController>();
    }

    WallEditShape CurrentShape
    {
        get
        {
            if (buildController == null) return null;
            if (buildController.SelectedWall == null) return null;

            var shape = buildController.SelectedWall.GetComponent<WallEditShape>();
            return shape;
        }
    }

    public int ControlPointCount
    {
        get
        {
            if (CurrentShape == null) return 0;
            return CurrentShape.ControlPointCount;
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        if (CurrentShape == null) return Vector3.zero;
        return CurrentShape.GetControlPointWorld(index);
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        if (CurrentShape == null) return;
        CurrentShape.SetControlPointWorld(index, worldPos);
    }

    public bool IsControlPointEditable(int index)
    {
        if (CurrentShape == null) return false;
        return CurrentShape.IsControlPointEditable(index);
    }

    public List<Vector3> GetPreviewPathWorld()
    {
        if (CurrentShape == null) return null;
        return CurrentShape.GetPreviewPathWorld();
    }
}