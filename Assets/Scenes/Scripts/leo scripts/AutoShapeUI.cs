using UnityEngine;

public class AutoShapeUI : MonoBehaviour
{
    [Header("Target")]
    public WallDrawInput wall;

    [Header("UI")]
    public bool showUI = true;
    public KeyCode toggleUIKey = KeyCode.Tab;

    public Vector2 panelPos = new Vector2(16, 16);
    public Vector2 panelSize = new Vector2(320, 250);

    [Header("Formes préréglées (centre écran → sol)")]
    [Min(0.05f)] public float uiPresetCircleRadiusM = 2f;
    [Min(0.05f)] public float uiPresetSquareSideM = 3f;
    [Min(0.05f)] public float uiPresetTriangleSideM = 3f;

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
            GUI.Box(new Rect(panelPos.x, panelPos.y, panelSize.x, 80), "Wall draw");
            GUI.Label(
                new Rect(panelPos.x + 10, panelPos.y + 30, panelSize.x - 20, 40),
                "Assigne 'Wall' dans l'Inspector (WallDrawInput)."
            );
            return;
        }

        Rect r = new Rect(panelPos.x, panelPos.y, panelSize.x, panelSize.y);
        GUI.Box(r, "Wall draw");

        float x = r.x + 12;
        float y = r.y + 30;

        string autoLabel = wall.enableAutoShapes
            ? "Auto-formes: ON"
            : "Auto-formes: OFF";
        if (GUI.Button(new Rect(x, y, 280, 26), autoLabel))
            wall.enableAutoShapes = !wall.enableAutoShapes;

        y += 32;
        string gridLabel = wall.enableGridSnap
            ? "Snap grille: ON"
            : "Snap grille: OFF";
        if (GUI.Button(new Rect(x, y, 280, 26), gridLabel))
            wall.enableGridSnap = !wall.enableGridSnap;

        y += 36;
        WallBuildController bc = wall.wallBuild;
        GUI.enabled = bc != null && bc.wallPrefab != null;
        if (GUI.Button(new Rect(x, y, 88, 26), "Cercle") && bc != null)
            bc.SpawnUiPresetCircle(uiPresetCircleRadiusM);
        if (GUI.Button(new Rect(x + 96, y, 88, 26), "Carré") && bc != null)
            bc.SpawnUiPresetSquare(uiPresetSquareSideM);
        if (GUI.Button(new Rect(x + 192, y, 88, 26), "Triangle") && bc != null)
            bc.SpawnUiPresetTriangle(uiPresetTriangleSideM);
        GUI.enabled = true;

        GUI.Label(
            new Rect(x, r.yMax - 22, panelSize.x - 24, 18),
            "Tab = show/hide UI"
        );
    }
}