using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BuildMenuItemButton : MonoBehaviour
{
    [Header("Data")]
    public string itemId = "build_item";
    public string displayName = "Build Item";
    public Button button;
    public TMP_Text label;

    private BuildMenuUI owner;

    public void Setup(BuildMenuUI menu)
    {
        owner = menu;

        if (label != null)
            label.text = displayName;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
    }

    private void HandleClick()
    {
        if (owner != null)
            owner.SelectItem(itemId, displayName);
    }
}
