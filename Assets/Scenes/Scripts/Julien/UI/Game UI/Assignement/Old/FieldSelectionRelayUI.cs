using UnityEngine;

[DisallowMultipleComponent]
public class FieldSelectionRelayUI : MonoBehaviour
{
    [SerializeField] private FieldAssignmentMenuUI assignmentMenu;

    public void SelectField(ResourceTickBehaviour field)
    {
        if (assignmentMenu != null)
            assignmentMenu.SetSelectedField(field);
    }
}
