using TMPro;
using UnityEngine;

public class PickupPromptUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text promptText;

    private void Awake()
    {
        Hide();
    }

    public void Show(string text)
    {
        if (root != null)
            root.SetActive(true);

        if (promptText != null)
            promptText.text = text;
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }
}
