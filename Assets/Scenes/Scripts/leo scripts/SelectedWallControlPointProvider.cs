using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class SelectedWallControlPointProvider : MonoBehaviour, IControlPointProvider, IControlPointPathProvider
{
    public WallBuildController buildController;

    void Awake()
    {
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();
    }

    /// <summary>Provider réellement utilisé pour les poignées (même logique que <see cref="WallBuildController.ResolveBestProvider"/>).</summary>
    public IControlPointProvider ActiveWallProvider => CurrentProviderComponent as IControlPointProvider;

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

            if (w == null)
                return null;

            WallEditShape editShape = w.GetComponent<WallEditShape>();
            if (editShape != null)
                return editShape;

            WallSelectable selectable = w.GetComponent<WallSelectable>();
            if (selectable != null)
            {
                if (selectable.providerBehaviour == null)
                    selectable.AutoFindProvider();

                if (selectable.providerBehaviour != null && selectable.providerBehaviour is IControlPointProvider)
                    return selectable.providerBehaviour;
            }

            MonoBehaviour[] monos = w.GetComponents<MonoBehaviour>();
            for (int i = 0; i < monos.Length; i++)
            {
                MonoBehaviour mb = monos[i];
                if (mb == null || !(mb is IControlPointProvider))
                    continue;

                if (mb is WallObject)
                    continue;

                return mb;
            }

            return w;
        }
    }

    public int ControlPointCount
    {
        get
        {
            Component c = CurrentProviderComponent;
            if (c == null) return 0;
            if (c is IControlPointProvider p) return Mathf.Max(0, p.ControlPointCount);
            return 0;
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        Component c = CurrentProviderComponent;
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
        Component c = CurrentProviderComponent;
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
        Component c = CurrentProviderComponent;
        if (c == null) return false;
        if (c is IControlPointProvider p)
            return index >= 0 && index < p.ControlPointCount && p.IsControlPointEditable(index);
        return false;
    }

    public List<Vector3> GetPreviewPathWorld()
    {
        Component c = CurrentProviderComponent;
        if (c == null) return null;
        if (c is IControlPointPathProvider pathProvider)
            return pathProvider.GetPreviewPathWorld();
        return null;
    }
}
