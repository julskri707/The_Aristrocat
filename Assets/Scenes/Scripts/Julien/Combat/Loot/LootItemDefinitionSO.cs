using UnityEngine;

[CreateAssetMenu(fileName = "LootItem_", menuName = "THE ARISTROCAT/Loot/Item Definition")]
public class LootItemDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string itemId = "item_id";
    public string displayName = "New Loot Item";

    [Header("World")]
    public GameObject worldPickupPrefab;

    [Header("Future Use")]
    public Sprite icon;
    public bool stackable = true;
}
