using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu clic droit sur le point central d’un lot fermé : créer une maison (plancher) ou un champ (réservé).
/// </summary>
public class LotBuildMenuUI : MonoBehaviour
{
    [Header("Scene References")]
    public RectTransform menuRoot;
    public RectTransform panelRoot;
    public Button buttonHouse;
    public Button buttonField;

    [Header("Optional UI")]
    public Button backgroundCloseButton;
    public Text titleText;

    [Header("Behaviour")]
    public bool closeOnEscape = true;
    [Tooltip("Ferme le menu si clic gauche en dehors du panneau (pas sur le petit encadré).")]
    public bool closeOnLeftClickOutsidePanel = true;
    public Vector2 panelScreenOffset = new Vector2(18f, -18f);
    public Vector2 screenPadding = new Vector2(18f, 18f);

    [Header("Default labels (si les Text des boutons sont vides)")]
    public string defaultHouseLabel = "Créer une maison";
    public string defaultFieldLabel = "Créer un champ (bientôt)";

    [Header("House")]
    public Material defaultParquetMaterial;

    [Header("Formes au centre du lot (optionnel — assigner les boutons dans la scène)")]
    [Tooltip("Crée un mur fermé circulaire centré sur le lot (même centre que « Ajouter un mur » depuis le pivot).")]
    public Button buttonPresetCircleAtLotCenter;
    [Tooltip("Triangle équilatéral centré sur le lot.")]
    public Button buttonPresetTriangleAtLotCenter;
    [Min(0.05f)] public float presetCircleRadiusMeters = 2f;
    [Min(0.05f)] public float presetTriangleSideMeters = 3f;

    [Header("Optional References")]
    public WallBuildController buildController;

    public WallObject CurrentLotWall { get; private set; }
    public bool IsOpen => menuRoot != null && menuRoot.gameObject.activeSelf;

    void Awake()
    {
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        if (backgroundCloseButton != null)
            backgroundCloseButton.onClick.AddListener(Close);

        if (buttonHouse != null)
            buttonHouse.onClick.AddListener(OnHouseClicked);

        if (buttonPresetCircleAtLotCenter != null)
            buttonPresetCircleAtLotCenter.onClick.AddListener(OnPresetCircleAtLotCenterClicked);

        if (buttonPresetTriangleAtLotCenter != null)
            buttonPresetTriangleAtLotCenter.onClick.AddListener(OnPresetTriangleAtLotCenterClicked);

        if (buttonField != null)
        {
            buttonField.onClick.AddListener(OnFieldClicked);
            buttonField.interactable = false;
        }

        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);

        ApplyDefaultButtonLabels();
    }

    void ApplyDefaultButtonLabels()
    {
        SetButtonLabel(buttonHouse, defaultHouseLabel);
        SetButtonLabel(buttonField, defaultFieldLabel);
    }

    static void SetButtonLabel(Button button, string label)
    {
        if (button == null || string.IsNullOrEmpty(label))
            return;

        Text t = button.GetComponentInChildren<Text>(true);
        if (t != null)
            t.text = label;
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

    public void OpenForClosedLot(WallObject wall, Vector2 screenPosition)
    {
        if (wall == null || menuRoot == null || panelRoot == null)
            return;

        CurrentLotWall = wall;

        if (buildController != null)
            buildController.ForceSelectWall(wall);

        if (titleText != null)
            titleText.text = "Ce terrain";

        WallEditShape wes = wall.GetComponent<WallEditShape>();
        bool canPresetAtLotCenter = wes != null && wes.TryGetHouseLotSpawnCenterWorld(out _);
        if (buttonPresetCircleAtLotCenter != null)
            buttonPresetCircleAtLotCenter.interactable = canPresetAtLotCenter;
        if (buttonPresetTriangleAtLotCenter != null)
            buttonPresetTriangleAtLotCenter.interactable = canPresetAtLotCenter;

        menuRoot.gameObject.SetActive(true);
        PositionPanel(screenPosition);
    }

    /// <inheritdoc cref="OpenForClosedLot"/>
    public void OpenForRectangleLot(WallObject wall, Vector2 screenPosition) =>
        OpenForClosedLot(wall, screenPosition);

    public void Close()
    {
        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);

        CurrentLotWall = null;
    }

    void OnHouseClicked()
    {
        if (CurrentLotWall == null)
        {
            Close();
            return;
        }

        WallEditShape edit = CurrentLotWall.GetComponent<WallEditShape>();
        if (edit == null || !edit.IsClosedLoopPath)
        {
            Close();
            return;
        }

        HouseParquetFloor floor = CurrentLotWall.GetComponent<HouseParquetFloor>();
        if (floor == null)
            floor = CurrentLotWall.gameObject.AddComponent<HouseParquetFloor>();

        if (floor.parquetMaterial == null && defaultParquetMaterial != null)
            floor.parquetMaterial = defaultParquetMaterial;

        if (buildController != null)
            floor.storeyHeightMeters = buildController.AddFloorHeightMeters;

        if (edit.shapeKind == WallEditShape.ShapeKind.Rectangle)
            floor.ApplyOrRefresh(CurrentLotWall, edit);
        else
            floor.ApplyOrRefreshFromClosedPreviewPath(CurrentLotWall, edit);

        // Une forme qui devient maison doit rejoindre immédiatement une maison voisine déjà enveloppée.
        // Cela réutilise le pipeline existant (TryMergeCommittedShapeIntoHouse + sources d'enveloppe N lots).
        if (buildController != null)
            buildController.TryMergeWallWithAdjacentLots(CurrentLotWall);

        Close();
    }

    void OnFieldClicked()
    {
        // Champs : réservé pour une prochaine itération.
        Close();
    }

    void OnPresetCircleAtLotCenterClicked()
    {
        WallObject lot = CurrentLotWall;
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController != null && lot != null)
            buildController.SpawnUiPresetCircleAtReferenceLotCenter(lot, presetCircleRadiusMeters);
    }

    void OnPresetTriangleAtLotCenterClicked()
    {
        WallObject lot = CurrentLotWall;
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        Close();

        if (buildController != null && lot != null)
            buildController.SpawnUiPresetTriangleAtReferenceLotCenter(lot, presetTriangleSideMeters);
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
