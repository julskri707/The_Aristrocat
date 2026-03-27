using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AssignmentListItemUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    [SerializeField] private Button mainButton;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Image backgroundImage;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(0.8f, 0.95f, 1f, 1f);

    private System.Action<PointerEventDataEx> onLeftClick;
    private System.Action<PointerEventDataEx> onRightClick;

    public object Payload { get; private set; }

    public void Setup(string label, object payload, System.Action<PointerEventDataEx> leftClick, System.Action<PointerEventDataEx> rightClick)
    {
        Payload = payload;
        onLeftClick = leftClick;
        onRightClick = rightClick;

        if (labelText != null)
            labelText.text = label;

        Select(false);
    }

    public void Select(bool selected)
    {
        if (backgroundImage != null)
            backgroundImage.color = selected ? selectedColor : normalColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null)
            return;

        var clickData = new PointerEventDataEx(this, Payload, eventData.position);

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            onRightClick?.Invoke(clickData);
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            onLeftClick?.Invoke(clickData);
        }
    }

    public struct PointerEventDataEx
    {
        public AssignmentListItemUI source;
        public object payload;
        public Vector2 screenPosition;

        public PointerEventDataEx(AssignmentListItemUI source, object payload, Vector2 screenPosition)
        {
            this.source = source;
            this.payload = payload;
            this.screenPosition = screenPosition;
        }
    }
}
