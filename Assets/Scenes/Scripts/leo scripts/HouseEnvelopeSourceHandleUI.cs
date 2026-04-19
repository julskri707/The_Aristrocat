using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Cinématique capturée avant destruction de la rose pour reprendre le drag sur le pivot violet après split.</summary>
public struct EnvelopePinkDragCapture
{
    public Plane DragPlane;
    public Vector3 OffsetWorld;
    public float DragPivotY;
    public bool IsValid;
}

/// <summary>
/// Poignée rose : déplace un lot source (plan) sous l'enveloppe maison ; l'enveloppe est recalculée en direct.
/// </summary>
[DefaultExecutionOrder(1002)]
[RequireComponent(typeof(RectTransform))]
public class HouseEnvelopeSourceHandleUI : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static readonly Color IdlePink = new Color(1f, 0.42f, 0.72f, 1f);
    public static readonly Color SelectedOrange = new Color(1f, 0.55f, 0.12f, 1f);

    public Camera cam;
    public WallEditShape envelopeEdit;
    public int sourceLotIndex;

    public float groundY = 0f;

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

    static WallUndoManager s_Undo;
    static WallDrawInput s_DrawInput;
    static WallBuildController s_Build;

    /// <summary>Pendant qu’une rose est en drag : on masque la surbrillance <see cref="PendingHighlightSourceLotIndex"/>
    /// sur les autres — sinon deux plans passent en orange (drag + pending post-fusion).</summary>
    static int s_EnvelopePinkDragDepth;

    /// <summary>Rose actuellement en drag (split d’enveloppe : reprise sur le pivot du lot choisi).</summary>
    public static HouseEnvelopeSourceHandleUI ActiveDragInstance { get; private set; }

    /// <summary>Dernier plan rose ayant reçu un PointerDown (pour surbrillance après fusion enveloppe).</summary>
    public static int LastInteractedSourceLotIndex { get; private set; } = -1;

    /// <summary>Surcharge visuelle orange sur la rose d’index donné (sans drag).</summary>
    public static int PendingHighlightSourceLotIndex { get; set; } = -1;

    public static void ClearPinkHighlightTracking()
    {
        LastInteractedSourceLotIndex = -1;
        PendingHighlightSourceLotIndex = -1;
    }

    public EnvelopePinkDragCapture CaptureForSplitResume()
    {
        if (!_dragging)
            return default;

        return new EnvelopePinkDragCapture
        {
            DragPlane = _dragPlane,
            OffsetWorld = _offsetWorld,
            DragPivotY = _dragPivotY,
            IsValid = true
        };
    }

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

    void Awake()
    {
        CacheCanvas();
    }

    void OnEnable()
    {
        CacheCanvas();
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

    WallEditShape TryResolveSourceEdit()
    {
        if (envelopeEdit == null || envelopeEdit.wall == null)
            return null;

        HouseExteriorEnvelopeSources meta = envelopeEdit.wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || sourceLotIndex < 0 || sourceLotIndex >= meta.SourceLotObjects.Count)
            return null;

        GameObject go = meta.SourceLotObjects[sourceLotIndex];
        if (go == null)
            return null;

        return go.GetComponent<WallEditShape>();
    }

    Vector3 GetSourceCentroidWorld(WallEditShape srcEdit)
    {
        if (srcEdit == null)
            return Vector3.zero;

        switch (srcEdit.shapeKind)
        {
            case WallEditShape.ShapeKind.Rectangle:
                return srcEdit.GetControlPointWorld(8);
            case WallEditShape.ShapeKind.Triangle:
                return srcEdit.GetControlPointWorld(3);
            case WallEditShape.ShapeKind.Ellipse:
                return srcEdit.GetControlPointWorld(4);
            case WallEditShape.ShapeKind.Free:
            {
                var path = srcEdit.GetPreviewPathWorld();
                if (path == null || path.Count < 2)
                    return srcEdit.transform.position;
                int n = path.Count;
                if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
                    n--;
                Vector3 s = Vector3.zero;
                for (int i = 0; i < n; i++)
                    s += path[i];
                return s / Mathf.Max(1, n);
            }
            default:
                return srcEdit.transform.position;
        }
    }

    Vector3 GetPivotWorld()
    {
        WallEditShape src = TryResolveSourceEdit();
        return GetSourceCentroidWorld(src);
    }

    void LateUpdate()
    {
        if (cam == null || envelopeEdit == null || !envelopeEdit.isActiveAndEnabled)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        WallEditShape srcEdit = TryResolveSourceEdit();
        if (srcEdit == null)
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
            ApplyRaycastTargets();
            _rect.SetAsLastSibling();
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
        ApplyRaycastTargets();
        _rect.SetAsLastSibling();
    }

    void ApplyRaycastTargets()
    {
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);
        if (_graphics == null)
            return;

        bool on = true;
        if (ControlPointHandleUI.IsDraggingAnyHandle && !_dragging)
            on = false;
        else if (envelopeEdit != null && envelopeEdit.wall != null)
        {
            HouseExteriorEnvelopeSources hes = envelopeEdit.wall.GetComponent<HouseExteriorEnvelopeSources>();
            if (hes != null && hes.HasMultipleSourceLots)
                on = EnvelopeOverlayHandleFocus.ShouldPinkReceiveRaycasts(envelopeEdit.wall);
        }

        for (int i = 0; i < _graphics.Length; i++)
        {
            if (_graphics[i] != null)
                _graphics[i].raycastTarget = on;
        }
    }

    void ApplyVisualState()
    {
        // Si un sommet (ou tout le contour) du même mur que l’enveloppe est sélectionné, ne pas garder l’orange
        // « pending » sur une rose — sinon deux oranges (sommet + rose). Comparaison par mur, pas seulement par référence.
        bool handleSelectionOnThisWall =
            envelopeEdit != null &&
            envelopeEdit.wall != null &&
            ControlPointHandleUI.IsVertexOrBulkSelectionActiveOnWall(envelopeEdit.wall);

        // Pendant un drag de sommet ou du pivot, s_EnvelopePinkDragDepth peut rester à 0 : le pending post-fusion
        // ne doit pas rester orange en parallèle (sinon deux oranges sans que la sélection soit bien détectée).
        bool blockPendingBecauseOtherHandleDrag =
            ControlPointHandleUI.IsDraggingAnyHandle && !_dragging;

        bool pendingOk = PendingHighlightSourceLotIndex >= 0 &&
                           PendingHighlightSourceLotIndex == sourceLotIndex &&
                           s_EnvelopePinkDragDepth == 0 &&
                           !handleSelectionOnThisWall &&
                           !blockPendingBecauseOtherHandleDrag;
        bool on = _dragging || pendingOk;
        Color c = on ? SelectedOrange : IdlePink;
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

    /// <summary>Recolorise cette rose selon <see cref="PendingHighlightSourceLotIndex"/> (après fusion enveloppe).</summary>
    public void RefreshPinkVisualNow()
    {
        ApplyVisualState();
    }

    /// <summary>Après <c>RebuildOverlay</c>, les roses peuvent ne pas avoir encore relu le statique ; force une passe.</summary>
    public static void RefreshAllPinkHandleVisuals()
    {
        HouseEnvelopeSourceHandleUI[] arr = FindObjectsByType<HouseEnvelopeSourceHandleUI>(FindObjectsSortMode.None);
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] != null)
                arr[i].RefreshPinkVisualNow();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (cam == null || envelopeEdit == null || envelopeEdit.wall == null)
            return;

        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        // PointerDown fantôme après rebuild (gauche encore enfoncé) : retirer le pending avant le garde-fou.
        ClearPinkHighlightTracking();

        if (ControlPointHandleUI.ShouldBlockNewOverlayPointerDown())
            return;

        WallEditShape srcEdit = TryResolveSourceEdit();
        if (srcEdit == null)
            return;

        if (ControlPointHandleUI.IsPointerDragOwnedByAnother(this))
            return;

        if (ControlPointHandleUI.IsDraggingAnyHandle && !_dragging)
            return;

        EnvelopeOverlayHandleFocus.SetFocusPink(envelopeEdit.wall);

        ControlPointHandleUI.RegisterPointerDragOwner(this);

        WallUndoManager undo = GetUndoManager();
        if (undo != null)
            undo.RecordSnapshot("Move house source plane");

        ControlPointHandleUI.NotifyWallPointSelectionClearedForPivot();
        ControlPointHandleUI.SetPivotBulkMoveDragging(true, envelopeEdit);

        LastInteractedSourceLotIndex = sourceLotIndex;

        ActiveDragInstance = this;
        _dragging = true;
        s_EnvelopePinkDragDepth++;
        if (envelopeEdit.UsesMergedLotOrthogonalHandles)
            envelopeEdit.NotifyOrthogonalVertexDragStrokeStarted();

        Vector3 startWorld = GetPivotWorld();
        _dragPivotY = startWorld.y;
        _dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        if (TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            _offsetWorld = startWorld - hit;
        else
            _offsetWorld = Vector3.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || cam == null || envelopeEdit == null || envelopeEdit.wall == null)
            return;

        WallEditShape srcEdit = TryResolveSourceEdit();
        if (srcEdit == null)
            return;

        if (!TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            return;

        Vector3 intended = hit + _offsetWorld;
        Vector3 target = new Vector3(intended.x, _dragPivotY, intended.z);

        WallDrawInput di = GetWallDrawInput();
        if (di != null && di.enableGridSnap)
            target = di.SnapWorldPointForEditing(target);

        Vector3 before = GetSourceCentroidWorld(srcEdit);
        Vector3 delta = new Vector3(target.x - before.x, 0f, target.z - before.z);
        if (delta.sqrMagnitude < 1e-16f)
            return;

        srcEdit.TranslateClosedLotGeometryXZ(delta);

        WallBuildController bc = GetBuildController();
        if (bc != null)
            bc.TryRebuildHouseOuterEnvelopeFromSources(
                envelopeEdit.wall,
                snapMergedOutlineToGrid: false,
                refreshControlPointOverlay: false,
                recordUndoSnapshotWhenAutoSplit: false,
                immediateFullCladdingRefresh: false,
                preferSelectSourceWallAfterSplit: srcEdit.wall);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        // Rebuild + fusion d’abord tant que ActiveDragInstance / SetPivotBulkMoveDragging sont encore actifs :
        // sinon TryMerge → ForceSelectWall → RebuildOverlay laisse le violet capter les raycasts un instant
        // et casse la cohérence « je termine un drag rose ».
        WallBuildController bc = GetBuildController();
        if (bc != null && envelopeEdit != null && envelopeEdit.wall != null)
        {
            WallEditShape srcForPrefer = TryResolveSourceEdit();
            WallObject preferSourceWall = srcForPrefer != null ? srcForPrefer.wall : null;

            bc.TryRebuildHouseOuterEnvelopeFromSources(
                envelopeEdit.wall,
                snapMergedOutlineToGrid: true,
                refreshControlPointOverlay: false,
                recordUndoSnapshotWhenAutoSplit: true,
                immediateFullCladdingRefresh: false,
                preferSelectSourceWallAfterSplit: preferSourceWall);

            bc.TryMergeWallWithAdjacentLots(envelopeEdit.wall);
            bc.ScheduleEnvelopePinkReleaseVisualFollowup(envelopeEdit.wall);

            if (LastInteractedSourceLotIndex >= 0)
                PendingHighlightSourceLotIndex = LastInteractedSourceLotIndex;
            EnvelopeOverlayHandleFocus.SetFocusPink(envelopeEdit.wall);
        }

        _dragging = false;
        if (s_EnvelopePinkDragDepth > 0)
            s_EnvelopePinkDragDepth--;
        if (ActiveDragInstance == this)
            ActiveDragInstance = null;
        ControlPointHandleUI.ClearPointerDragOwnerIf(this);
        ControlPointHandleUI.SetPivotBulkMoveDragging(false, null);

        if (envelopeEdit != null)
            envelopeEdit.NotifyOrthogonalVertexDragStrokeEnded();
    }

    void OnDisable()
    {
        if (_dragging)
        {
            _dragging = false;
            if (s_EnvelopePinkDragDepth > 0)
                s_EnvelopePinkDragDepth--;
            if (ActiveDragInstance == this)
                ActiveDragInstance = null;
            ControlPointHandleUI.ClearPointerDragOwnerIf(this);
            ControlPointHandleUI.SetPivotBulkMoveDragging(false, null);
        }
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
