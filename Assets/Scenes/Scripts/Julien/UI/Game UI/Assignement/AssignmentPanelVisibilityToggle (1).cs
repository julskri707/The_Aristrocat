using UnityEngine;

public class AssignmentPanelVisibilityToggle : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private GameObject panelRoot;

    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.E;

    [Header("State")]
    [SerializeField] private bool startHidden = true;

    [Header("Optional")]
    [SerializeField] private FirstThirdPersonController playerController;

    private bool isVisible;

    private void Awake()
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("[AssignmentPanelVisibilityToggle] panelRoot is missing on " + gameObject.name, this);
            return;
        }

        isVisible = !startHidden;
        ApplyState();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            Toggle();
        }
    }

    public void Show()
    {
        isVisible = true;
        ApplyState();
    }

    public void Hide()
    {
        isVisible = false;
        ApplyState();
    }

    public void Toggle()
    {
        isVisible = !isVisible;
        ApplyState();
    }

    private void ApplyState()
    {
        if (panelRoot != null)
            panelRoot.SetActive(isVisible);

        if (playerController != null)
            playerController.SetUICursorUnlockedFromExternal(isVisible);
    }
}
