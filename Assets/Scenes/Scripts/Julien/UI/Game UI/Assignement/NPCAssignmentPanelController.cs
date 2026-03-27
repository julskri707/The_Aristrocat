using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NPCAssignmentPanelController : MonoBehaviour
{
    [Header("Lists")]
    [SerializeField] private Transform fieldListRoot;
    [SerializeField] private Transform npcListRoot;
    [SerializeField] private AssignmentListItemUI listItemPrefab;

    [Header("Foldouts")]
    [SerializeField] private Button toggleFieldsButton;
    [SerializeField] private Button toggleNpcsButton;
    [SerializeField] private TMP_Text toggleFieldsLabel;
    [SerializeField] private TMP_Text toggleNpcsLabel;
    [SerializeField] private GameObject fieldContentRoot;
    [SerializeField] private GameObject npcContentRoot;

    [Header("Status")]
    [SerializeField] private TMP_Text selectedNpcText;
    [SerializeField] private TMP_Text selectedFieldText;
    [SerializeField] private TMP_Text hintText;

    [Header("Popups")]
    [SerializeField] private NPCContextMenuUI contextMenu;
    [SerializeField] private NPCInfoPopupUI infoPopup;

    [Header("Refresh")]
    [SerializeField] private bool refreshEverySecond = true;
    [SerializeField] private float refreshInterval = 1f;

    private readonly List<AssignmentListItemUI> spawnedFieldItems = new List<AssignmentListItemUI>();
    private readonly List<AssignmentListItemUI> spawnedNpcItems = new List<AssignmentListItemUI>();

    private readonly List<AssignableWorkAreaUIAdapter> cachedAreas = new List<AssignableWorkAreaUIAdapter>();
    private readonly List<NPCAssignmentUIAdapter> cachedNpcs = new List<NPCAssignmentUIAdapter>();

    private NPCAssignmentUIAdapter selectedNpc;
    private AssignableWorkAreaUIAdapter selectedArea;

    private bool fieldsExpanded = true;
    private bool npcsExpanded = true;
    private bool assignModeActive;
    private float refreshTimer;

    private void Awake()
    {
        if (toggleFieldsButton != null) toggleFieldsButton.onClick.AddListener(ToggleFields);
        if (toggleNpcsButton != null) toggleNpcsButton.onClick.AddListener(ToggleNpcs);

        ApplyFoldoutState();
        RefreshAll();
        UpdateSelectionTexts();
        SetHint("Rechtsklick auf einen NPC für Assign / Deassign / Infos.");
    }

    private void Update()
    {
        if (refreshEverySecond)
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = refreshInterval;
                RefreshAll();
            }
        }

        if (assignModeActive && Input.GetMouseButtonDown(1))
        {
            assignModeActive = false;
            SetHint("Zuweisung abgebrochen.");
        }
    }

    public void BeginAssignModeFromContext(NPCAssignmentUIAdapter npc)
    {
        if (npc == null) return;

        SelectNpc(npc);
        assignModeActive = true;
        SetHint("Assign-Modus aktiv: Jetzt links auf ein Feld oder einen Forst in der Liste klicken.");
    }

    public void UnassignNpc(NPCAssignmentUIAdapter npc)
    {
        if (npc == null || npc.WorkerAssignment == null) return;

        npc.WorkerAssignment.AssignTo(null);

        if (selectedNpc == npc)
            assignModeActive = false;

        RefreshAll();
        UpdateSelectionTexts();
        SetHint(npc.GetDisplayName() + " ist jetzt arbeitslos.");
    }

    public void OpenNpcInfo(NPCAssignmentUIAdapter npc)
    {
        if (infoPopup != null)
            infoPopup.Show(npc);
    }

    public void RefreshAll()
    {
        CacheAreas();
        CacheNpcs();
        RebuildFieldList();
        RebuildNpcList();
        UpdateSelectionTexts();
    }

    private void CacheAreas()
    {
        cachedAreas.Clear();
        AssignableWorkAreaUIAdapter[] areas = FindObjectsByType<AssignableWorkAreaUIAdapter>(FindObjectsSortMode.None);
        for (int i = 0; i < areas.Length; i++)
        {
            if (areas[i] != null && areas[i].TargetBehaviour != null)
                cachedAreas.Add(areas[i]);
        }
    }

    private void CacheNpcs()
    {
        cachedNpcs.Clear();
        NPCAssignmentUIAdapter[] npcs = FindObjectsByType<NPCAssignmentUIAdapter>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            if (npcs[i] != null && npcs[i].WorkerAssignment != null)
                cachedNpcs.Add(npcs[i]);
        }
    }

    private void RebuildFieldList()
    {
        ClearItems(spawnedFieldItems);

        if (fieldListRoot == null || listItemPrefab == null) return;

        for (int i = 0; i < cachedAreas.Count; i++)
        {
            AssignableWorkAreaUIAdapter area = cachedAreas[i];
            AssignmentListItemUI item = Instantiate(listItemPrefab, fieldListRoot);
            item.Setup(area.GetDisplayName(), area, HandleFieldLeftClick, HandleFieldRightClick);
            item.Select(area == selectedArea);
            spawnedFieldItems.Add(item);
        }
    }

    private void RebuildNpcList()
    {
        ClearItems(spawnedNpcItems);

        if (npcListRoot == null || listItemPrefab == null) return;

        for (int i = 0; i < cachedNpcs.Count; i++)
        {
            NPCAssignmentUIAdapter npc = cachedNpcs[i];
            string label = npc.GetDisplayName() + "  |  " + npc.GetCurrentJobText();
            AssignmentListItemUI item = Instantiate(listItemPrefab, npcListRoot);
            item.Setup(label, npc, HandleNpcLeftClick, HandleNpcRightClick);
            item.Select(npc == selectedNpc);
            spawnedNpcItems.Add(item);
        }
    }

    private void HandleFieldLeftClick(AssignmentListItemUI.PointerEventDataEx evt)
    {
        AssignableWorkAreaUIAdapter area = evt.payload as AssignableWorkAreaUIAdapter;
        if (area == null) return;

        if (selectedArea == area)
        {
            DeselectArea();
            assignModeActive = false;
            SetHint("Feld/Forst-Auswahl aufgehoben.");
            return;
        }

        SelectArea(area);

        if (assignModeActive && selectedNpc != null && selectedNpc.WorkerAssignment != null)
        {
            selectedNpc.WorkerAssignment.AssignTo(area.TargetBehaviour);
            assignModeActive = false;
            SetHint(selectedNpc.GetDisplayName() + " wurde zugewiesen: " + area.GetDisplayName());
            RefreshAll();
        }
        else
        {
            SetHint("Ausgewählt: " + area.GetDisplayName());
        }
    }

    private void HandleFieldRightClick(AssignmentListItemUI.PointerEventDataEx evt)
    {
        HandleFieldLeftClick(evt);
    }

    private void HandleNpcLeftClick(AssignmentListItemUI.PointerEventDataEx evt)
    {
        NPCAssignmentUIAdapter npc = evt.payload as NPCAssignmentUIAdapter;
        if (npc == null) return;

        if (selectedNpc == npc)
        {
            DeselectNpc();
            assignModeActive = false;
            SetHint("NPC-Auswahl aufgehoben.");
            return;
        }

        SelectNpc(npc);
        SetHint("NPC ausgewählt: " + npc.GetDisplayName());
    }

    private void HandleNpcRightClick(AssignmentListItemUI.PointerEventDataEx evt)
    {
        NPCAssignmentUIAdapter npc = evt.payload as NPCAssignmentUIAdapter;
        if (npc == null) return;

        SelectNpc(npc);

        if (contextMenu != null)
            contextMenu.Open(this, selectedNpc, evt.screenPosition);
    }

    private void SelectNpc(NPCAssignmentUIAdapter npc)
    {
        selectedNpc = npc;
        UpdateSelectionTexts();
        RefreshSelectionVisuals();
    }

    private void SelectArea(AssignableWorkAreaUIAdapter area)
    {
        selectedArea = area;
        UpdateSelectionTexts();
        RefreshSelectionVisuals();
    }

    private void DeselectNpc()
    {
        selectedNpc = null;
        UpdateSelectionTexts();
        RefreshSelectionVisuals();
    }

    private void DeselectArea()
    {
        selectedArea = null;
        UpdateSelectionTexts();
        RefreshSelectionVisuals();
    }

    private void RefreshSelectionVisuals()
    {
        for (int i = 0; i < spawnedNpcItems.Count; i++)
        {
            NPCAssignmentUIAdapter npc = spawnedNpcItems[i].Payload as NPCAssignmentUIAdapter;
            spawnedNpcItems[i].Select(npc == selectedNpc);
        }

        for (int i = 0; i < spawnedFieldItems.Count; i++)
        {
            AssignableWorkAreaUIAdapter area = spawnedFieldItems[i].Payload as AssignableWorkAreaUIAdapter;
            spawnedFieldItems[i].Select(area == selectedArea);
        }
    }

    private void UpdateSelectionTexts()
    {
        if (selectedNpcText != null)
            selectedNpcText.text = selectedNpc != null ? ("NPC: " + selectedNpc.GetDisplayName()) : "NPC: -";

        if (selectedFieldText != null)
            selectedFieldText.text = selectedArea != null ? ("Feld/Forst: " + selectedArea.GetDisplayName()) : "Feld/Forst: -";
    }

    private void ToggleFields()
    {
        fieldsExpanded = !fieldsExpanded;
        ApplyFoldoutState();
    }

    private void ToggleNpcs()
    {
        npcsExpanded = !npcsExpanded;
        ApplyFoldoutState();
    }

    private void ApplyFoldoutState()
    {
        if (fieldContentRoot != null) fieldContentRoot.SetActive(fieldsExpanded);
        if (npcContentRoot != null) npcContentRoot.SetActive(npcsExpanded);

        if (toggleFieldsLabel != null) toggleFieldsLabel.text = (fieldsExpanded ? "▼ " : "▶ ") + "Felder / Forst";
        if (toggleNpcsLabel != null) toggleNpcsLabel.text = (npcsExpanded ? "▼ " : "▶ ") + "NPCs";
    }

    private void SetHint(string text)
    {
        if (hintText != null)
            hintText.text = text;
    }

    private void ClearItems(List<AssignmentListItemUI> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                Destroy(list[i].gameObject);
        }
        list.Clear();
    }
}
