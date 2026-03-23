using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class SceneBrightnessOverlayRegister : MonoBehaviour
{
    private void Start()
    {
        if (GameSettingsRuntime.Instance != null)
            GameSettingsRuntime.Instance.RegisterBrightnessOverlay(GetComponent<Image>());
    }
}