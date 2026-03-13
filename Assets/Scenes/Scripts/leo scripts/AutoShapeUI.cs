using UnityEngine;

public class AutoShapeUI : MonoBehaviour
{
    [Header("Target")]
    public WallDrawInput wall;

    [Header("UI")]
    public bool showUI = true;
    public KeyCode toggleUIKey = KeyCode.Tab;

    public Vector2 panelPos = new Vector2(16, 16);
    public Vector2 panelSize = new Vector2(340, 380);

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
            GUI.Label(
                new Rect(panelPos.x + 10, panelPos.y + 30, panelSize.x - 20, 40),
                "Assigne 'Wall' dans l'Inspector (WallDrawInput)."
            );
            return;
        }

        Rect r = new Rect(panelPos.x, panelPos.y, panelSize.x, panelSize.y);
        GUI.Box(r, "Auto Shapes (TinyGlade-like)");

        float x = r.x + 12;
        float y = r.y + 28;

        wall.enableAutoShapes = GUI.Toggle(
            new Rect(x, y, 280, 22),
            wall.enableAutoShapes,
            "Enable AutoShapes (Master)"
        );
        y += 26;

        GUI.enabled = wall.enableAutoShapes;

        wall.autoStraightLine = GUI.Toggle(
            new Rect(x, y, 280, 22),
            wall.autoStraightLine,
            "Auto Straight Line"
        );
        y += 22;

        wall.autoCircle = GUI.Toggle(
            new Rect(x, y, 280, 22),
            wall.autoCircle,
            "Auto Circle"
        );
        y += 22;

        wall.autoRectangle = GUI.Toggle(
            new Rect(x, y, 280, 22),
            wall.autoRectangle,
            "Auto Rectangle / Square"
        );
        y += 22;

        wall.autoTriangle = GUI.Toggle(
            new Rect(x, y, 280, 22),
            wall.autoTriangle,
            "Auto Triangle"
        );
        y += 26;

        wall.requireClosedLoop = GUI.Toggle(
            new Rect(x, y, 280, 22),
            wall.requireClosedLoop,
            "Require Closed Loop"
        );
        y += 28;

        GUI.Label(new Rect(x, y, 280, 18), "Tolerance: " + wall.tolerance.ToString("0.00"));
        wall.tolerance = GUI.HorizontalSlider(
            new Rect(x, y + 18, 280, 18),
            wall.tolerance,
            0.02f,
            0.35f
        );
        y += 42;

        GUI.Label(new Rect(x, y, 280, 18), "Line Strictness: " + wall.straightLineToleranceMultiplier.ToString("0.00"));
        wall.straightLineToleranceMultiplier = GUI.HorizontalSlider(
            new Rect(x, y + 18, 280, 18),
            wall.straightLineToleranceMultiplier,
            0.01f,
            0.20f
        );
        y += 42;

        GUI.Label(new Rect(x, y, 280, 18), "Circle Strictness: " + wall.circleStrictnessMultiplier.ToString("0.00"));
        wall.circleStrictnessMultiplier = GUI.HorizontalSlider(
            new Rect(x, y + 18, 280, 18),
            wall.circleStrictnessMultiplier,
            0.01f,
            0.25f
        );
        y += 42;

        GUI.Label(new Rect(x, y, 280, 18), "Triangle Tolerance: " + wall.triangleToleranceMultiplier.ToString("0.00"));
        wall.triangleToleranceMultiplier = GUI.HorizontalSlider(
            new Rect(x, y + 18, 280, 18),
            wall.triangleToleranceMultiplier,
            0.5f,
            8.0f
        );
        y += 42;

        GUI.Label(new Rect(x, y, 280, 18), "Triangle Max Apex Angle: " + wall.roundedTriangleMaxApexAngle.ToString("0"));
        wall.roundedTriangleMaxApexAngle = GUI.HorizontalSlider(
            new Rect(x, y + 18, 280, 18),
            wall.roundedTriangleMaxApexAngle,
            40f,
            150f
        );
        y += 42;

        GUI.Label(new Rect(x, y, 280, 18), "Circle Resolution: " + wall.circleResolution);
        wall.circleResolution = Mathf.RoundToInt(GUI.HorizontalSlider(
            new Rect(x, y + 18, 280, 18),
            wall.circleResolution,
            16,
            128
        ));
        y += 40;

        GUI.enabled = true;

        GUI.Label(
            new Rect(x, r.yMax - 22, panelSize.x - 24, 18),
            "Tab = show/hide UI"
        );
    }
}