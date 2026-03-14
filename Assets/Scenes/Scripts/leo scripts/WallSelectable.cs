using UnityEngine;

public class WallSelectable : MonoBehaviour
{
    [Header("Auto")]
    public MonoBehaviour providerBehaviour; // doit implémenter IControlPointProvider

    [Header("Optional")]
    public bool autoFindProviderOnAwake = true;

    void Awake()
    {
        if (autoFindProviderOnAwake)
            AutoFindProvider();
    }

    public void AutoFindProvider()
    {
        providerBehaviour = null;

        // 1) Priorité absolue : WallEditShape
        WallEditShape editShape = GetComponent<WallEditShape>();
        if (editShape != null)
        {
            providerBehaviour = editShape;
            return;
        }

        // 2) Autres providers utiles sur le GO, mais on évite les providers du path brut.
        MonoBehaviour[] monos = GetComponents<MonoBehaviour>();
        for (int i = 0; i < monos.Length; i++)
        {
            MonoBehaviour m = monos[i];
            if (m == null)
                continue;

            if (!(m is IControlPointProvider))
                continue;

            if (m is WallObject)
                continue;

            if (m is WallControlPointProvider)
                continue;

            if (m is WallControlPointProvider_WallObject)
                continue;

            providerBehaviour = m;
            return;
        }

        // 3) Puis dans les enfants.
        monos = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < monos.Length; i++)
        {
            MonoBehaviour m = monos[i];
            if (m == null)
                continue;

            if (!(m is IControlPointProvider))
                continue;

            if (m is WallObject)
                continue;

            if (m is WallControlPointProvider)
                continue;

            if (m is WallControlPointProvider_WallObject)
                continue;

            providerBehaviour = m;
            return;
        }

        // 4) Fallback ultime.
        WallObject wall = GetComponent<WallObject>();
        if (wall != null)
            providerBehaviour = wall;
    }

    public IControlPointProvider GetProvider()
    {
        return providerBehaviour as IControlPointProvider;
    }
}
