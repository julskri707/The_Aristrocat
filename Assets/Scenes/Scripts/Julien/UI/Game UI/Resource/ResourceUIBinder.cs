using System;
using UnityEngine;

[DisallowMultipleComponent]
public class ResourceUIBinder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ResourceManager resourceManager;
    [SerializeField] private ResourceUIEntry[] entries;

    private void OnEnable()
    {
        if (resourceManager != null)
            resourceManager.OnAnyResourceChanged += HandleAnyResourceChanged;

        RefreshAll();
    }

    private void OnDisable()
    {
        if (resourceManager != null)
            resourceManager.OnAnyResourceChanged -= HandleAnyResourceChanged;
    }

    private void Start()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (resourceManager == null || entries == null)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            ResourceUIEntry entry = entries[i];
            if (entry == null)
                continue;

            if (!TryParseResourceType(entry.ResourceId, out ResourceManager.ResourceType resourceType))
            {
                Debug.LogWarning($"[ResourceUIBinder] Unknown ResourceId '{entry.ResourceId}' on '{entry.name}'.", entry);
                continue;
            }

            float amount = resourceManager.Get(resourceType);
            entry.SetValue(amount);
        }
    }

    private void HandleAnyResourceChanged(ResourceManager.ResourceType resourceType, float newValue)
    {
        if (entries == null)
            return;

        string changedName = resourceType.ToString();

        for (int i = 0; i < entries.Length; i++)
        {
            ResourceUIEntry entry = entries[i];
            if (entry == null)
                continue;

            if (string.Equals(entry.ResourceId, changedName, StringComparison.OrdinalIgnoreCase))
                entry.SetValue(newValue);
        }
    }

    private bool TryParseResourceType(string resourceId, out ResourceManager.ResourceType resourceType)
    {
        if (!string.IsNullOrWhiteSpace(resourceId) &&
            Enum.TryParse(resourceId, true, out resourceType))
        {
            return true;
        }

        resourceType = default;
        return false;
    }
}
