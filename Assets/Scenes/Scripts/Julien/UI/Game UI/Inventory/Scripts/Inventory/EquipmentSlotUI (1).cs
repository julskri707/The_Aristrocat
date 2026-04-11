using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EquipmentSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("Config")]
    [SerializeField] private EquipmentSlotType slotType = EquipmentSlotType.ExtraSlot1;
    [SerializeField] private PlayerEquipment playerEquipment;

    [Header("UI")]
    [SerializeField] private RawImage iconRawImage;
    [SerializeField] private TMP_Text slotLabelText;
    [SerializeField] private TMP_Text activeMarkerText;
    [SerializeField] private GameObject emptyStateGraphic;
    [SerializeField] private InventoryTooltipUI tooltipUI;

    private void Update()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (playerEquipment == null)
            return;

        InventoryItemData item = playerEquipment.GetEquippedItem((int)slotType);
        bool hasItem = item != null;

        if (iconRawImage != null)
        {
            iconRawImage.enabled = hasItem;
            iconRawImage.texture = hasItem ? item.iconTexture : null;
        }

        if (slotLabelText != null)
            slotLabelText.text = "Slot " + ((int)slotType + 1);

        if (activeMarkerText != null)
            activeMarkerText.text = playerEquipment.ActiveSlotIndex == (int)slotType ? "AKTIV" : "";

        if (emptyStateGraphic != null)
            emptyStateGraphic.SetActive(!hasItem);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipUI == null || playerEquipment == null)
            return;

        InventoryItemData item = playerEquipment.GetEquippedItem((int)slotType);
        if (item == null)
            return;

        tooltipUI.Show(item.itemDisplayName, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipUI != null)
            tooltipUI.Hide();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (playerEquipment == null || InventoryUIDragController.Instance == null)
            return;

        InventoryItemData item = playerEquipment.GetEquippedItem((int)slotType);
        if (item == null)
            return;

        InventoryUIDragController.Instance.BeginEquipmentDrag((int)slotType, item);
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
        if (playerEquipment == null || InventoryUIDragController.Instance == null || !InventoryUIDragController.Instance.IsDragging)
            return;

        if (InventoryUIDragController.Instance.OriginType == InventoryUIDragController.DragOriginType.Inventory)
        {
            InventoryItemData item = InventoryUIDragController.Instance.DraggedItem;
            bool success = playerEquipment.EquipFromInventoryToSlot((int)slotType, item);
            if (!success)
                return;

            InventoryUIDragController.Instance.MarkDropHandled();
            InventoryUIDragController.Instance.EndDrag();
            return;
        }

        if (InventoryUIDragController.Instance.OriginType == InventoryUIDragController.DragOriginType.Equipment)
        {
            bool success = playerEquipment.SwapEquipmentSlots(InventoryUIDragController.Instance.SourceIndex, (int)slotType);
            if (!success)
                return;

            InventoryUIDragController.Instance.MarkDropHandled();
            InventoryUIDragController.Instance.EndDrag();
        }
    }
}
