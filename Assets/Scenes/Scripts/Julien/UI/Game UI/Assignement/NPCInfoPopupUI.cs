using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NPCInfoPopupUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        Hide();
    }

    public void Show(NPCAssignmentUIAdapter npc)
    {
        if (root == null) return;

        root.SetActive(true);

        if (titleText != null)
            titleText.text = npc != null ? npc.GetDisplayName() : "NPC";

        if (bodyText != null)
            bodyText.text = npc != null ? npc.BuildInfoText() : "Keine Infos.";
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }
}
