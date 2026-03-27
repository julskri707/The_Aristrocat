using UnityEngine;

[DisallowMultipleComponent]
public class TreePrefabId : MonoBehaviour
{
    [Header("Save ID")]
    [SerializeField] private string prefabId = "tree_oak_01";

    public string PrefabId => prefabId;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(prefabId))
        {
            prefabId = gameObject.name.ToLower().Replace(" ", "_");
        }
    }
}