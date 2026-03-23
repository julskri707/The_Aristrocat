using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class MainMenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Target")]
    [SerializeField] private RectTransform targetTransform;
    [SerializeField] private Image targetImage;
    [SerializeField] private TMP_Text targetText;

    [Header("Scale")]
    [SerializeField] private Vector3 normalScale = Vector3.one;
    [SerializeField] private Vector3 hoverScale = new Vector3(1.06f, 1.06f, 1.06f);

    [Header("Colors")]
    [SerializeField] private Color normalImageColor = new Color(0.22f, 0.18f, 0.14f, 0.92f);
    [SerializeField] private Color hoverImageColor = new Color(0.38f, 0.29f, 0.18f, 0.98f);
    [SerializeField] private Color normalTextColor = new Color(0.95f, 0.89f, 0.76f, 1f);
    [SerializeField] private Color hoverTextColor = Color.white;

    private void Reset()
    {
        targetTransform = transform as RectTransform;
        targetImage = GetComponent<Image>();
        targetText = GetComponentInChildren<TMP_Text>();
    }

    private void Awake()
    {
        ApplyNormalInstant();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetTransform != null) targetTransform.localScale = hoverScale;
        if (targetImage != null) targetImage.color = hoverImageColor;
        if (targetText != null) targetText.color = hoverTextColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ApplyNormalInstant();
    }

    private void ApplyNormalInstant()
    {
        if (targetTransform != null) targetTransform.localScale = normalScale;
        if (targetImage != null) targetImage.color = normalImageColor;
        if (targetText != null) targetText.color = normalTextColor;
    }
}