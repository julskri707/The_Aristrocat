using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class ControlPointHandleUI : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Binding (assigné par le Manager)")]
    public Camera cam;
    public IControlPointProvider provider;
    public int index;

    /// <summary>
    /// Identifiant simple : cette poignée suit le contour « forme » murale (<see cref="WallEditShape"/>, etc.) ou un système hors forme (toit, catalogue…).
    /// Source de vérité : <see cref="ControlPointShapeMembership.BelongsToWallShape"/>.
    /// </summary>
    public bool ControlPointBelongsToWallShape => ControlPointShapeMembership.BelongsToWallShape(provider);

    [Header("Drag")]
    public float groundY = 0f;
    public bool dragOnGroundPlane = true;

    [Header("UI")]
    [Tooltip("When true, the selected handle is moved to the front of the handles layer once when selection changes — not every frame (per-frame reorder was very expensive).")]
    public bool keepOnTop = true;
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(1f, 0.55f, 0.12f, 1f);

    public static bool IsDraggingAnyHandle { get; private set; }

    /// <summary>
    /// Vrai entre PointerDown et PointerUp pour le déplacement d’une poignée de sommet (pas le pivot lot entier).
    /// À utiliser pour reporter la fusion de points proches jusqu’au relâchement.
    /// </summary>
    public static bool IsVertexHandleDragActive { get; private set; }

    /// <summary>Poignée ou pivot qui a reçu le PointerDown : bloque tout autre clic jusqu’au PointerUp.</summary>
    static object s_pointerDragOwner;

    /// <summary>
    /// Après <c>RebuildOverlay</c> avec le bouton encore enfoncé : l’EventSystem peut envoyer un PointerDown
    /// sur le nouveau pivot central sous le curseur (faux clic). Ignoré jusqu’au relâchement.
    /// </summary>
    static bool s_BlockNewOverlayPointerDownUntilPrimaryRelease;

    public static void NotifyOverlayRebuildWhilePrimaryButtonMayStillBeHeld()
    {
        if (!Application.isPlaying)
            return;
        if (PrimaryPointerButtonOrTouchHeld())
            s_BlockNewOverlayPointerDownUntilPrimaryRelease = true;
    }

    static bool PrimaryPointerButtonOrTouchHeld()
    {
        if (Input.GetMouseButton(0))
            return true;
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            return t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
        }
        return false;
    }

    /// <summary>Vrai : ignorer ce PointerDown sur une poignée overlay (rebuild mid-geste).</summary>
    public static bool ShouldBlockNewOverlayPointerDown()
    {
        if (!s_BlockNewOverlayPointerDownUntilPrimaryRelease)
            return false;
        if (!PrimaryPointerButtonOrTouchHeld())
        {
            s_BlockNewOverlayPointerDownUntilPrimaryRelease = false;
            return false;
        }
        return true;
    }

    /// <summary>
    /// À appeler chaque frame : si le garde-fou est actif mais le bouton n’est plus enfoncé
    /// (relâchement sans passer par une poignée), on réinitialise — sinon plus aucun clic n’est accepté.
    /// </summary>
    public static void TickClearOverlayPointerBlockWhenPrimaryReleased()
    {
        if (!s_BlockNewOverlayPointerDownUntilPrimaryRelease)
            return;
        if (!PrimaryPointerButtonOrTouchHeld())
            s_BlockNewOverlayPointerDownUntilPrimaryRelease = false;
    }

    /// <summary>Après undo / restauration : l’overlay est reconstruit — ne pas laisser le blocage actif.</summary>
    public static void ClearOverlayPointerBlockAfterUndoOrRestore()
    {
        s_BlockNewOverlayPointerDownUntilPrimaryRelease = false;
    }

    public static bool IsPointerDragOwnedByAnother(object self) =>
        s_pointerDragOwner != null && !ReferenceEquals(s_pointerDragOwner, self);

    public static void RegisterPointerDragOwner(object self) => s_pointerDragOwner = self;

    public static void ClearPointerDragOwnerIf(object self)
    {
        if (ReferenceEquals(s_pointerDragOwner, self))
            s_pointerDragOwner = null;
    }

    /// <summary>
    /// Après séparation auto de l’enveloppe maison : le GameObject de la poignée peut être détruit avant PointerUp.
    /// </summary>
    public static void ForceCancelActivePointerDrag()
    {
        s_pointerDragOwner = null;
        s_PivotBulkMoveProvider = null;
        IsDraggingAnyHandle = false;
        IsVertexHandleDragActive = false;
    }

    public static IControlPointProvider SelectedProvider { get; private set; }
    public static int SelectedIndex { get; private set; } = -1;
    public static bool SelectAllOnProvider { get; private set; }

    /// <summary>
    /// Si vrai cette frame : la molette a fait tourner un mur (handle central) — le zoom caméra est ignoré.
    /// </summary>
    public static bool BlockCameraZoomFromWallShapeScroll { get; set; }

    static bool s_RotateScrollUndoArmed = true;

    static IControlPointProvider s_PivotBulkMoveProvider;
    public static WallEditShape ActivePivotBulkMoveWallEdit => s_PivotBulkMoveProvider as WallEditShape;

    /// <summary>
    /// Wall whose control point is currently being dragged (for performance: only that wall should mark cladding dirty).
    /// </summary>
    public static WallObject TryGetWallObjectForDraggedProvider()
    {
        if (s_PivotBulkMoveProvider is WallEditShape pivotEdit && pivotEdit.wall != null)
            return pivotEdit.wall;

        if (SelectedProvider == null)
            return null;

        if (SelectedProvider is WallEditShape edit && edit.wall != null)
            return edit.wall;

        if (SelectedProvider is HouseRoofControlPointProvider roofSel && roofSel.HostWall != null)
            return roofSel.HostWall;

        if (SelectedProvider is Component c)
            return c.GetComponent<WallObject>();

        return null;
    }

    /// <summary>
    /// Sélection de contour (sommet ou tout le mur) sur ce mur — pour désactiver le surlignage « pending » des roses enveloppe
    /// même si <see cref="SelectedProvider"/> n’est pas la même instance de <see cref="WallEditShape"/> que <c>envelopeEdit</c>.
    /// </summary>
    public static bool IsVertexOrBulkSelectionActiveOnWall(WallObject wall)
    {
        if (wall == null)
            return false;
        if (SelectAllOnProvider && ProviderBelongsToWall(SelectedProvider, wall))
            return true;
        if (SelectedIndex < 0)
            return false;
        return ProviderBelongsToWall(SelectedProvider, wall);
    }

    static bool ProviderBelongsToWall(IControlPointProvider provider, WallObject wall)
    {
        if (provider == null || wall == null)
            return false;
        if (provider is WallEditShape wes)
            return wes.wall == wall;
        if (provider is HouseRoofControlPointProvider roofProv)
            return roofProv.HostWall == wall;
        if (provider is Component c)
            return c.GetComponent<WallObject>() == wall;
        return false;
    }

    /// <summary>
    /// Déplacement global via <see cref="MergedLotShapePivotHandleUI"/> : même signal que drag de handle pour le cladding.
    /// </summary>
    public static void SetPivotBulkMoveDragging(bool dragging, WallEditShape edit)
    {
        if (dragging)
        {
            s_PivotBulkMoveProvider = edit;
            IsDraggingAnyHandle = true;
        }
        else
        {
            if (edit == null || ReferenceEquals(s_PivotBulkMoveProvider, edit))
                s_PivotBulkMoveProvider = null;
            IsDraggingAnyHandle = false;
        }
    }

    /// <summary>
    /// Avant drag du pivot : retire la sélection d’un sommet pour éviter double surbrillance.
    /// </summary>
    public static void NotifyWallPointSelectionClearedForPivot()
    {
        SelectedProvider = null;
        SelectedIndex = -1;
        SelectAllOnProvider = false;
    }
    private static int s_LastDeleteFrame = -1;
    private static int s_LastSelectAllFrame = -1;

    static WallUndoManager s_CachedUndoManager;
    static WallDrawInput s_CachedWallDrawInput;
    static ControlPointOverlayManager s_CachedOverlayManager;
    static WallBuildController s_CachedBuildController;

    static WallUndoManager GetUndoManager()
    {
        if (s_CachedUndoManager == null)
            s_CachedUndoManager = FindFirstObjectByType<WallUndoManager>();
        return s_CachedUndoManager;
    }

    static WallDrawInput GetWallDrawInput()
    {
        if (s_CachedWallDrawInput == null)
            s_CachedWallDrawInput = FindFirstObjectByType<WallDrawInput>();
        return s_CachedWallDrawInput;
    }

    static ControlPointOverlayManager GetOverlayManager()
    {
        if (s_CachedOverlayManager == null)
            s_CachedOverlayManager = FindFirstObjectByType<ControlPointOverlayManager>();
        return s_CachedOverlayManager;
    }

    static WallBuildController GetBuildController()
    {
        if (s_CachedBuildController == null)
            s_CachedBuildController = FindFirstObjectByType<WallBuildController>();
        return s_CachedBuildController;
    }

    private RectTransform _rect;
    private Graphic _graphic;
    private Graphic[] _graphics;
    private SpriteRenderer _spriteRenderer;
    private SpriteRenderer[] _spriteRenderers;
    private bool _dragging;
    private Plane _dragPlane;
    private Vector3 _offsetWorld;
    private Vector3 _dragWholeShapeStartWorld;
    private bool _wholeShapeLatticeStepDrag;

    bool _pushedMergedPivotOverlayPreserveForCenterHouseDrag;

    /// <summary>
    /// Unity n’appelle <see cref="OnDrag"/> que si le pointeur a bougé au-delà du seuil : sans ça, un simple clic
    /// déclenchait quand même le relâchement « post-drag » (fusion maison, rebuild enveloppe) et pouvait dupliquer la forme.
    /// </summary>
    bool _pointerDragSawMoveEvent;

    private Canvas _rootCanvas;
    private RectTransform _canvasRect;
    private Camera _uiCamera;

    Vector3 _lastWorldForLayout;
    Vector3 _lastCamPosForLayout;
    Quaternion _lastCamRotForLayout;
    bool _hasLayoutCache;
    bool _lastSelectedForTop;

    /// <summary>
    /// Poignée hors champ / derrière la caméra : on masque les graphiques sans <see cref="GameObject.SetActive"/>,
    /// sinon <see cref="LateUpdate"/> ne tourne plus et l’overlay ne se réaffiche jamais au retour caméra.
    /// </summary>
    bool _sceneCameraLayoutSuppressed;

    void Awake()
    {
        CacheCanvas();
    }

    void Update()
    {
        // Suppr / Échap / Ctrl+A : indépendant du layout écran — sinon les poignées hors champ ou masquées ne reçoivent jamais les touches.
        if (cam != null && provider != null &&
            provider == SelectedProvider &&
            (SelectAllOnProvider || index == SelectedIndex))
            HandleDeleteInput();

        if (cam == null || provider == null)
            return;
        if (SelectedProvider != provider || SelectedIndex != index)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (IsCatalogCentralDragHandle(provider, index))
        {
            if (Mathf.Abs(scroll) < 1e-6f)
            {
                s_RotateScrollUndoArmed = true;
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            WallUndoManager undoCatalog = GetUndoManager();
            if (undoCatalog != null && s_RotateScrollUndoArmed)
            {
                undoCatalog.RecordSnapshot("Rotate catalog object");
                s_RotateScrollUndoArmed = false;
            }

            Component provComp = provider as Component;
            if (provComp != null)
            {
                int steps = Mathf.RoundToInt(scroll * 10f);
                if (steps == 0)
                    steps = scroll > 0f ? 1 : -1;
                if (provComp is PlacedWallOpeningManipulator wallOp)
                {
                    int n = Mathf.Max(1, Mathf.Abs(steps));
                    for (int i = 0; i < n; i++)
                        wallOp.ApplyCenterWheelQuarterTurn();
                }
                else
                {
                    provComp.transform.Rotate(0f, steps * 90f, 0f, Space.World);
                    if (provComp is PlacedStairManipulator stair)
                        stair.NotifyRotatedWithFootprintClamp();
                }
            }

            BlockCameraZoomFromWallShapeScroll = true;
            return;
        }

        if (provider is not WallEditShape edit)
            return;
        if (!IsCenterLikeHandle(edit, index))
            return;

        if (Mathf.Abs(scroll) < 1e-6f)
        {
            s_RotateScrollUndoArmed = true;
            return;
        }

        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        bool interiorElevateWithShift =
            edit.IsOpenFreeVerticalCenterHandleIndex(index) && shiftDown && !ctrlDown;

        WallUndoManager undo = GetUndoManager();
        if (undo != null && s_RotateScrollUndoArmed)
        {
            undo.RecordSnapshot(interiorElevateWithShift ? "Wall elevation" : "Rotate Shape");
            s_RotateScrollUndoArmed = false;
        }

        if (interiorElevateWithShift)
        {
            edit.OffsetShapeWorldY(scroll * edit.verticalScrollElevationMetersPerWheelUnit);
            BlockCameraZoomFromWallShapeScroll = true;
            return;
        }

        // Rotation modes:
        // - Wheel only: 16 standard orientations (22.5° per step)
        // - Ctrl + wheel: legacy continuous 360° rotation
        // - Ctrl + Shift + wheel: finer snapped rotation
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
        BlockCameraZoomFromWallShapeScroll = true;
    }

    /// <summary>À appeler depuis la caméra en LateUpdate : retourne true si le zoom doit être ignoré (une seule fois).</summary>
    public static bool ConsumeWallScrollBlockForCamera()
    {
        if (!BlockCameraZoomFromWallShapeScroll)
            return false;
        BlockCameraZoomFromWallShapeScroll = false;
        return true;
    }

    /// <summary>
    /// Après destruction d’un mur (fusion, etc.) : réinitialise la sélection si le provider n’existe plus.
    /// </summary>
    public static void ClearStaleWallSelectionState()
    {
        UnityEngine.Object p = SelectedProvider as UnityEngine.Object;
        if (p != null)
            return;

        SelectedProvider = null;
        SelectedIndex = -1;
        SelectAllOnProvider = false;
        IsDraggingAnyHandle = false;
        s_PivotBulkMoveProvider = null;
        s_pointerDragOwner = null;
    }

    /// <summary>
    /// Après fusion / changement structurel : rattache la sélection UI au mur cible (pivot global si disponible).
    /// </summary>
    public static void ApplyEditingSelectionToWallEditShape(WallEditShape edit)
    {
        if (edit == null)
            return;

        SelectedProvider = edit;

        if (SelectAllOnProvider)
            return;

        int n = edit.ControlPointCount;
        if (n <= 0)
        {
            SelectedIndex = -1;
            return;
        }

        // Enveloppe maison multi-plans : ne pas auto-sélectionner l’indice « centre » (souvent masqué par l’overlay) ;
        // la rose / l’enveloppe portent l’intention — évite l’effet « centre verrouillé » avec le pivot violet.
        if (edit.wall != null)
        {
            HouseExteriorEnvelopeSources hes = edit.wall.GetComponent<HouseExteriorEnvelopeSources>();
            if (hes != null && hes.HasMultipleSourceLots)
            {
                SelectedIndex = -1;
                return;
            }
        }

        if (edit.TryGetShapeBulkMovePivotInfo(out int sk, out _) && sk >= 0 && sk < n)
        {
            SelectedIndex = sk;
            return;
        }

        if (SelectedIndex < 0 || SelectedIndex >= n)
            SelectedIndex = 0;
    }

    /// <summary>
    /// Après fusion / passage enveloppe multi-plans : ne pas auto-sélectionner le pivot global (évite de « voler » le drag vers le centre violet).
    /// La rose reste mise en avant via le champ statique correspondant sur <c>HouseEnvelopeSourceHandleUI</c>.
    /// </summary>
    public static void ApplyEditingSelectionAfterHouseEnvelopeMerge(WallEditShape envelopeEdit)
    {
        if (envelopeEdit == null)
            return;

        SelectedProvider = envelopeEdit;
        SelectAllOnProvider = false;
        SelectedIndex = -1;
        MergedLotShapePivotHandleUI.ClearActivePivotForScroll();
    }

    /// <summary>
    /// Après fusion : Rectangle → Free ; l’index 8 (centre) n’existe plus sans recréer les handles.
    /// </summary>
    public static void ResyncSelectionAfterMergeIntoSurvivor(WallEditShape survivorEdit)
    {
        ClearStaleWallSelectionState();
        ApplyEditingSelectionToWallEditShape(survivorEdit);
    }

    void OnEnable()
    {
        CacheCanvas();
        _sceneCameraLayoutSuppressed = false;
    }

    void OnDisable()
    {
        if (_pushedMergedPivotOverlayPreserveForCenterHouseDrag)
        {
            _pushedMergedPivotOverlayPreserveForCenterHouseDrag = false;
            MergedLotShapePivotHandleUI.PopMergedPivotOverlayPreserve();
        }

        if (_dragging)
        {
            _dragging = false;
            IsDraggingAnyHandle = false;
            IsVertexHandleDragActive = false;
            ClearPointerDragOwnerIf(this);
        }
    }

    void CacheCanvas()
    {
        _rect = (RectTransform)transform;
        if (_graphic == null)
            _graphic = GetComponent<Graphic>();
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);
        if (_spriteRenderer == null)
            _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderers == null || _spriteRenderers.Length == 0)
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);

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

        if (_rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            _uiCamera = null;
        else
            _uiCamera = _rootCanvas.worldCamera;

        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
    }

    void EnsureGraphicsCachedForCameraSuppress()
    {
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);
        if (_spriteRenderers == null || _spriteRenderers.Length == 0)
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
    }

    void SetSceneCameraLayoutSuppressed(bool suppressed)
    {
        if (_sceneCameraLayoutSuppressed == suppressed)
            return;

        _sceneCameraLayoutSuppressed = suppressed;
        EnsureGraphicsCachedForCameraSuppress();

        if (_graphics != null)
        {
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] == null)
                    continue;
                _graphics[i].enabled = !suppressed;
                _graphics[i].raycastTarget = !suppressed;
            }
        }

        if (_spriteRenderers != null)
        {
            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null)
                    _spriteRenderers[i].enabled = !suppressed;
            }
        }
    }

    void LateUpdate()
    {
        if (cam == null || provider == null)
            return;

        if (_rect == null || _canvasRect == null)
            CacheCanvas();

        if (_canvasRect == null)
            return;

        int count = provider.ControlPointCount;
        if (index < 0 || index >= count)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        if (!provider.IsControlPointEditable(index))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        Vector3 world = provider.GetControlPointWorld(index);

        if (_hasLayoutCache
            && (world - _lastWorldForLayout).sqrMagnitude < 1e-10f
            && (cam.transform.position - _lastCamPosForLayout).sqrMagnitude < 1e-10f
            && Quaternion.Angle(cam.transform.rotation, _lastCamRotForLayout) < 0.01f)
        {
            if (_sceneCameraLayoutSuppressed)
            {
                SetSceneCameraLayoutSuppressed(true);
                return;
            }

            ApplySelectionColor();
            bool selectedNow = provider != null &&
                               provider == SelectedProvider &&
                               (SelectAllOnProvider || index == SelectedIndex);
            if (keepOnTop && selectedNow != _lastSelectedForTop)
            {
                if (selectedNow)
                    _rect.SetAsLastSibling();
                _lastSelectedForTop = selectedNow;
            }
            else if (!keepOnTop)
                _lastSelectedForTop = selectedNow;

            return;
        }

        _lastWorldForLayout = world;
        _lastCamPosForLayout = cam.transform.position;
        _lastCamRotForLayout = cam.transform.rotation;
        _hasLayoutCache = true;

        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
        {
            SetSceneCameraLayoutSuppressed(true);
            return;
        }

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, _uiCamera, out localPoint))
        {
            SetSceneCameraLayoutSuppressed(true);
            return;
        }

        SetSceneCameraLayoutSuppressed(false);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        _rect.anchoredPosition = localPoint;
        ApplySelectionColor();

        bool selected = provider != null &&
                        provider == SelectedProvider &&
                        (SelectAllOnProvider || index == SelectedIndex);
        if (keepOnTop)
        {
            if (selected != _lastSelectedForTop)
            {
                if (selected)
                    _rect.SetAsLastSibling();
                _lastSelectedForTop = selected;
            }
        }
        else
            _lastSelectedForTop = selected;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (cam == null || provider == null)
            return;

        // Avant le garde-fou rebuild : un PointerDown « fantôme » doit quand même retirer le pending rose,
        // sinon rose orange + (futur) sommet orange tant que le joueur ne reclique pas.
        HouseEnvelopeSourceHandleUI.ClearPinkHighlightTracking();

        if (ShouldBlockNewOverlayPointerDown())
            return;

        if (IsPointerDragOwnedByAnother(this))
            return;

        int count = provider.ControlPointCount;
        if (index < 0 || index >= count)
            return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            // Clic droit : cycle sommet central ou toutes les poignées latérales (liste jusqu'à 4).
            int roofLateralSlot = provider is HouseRoofControlPointProvider rp ? index - rp.IdxHorizontalApexMove : -1;
            if (provider is HouseRoofControlPointProvider roofProvider &&
                (index == HouseRoofControlPointProvider.IdxHeight ||
                 (roofLateralSlot >= 0 && roofLateralSlot < roofProvider.LateralApexControlCount)))
            {
                bool ctrl =
                    Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                Vector3 worldHit = transform.position;
                Plane hp = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
                Ray rr = cam.ScreenPointToRay(eventData.position);
                if (hp.Raycast(rr, out float hh))
                    worldHit = rr.GetPoint(hh);

                HouseRoofSystem roofSys = roofProvider.GetComponentInParent<HouseRoofSystem>();
                bool changed = false;
                if (roofSys != null)
                    roofSys.ApplyRoofHeightHandleRightClickCycle(worldHit, roofProvider, ctrl, out changed);

                WallUndoManager undoRight = GetUndoManager();
                if (undoRight != null && changed)
                    undoRight.RecordSnapshot("Roof Horizontal Apex Handle Mode");

                SelectedProvider = provider;
                SelectedIndex = index;
                SelectAllOnProvider = false;

                // Rebuild à chaque clic : le cycle toit met parfois à jour sans changement de hash (re-snap, etc.) — l’UI doit toujours suivre.
                ControlPointOverlayManager overlay = GetOverlayManager();
                if (overlay != null)
                    overlay.RebuildOverlay();
                return;
            }

            TryOpenLotBuildMenuIfClosedLotCenter(eventData);
            return;
        }

        // Un seul drag à la fois : pas de nouveau clic sur une autre poignée tant que le bouton n’est pas relâché.
        if (IsDraggingAnyHandle && !_dragging)
            return;

        WallUndoManager undo = GetUndoManager();
        if (undo != null)
            undo.RecordSnapshot("Move Handle");

        SelectedProvider = provider;
        SelectedIndex = index;
        SelectAllOnProvider = false;

        _dragging = true;
        _pointerDragSawMoveEvent = false;
        IsDraggingAnyHandle = true;
        IsVertexHandleDragActive = true;
        RegisterPointerDragOwner(this);

        // Lot source rattaché à une enveloppe : sortir du mode « pivot violet prioritaire » pour que les roses
        // reçoivent à nouveau les raycasts (voir EnvelopeOverlayHandleFocus).
        if (provider is WallEditShape editBundled && editBundled.wall != null)
        {
            WallObject env = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(editBundled.wall);
            if (env != null)
                EnvelopeOverlayHandleFocus.SetFocusPink(env);
        }

        if (provider is WallEditShape editStroke && editStroke.UsesMergedLotOrthogonalHandles)
            editStroke.NotifyOrthogonalVertexDragStrokeStarted(index);

        Vector3 startWorld = provider.GetControlPointWorld(index);

        if (dragOnGroundPlane)
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        else
            _dragPlane = new Plane(-cam.transform.forward, startWorld);

        if (provider is IControlPointDragPlaneProvider dragPlaneProv &&
            dragPlaneProv.TryGetDragPlane(index, cam, startWorld, out Plane customPlane))
            _dragPlane = customPlane;

        if (TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            _offsetWorld = startWorld - hit;
        else
            _offsetWorld = Vector3.zero;

        _dragWholeShapeStartWorld = startWorld;
        WallDrawInput di = GetWallDrawInput();
        _wholeShapeLatticeStepDrag = di != null && di.enableGridSnap &&
                                     provider is WallEditShape edit && IsCenterLikeHandle(edit, index) &&
                                     di.TryGetMainGridLatticeStepXZ(out _, out _);

        if (provider is WallEditShape edHouse &&
            IsCenterLikeHandle(edHouse, index) &&
            edHouse.wall != null)
        {
            HouseParquetFloor hf = edHouse.wall.GetComponent<HouseParquetFloor>();
            if (hf != null && hf.IsDesignatedHouseLot)
            {
                MergedLotShapePivotHandleUI.PushMergedPivotOverlayPreserve();
                _pushedMergedPivotOverlayPreserveForCenterHouseDrag = true;
            }
        }
    }

    /// <summary>
    /// Clic droit sur la poignée « centre » (bleu) d’un lot fermé : menu maison / champ (voir <see cref="IsCenterLikeHandle"/>).
    /// </summary>
    void TryOpenLotBuildMenuIfClosedLotCenter(PointerEventData eventData)
    {
        if (provider is not WallEditShape edit)
            return;

        if (!IsCenterLikeHandle(edit, index) || !edit.IsClosedLoopPath)
            return;

        WallObject wall = edit.wall;
        if (wall == null)
            return;

        LotBuildMenuUI lot = FindFirstObjectByType<LotBuildMenuUI>(FindObjectsInactive.Include);
        if (lot == null)
            return;

        WallContextMenuUI ctx = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);
        if (ctx != null && ctx.IsOpen)
            ctx.Close();

        lot.OpenForClosedLot(wall, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || cam == null || provider == null)
            return;

        _pointerDragSawMoveEvent = true;

        int count = provider.ControlPointCount;
        if (index < 0 || index >= count)
            return;

        if (!TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            return;

        Vector3 intended = hit + _offsetWorld;
        Vector3 newWorld;

        WallDrawInput di = GetWallDrawInput();
        if (_wholeShapeLatticeStepDrag && di != null)
        {
            newWorld = intended;
            newWorld.y = _dragWholeShapeStartWorld.y;
            if (di.enableGridSnap && di.snapToHierarchicalVisualGrid)
                newWorld = di.SnapWorldToUniformMainLattice(newWorld);
        }
        else
            newWorld = SnapDraggedPointIfNeeded(intended);

        provider.SetControlPointWorld(index, newWorld);

        if (provider is WallEditShape wed && wed.wall != null)
            TryRefreshBundledHouseEnvelopeDuringSourceDrag(wed);

        if (_wholeShapeLatticeStepDrag &&
            di != null &&
            di.enableGridSnap &&
            provider is WallEditShape editShape &&
            !ReferenceEquals(ActivePivotBulkMoveWallEdit, editShape))
            editShape.SnapAllControlPointsToHierarchicalGrid(di);

        // Ne pas appeler TryMergeWallWithAdjacentLots ici : à chaque frame de drag ça relance toute la fusion
        // et peut instancier une nouvelle enveloppe / empiler géométrie (voir MergedLotShapePivotHandleUI :
        // fusion au relâchement uniquement). La fusion voisins se fait dans OnPointerUp.
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        bool didDrag = _dragging;
        bool wasWholeShapeLatticeCenterDrag = _wholeShapeLatticeStepDrag;
        _dragging = false;
        IsDraggingAnyHandle = false;
        IsVertexHandleDragActive = false;
        ClearPointerDragOwnerIf(this);
        _wholeShapeLatticeStepDrag = false;

        if (_pushedMergedPivotOverlayPreserveForCenterHouseDrag)
        {
            _pushedMergedPivotOverlayPreserveForCenterHouseDrag = false;
            MergedLotShapePivotHandleUI.PopMergedPivotOverlayPreserve();
        }

        // Aimantation coin / autre mur : uniquement au relâchement (pas pendant le drag).
        bool didSnapOrthogonalHandleToCorner = false;
        bool orthogonalSnapTargetWasForeignCorner = false;
        bool meaningfulPointerDrag = didDrag && _pointerDragSawMoveEvent;
        if (meaningfulPointerDrag &&
            !wasWholeShapeLatticeCenterDrag &&
            cam != null &&
            provider is WallEditShape editRelease &&
            editRelease.UsesMergedLotOrthogonalHandles)
        {
            WallBuildController bc = GetBuildController();
            if (bc != null &&
                bc.snapOrthogonalEditHandlesToWallCorners &&
                TryScreenToPlaneWorld(eventData.position, _dragPlane, out Vector3 hitRelease))
            {
                Vector3 releaseWorld = hitRelease + _offsetWorld;
                releaseWorld = SnapDraggedPointIfNeeded(releaseWorld);
                didSnapOrthogonalHandleToCorner = bc.TrySnapWorldPointForOrthogonalHandleDrag(
                    ref releaseWorld, editRelease, index, out orthogonalSnapTargetWasForeignCorner);
                provider.SetControlPointWorld(index, releaseWorld);
            }
        }

        // Fusion auto des points proches d’abord, centrage grille ensuite (priorité autoconnexion).
        if (provider is WallEditShape editEnd)
        {
            if (meaningfulPointerDrag && !wasWholeShapeLatticeCenterDrag)
            {
                WallDrawInput di = GetWallDrawInput();
                bool skipLeafSnap = editEnd.IsOrthogonalWallMidHandleIndex(index);
                Vector3 anchorBeforeMerge = default;
                if (!skipLeafSnap)
                    anchorBeforeMerge = editEnd.GetControlPointWorld(index);

                editEnd.NotifyOrthogonalVertexDragStrokeEnded(index);

                if (!skipLeafSnap && di != null)
                    editEnd.SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(di, anchorBeforeMerge);
            }
            else
                editEnd.NotifyOrthogonalVertexDragStrokeEnded(index);
        }

        // Fusion lots avec les voisins — sauf si la poignée déplacée est le coin rentrant intérieur (ongle ~270°) du L/U :
        // dans ce cas TryMergeWallWithAdjacentLots provoquait des fusions en chaîne avec les deux bras du même contour.
        if (meaningfulPointerDrag && provider is WallEditShape movedEdit && movedEdit.wall != null)
        {
            WallBuildController bc = GetBuildController();
            if (bc != null)
            {
                WallObject bundledEnv = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(movedEdit.wall);
                if (bundledEnv != null)
                {
                    bc.TryRebuildHouseOuterEnvelopeFromSources(
                        bundledEnv,
                        snapMergedOutlineToGrid: true,
                        refreshControlPointOverlay: false,
                        recordUndoSnapshotWhenAutoSplit: true,
                        immediateFullCladdingRefresh: true,
                        preferSelectSourceWallAfterSplit: movedEdit.wall);

                    bc.TryMergeWallWithAdjacentLots(bundledEnv);
                    bc.ScheduleEnvelopePinkReleaseVisualFollowup(bundledEnv);
                    bc.ScheduleDeferredHouseEnvelopeRebuildAfterWhiteHandleEdit(movedEdit.wall);
                }
                else
                {
                    bool skipMerge = movedEdit.UsesMergedLotOrthogonalHandles &&
                                     (movedEdit.IsOrthogonalReflexInteriorCornerAtIndex(index) ||
                                      movedEdit.ShouldSuppressInterWallSnapAndLotMergeAtIndex(index));

                    bool mergeAllowed = true;
                    if (movedEdit.UsesMergedLotOrthogonalHandles &&
                        bc.MergeAdjacentLotsOnlyWhenOrthogonalHandleSnappedToForeignCorner)
                    {
                        // Déplacement du lot entier (poignée centre bleue, etc.) : fusion dès que les empreintes
                        // se touchent / se chevauchent — pas besoin d’accrocher un sommet à un coin extérieur.
                        // Le garde coin-étranger reste pour les poignées de contour (évite fusions accidentelles en L/U).
                        if (IsCenterLikeHandle(movedEdit, index))
                            mergeAllowed = true;
                        else
                        {
                            // Ne fusionne avec un mur voisin que si le relâchement a vraiment accroché à un coin /
                            // intersection hors des poignées de ce lot (sinon simple contact ou snap interne).
                            mergeAllowed = didSnapOrthogonalHandleToCorner && orthogonalSnapTargetWasForeignCorner;
                        }
                    }

                    if (!skipMerge && mergeAllowed)
                        bc.TryMergeWallWithAdjacentLots(movedEdit.wall);
                    bc.ScheduleDeferredHouseEnvelopeRebuildAfterWhiteHandleEdit(movedEdit.wall);
                }
            }
        }
    }

    static void TryRefreshBundledHouseEnvelopeDuringSourceDrag(WallEditShape sourceEdit)
    {
        // Ne pas appeler ResolveEnvelopeForSourceLot ici : exécuté chaque frame pendant le drag.
        // Un tag manquant forcerait FindObjectsByType en boucle (gel / crash). Le tag est réparé
        // au PointerDown (SetFocusPink) et au relâchement (fusion enveloppe).
        WallObject env = HouseEnvelopeBundledSourceTag.GetEnvelopeIfBundled(sourceEdit.wall);
        if (env == null)
            return;

        WallBuildController bc = GetBuildController();
        if (bc == null)
            return;

        // Contour enveloppe + maillage vectoriel : à jour chaque frame. Avec immediateFullCladdingRefresh: false
        // on évite parquet + coroutine lourds, mais il faut quand même recalculer les pierres sur l’enveloppe
        // (sinon décalage visible typique des formes « maison » multi-plans).
        bc.TryRebuildHouseOuterEnvelopeFromSources(
            env,
            snapMergedOutlineToGrid: false,
            refreshControlPointOverlay: false,
            recordUndoSnapshotWhenAutoSplit: false,
            immediateFullCladdingRefresh: false,
            preferSelectSourceWallAfterSplit: sourceEdit.wall);

        WallCladdingGenerator envCladding = env.GetComponent<WallCladdingGenerator>();
        if (envCladding != null)
            envCladding.EnsureStoneCladdingEnabledAndRefresh();
    }

    private bool TryScreenToPlaneWorld(Vector2 screenPos, Plane plane, out Vector3 world)
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

    private Vector3 SnapDraggedPointIfNeeded(Vector3 world)
    {
        if (provider is HouseRoofControlPointProvider)
            return world;
        if (provider is PlacedWallOpeningManipulator)
            return world;

        WallDrawInput di = GetWallDrawInput();
        if (di == null || !di.enableGridSnap)
            return world;

        return di.SnapWorldPointForEditing(world);
    }

    /// <summary>
    /// Poignée de déplacement « objet catalogue » : lit / prefabs sol (0) ; escalier (4).
    /// </summary>
    static bool IsCatalogCentralDragHandle(IControlPointProvider prov, int handleIndex)
    {
        return prov is CatalogPlacedObjectDraggable && handleIndex == 0
               || prov is PlacedStairManipulator && handleIndex == 4
               || prov is PlacedWallOpeningManipulator && handleIndex == 4;
    }

    /// <summary>
    /// Indices match WallEditShape: rectangle 8, triangle centroid 3, ellipse center 4, open arc center 2.
    /// </summary>
    static bool IsCenterLikeHandle(WallEditShape edit, int handleIndex)
    {
        switch (edit.shapeKind)
        {
            case WallEditShape.ShapeKind.Rectangle: return handleIndex == 8;
            case WallEditShape.ShapeKind.Triangle: return handleIndex == 3;
            case WallEditShape.ShapeKind.Ellipse: return handleIndex == 4;
            case WallEditShape.ShapeKind.OpenArc: return handleIndex == 2;
            case WallEditShape.ShapeKind.Free:
                if (edit.IsOpenFreeVerticalCenterHandleIndex(handleIndex))
                    return true;
                if (!edit.IsClosedLoopPath || !edit.UsesMergedLotOrthogonalHandles)
                    return false;
                int c = edit.ControlPointCount;
                return c > 1 && handleIndex == c - 1;
            default: return false;
        }
    }

    private void ApplySelectionColor()
    {
        // Sommet haut : toujours jaune. Poignées faîtage horizontal : ambre entre jaune et orange (fixes).
        const float roofAmberA = 1f;
        const float roofAmberAG = 0.82f;
        const float roofAmberAB = 0.28f;
        const float roofAmberB = 1f;
        const float roofAmberBG = 0.68f;
        const float roofAmberBB = 0.22f;

        if (IsCatalogCentralDragHandle(provider, index))
        {
            Color baseGreen = new Color(0.22f, 0.78f, 0.34f, 1f);
            Color selGreen = new Color(0.35f, 0.95f, 0.48f, 1f);
            bool sel = provider != null &&
                       provider == SelectedProvider &&
                       (SelectAllOnProvider || index == SelectedIndex);
            Color cCenter = sel ? selGreen : baseGreen;
            ApplyColorToHandleGraphics(cCenter);
            return;
        }

        if (provider is HouseRoofControlPointProvider roofCp)
        {
            Color roofTint = default;
            bool roofTintSet = false;
            if (index == HouseRoofControlPointProvider.IdxHeight)
            {
                roofTint = Color.yellow;
                roofTintSet = true;
            }
            else if (roofCp.IsHorizontalApexHandleEnabled)
            {
                int ls = index - roofCp.IdxHorizontalApexMove;
                if (ls >= 0 && ls < roofCp.LateralApexControlCount)
                {
                    roofTint = (ls % 2 == 0)
                        ? new Color(roofAmberA, roofAmberAG, roofAmberAB, 1f)
                        : new Color(roofAmberB, roofAmberBG, roofAmberBB, 1f);
                    roofTintSet = true;
                }
            }

            if (roofTintSet)
            {
                if (_graphic != null)
                    _graphic.color = roofTint;
                if (_graphics != null)
                {
                    for (int i = 0; i < _graphics.Length; i++)
                    {
                        if (_graphics[i] != null)
                            _graphics[i].color = roofTint;
                    }
                }

                if (_spriteRenderer != null)
                    _spriteRenderer.color = roofTint;
                if (_spriteRenderers != null)
                {
                    for (int i = 0; i < _spriteRenderers.Length; i++)
                    {
                        if (_spriteRenderers[i] != null)
                            _spriteRenderers[i].color = roofTint;
                    }
                }

                return;
            }
        }

        bool selected = provider != null &&
                        provider == SelectedProvider &&
                        (SelectAllOnProvider || index == SelectedIndex);
        Color c = selected ? selectedColor : normalColor;
        ApplyColorToHandleGraphics(c);
    }

    void ApplyColorToHandleGraphics(Color c)
    {
        if (_graphic != null)
            _graphic.color = c;
        if (_graphics != null)
        {
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] != null)
                    _graphics[i].color = c;
            }
        }

        if (_spriteRenderer != null)
            _spriteRenderer.color = c;
        if (_spriteRenderers != null)
        {
            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null)
                    _spriteRenderers[i].color = c;
            }
        }
    }

    private void HandleDeleteInput()
    {
        if (provider == null || provider != SelectedProvider)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SelectAllOnProvider = false;
            return;
        }

        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool selectAllPressed = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Q);
        if (ctrlDown && selectAllPressed)
        {
            if (s_LastSelectAllFrame != Time.frameCount)
            {
                SelectAllOnProvider = true;
                s_LastSelectAllFrame = Time.frameCount;
            }
            return;
        }

        bool isCurrentPoint = index == SelectedIndex;
        if (!SelectAllOnProvider && !isCurrentPoint)
            return;

        // Poignées de mouvement (pivot global, milieux d’arête, milieux de mur orthogonal…) :
        // Suppr = supprimer toute la forme (mur).
        if (provider is WallEditShape moveEdit &&
            !SelectAllOnProvider &&
            moveEdit.IsNonDeletableMovementHandleIndex(index))
        {
            if (!TryReserveWallShapeDeleteHotkey())
                return;

            WallUndoManager undoMove = GetUndoManager();
            if (undoMove != null)
                undoMove.RecordSnapshot("Delete Wall (Move Handle)");

            bool removedWhole = TryDeleteWholeWall();
            if (!removedWhole)
                return;

            ControlPointOverlayManager overlayMove = GetOverlayManager();
            if (overlayMove != null)
                overlayMove.RebuildOverlay();
            return;
        }

        if (!TryReserveWallShapeDeleteHotkey())
            return;

        bool removed = TryDeleteSelectedPoint();
        if (!removed)
            return;

        ControlPointOverlayManager overlay = GetOverlayManager();
        if (overlay != null)
            overlay.RebuildOverlay();
    }

    private bool TryDeleteSelectedPoint()
    {
        WallUndoManager undo = GetUndoManager();

        // Ctrl+A / Ctrl+Q puis Suppr : suppression du mur entier (comportement d’origine).
        if (SelectAllOnProvider)
        {
            if (undo != null)
                undo.RecordSnapshot("Delete Wall (All Points)");
            return TryDeleteWholeWall();
        }

        WallEditShape editShape = provider as WallEditShape;
        if (editShape == null)
            return false;

        // Les formes analytiques (rectangle / triangle / ellipse / arc) gardent leurs poignées :
        // seule une forme libre (points "normaux") peut supprimer un sommet individuellement.
        if (editShape.shapeKind != WallEditShape.ShapeKind.Free)
            return false;

        // Mur ouvert réduit à un segment (2 points) : comme avant, une suppression enlève tout le mur.
        if (editShape.shapeKind == WallEditShape.ShapeKind.Free &&
            !editShape.IsClosedLoopPath &&
            editShape.freeControlPoints != null &&
            editShape.freeControlPoints.Count == 2)
        {
            if (undo != null)
                undo.RecordSnapshot("Delete Handle");
            return TryDeleteWholeWall();
        }

        // Demi-cercle (arc ouvert), polyline en S, sommets de contours fermés, etc. : un seul point.
        if (undo != null)
            undo.RecordSnapshot("Delete Handle");
        return editShape.RemoveControlPointAt(index);
    }

    private bool TryDeleteWholeWall()
    {
        Component providerComponent = provider as Component;
        if (providerComponent == null)
            return false;

        WallObject wall = providerComponent.GetComponent<WallObject>();
        if (wall == null && provider is WallEditShape wes && wes.wall != null)
            wall = wes.wall;

        // Enveloppe multi-lots : supprimer un seul lot source (forme focalisée), pas tout l’ensemble.
        wall = ResolveSingleShapeDeleteTarget(wall);

        return DestroyWholeWallGameObject(wall);
    }

    static WallObject ResolveSingleShapeDeleteTarget(WallObject wall)
    {
        if (wall == null)
            return null;

        HouseExteriorEnvelopeSources env = wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (env == null || !env.HasMultipleSourceLots || !env.UseIndependentSourceHandlesForHouseEnvelope)
            return wall;

        ControlPointOverlayManager overlay = GetOverlayManager();
        int focused = overlay != null ? overlay.IndependentHouseEnvelopeFocusedSourceLotIndex : -1;
        IReadOnlyList<GameObject> src = env.SourceLotObjects;
        if (src == null || focused < 0 || focused >= src.Count)
            return wall;

        GameObject go = src[focused];
        if (go == null)
            return wall;

        WallObject srcWall = go.GetComponent<WallObject>();
        return srcWall != null ? srcWall : wall;
    }

    /// <summary>
    /// Détruit le GameObject du mur et nettoie sélection / pivot actif (sans snapshot undo — l’appelant enregistre si besoin).
    /// </summary>
    public static bool DestroyWholeWallGameObject(WallObject wall)
    {
        if (wall == null)
            return false;

        WallBuildController build = GetBuildController();
        if (build != null)
            build.UnregisterWall(wall);

        MergedLotShapePivotHandleUI.InvalidateActivePivotIfTargetsWall(wall);

        ControlPointOverlayManager overlay = GetOverlayManager();
        if (overlay != null)
            overlay.ClearTarget();

        SelectedProvider = null;
        SelectedIndex = -1;
        SelectAllOnProvider = false;

        if (Application.isPlaying)
            Destroy(wall.gameObject);
        else
            DestroyImmediate(wall.gameObject);

        return true;
    }

    /// <summary>
    /// Suppr / Retour arrière : une seule action par frame (sommet, pivot violet, Ctrl+A…).
    /// </summary>
    public static bool TryReserveWallShapeDeleteHotkey()
    {
        if (!Input.GetKeyDown(KeyCode.Delete) && !Input.GetKeyDown(KeyCode.Backspace))
            return false;
        if (s_LastDeleteFrame == Time.frameCount)
            return false;
        s_LastDeleteFrame = Time.frameCount;
        return true;
    }
}
