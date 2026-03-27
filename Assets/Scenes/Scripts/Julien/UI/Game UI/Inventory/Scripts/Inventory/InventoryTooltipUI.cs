using TMPro;
using UnityEngine;

public class InventoryTooltipUI : MonoBehaviour
{
    [SerializeField] private RectTransform root;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private Vector2 screenOffset = new Vector2(16f, -16f);

    private void Awake()
    {
        Hide();
    }

    private void Update()
    {
        if (root != null && root.gameObject.activeSelf)
        {
            root.position = (Vector2)Input.mousePosition + screenOffset;
        }
    }

    public void Show(string itemName, Vector2 screenPosition)
    {
        if (root == null)
            return;

        root.gameObject.SetActive(true);
        root.position = screenPosition + screenOffset;

        if (itemNameText != null)
            itemNameText.text = itemName;
    }

    public void Hide()
    {
        if (root != null)
            root.gameObject.SetActive(false);
    }
}
