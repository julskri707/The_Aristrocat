using UnityEngine;

[DisallowMultipleComponent]
public class UIToggleBlocker : MonoBehaviour
{
    [SerializeField] private GameObject[] panelsToCheck;
    [SerializeField] private MonoBehaviour[] scriptsToDisableWhileUIOpen;

    private void Update()
    {
        bool anyOpen = false;

        if (panelsToCheck != null)
        {
            for (int i = 0; i < panelsToCheck.Length; i++)
            {
                if (panelsToCheck[i] != null && panelsToCheck[i].activeSelf)
                {
                    anyOpen = true;
                    break;
                }
            }
        }

        if (scriptsToDisableWhileUIOpen == null)
            return;

        for (int i = 0; i < scriptsToDisableWhileUIOpen.Length; i++)
        {
            if (scriptsToDisableWhileUIOpen[i] != null)
                scriptsToDisableWhileUIOpen[i].enabled = !anyOpen;
        }
    }
}
