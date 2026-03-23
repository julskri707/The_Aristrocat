using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class WallStyleButtonUI : MonoBehaviour
{
    [Header("References")]
    public Button button;
    public Image iconImage;
    public Text labelText;
    public Image backgroundImage;
    public GameObject selectedMarker;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(0.90f, 0.96f, 1.00f, 1.00f);

    private WallStyleDefinition _boundStyle;

    void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();
    }

    public void Bind(WallStyleDefinition style, bool isSelected, Action<WallStyleDefinition> onClicked)
    {
        _boundStyle = style;

        if (labelText != null)
            labelText.text = style != null ? style.displayName : "Unnamed Style";

        if (iconImage != null)
        {
            bool hasIcon = style != null && style.icon != null;
            iconImage.gameObject.SetActive(hasIcon);

            if (hasIcon)
            {
                iconImage.sprite = style.icon;
                iconImage.color = Color.white;
            }
        }

        if (backgroundImage != null)
            backgroundImage.color = isSelected ? selectedColor : normalColor;

        if (selectedMarker != null)
            selectedMarker.SetActive(isSelected);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (_boundStyle != null)
                    onClicked?.Invoke(_boundStyle);
            });
        }
    }
}
