using UnityEngine;

[DisallowMultipleComponent]
public class NPCAssignmentUIAdapter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WorkerAssignment workerAssignment;
    [SerializeField] private NPCIdentityRandomProfile identity;

    [Header("Optional Labels")]
    [SerializeField] private string npcTypeLabel = "Bauer";

    public WorkerAssignment WorkerAssignment => workerAssignment;
    public NPCIdentityRandomProfile Identity => identity;

    private void Reset()
    {
        if (workerAssignment == null) workerAssignment = GetComponent<WorkerAssignment>();
        if (identity == null) identity = GetComponent<NPCIdentityRandomProfile>();
    }

    private void OnValidate()
    {
        if (workerAssignment == null) workerAssignment = GetComponent<WorkerAssignment>();
        if (identity == null) identity = GetComponent<NPCIdentityRandomProfile>();
    }

    public string GetDisplayName()
    {
        if (identity != null && !string.IsNullOrWhiteSpace(identity.DisplayName))
            return identity.DisplayName;

        return gameObject.name;
    }

    public string GetCurrentJobText()
    {
        if (workerAssignment == null)
            return npcTypeLabel + " - Kein WorkerAssignment";

        ResourceTickBehaviour assigned = workerAssignment.assignedField;
        if (assigned == null)
            return "Arbeitslos";

        AssignableWorkAreaUIAdapter area = assigned.GetComponent<AssignableWorkAreaUIAdapter>();
        if (area != null)
        {
            if (area.AreaType == AssignableWorkAreaUIAdapter.WorkAreaType.Forst)
                return "Förster - " + area.GetDisplayName();

            return "Bauer - " + area.GetDisplayName();
        }

        return npcTypeLabel + " - " + assigned.gameObject.name;
    }

    public string BuildInfoText()
    {
        var info = identity != null ? identity.GetInfo() : default;
        string nameValue = identity != null ? info.displayName : GetDisplayName();
        string ageValue = identity != null ? info.age.ToString() : "-";
        string originValue = identity != null ? info.origin : "-";
        string talentValue = identity != null ? info.talent : "-";
        string traitValue = identity != null ? info.trait : "-";

        return
            "Name: " + nameValue + "\n" +
            "Alter: " + ageValue + "\n" +
            "Herkunft: " + originValue + "\n" +
            "Talent: " + talentValue + "\n" +
            "Eigenschaft: " + traitValue + "\n" +
            "Status: " + GetCurrentJobText();
    }
}
