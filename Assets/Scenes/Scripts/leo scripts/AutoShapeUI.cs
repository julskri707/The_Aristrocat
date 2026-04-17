using UnityEngine;

public class AutoShapeUI : MonoBehaviour
{
    [Header("Target")]
    public WallDrawInput wall;

    [Header("UI")]
    public bool showUI = true;
    public KeyCode toggleUIKey = KeyCode.Tab;

    public Vector2 panelPos = new Vector2(16, 16);
    public Vector2 panelSize = new Vector2(320, 110);

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
            GUI.Box(new Rect(panelPos.x, panelPos.y, panelSize.x, 80), "Grid");
            GUI.Label(
                new Rect(panelPos.x + 10, panelPos.y + 30, panelSize.x - 20, 40),
                "Assigne 'Wall' dans l'Inspector (WallDrawInput)."
            );
            return;
        }

        Rect r = new Rect(panelPos.x, panelPos.y, panelSize.x, panelSize.y);
        GUI.Box(r, "Grid");

        float x = r.x + 12;
        float y = r.y + 30;

        string gridButtonLabel = wall.enableGridSnap
            ? "Grid: ON (cliquer pour desactiver)"
            : "Grid: OFF (cliquer pour activer)";
        if (GUI.Button(new Rect(x, y, 280, 26), gridButtonLabel))
            wall.enableGridSnap = !wall.enableGridSnap;

        GUI.Label(
            new Rect(x, r.yMax - 22, panelSize.x - 24, 18),
            "Tab = show/hide UI"
        );
    }
}