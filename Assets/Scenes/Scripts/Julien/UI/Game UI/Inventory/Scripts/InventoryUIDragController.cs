using UnityEngine;
using UnityEngine.UI;

public class InventoryUIDragController : MonoBehaviour
{
    public enum DragOriginType
    {
        None,
        Inventory,
        Equipment
    }

    public static InventoryUIDragController Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private RectTransform dragRoot;
    [SerializeField] private RawImage dragIcon;

    private bool isDragging;
    private bool dropHandled;
    private DragOriginType originType;
    private int sourceIndex = -1;
    private InventoryItemData draggedItem;

    public bool IsDragging => isDragging;
    public DragOriginType OriginType => originType;
    public int SourceIndex => sourceIndex;
    public InventoryItemData DraggedItem => draggedItem;

    private void Awake()
    {
        Instance = this;
        HideVisual();
    }

    private void Update()
    {
        if (isDragging && dragRoot != null)
            dragRoot.position = Input.mousePosition;
    }

    public void BeginInventoryDrag(int inventorySlotIndex, InventoryItemData item)
    {
        if (item == null)
            return;

        isDragging = true;
        dropHandled = false;
        originType = DragOriginType.Inventory;
        sourceIndex = inventorySlotIndex;
        draggedItem = item;

        ShowVisual(item);
    }

    public void BeginEquipmentDrag(int equipmentSlotIndex, InventoryItemData item)
    {
        if (item == null)
            return;

        isDragging = true;
        dropHandled = false;
        originType = DragOriginType.Equipment;
        sourceIndex = equipmentSlotIndex;
        draggedItem = item;

        ShowVisual(item);
    }

    public void MarkDropHandled()
    {
        dropHandled = true;
    }

    public void EndDrag()
    {
        isDragging = false;
        originType = DragOriginType.None;
        sourceIndex = -1;
        draggedItem = null;
        dropHandled = false;
        HideVisual();
    }

    private void ShowVisual(InventoryItemData item)
    {
        if (dragRoot != null)
            dragRoot.gameObject.SetActive(true);

        if (dragIcon != null)
        {
            dragIcon.enabled = item != null && item.iconTexture != null;
            dragIcon.texture = item != null ? item.iconTexture : null;
        }

        if (dragRoot != null)
            dragRoot.position = Input.mousePosition;
    }

    private void HideVisual()
    {
        if (dragRoot != null)
            dragRoot.gameObject.SetActive(false);

        if (dragIcon != null)
        {
            dragIcon.texture = null;
            dragIcon.enabled = false;
        }
    }
}
