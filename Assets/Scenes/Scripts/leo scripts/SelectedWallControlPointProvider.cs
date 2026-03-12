using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

[DisallowMultipleComponent]
public class SelectedWallControlPointProvider : MonoBehaviour, IControlPointProvider, IControlPointPathProvider
{
    public WallBuildController buildController;

    void Awake()
    {
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();
    }

    Component CurrentProviderComponent
    {
        get
        {
            WallObject w = null;

            if (buildController != null)
                w = buildController.SelectedWall;

            if (w == null)
            {
                w = FindFirstObjectByType<WallObject>();
                if (w != null && buildController != null)
                    buildController.ForceSelectWall(w);
            }

            if (w == null) return null;

            // priorité : un composant qui implémente déjà IControlPointProvider
            var monos = w.GetComponents<MonoBehaviour>();
            foreach (var mb in monos)
            {
                if (mb is IControlPointProvider)
                    return mb;
            }

            return null;
        }
    }

    public int ControlPointCount
    {
        get
        {
            var c = CurrentProviderComponent;
            if (c == null) return 0;

            if (c is IControlPointProvider p)
                return Mathf.Max(0, p.ControlPointCount);

            return 0;
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        var c = CurrentProviderComponent;
        if (c == null) return Vector3.zero;

        if (c is IControlPointProvider p)
        {
            int count = p.ControlPointCount;
            if (index < 0 || index >= count) return Vector3.zero;
            return p.GetControlPointWorld(index);
        }

        return Vector3.zero;
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        var c = CurrentProviderComponent;
        if (c == null) return;

        if (c is IControlPointProvider p)
        {
            int count = p.ControlPointCount;
            if (index < 0 || index >= count) return;
            p.SetControlPointWorld(index, worldPos);
        }
    }

    public bool IsControlPointEditable(int index)
    {
        var c = CurrentProviderComponent;
        if (c == null) return false;

        if (c is IControlPointProvider p)
            return index >= 0 && index < p.ControlPointCount && p.IsControlPointEditable(index);

        return false;
    }

    public List<Vector3> GetPreviewPathWorld()
    {
        var c = CurrentProviderComponent;
        if (c == null) return null;

        if (c is IControlPointPathProvider pathProvider)
            return pathProvider.GetPreviewPathWorld();

        return null;
    }
}