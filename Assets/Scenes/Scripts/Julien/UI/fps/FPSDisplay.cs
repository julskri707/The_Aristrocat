using UnityEngine;

public class FPSDisplay : MonoBehaviour
{
    [SerializeField] private int fontSize = 24;
    [SerializeField] private Vector2 position = new Vector2(10f, 10f);

    private float deltaTime;

    private void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    private void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = fontSize;
        style.normal.textColor = Color.white;

        Rect rect = new Rect(position.x, position.y, 200f, 40f);

        float fps = 1.0f / deltaTime;
        string text = "FPS: " + Mathf.Ceil(fps).ToString();

        GUI.Label(rect, text, style);
    }
}
