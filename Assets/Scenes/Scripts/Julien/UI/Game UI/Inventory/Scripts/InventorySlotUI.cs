using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI")]
    [SerializeField] private RawImage iconRawImage;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private GameObject emptyStateGraphic;

    private InventoryTooltipUI tooltipUI;
    private PlayerInventory.InventorySlot boundSlot;

    public void Bind(PlayerInventory.InventorySlot slot, InventoryTooltipUI tooltip)
    {
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
        {
            amountText.text = hasItem ? boundSlot.amount.ToString() : "";
        }

        if (emptyStateGraphic != null)
        {
            emptyStateGraphic.SetActive(!hasItem);
        }
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
}
