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
        // Cherche un MonoBehaviour sur ce GO qui implémente IControlPointProvider
        var monos = GetComponents<MonoBehaviour>();
        foreach (var m in monos)
        {
            if (m == null) continue;
            if (m is IControlPointProvider)
            {
                providerBehaviour = m;
                return;
            }
        }

        // Sinon cherche dans les enfants
        monos = GetComponentsInChildren<MonoBehaviour>();
        foreach (var m in monos)
        {
            if (m == null) continue;
            if (m is IControlPointProvider)
            {
                providerBehaviour = m;
                return;
            }
        }
    }

    public IControlPointProvider GetProvider()
    {
        return providerBehaviour as IControlPointProvider;
    }
}
