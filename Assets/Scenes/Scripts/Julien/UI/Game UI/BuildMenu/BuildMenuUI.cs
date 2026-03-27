using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class BuildMenuUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text selectedLabel;
    [SerializeField] private BuildMenuItemButton[] buildButtons;

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.B;
    [SerializeField] private bool openOnStart = false;

    public string SelectedItemId { get; private set; }
    public string SelectedItemName { get; private set; }

    public event Action<string> OnBuildItemSelected;

    private void Start()
    {
        SetOpen(openOnStart);

        if (buildButtons != null)
        {
            for (int i = 0; i < buildButtons.Length; i++)
            {
                if (buildButtons[i] != null)
                    buildButtons[i].Setup(this);
            }
        }

        RefreshLabel();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    public void SelectItem(string itemId, string displayName)
    {
        SelectedItemId = itemId;
        SelectedItemName = string.IsNullOrWhiteSpace(displayName) ? itemId : displayName;

        RefreshLabel();
        OnBuildItemSelected?.Invoke(SelectedItemId);
    }

    public void ClearSelection()
    {
        SelectedItemId = string.Empty;
        SelectedItemName = string.Empty;
        RefreshLabel();
    }

    public void Toggle()
    {
        bool isOpen = panelRoot != null && panelRoot.activeSelf;
        SetOpen(!isOpen);
    }

    public void SetOpen(bool open)
    {
        if (panelRoot != null)
            panelRoot.SetActive(open);
    }

    private void RefreshLabel()
    {
        if (selectedLabel == null)
            return;

        selectedLabel.text = string.IsNullOrWhiteSpace(SelectedItemName)
            ? "Ausgewählt: Nichts"
            : $"Ausgewählt: {SelectedItemName}";
    }
}
