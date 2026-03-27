using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class WorkerAssignmentButtonUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button button;

    private WorkerAssignment worker;
    private FieldAssignmentMenuUI owner;

    public void Setup(FieldAssignmentMenuUI menu, WorkerAssignment targetWorker)
    {
        owner = menu;
        worker = targetWorker;

        if (label != null)
            label.text = targetWorker != null ? targetWorker.name : "Missing Worker";

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
    }

    private void HandleClick()
    {
        if (owner != null && worker != null)
            owner.AssignWorker(worker);
    }
}
