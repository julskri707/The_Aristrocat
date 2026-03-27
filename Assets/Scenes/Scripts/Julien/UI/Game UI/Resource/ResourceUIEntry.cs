using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class ResourceUIEntry : MonoBehaviour
{
    [SerializeField] private string resourceId = "Gold";
    [SerializeField] private TMP_Text label;
    [SerializeField] private string prefix = "";
    [SerializeField] private string suffix = "";

    public string ResourceId => resourceId;

    public void SetValue(float amount)
    {
        if (label != null)
            label.text = $"{prefix}{Mathf.RoundToInt(amount)}{suffix}";
    }
}
