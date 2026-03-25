public struct LootDropResult
{
    public LootItemDefinitionSO itemDefinition;
    public int quantity;

    public LootDropResult(LootItemDefinitionSO itemDefinition, int quantity)
    {
        this.itemDefinition = itemDefinition;
        this.quantity = quantity;
    }
}
