using UnityEngine;

[DisallowMultipleComponent]
public class WallStyleInstance : MonoBehaviour
{
    [Header("Current Style")]
    public WallStyleDefinition currentStyle;

    [SerializeField] private string currentStyleId;
    [SerializeField] private string currentStyleName;

    public string CurrentStyleId => currentStyle != null ? currentStyle.styleId : currentStyleId;
    public string CurrentStyleName => currentStyle != null ? currentStyle.displayName : currentStyleName;

    public void SetCurrentStyle(WallStyleDefinition style)
    {
        currentStyle = style;
        currentStyleId = style != null ? style.styleId : string.Empty;
        currentStyleName = style != null ? style.displayName : string.Empty;
    }
}
