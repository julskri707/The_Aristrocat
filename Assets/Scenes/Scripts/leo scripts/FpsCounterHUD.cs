using UnityEngine;

/// <summary>
/// Affiche les FPS (moyenne lissée) en haut à droite de l’écran (IMGUI).
/// Ajoute ce composant sur n’importe quel GameObject actif dans la scène (ex. une caméra ou un objet « Bootstrap »).
/// </summary>
public sealed class FpsCounterHUD : MonoBehaviour
{
    [SerializeField] bool visible = true;
    [SerializeField, Min(8)] int fontSize = 22;
    [SerializeField, Min(4f)] float margin = 14f;
    [SerializeField, Range(0.02f, 1f)] float smoothSeconds = 0.25f;

    float _smoothedFps = 60f;
    GUIStyle _style;

    void Update()
    {
        if (!visible)
            return;

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
            return;

        float instant = 1f / dt;
        float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.02f, smoothSeconds));
        _smoothedFps = Mathf.Lerp(_smoothedFps, instant, t);
    }

    void OnGUI()
    {
        if (!visible)
            return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperRight,
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        const float w = 220f;
        float h = Mathf.Max(28f, fontSize + 10f);
        var rect = new Rect(Screen.width - w - margin, margin, w, h);
        GUI.Label(rect, $"{_smoothedFps:0} FPS", _style);
    }
}
