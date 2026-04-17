using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("UI")]
    [SerializeField] private RawImage iconRawImage;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private GameObject emptyStateGraphic;

    private InventoryTooltipUI tooltipUI;
    private PlayerInventory inventory;
    private PlayerEquipment equipment;
    private PlayerInventory.InventorySlot boundSlot;
    private int slotIndex;

    public void Bind(PlayerInventory inventoryRef, PlayerEquipment equipmentRef, int inventorySlotIndex, PlayerInventory.InventorySlot slot, InventoryTooltipUI tooltip)
    {
        inventory = inventoryRef;
        equipment = equipmentRef;
        slotIndex = inventorySlotIndex;
        boundSlot = slot;
        tooltipUI = tooltip;
        Refresh();
    }

    public void Refresh()
    {
        bool hasItem = boundSlot != null && !boundSlot.IsEmpty;

        if (iconRawImage != null)
        {
            iconRawImage.enabled = hasItem;
            iconRawImage.texture = hasItem ? boundSlot.itemData.iconTexture : null;
        }

        if (amountText != null)
            amountText.text = hasItem ? boundSlot.amount.ToString() : "";

        if (emptyStateGraphic != null)
            emptyStateGraphic.SetActive(!hasItem);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipUI == null || boundSlot == null || boundSlot.IsEmpty)
            return;

        tooltipUI.Show(boundSlot.itemData.itemDisplayName, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipUI != null)
            tooltipUI.Hide();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (boundSlot == null || boundSlot.IsEmpty)
            return;

        if (InventoryUIDragController.Instance == null)
            return;

        InventoryUIDragController.Instance.BeginInventoryDrag(slotIndex, boundSlot.itemData);
    }

    public void OnDrag(PointerEventData eventData)
    {
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (InventoryUIDragController.Instance == null || !InventoryUIDragController.Instance.IsDragging)
            return;

        InventoryUIDragController.Instance.EndDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (InventoryUIDragController.Instance == null || !InventoryUIDragController.Instance.IsDragging)
            return;

        if (InventoryUIDragController.Instance.OriginType != InventoryUIDragController.DragOriginType.Equipment)
            return;

        if (equipment == null)
            return;

        bool success = equipment.UnequipToInventory(InventoryUIDragController.Instance.SourceIndex);
        if (!success)
            return;

        InventoryUIDragController.Instance.MarkDropHandled();
        InventoryUIDragController.Instance.EndDrag();

        if (inventory != null)
            inventory.ForceRefresh();
    }
}
