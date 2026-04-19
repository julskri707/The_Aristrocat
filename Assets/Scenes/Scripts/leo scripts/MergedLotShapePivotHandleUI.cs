using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Poignée séparée des sommets : déplace toute la forme (lot orthogonal fusionné, rectangle, triangle, ellipse, arc ouvert).
/// </summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(RectTransform))]
public class MergedLotShapePivotHandleUI : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static readonly Color SelectedOrange = new Color(1f, 0.55f, 0.12f, 1f);
    /// <summary>Lot fermé non désigné « maison » (champ / brouillon).</summary>
    public static readonly Color IdleBlueField = new Color(0.4f, 0.72f, 1f, 1f);
    /// <summary>Maison désignée (menu lot) — un seul plan / pas d’enveloppe multi-sources.</summary>
    public static readonly Color IdleRoseDesignatedHouse = new Color(1f, 0.42f, 0.72f, 1f);
    /// <summary>Enveloppe maison avec plusieurs plans sources fusionnés.</summary>
    public static readonly Color IdleVioletHouse = new Color(0.62f, 0.38f, 0.98f, 1f);

    [Header("Binding (assigné par ControlPointOverlayManager)")]
    public Camera cam;
    public WallEditShape edit;

    [Header("Drag")]
    public float groundY = 0f;

    [Header("Clic pivot violet")]
    [Tooltip("Bouton enfoncé plus longtemps sans démarrer un drag : au relâchement, aucune sélection ni drag.")]
    [Min(0.05f)] public float pivotLongPressIgnoreSeconds = 0.38f;
    [Tooltip("Durée max pression pour compter comme un « clic court » (sélection du violet, sans déplacement).")]
    [Min(0.02f)] public float pivotShortClickMaxSeconds = 0.22f;
    [Tooltip("Déplacement écran minimal avant d’engager un drag de la forme entière.")]
    [Min(1f)] public float pivotDragStartSlopScreenPx = 10f;

    [Header("UI")]
    [Tooltip("Si vrai : la poignée centre reste toujours au-dessus des poignées blanches (rendu et priorité de raycast).")]
    public bool alwaysDrawAboveVertexHandles = true;

    [Tooltip("Si alwaysDrawAboveVertexHandles est faux : ne remonter au premier plan que pendant le drag ou la molette sur le pivot.")]
    public bool keepOnTopWhenActive = true;

    public static MergedLotShapePivotHandleUI ActivePivotForScroll { get; private set; }

    /// <summary>
    /// Profondeur : tant que &gt; 0, <see cref="ControlPointOverlayManager"/> ne détruit pas l’instance du pivot violet
    /// lors d’un rebuild (fusion / enveloppe pendant un drag continu).
    /// </summary>
    static int s_MergedPivotOverlayPreserveDepth;

    /// <summary>Push au PointerDown pivot (ou poignée centre maison) ; Pop au relâchement.</summary>
    public static void PushMergedPivotOverlayPreserve() => s_MergedPivotOverlayPreserveDepth++;

    public static void PopMergedPivotOverlayPreserve()
    {
        if (s_MergedPivotOverlayPreserveDepth > 0)
            s_MergedPivotOverlayPreserveDepth--;
    }

    public static bool ShouldPreserveMergedPivotThroughOverlayClear => s_MergedPivotOverlayPreserveDepth > 0;

    /// <summary>Pivot violet en drag bulk : évite <see cref="ClearActivePivotForScroll"/> et garde la molette active.</summary>
    static MergedLotShapePivotHandleUI s_ActiveBulkDragPivot;

    RectTransform _rect;
    Graphic[] _graphics;
    Canvas _rootCanvas;
    RectTransform _canvasRect;
    Camera _uiCamera;

    bool _dragging;
    float _dragPivotY;
    Plane _dragPlane;
    Vector3 _offsetWorld;
    Vector3 _lastWorldForLayout;
    Vector3 _lastCamPosForLayout;
    Quaternion _lastCamRotForLayout;
    bool _hasLayoutCache;
    bool _lastActiveForTop;

    /// <summary>Après split d’enveloppe : la souris n’a pas refait un PointerDown sur le nouveau pivot — on suit le bouton en Update.</summary>
    bool _dragUsesRawMouseContinuation;

    int _standardCenterIndex = -1;
    bool _useMergedCentroid;

    float _pivotLeftDownUnscaledTime = -1f;
    Vector2 _pivotLeftDownScreenPos;
    bool _pivotBulkDragCommitted;

    static WallUndoManager s_Undo;
    static WallDrawInput s_DrawInput;
    static WallBuildController s_Build;
    static bool s_ScrollUndoArmed = true;

    static WallUndoManager GetUndoManager()
    {
        if (s_Undo == null)
            s_Undo = FindFirstObjectByType<WallUndoManager>();
        return s_Undo;
    }

    static WallDrawInput GetWallDrawInput()
    {
        if (s_DrawInput == null)
            s_DrawInput = FindFirstObjectByType<WallDrawInput>();
        return s_DrawInput;
    }

    static WallBuildController GetBuildController()
    {
        if (s_Build == null)
            s_Build = FindFirstObjectByType<WallBuildController>();
        return s_Build;
    }

    public static void ClearActivePivotForScroll()
    {
        if (s_ActiveBulkDragPivot != null)
            return;
        ActivePivotForScroll = null;
    }

    public static void ApplySplitResumeToWallIfPresent(WallObject wall, EnvelopePinkDragCapture capture)
    {
        if (wall == null || !capture.IsValid)
            return;

        MergedLotShapePivotHandleUI[] arr = FindObjectsByType<MergedLotShapePivotHandleUI>(FindObjectsSortMode.None);
        for (int i = 0; i < arr.Length; i++)
        {
            MergedLotShapePivotHandleUI p = arr[i];
            if (p == null || p.edit == null || p.edit.wall != wall)
                continue;

            p.ApplySplitResume(capture);
            return;
        }
    }

    static bool WallIsDesignatedHouseLot(WallEditShape e)
    {
        if (e == null || e.wall == null)
            return false;
        HouseParquetFloor f = e.wall.GetComponent<HouseParquetFloor>();
        return f != null && f.IsDesignatedHouseLot;
    }

    /// <summary>
    /// Si vrai : les poignées roses (plans sources) doivent rester au-dessus du pivot pour le raycast — ne pas remonter le centre.
    /// </summary>
    static bool HasHouseEnvelopeSourceHandlesAbovePivot(WallEditShape e)
    {
        if (e == null || e.wall == null)
            return false;
        HouseExteriorEnvelopeSources hes = e.wall.GetComponent<HouseExteriorEnvelopeSources>();
        return hes != null && hes.HasMultipleSourceLots;
    }

    static bool EnvelopeHasMultipleSourcePlans(WallEditShape e) => HasHouseEnvelopeSourceHandlesAbovePivot(e);

    /// <summary>
    /// Enveloppe multi-sources : masquer le pivot violet pendant tout drag de poignée (rose, centre, sommets…).
    /// On utilise uniquement <see cref="ControlPointHandleUI.IsDraggingAnyHandle"/> — pas le mur sous le curseur : après
    /// fusion au milieu d’un drag, <c>TryGetWallObjectForDraggedProvider</c> peut être incohérent / null et faisait
    /// réapparaître le violet. Le drag sur ce pivot a <c>_dragging</c> : le bloc appelant exige <c>!_dragging</c>.
    /// </summary>
    static bool ShouldHideMultiSourceEnvelopeVioletDuringOtherGesture(WallEditShape envelopeEdit)
    {
        if (envelopeEdit == null || envelopeEdit.wall == null)
            return false;

        HouseExteriorEnvelopeSources hes = envelopeEdit.wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (hes == null || !hes.HasMultipleSourceLots)
            return false;

        return ControlPointHandleUI.IsDraggingAnyHandle;
    }

    /// <summary>
    /// Mur enveloppe maison : dès que le composant existe (fusion / recalcul), pas seulement quand
    /// <see cref="HouseExteriorEnvelopeSources.HasMultipleSourceLots"/> est déjà vrai — évite un frame où le violet
    /// prend l’orange via <see cref="ActivePivotForScroll"/> à la création du pivot.
    /// </summary>
    static bool WallHasExteriorEnvelopeMeta(WallEditShape e)
    {
        return e != null && e.wall != null && e.wall.GetComponent<HouseExteriorEnvelopeSources>() != null;
    }

    /// <summary>
    /// Enveloppe maison : la surbrillance « active » du lot est sur les roses ; le violet ne doit pas passer
    /// en orange via le seul focus molette / état après fusion — uniquement pendant un drag réel sur ce pivot.
    /// </summary>
    bool PivotUiEmphasizedLikeSelection()
    {
        if (WallHasExteriorEnvelopeMeta(edit))
        {
            // Drag rose sur cette enveloppe : ne pas afficher le pivot comme « sélectionné » (orange).
            if (HouseEnvelopeSourceHandleUI.ActiveDragInstance != null &&
                HouseEnvelopeSourceHandleUI.ActiveDragInstance.envelopeEdit == edit)
                return false;
            return _dragging;
        }

        return _dragging || ActivePivotForScroll == this;
    }

    public void RefreshPivotVisualNow()
    {
        ApplyVisualState();
    }

    public static void RefreshAllPivotVisualStates()
    {
        MergedLotShapePivotHandleUI[] arr = FindObjectsByType<MergedLotShapePivotHandleUI>(FindObjectsSortMode.None);
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] != null)
                arr[i].RefreshPivotVisualNow();
        }
    }

    void Awake()
    {
        CacheCanvas();
    }

    void OnEnable()
    {
        CacheCanvas();
    }

    /// <summary>
    /// Raycast + dernier focus rose/violet (<see cref="EnvelopeOverlayHandleFocus"/>) ; pendant un autre drag, ce pivot est inactif sauf si c’est lui.
    /// </summary>
    bool ShouldGraphicsReceiveRaycasts()
    {
        // Même si IsDraggingAnyHandle est faux un instant, le drag rose reste actif : ne pas laisser le violet
        // capter le curseur au joint quand l’enveloppe se redessine (évite « auto-sélection » du pivot).
        if (edit != null &&
            HouseEnvelopeSourceHandleUI.ActiveDragInstance != null &&
            HouseEnvelopeSourceHandleUI.ActiveDragInstance.envelopeEdit == edit)
            return false;

        if (ControlPointHandleUI.IsDraggingAnyHandle)
            return _dragging;
        if (edit != null && edit.wall != null)
        {
            HouseExteriorEnvelopeSources hes = edit.wall.GetComponent<HouseExteriorEnvelopeSources>();
            if (hes != null && hes.HasMultipleSourceLots)
                return EnvelopeOverlayHandleFocus.ShouldVioletReceiveRaycasts(edit.wall);
        }

        return true;
    }

    void CacheCanvas()
    {
        _rect = (RectTransform)transform;
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);

        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            _rootCanvas = null;
            _canvasRect = null;
            _uiCamera = null;
            return;
        }

        _rootCanvas = parentCanvas.rootCanvas;
        _canvasRect = _rootCanvas.transform as RectTransform;
        _uiCamera = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;

        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
    }

    bool ResolveMode()
    {
        if (edit == null || !edit.TryGetShapeBulkMovePivotInfo(out _standardCenterIndex, out _useMergedCentroid))
            return false;
        if (_useMergedCentroid)
            _standardCenterIndex = -1;
        return true;
    }

    Vector3 GetPivotWorld()
    {
        if (_useMergedCentroid)
            return edit.GetMergedOrthogonalShapeCentroidWorld();
        return edit.GetControlPointWorld(_standardCenterIndex);
    }

    void Update()
    {
        if (_dragging && _dragUsesRawMouseContinuation)
        {
            if (!PrimaryPointerHeld())
            {
                EndBulkDragFromRawContinuation();
                return;
            }

            ProcessBulkDragAtScreen(PrimaryPointerPosition());
            return;
        }

        // Rotation à la molette uniquement pendant un drag du pivot (pas après un simple clic de sélection).
        if (cam == null || edit == null || !_dragging || ActivePivotForScroll != this)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-6f)
        {
            s_ScrollUndoArmed = true;
            return;
        }

        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        WallUndoManager undo = GetUndoManager();
        if (undo != null && s_ScrollUndoArmed)
        {
            undo.RecordSnapshot("Rotate Shape");
            s_ScrollUndoArmed = false;
        }

        if (ctrlDown && shiftDown)
        {
            int steps = Mathf.RoundToInt(scroll * 10f);
            if (steps == 0)
                steps = scroll > 0f ? 1 : -1;
            edit.ApplyCenterScrollRotationQuantized(steps, 64);
        }
        else if (ctrlDown)
        {
            edit.ApplyCenterScrollRotation(scroll);
        }
        else
        {
            int steps = Mathf.RoundToInt(scroll * 10f);
            if (steps == 0)
                steps = scroll > 0f ? 1 : -1;
            edit.ApplyCenterScrollRotationQuantized(steps, 16);
        }

        ControlPointHandleUI.BlockCameraZoomFromWallShapeScroll = true;
    }

    static bool PrimaryPointerHeld()
    {
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            return t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
        }

        return Input.GetMouseButton(0);
    }

    static Vector2 PrimaryPointerPosition()
    {
        if (Input.touchCount > 0)
            return Input.GetTouch(0).position;
        return Input.mousePosition;
    }

    void LateUpdate()
    {
        if (cam == null || edit == null || !ResolveMode())
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        if (EnvelopeHasMultipleSourcePlans(edit) &&
            !_dragging &&
            ShouldHideMultiSourceEnvelopeVioletDuringOtherGesture(edit))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        if (_rect == null || _canvasRect == null)
            CacheCanvas();
        if (_canvasRect == null)
            return;

        Vector3 world = GetPivotWorld();

        if (_hasLayoutCache
            && (world - _lastWorldForLayout).sqrMagnitude < 1e-10f
            && (cam.transform.position - _lastCamPosForLayout).sqrMagnitude < 1e-10f
            && Quaternion.Angle(cam.transform.rotation, _lastCamRotForLayout) < 0.01f)
        {
            ApplyVisualState();
            SetAllGraphicsRaycastTarget(ShouldGraphicsReceiveRaycasts());

            if (alwaysDrawAboveVertexHandles && !HasHouseEnvelopeSourceHandlesAbovePivot(edit))
                _rect.SetAsLastSibling();
            else if (keepOnTopWhenActive)
            {
                bool showTop = PivotUiEmphasizedLikeSelection();
                if (showTop != _lastActiveForTop)
                {
                    if (showTop && !HasHouseEnvelopeSourceHandlesAbovePivot(edit))
                        _rect.SetAsLastSibling();
                    _lastActiveForTop = showTop;
                }
            }

            return;
        }

        _lastWorldForLayout = world;
        _lastCamPosForLayout = cam.transform.position;
        _lastCamRotForLayout = cam.transform.rotation;
        _hasLayoutCache = true;

        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0f)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, _uiCamera, out Vector2 local))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        _rect.anchoredPosition = local;
        ApplyVisualState();

        SetAllGraphicsRaycastTarget(ShouldGraphicsReceiveRaycasts());

        if (alwaysDrawAboveVertexHandles && !HasHouseEnvelopeSourceHandlesAbovePivot(edit))
            _rect.SetAsLastSibling();
        else if (keepOnTopWhenActive)
        {
            bool showTop = PivotUiEmphasizedLikeSelection();
            if (showTop != _lastActiveForTop)
            {
                if (showTop && !HasHouseEnvelopeSourceHandlesAbovePivot(edit))
                    _rect.SetAsLastSibling();
                _lastActiveForTop = showTop;
            }
        }
        else
            _lastActiveForTop = false;
    }

    void ApplyVisualState()
    {
        bool on = PivotUiEmphasizedLikeSelection();
        // Bleu → rose : dès que le lot est désigné « maison ». Violet si enveloppe multi-plans (fusion de plusieurs lots).
        Color idle;
        if (!WallIsDesignatedHouseLot(edit))
            idle = IdleBlueField;
        else if (EnvelopeHasMultipleSourcePlans(edit))
            idle = IdleVioletHouse;
        else
            idle = IdleRoseDesignatedHouse;
        Color c = on ? SelectedOrange : idle;
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);
        if (_graphics == null)
            return;
        for (int i = 0; i < _graphics.Length; i++)
        {
            if (_graphics[i] != null)
                _graphics[i].color = c;
        }
    }

    void SetAllGraphicsRaycastTarget(bool on)
    {
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);
        if (_graphics == null)
            return;
        for (int i = 0; i < _graphics.Length; i++)
        {
            if (_graphics[i] != null)
                _graphics[i].raycastTarget = on;
        }
    }

    /// <summary>Clic droit sur le pivot (lot désigné « maison ») : mur / étage.</summary>
    void TryOpenHousePivotActionsMenu(PointerEventData eventData)
    {
        if (edit == null || edit.wall == null || !edit.IsClosedLoopPath)
            return;

        PivotPointActionsMenuUI pivotMenu = FindFirstObjectByType<PivotPointActionsMenuUI>(FindObjectsInactive.Include);
        if (pivotMenu == null)
        {
            Debug.LogWarning(
                "[MergedLotShapePivotHandleUI] Aucun PivotPointActionsMenuUI dans la scène : clic droit pivot violet ignoré. " +
                "Ajoute le composant sur ton Canvas (même principe que LotBuildMenuUI) et assigne menuRoot / panelRoot / boutons.",
                this);
            return;
        }

        WallContextMenuUI ctx = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);
        if (ctx != null && ctx.IsOpen)
            ctx.Close();

        LotBuildMenuUI lot = FindFirstObjectByType<LotBuildMenuUI>(FindObjectsInactive.Include);
        if (lot != null && lot.IsOpen)
            lot.Close();

        pivotMenu.OpenForWall(edit.wall, eventData.position);
    }

    void TryOpenLotBuildMenuForClosedLotPivot(PointerEventData eventData)
    {
        if (edit == null || !edit.IsClosedLoopPath)
            return;

        WallObject wall = edit.wall;
        if (wall == null)
            return;

        LotBuildMenuUI lotMenu = FindFirstObjectByType<LotBuildMenuUI>(FindObjectsInactive.Include);
        if (lotMenu == null)
            return;

        WallContextMenuUI ctx = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);
        if (ctx != null && ctx.IsOpen)
            ctx.Close();

        PivotPointActionsMenuUI pivotMenu = FindFirstObjectByType<PivotPointActionsMenuUI>(FindObjectsInactive.Include);
        if (pivotMenu != null && pivotMenu.IsOpen)
            pivotMenu.Close();

        lotMenu.OpenForClosedLot(wall, eventData.position);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (cam == null || edit == null || !ResolveMode())
            return;

        // Même logique que ControlPointHandleUI : un faux PointerDown après rebuild doit quand même nettoyer le pending rose.
        HouseEnvelopeSourceHandleUI.ClearPinkHighlightTracking();

        if (ControlPointHandleUI.ShouldBlockNewOverlayPointerDown())
            return;

        if (ControlPointHandleUI.IsPointerDragOwnedByAnother(this))
            return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (edit.wall != null)
                EnvelopeOverlayHandleFocus.SetFocusViolet(edit.wall);
            if (WallIsDesignatedHouseLot(edit))
                TryOpenHousePivotActionsMenu(eventData);
            else
                TryOpenLotBuildMenuForClosedLotPivot(eventData);
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (ControlPointHandleUI.IsDraggingAnyHandle && !_dragging)
            return;

        _pivotLeftDownUnscaledTime = Time.unscaledTime;
        _pivotLeftDownScreenPos = eventData.position;
        _pivotBulkDragCommitted = false;
        ControlPointHandleUI.RegisterPointerDragOwner(this);
    }

    void CommitPivotBulkDrag(PointerEventData eventData)
    {
        if (_pivotBulkDragCommitted || edit == null)
            return;

        _pivotBulkDragCommitted = true;

        if (edit.wall != null)
            EnvelopeOverlayHandleFocus.SetFocusViolet(edit.wall);

        WallUndoManager undo = GetUndoManager();
        if (undo != null)
            undo.RecordSnapshot("Move Shape (whole)");

        PushMergedPivotOverlayPreserve();
        s_ActiveBulkDragPivot = this;
        ActivePivotForScroll = this;
        ControlPointHandleUI.NotifyWallPointSelectionClearedForPivot();
        ControlPointHandleUI.SetPivotBulkMoveDragging(true, edit);

        _dragging = true;
        _dragUsesRawMouseContinuation = false;
        if (edit.UsesMergedLotOrthogonalHandles)
            edit.NotifyOrthogonalVertexDragStrokeStarted();

        Vector3 startWorld = GetPivotWorld();
        _dragPivotY = startWorld.y;
        _dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        if (TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            _offsetWorld = startWorld - hit;
        else
            _offsetWorld = Vector3.zero;

        _pivotLeftDownUnscaledTime = -1f;
    }

    void ReleasePendingLeftPress(PointerEventData eventData)
    {
        float dur = Time.unscaledTime - _pivotLeftDownUnscaledTime;
        Vector2 delta = eventData.position - _pivotLeftDownScreenPos;
        float slop = pivotDragStartSlopScreenPx;
        bool longHold = dur >= pivotLongPressIgnoreSeconds;
        bool shortClick = !longHold && dur <= pivotShortClickMaxSeconds && delta.sqrMagnitude < slop * slop;

        _pivotLeftDownUnscaledTime = -1f;
        _pivotBulkDragCommitted = false;
        ControlPointHandleUI.ClearPointerDragOwnerIf(this);

        if (longHold)
            return;

        if (shortClick && edit != null && edit.wall != null)
        {
            EnvelopeOverlayHandleFocus.SetFocusViolet(edit.wall);
            ActivePivotForScroll = this;
            RefreshPivotVisualNow();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragUsesRawMouseContinuation)
        {
            ProcessBulkDragAtScreen(eventData.position);
            return;
        }

        if (cam == null || edit == null || !ResolveMode())
            return;

        if (_pivotLeftDownUnscaledTime >= 0f && !_pivotBulkDragCommitted)
        {
            if (Time.unscaledTime - _pivotLeftDownUnscaledTime >= pivotLongPressIgnoreSeconds)
                return;

            float slop = pivotDragStartSlopScreenPx;
            if ((eventData.position - _pivotLeftDownScreenPos).sqrMagnitude >= slop * slop)
                CommitPivotBulkDrag(eventData);
        }

        if (_dragging)
            ProcessBulkDragAtScreen(eventData.position);
    }

    /// <summary>
    /// Enveloppe multi-plans : le contour fusionné bouge avec le pivot violet ; les lots sources doivent subir la même translation XZ
    /// pour que les roses (centres des plans) suivent le déplacement.
    /// </summary>
    static void TranslateHouseEnvelopeSourceLotsWithEnvelope(WallEditShape envelopeEdit, Vector3 deltaXZ)
    {
        deltaXZ.y = 0f;
        if (envelopeEdit == null || deltaXZ.sqrMagnitude < 1e-18f)
            return;

        WallObject envelopeWall = envelopeEdit.wall;
        if (envelopeWall == null)
            return;

        HouseExteriorEnvelopeSources meta = envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null)
            return;

        IReadOnlyList<GameObject> srcGos = meta.SourceLotObjects;
        if (srcGos == null || srcGos.Count == 0)
            return;

        for (int i = 0; i < srcGos.Count; i++)
        {
            GameObject go = srcGos[i];
            if (go == null)
                continue;

            WallEditShape srcEd = go.GetComponent<WallEditShape>();
            if (srcEd == null || srcEd == envelopeEdit)
                continue;

            srcEd.TranslateClosedLotGeometryXZ(deltaXZ);
        }
    }

    void ProcessBulkDragAtScreen(Vector2 screenPos)
    {
        if (!_dragging || cam == null || edit == null || !ResolveMode())
            return;
        if (!TryScreenToPlaneWorld(screenPos, _dragPlane, out var hit))
            return;

        Vector3 intended = hit + _offsetWorld;
        Vector3 target = new Vector3(intended.x, _dragPivotY, intended.z);

        WallDrawInput di = GetWallDrawInput();
        if (di != null && di.enableGridSnap)
            target = di.SnapWorldPointForEditing(target);

        Vector3 pivotBefore = GetPivotWorld();
        Vector3 deltaBulk = new Vector3(target.x - pivotBefore.x, 0f, target.z - pivotBefore.z);

        if (_useMergedCentroid)
            edit.TrySetMergedOrthogonalShapeCentroidWorld(target);
        else
            edit.SetControlPointWorld(_standardCenterIndex, target);

        TranslateHouseEnvelopeSourceLotsWithEnvelope(edit, deltaBulk);

        // Pas de TryMergeWallWithAdjacentLots pendant le drag : sinon fusion en chaîne / enveloppe qui saute
        // dès qu’un nouveau lot touche — la fusion est faite au relâchement (OnPointerUp / EndBulkDragFromRawContinuation).
    }

    void ApplySplitResume(EnvelopePinkDragCapture capture)
    {
        if (!capture.IsValid || cam == null || !ResolveMode())
            return;

        _pivotLeftDownUnscaledTime = -1f;
        _pivotBulkDragCommitted = true;

        PushMergedPivotOverlayPreserve();
        s_ActiveBulkDragPivot = this;
        _dragging = true;
        _dragUsesRawMouseContinuation = true;
        ActivePivotForScroll = this;
        if (edit != null && edit.wall != null)
            EnvelopeOverlayHandleFocus.SetFocusViolet(edit.wall);
        ControlPointHandleUI.RegisterPointerDragOwner(this);
        ControlPointHandleUI.SetPivotBulkMoveDragging(true, edit);
        _dragPlane = capture.DragPlane;
        _offsetWorld = capture.OffsetWorld;
        _dragPivotY = capture.DragPivotY;
        if (edit != null && edit.UsesMergedLotOrthogonalHandles)
            edit.NotifyOrthogonalVertexDragStrokeStarted();
    }

    void EndBulkDragFromRawContinuation()
    {
        if (!_dragging)
            return;

        _dragging = false;
        _dragUsesRawMouseContinuation = false;
        PopMergedPivotOverlayPreserve();
        if (s_ActiveBulkDragPivot == this)
            s_ActiveBulkDragPivot = null;
        ControlPointHandleUI.ClearPointerDragOwnerIf(this);
        ControlPointHandleUI.SetPivotBulkMoveDragging(false, null);

        if (edit != null)
            edit.NotifyOrthogonalVertexDragStrokeEnded();

        if (edit != null && edit.wall != null)
        {
            WallBuildController bc = GetBuildController();
            if (bc != null)
                bc.TryMergeWallWithAdjacentLots(edit.wall);
        }

        if (ActivePivotForScroll == this)
            ActivePivotForScroll = null;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_dragUsesRawMouseContinuation)
            return;

        if (eventData.button == PointerEventData.InputButton.Left &&
            _pivotLeftDownUnscaledTime >= 0f &&
            !_pivotBulkDragCommitted)
        {
            ReleasePendingLeftPress(eventData);
            return;
        }

        if (!_dragging)
            return;

        _pivotLeftDownUnscaledTime = -1f;

        _dragging = false;
        PopMergedPivotOverlayPreserve();
        if (s_ActiveBulkDragPivot == this)
            s_ActiveBulkDragPivot = null;
        ControlPointHandleUI.ClearPointerDragOwnerIf(this);
        ControlPointHandleUI.SetPivotBulkMoveDragging(false, null);

        if (edit != null)
            edit.NotifyOrthogonalVertexDragStrokeEnded();

        if (edit != null && edit.wall != null)
        {
            WallBuildController bc = GetBuildController();
            if (bc != null)
                bc.TryMergeWallWithAdjacentLots(edit.wall);
        }

        if (ActivePivotForScroll == this)
            ActivePivotForScroll = null;
    }

    void OnDisable()
    {
        if (_pivotLeftDownUnscaledTime >= 0f && !_pivotBulkDragCommitted)
        {
            _pivotLeftDownUnscaledTime = -1f;
            ControlPointHandleUI.ClearPointerDragOwnerIf(this);
        }

        if (_dragging)
        {
            if (_dragUsesRawMouseContinuation)
                _dragUsesRawMouseContinuation = false;
            _dragging = false;
            PopMergedPivotOverlayPreserve();
            if (s_ActiveBulkDragPivot == this)
                s_ActiveBulkDragPivot = null;
            ControlPointHandleUI.ClearPointerDragOwnerIf(this);
            ControlPointHandleUI.SetPivotBulkMoveDragging(false, null);
        }

        if (ActivePivotForScroll == this)
            ActivePivotForScroll = null;
    }

    bool TryScreenToPlaneWorld(Vector2 screenPos, Plane plane, out Vector3 world)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (plane.Raycast(ray, out float enter))
        {
            world = ray.GetPoint(enter);
            return true;
        }

        world = default;
        return false;
    }
}
