using UnityEngine;

public class SceneCameraSettingsRegister : MonoBehaviour
{
    private void Start()
    {
        if (GameSettingsRuntime.Instance != null)
            GameSettingsRuntime.Instance.RegisterTargetCamera(GetComponent<Camera>());
    }
}