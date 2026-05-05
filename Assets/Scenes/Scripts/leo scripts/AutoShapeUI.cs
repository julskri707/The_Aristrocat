using UnityEngine;

public class AutoShapeUI : MonoBehaviour
{
    [Header("Target")]
    public WallDrawInput wall;

    [Tooltip("Au démarrage : si les valeurs correspondent encore à d’anciens jeux (2/3/3, 18/27/27, 9/13,5/13,5), les aligne sur 10 m (rayon cercle + côtés).")]
    public bool autoUpgradeLegacyPresetDimensions = true;

    [Header("UI")]
    public bool showUI = true;
    public KeyCode toggleUIKey = KeyCode.Tab;

    public Vector2 panelPos = new Vector2(16, 16);
    public Vector2 panelSize = new Vector2(320, 306);

    [Header("Formes préréglées (centre écran → sol)")]
    [Min(0.05f)] public float uiPresetCircleRadiusM = 10f;
    [Min(0.05f)] public float uiPresetSquareSideM = 10f;
    [Min(0.05f)] public float uiPresetTriangleSideM = 10f;

    void Awake()
    {
        if (!autoUpgradeLegacyPresetDimensions)
            return;

        bool Near(float a, float b, float eps = 0.02f) => Mathf.Abs(a - b) < eps;

        bool TripleNear(float cr, float sq, float tr, float eps = 0.02f) =>
            Near(uiPresetCircleRadiusM, cr, eps) &&
            Near(uiPresetSquareSideM, sq, eps) &&
            Near(uiPresetTriangleSideM, tr, eps);

        if (TripleNear(2f, 3f, 3f, 0.001f) ||
            TripleNear(18f, 27f, 27f) ||
            TripleNear(9f, 13.5f, 13.5f))
        {
            uiPresetCircleRadiusM = 10f;
            uiPresetSquareSideM = 10f;
            uiPresetTriangleSideM = 10f;
        }
    }

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

        const float margin = 12f;
        const float headerBelowTitle = 26f;
        const float rowH = 28f;
        const float rowGap = 10f;
        const float sectionGap = 14f;
        const float hintH = 20f;
        const float presetGap = 12f;
        const float bottomPad = 24f;
        const float imGuiBoxCaptionSlack = 16f;

        float minPanelHeight =
            margin +
            headerBelowTitle +
            rowH + rowGap + rowH +
            sectionGap +
            hintH +
            presetGap +
            rowH +
            bottomPad +
            imGuiBoxCaptionSlack;

        float panelH = Mathf.Max(panelSize.y, minPanelHeight);
        Rect r = new Rect(panelPos.x, panelPos.y, panelSize.x, panelH);
        GUI.Box(r, "Wall draw");

        float innerWidth = r.width - margin * 2f;
        float x = r.x + margin;
        float y = r.y + margin + headerBelowTitle;

        string autoLabel = wall.enableAutoShapes
            ? "Auto-formes: ON"
            : "Auto-formes: OFF";
        if (GUI.Button(new Rect(x, y, innerWidth, rowH), autoLabel))
            wall.enableAutoShapes = !wall.enableAutoShapes;

        y += rowH + rowGap;
        string gridLabel = wall.enableGridSnap
            ? "Snap grille: ON"
            : "Snap grille: OFF";
        if (GUI.Button(new Rect(x, y, innerWidth, rowH), gridLabel))
            wall.enableGridSnap = !wall.enableGridSnap;

        y += rowH + sectionGap;
        GUI.Label(new Rect(x, y, innerWidth, hintH), "Tab : afficher / masquer ce panneau");

        y += hintH + presetGap;
        WallBuildController bc = ResolveWallBuildController();
        GUI.enabled = bc != null && bc.wallPrefab != null;
        float tripGap = 8f;
        float tripW = (innerWidth - tripGap * 2f) / 3f;
        float m = wall != null ? Mathf.Max(0.01f, wall.uiPresetSpawnSizeMultiplier) : 1f;
        if (GUI.Button(new Rect(x, y, tripW, rowH), "Cercle"))
            TrySpawnUiPreset(bc, () => bc.SpawnUiPresetCircle(uiPresetCircleRadiusM * m));
        if (GUI.Button(new Rect(x + tripW + tripGap, y, tripW, rowH), "Carré"))
            TrySpawnUiPreset(bc, () => bc.SpawnUiPresetSquare(uiPresetSquareSideM * m));
        if (GUI.Button(new Rect(x + 2f * (tripW + tripGap), y, tripW, rowH), "Triangle"))
            TrySpawnUiPreset(bc, () => bc.SpawnUiPresetTriangle(uiPresetTriangleSideM * m));
        GUI.enabled = true;
    }

    WallBuildController ResolveWallBuildController()
    {
        if (wall == null)
            return null;
        if (wall.wallBuild != null)
            return wall.wallBuild;
        wall.wallBuild = FindFirstObjectByType<WallBuildController>();
        return wall.wallBuild;
    }

    void TrySpawnUiPreset(WallBuildController bc, System.Action spawn)
    {
        if (bc == null || wall == null || spawn == null)
            return;
        bc.BindWallDrawInput(wall);
        spawn();
    }
}