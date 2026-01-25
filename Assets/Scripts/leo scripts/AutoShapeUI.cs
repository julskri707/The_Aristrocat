using UnityEngine;

public class AutoShapeUI : MonoBehaviour
{
    [Header("Target")]
    public WallDrawInput wall; // glisse ton WallDrawer ici

    [Header("UI")]
    public bool showUI = true;
    public KeyCode toggleUIKey = KeyCode.Tab;

    public Vector2 panelPos = new Vector2(16, 16);
    public Vector2 panelSize = new Vector2(280, 240);

    void Update()
    {
        if (Input.GetKeyDown(toggleUIKey))
            showUI = !showUI;
    }

    void OnGUI()
    {
        if (!showUI) return;
        if (wall == null)
        {
            GUI.Box(new Rect(panelPos.x, panelPos.y, panelSize.x, 80), "Auto Shapes");
            GUI.Label(new Rect(panelPos.x + 10, panelPos.y + 30, panelSize.x - 20, 40),
                "Assigne 'Wall' dans l'Inspector (WallDrawInput).");
            return;
        }

        Rect r = new Rect(panelPos.x, panelPos.y, panelSize.x, panelSize.y);
        GUI.Box(r, "Auto Shapes (TinyGlade-like)");

        float x = r.x + 12;
        float y = r.y + 28;

        wall.enableAutoShapes = GUI.Toggle(new Rect(x, y, 240, 22), wall.enableAutoShapes, "Enable AutoShapes (Master)");
        y += 26;

        GUI.enabled = wall.enableAutoShapes;

        wall.autoCircle = GUI.Toggle(new Rect(x, y, 240, 22), wall.autoCircle, "Auto Circle");
        y += 22;

        wall.autoRectangle = GUI.Toggle(new Rect(x, y, 240, 22), wall.autoRectangle, "Auto Rectangle / Square");
        y += 22;

        wall.autoTriangle = GUI.Toggle(new Rect(x, y, 240, 22), wall.autoTriangle, "Auto Triangle");
        y += 26;

        wall.requireClosedLoop = GUI.Toggle(new Rect(x, y, 240, 22), wall.requireClosedLoop, "Require Closed Loop");
        y += 30;

        GUI.Label(new Rect(x, y, 240, 18), $"Tolerance: {wall.tolerance:0.00}");
        wall.tolerance = GUI.HorizontalSlider(new Rect(x, y + 18, 240, 18), wall.tolerance, 0.02f, 0.35f);
        y += 46;

        GUI.Label(new Rect(x, y, 240, 18), $"Circle Resolution: {wall.circleResolution}");
        wall.circleResolution = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, y + 18, 240, 18), wall.circleResolution, 16, 128));

        GUI.enabled = true;

        // petit rappel
        GUI.Label(new Rect(x, r.yMax - 22, panelSize.x - 24, 18), "Tab = show/hide UI");
    }
}
