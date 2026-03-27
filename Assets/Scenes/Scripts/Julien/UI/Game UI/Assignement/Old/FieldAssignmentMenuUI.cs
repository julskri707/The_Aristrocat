using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class FieldAssignmentMenuUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text selectedFieldText;
    [SerializeField] private Transform workerButtonRoot;
    [SerializeField] private WorkerAssignmentButtonUI workerButtonPrefab;
    [SerializeField] private WorkerAssignment[] workers;

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;

    private readonly List<WorkerAssignmentButtonUI> spawnedButtons = new List<WorkerAssignmentButtonUI>();
    private ResourceTickBehaviour selectedField;

    private void Start()
    {
        RebuildWorkerButtons();
        RefreshFieldLabel();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    public void SetSelectedField(ResourceTickBehaviour field)
    {
        selectedField = field;
        RefreshFieldLabel();
    }

    public void AssignWorker(WorkerAssignment worker)
    {
        if (worker == null || selectedField == null)
        {
            Debug.LogWarning("[FieldAssignmentMenuUI] Worker oder Feld fehlt.");
            return;
        }

        worker.AssignTo(selectedField);
        RefreshFieldLabel();
    }

    public void UnassignWorker(WorkerAssignment worker)
    {
        if (worker == null)
            return;

        worker.AssignTo(null);
        RefreshFieldLabel();
    }

    public void Toggle()
    {
        if (panelRoot != null)
            panelRoot.SetActive(!panelRoot.activeSelf);
    }

    public void RebuildWorkerButtons()
    {
        if (workerButtonRoot == null || workerButtonPrefab == null)
            return;

        for (int i = workerButtonRoot.childCount - 1; i >= 0; i--)
            Destroy(workerButtonRoot.GetChild(i).gameObject);

        spawnedButtons.Clear();

        if (workers == null)
            return;

        for (int i = 0; i < workers.Length; i++)
        {
            if (workers[i] == null)
                continue;

            WorkerAssignmentButtonUI button = Instantiate(workerButtonPrefab, workerButtonRoot);
            button.Setup(this, workers[i]);
            spawnedButtons.Add(button);
        }
    }

    private void RefreshFieldLabel()
    {
        if (selectedFieldText == null)
            return;

        selectedFieldText.text = selectedField == null
            ? "Feld: keines ausgewählt"
            : $"Feld: {selectedField.name}";
    }
}
