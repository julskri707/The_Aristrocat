using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Clic droit : « étage » sur toute la maison liée (pivot violet) ou un seul plan (poignée rose).
/// </summary>
public enum HousePivotAddFloorScope
{
    /// <summary>Enveloppe + chaque lot sur <see cref="HouseExteriorEnvelopeSources"/> (pivot violet).</summary>
    EntireLinkedEnsemble = 0,
    /// <summary>Un seul mur / plan (poignée rose).</summary>
    CurrentWallOnly = 1,
}

/// <summary>
/// Menu contextuel (clic droit sur le pivot violet — lot « maison ») : ajouter un toit (placeholder), ajouter un étage.
/// Assigner les références UI dans l’inspecteur (même Canvas que <see cref="LotBuildMenuUI"/> possible).
/// </summary>
public class PivotPointActionsMenuUI : MonoBehaviour
{
    [Header("Scene References")]
    public RectTransform menuRoot;
    public RectTransform panelRoot;
    public Button buttonAddWall;
    public Button buttonAddFloor;
    [Tooltip("Cercle fermé centré sur le lot (même centre que mur intérieur depuis le menu).")]
    public Button buttonPresetCircleAtLotCenter;
    [Tooltip("Triangle équilatéral centré sur le lot.")]
    public Button buttonPresetTriangleAtLotCenter;
    [Tooltip("Optionnel : réactive les lots sources et supprime le mur enveloppe (fusion maison multi-plans).")]
    public Button buttonSplitEnvelopeLots;

    [Header("Optional UI")]
    public Button backgroundCloseButton;
    public Text titleText;

    [Header("Behaviour")]
    public bool closeOnEscape = true;
    public bool closeOnLeftClickOutsidePanel = true;
    public Vector2 panelScreenOffset = new Vector2(18f, -18f);
    public Vector2 screenPadding = new Vector2(18f, 18f);

    [Header("Default labels")]
    public string defaultTitle = "Lot maison";
    [Tooltip("Titre (poignée rose). Vide = defaultTitle.")]
    public string titleSinglePlan = "Un plan de la maison";
    [Tooltip("Titre (pivot violet, ensemble). Vide = defaultTitle (ex. « Lot maison »).")]
    public string titleEntireHouse = "";
    public string defaultAddWallLabel = "Ajouter un toit";
    public string defaultAddFloorLabel = "Ajouter un étage";
    public string defaultPresetCircleLabel = "Cercle au centre du lot";
    public string defaultPresetTriangleLabel = "Triangle au centre du lot";
    public string defaultSplitEnvelopeLabel = "Séparer les lots";

    [Header("Presets (m)")]
    [Min(0.05f)] public float presetCircleRadiusMeters = 2f;
    [Min(0.05f)] public float presetTriangleSideMeters = 3f;

    [Header("Optional References")]
    public WallBuildController buildController;
    public WallDrawInput wallDrawInput;

    public WallObject CurrentWall { get; private set; }
    public HousePivotAddFloorScope AddFloorScope { get; private set; }
    public bool IsOpen => menuRoot != null && menuRoot.gameObject.activeSelf;

    void Awake()
    {
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        if (wallDrawInput == null)
            wallDrawInput = FindFirstObjectByType<WallDrawInput>();

        if (backgroundCloseButton != null)
            backgroundCloseButton.onClick.AddListener(Close);

        if (buttonAddWall != null)
            buttonAddWall.onClick.AddListener(OnAddWallClicked);

        if (buttonAddFloor != null)
        {
            buttonAddFloor.onClick.AddListener(OnAddFloorClicked);
            buttonAddFloor.interactable = true;
        }

        if (buttonPresetCircleAtLotCenter != null)
            buttonPresetCircleAtLotCenter.onClick.AddListener(OnPresetCircleAtLotCenterClicked);

        if (buttonPresetTriangleAtLotCenter != null)
            buttonPresetTriangleAtLotCenter.onClick.AddListener(OnPresetTriangleAtLotCenterClicked);

        if (buttonSplitEnvelopeLots != null)
            buttonSplitEnvelopeLots.onClick.AddListener(OnSplitEnvelopeLotsClicked);

        ApplyDefaultButtonLabels();

        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);
    }

    void ApplyDefaultButtonLabels()
    {
        SetButtonLabel(buttonAddWall, defaultAddWallLabel);
        SetButtonLabel(buttonAddFloor, defaultAddFloorLabel);
        SetButtonLabel(buttonPresetCircleAtLotCenter, defaultPresetCircleLabel);
        SetButtonLabel(buttonPresetTriangleAtLotCenter, defaultPresetTriangleLabel);
        SetButtonLabel(buttonSplitEnvelopeLots, defaultSplitEnvelopeLabel);
    }

    static void SetButtonLabel(Button button, string label)
    {
        if (button == null || string.IsNullOrEmpty(label))
            return;

        Text t = button.GetComponentInChildren<Text>(true);
        if (t != null)
            t.text = label;

        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = label;
    }

    void Update()
    {
        if (!IsOpen)
            return;

        if (closeOnEscape && Input.GetKeyDown(KeyCode.Escape))
            Close();

        if (closeOnLeftClickOutsidePanel && Input.GetMouseButtonDown(0))
        {
            if (panelRoot == null)
            {
                Close();
                return;
            }

            Camera uiCam = GetUiCameraForRect(panelRoot);
            if (!RectTransformUtility.RectangleContainsScreenPoint(panelRoot, Input.mousePosition, uiCam))
                Close();
        }
    }

    static Camera GetUiCameraForRect(RectTransform rect)
    {
        if (rect == null)
            return null;

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null)
            return null;

        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    public void OpenForWall(WallObject wall, Vector2 screenPosition, HousePivotAddFloorScope addFloorScope = HousePivotAddFloorScope.EntireLinkedEnsemble)
    {
        if (wall == null || menuRoot == null || panelRoot == null)
            return;

        CurrentWall = wall;
        AddFloorScope = addFloorScope;

        if (buildController != null)
            buildController.ForceSelectWall(wall);

        if (titleText != null)
        {
            if (addFloorScope == HousePivotAddFloorScope.CurrentWallOnly)
                titleText.text = string.IsNullOrEmpty(titleSinglePlan) ? defaultTitle : titleSinglePlan;
            else
                titleText.text = string.IsNullOrEmpty(titleEntireHouse) ? defaultTitle : titleEntireHouse;
        }

        if (buttonAddFloor != null)
        {
            SetButtonLabel(
                buttonAddFloor,
                string.IsNullOrEmpty(defaultAddFloorLabel) ? "Ajouter un étage" : defaultAddFloorLabel);
            buttonAddFloor.interactable = CanAddFloorOnCurrentWall();
        }

        if (buttonAddWall != null)
            SetButtonLabel(buttonAddWall, "Ajouter un toit");

        if (buttonSplitEnvelopeLots != null)
        {
            if (addFloorScope == HousePivotAddFloorScope.CurrentWallOnly)
                buttonSplitEnvelopeLots.gameObject.SetActive(false);
            else
            {
                HouseExteriorEnvelopeSources he = wall.GetComponent<HouseExteriorEnvelopeSources>();
                bool canSplit = he != null && he.SourceLotObjects != null && he.SourceLotObjects.Count >= 1;
                buttonSplitEnvelopeLots.gameObject.SetActive(canSplit);
            }
        }

        WallEditShape wes = wall.GetComponent<WallEditShape>();
        bool canPresetAtLotCenter = wes != null && wes.TryGetHouseLotSpawnCenterWorld(out _);
        if (buttonPresetCircleAtLotCenter != null)
            buttonPresetCircleAtLotCenter.interactable = canPresetAtLotCenter;
        if (buttonPresetTriangleAtLotCenter != null)
            buttonPresetTriangleAtLotCenter.interactable = canPresetAtLotCenter;

        menuRoot.gameObject.SetActive(true);
        PositionPanel(screenPosition);
    }

    public void Close()
    {
        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);

        CurrentWall = null;
        AddFloorScope = HousePivotAddFloorScope.EntireLinkedEnsemble;
    }

    void OnAddWallClicked()
    {
        WallObject lot = CurrentWall;

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController != null && lot != null)
            buildController.AddRoofFromHouseMenu(lot);
    }

    void OnAddFloorClicked()
    {
        WallObject lot = CurrentWall;
        HousePivotAddFloorScope scope = AddFloorScope;

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController == null || lot == null)
            return;

        if (scope == HousePivotAddFloorScope.EntireLinkedEnsemble)
            buildController.AddFloorToEntireLinkedHouseEnsemble(lot);
        else
            buildController.AddFloorFromHouseMenu(lot);
    }

    void OnSplitEnvelopeLotsClicked()
    {
        WallObject lot = CurrentWall;

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController != null && lot != null)
            buildController.TrySplitHouseEnvelopeIntoSourceLots(lot);
    }

    void OnPresetCircleAtLotCenterClicked()
    {
        WallObject lot = CurrentWall;

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController != null && lot != null)
            buildController.SpawnUiPresetCircleAtReferenceLotCenter(lot, presetCircleRadiusMeters);
    }

    void OnPresetTriangleAtLotCenterClicked()
    {
        WallObject lot = CurrentWall;

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController != null && lot != null)
            buildController.SpawnUiPresetTriangleAtReferenceLotCenter(lot, presetTriangleSideMeters);
    }

    bool CanAddFloorOnCurrentWall()
    {
        if (CurrentWall == null)
            return false;

        WallEditShape edit = CurrentWall.GetComponent<WallEditShape>();
        if (edit == null || !edit.IsClosedLoopPath)
            return false;

        return edit.shapeKind == WallEditShape.ShapeKind.Rectangle ||
               edit.shapeKind == WallEditShape.ShapeKind.Free ||
               edit.shapeKind == WallEditShape.ShapeKind.Triangle ||
               edit.shapeKind == WallEditShape.ShapeKind.Ellipse;
    }

    void PositionPanel(Vector2 screenPosition)
    {
        RectTransform rootRect = menuRoot;
        if (rootRect == null || panelRoot == null)
            return;

        Canvas rootCanvas = rootRect.GetComponentInParent<Canvas>();
        Camera uiCamera = null;
        if (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = rootCanvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPosition, uiCamera, out Vector2 localPoint))
            localPoint = Vector2.zero;

        Vector2 anchored = localPoint + panelScreenOffset;
        panelRoot.anchoredPosition = anchored;

        Canvas.ForceUpdateCanvases();
        ClampPanelInsideRoot(rootRect, panelRoot);
    }

    void ClampPanelInsideRoot(RectTransform rootRect, RectTransform panel)
    {
        Vector3[] rootCorners = new Vector3[4];
        Vector3[] panelCorners = new Vector3[4];

        rootRect.GetLocalCorners(rootCorners);
        panel.GetLocalCorners(panelCorners);

        Vector3 panelPos = panel.localPosition;

        float left = panelPos.x + panelCorners[0].x;
        float right = panelPos.x + panelCorners[2].x;
        float bottom = panelPos.y + panelCorners[0].y;
        float top = panelPos.y + panelCorners[2].y;

        float minX = rootCorners[0].x + screenPadding.x;
        float maxX = rootCorners[2].x - screenPadding.x;
        float minY = rootCorners[0].y + screenPadding.y;
        float maxY = rootCorners[2].y - screenPadding.y;

        if (left < minX)
            panelPos.x += minX - left;
        if (right > maxX)
            panelPos.x -= right - maxX;
        if (bottom < minY)
            panelPos.y += minY - bottom;
        if (top > maxY)
            panelPos.y -= top - maxY;

        panel.localPosition = panelPos;
    }
}
