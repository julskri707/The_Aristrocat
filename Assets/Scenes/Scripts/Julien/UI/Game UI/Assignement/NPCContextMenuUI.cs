using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NPCContextMenuUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private RectTransform root;

    [Header("Buttons")]
    [SerializeField] private Button assignButton;
    [SerializeField] private Button unassignButton;
    [SerializeField] private Button infoButton;

    [Header("Optional")]
    [SerializeField] private TMP_Text titleText;

    private NPCAssignmentPanelController owner;
    private NPCAssignmentUIAdapter currentNpc;

    private void Awake()
    {
        HideImmediate();

        if (assignButton != null) assignButton.onClick.AddListener(HandleAssign);
        if (unassignButton != null) unassignButton.onClick.AddListener(HandleUnassign);
        if (infoButton != null) infoButton.onClick.AddListener(HandleInfo);
    }

    public void Open(NPCAssignmentPanelController panelOwner, NPCAssignmentUIAdapter npc, Vector3 screenPosition)
    {
        owner = panelOwner;
        currentNpc = npc;

        if (titleText != null)
            titleText.text = npc != null ? npc.GetDisplayName() : "NPC";

        if (root != null)
        {
            root.gameObject.SetActive(true);
            root.position = screenPosition;
        }
    }

    public void HideImmediate()
    {
        if (root != null)
            root.gameObject.SetActive(false);
    }

    private void HandleAssign()
    {
        owner?.BeginAssignModeFromContext(currentNpc);
        HideImmediate();
    }

    private void HandleUnassign()
    {
        owner?.UnassignNpc(currentNpc);
        HideImmediate();
    }

    private void HandleInfo()
    {
        owner?.OpenNpcInfo(currentNpc);
        HideImmediate();
    }
}
