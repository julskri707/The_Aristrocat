using UnityEngine;

[DisallowMultipleComponent]
public class AssignableWorkAreaUIAdapter : MonoBehaviour
{
    public enum WorkAreaType
    {
        Feld,
        Forst
    }

    [Header("Optional Direct References")]
    [SerializeField] private ResourceTickBehaviour targetBehaviour;
    [SerializeField] private FieldArea fieldArea;
    [SerializeField] private WoodcutterWorkArea woodcutterWorkArea;

    [Header("UI")]
    [SerializeField] private string displayNameOverride;
    [SerializeField] private WorkAreaType areaType = WorkAreaType.Feld;

    public ResourceTickBehaviour TargetBehaviour => targetBehaviour;
    public FieldArea FieldArea => fieldArea;
    public WoodcutterWorkArea WoodcutterWorkArea => woodcutterWorkArea;
    public WorkAreaType AreaType => areaType;

    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayNameOverride))
            return displayNameOverride;

        string typeLabel = areaType == WorkAreaType.Forst ? "Forst" : "Feld";

        if (woodcutterWorkArea != null)
            return typeLabel + " - " + woodcutterWorkArea.gameObject.name;

        return typeLabel + " - " + gameObject.name;
    }

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void Awake()
    {
        AutoAssignReferences();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
    }

    private void AutoAssignReferences()
    {
        if (fieldArea == null)
        {
            fieldArea = GetComponent<FieldArea>();
            if (fieldArea == null)
                fieldArea = GetComponentInParent<FieldArea>();
            if (fieldArea == null)
                fieldArea = GetComponentInChildren<FieldArea>();
        }

        if (woodcutterWorkArea == null)
        {
            woodcutterWorkArea = GetComponent<WoodcutterWorkArea>();
            if (woodcutterWorkArea == null)
                woodcutterWorkArea = GetComponentInParent<WoodcutterWorkArea>();
            if (woodcutterWorkArea == null)
                woodcutterWorkArea = GetComponentInChildren<WoodcutterWorkArea>();
        }

        if (targetBehaviour == null)
        {
            if (woodcutterWorkArea != null)
                targetBehaviour = woodcutterWorkArea.GetAssignmentField();

            if (targetBehaviour == null)
            {
                targetBehaviour = GetComponent<ResourceTickBehaviour>();
                if (targetBehaviour == null)
                    targetBehaviour = GetComponentInParent<ResourceTickBehaviour>();
                if (targetBehaviour == null)
                    targetBehaviour = GetComponentInChildren<ResourceTickBehaviour>();
            }
        }

        if (woodcutterWorkArea != null)
            areaType = WorkAreaType.Forst;
    }
}
