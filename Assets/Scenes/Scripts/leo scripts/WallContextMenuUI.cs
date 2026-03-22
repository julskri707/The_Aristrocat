using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WallContextMenuUI : MonoBehaviour
{
    [Header("Scene References")]
    public RectTransform menuRoot;
    public RectTransform panelRoot;
    public RectTransform buttonContainer;
    public WallStyleButtonUI buttonPrefab;

    [Header("Optional UI")]
    public Button backgroundCloseButton;
    public Button closeButton;
    public Text titleText;
    public Text subtitleText;

    [Header("Data")]
    public List<WallStyleDefinition> availableStyles = new List<WallStyleDefinition>();

    [Header("Behaviour")]
    public bool closeAfterApply = true;
    public bool closeOnEscape = true;
    public Vector2 panelScreenOffset = new Vector2(18f, -18f);
    public Vector2 screenPadding = new Vector2(18f, 18f);

    [Header("Optional References")]
    public WallBuildController buildController;
    public ControlPointOverlayManager overlay;

    private readonly List<WallStyleButtonUI> _spawnedButtons = new List<WallStyleButtonUI>();

    public WallObject CurrentWall { get; private set; }
    public bool IsOpen => menuRoot != null && menuRoot.gameObject.activeSelf;

    void Awake()
    {
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();

        if (backgroundCloseButton != null)
            backgroundCloseButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!IsOpen)
            return;

        if (closeOnEscape && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    public void OpenForWall(WallObject wall, Vector2 screenPosition)
    {
        if (wall == null || menuRoot == null || panelRoot == null)
            return;

        CurrentWall = wall;

        if (buildController != null)
            buildController.ForceSelectWall(wall);

        menuRoot.gameObject.SetActive(true);
        RefreshTexts();
        RebuildButtons();
        PositionPanel(screenPosition);
    }

    public void Close()
    {
        if (menuRoot != null)
            menuRoot.gameObject.SetActive(false);

        CurrentWall = null;
    }

    public void RefreshCurrentWall()
    {
        if (!IsOpen)
            return;

        RefreshTexts();
        RebuildButtons();
    }

    void RefreshTexts()
    {
        if (titleText != null)
            titleText.text = CurrentWall != null ? CurrentWall.name : "Wall";

        if (subtitleText != null)
        {
            WallStyleInstance instance = CurrentWall != null ? CurrentWall.GetComponent<WallStyleInstance>() : null;
            subtitleText.text = instance != null && instance.currentStyle != null
                ? $"Style actuel : {instance.currentStyle.displayName}"
                : "Choisis un style de mur";
        }
    }

    void RebuildButtons()
    {
        ClearButtons();

        if (buttonContainer == null || buttonPrefab == null || availableStyles == null)
            return;

        WallStyleDefinition currentStyle = null;
        if (CurrentWall != null)
        {
            WallStyleInstance instance = CurrentWall.GetComponent<WallStyleInstance>();
            if (instance != null)
                currentStyle = instance.currentStyle;
        }

        for (int i = 0; i < availableStyles.Count; i++)
        {
            WallStyleDefinition style = availableStyles[i];
            if (style == null)
                continue;

            WallStyleButtonUI button = Instantiate(buttonPrefab, buttonContainer);
            bool isSelected = currentStyle == style;
            button.Bind(style, isSelected, HandleStyleClicked);
            _spawnedButtons.Add(button);
        }
    }

    void ClearButtons()
    {
        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            if (_spawnedButtons[i] != null)
                Destroy(_spawnedButtons[i].gameObject);
        }

        _spawnedButtons.Clear();
    }

    void HandleStyleClicked(WallStyleDefinition style)
    {
        if (CurrentWall == null || style == null)
            return;

        WallStyleApplier.Apply(CurrentWall, style);

        if (buildController != null)
            buildController.ForceSelectWall(CurrentWall);
        else if (overlay != null)
            overlay.RebuildOverlay();

        RefreshTexts();
        RebuildButtons();

        if (closeAfterApply)
            Close();
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
