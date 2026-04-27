using System.Collections.Generic;
using System.Collections;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class WallBuildController : MonoBehaviour
{
    struct RectBounds
    {
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
        public float y;
    }

    struct LotMergeInfo
    {
        public WallObject wall;
        public WallEditShape edit;
        public RectBounds aabb;
        public List<WallOrthoMergeUtility.RectXZ> footprint;
    }

    [Header("References")]
    public Camera cam;
    public WallDrawInput drawInput;
    public ControlPointOverlayManager overlay;
    public WallUndoManager undoManager;

    [Header("Prefabs")]
    public WallObject wallPrefab;

    [Header("Default Style")]
    public WallStyleDefinition defaultWallStyle;

    [Header("Selection")]
    public LayerMask wallRaycastMask = ~0;
    public float rayDistance = 5000f;
    public bool handleSelectionInput = false;

    [Header("Debug")]
    public bool logDebug = false;

    [Header("Dessin — raccord aux murs existants")]
    [Tooltip("Si activé, le tracé et le relâchement s’aimantent aux sommets des murs déjà placés (évite les décarts au coin / à l’intersection).")]
    public bool snapDrawToExistingWallCorners = true;
    [Tooltip("Si activé, au relâchement d’une poignée de contour (lot L/U fusionné), recolle au même XZ qu’un coin voisin ou d’un autre mur — pas pendant le déplacement.")]
    public bool snapOrthogonalEditHandlesToWallCorners = true;
    [Tooltip("Distance max (m) pour accrocher le curseur à un sommet de mur existant.")]
    [SerializeField, Min(0.02f)] float wallCornerSnapRadius = 0.24f;
    public float WallCornerSnapRadius => wallCornerSnapRadius;
    [Tooltip("Obsolète : la fusion au relâchement du tracé est désactivée. Les lots ne s’unissent en périmètre extérieur qu’en rapprochant des murs déjà désignés « maison ».")]
    [SerializeField] bool mergeLotsWhenCommitSnappedToWallCorner = true;
    [Tooltip("Après déplacement d’une poignée de contour (lot orthogonal) : ne fusionner avec un mur voisin que si le relâchement a accroché la poignée à un coin ou une intersection qui n’est pas déjà une poignée de ce même lot. Sinon la fusion peut se déclencher dès que les contours se touchent sans raccord volontaire au coin.")]
    [SerializeField] bool mergeAdjacentLotsOnlyWhenOrthogonalHandleSnappedToForeignCorner = true;

    /// <summary>Voir tooltip du champ : fusion après drag de poignée seulement si accrochage à un coin hors du lot courant.</summary>
    public bool MergeAdjacentLotsOnlyWhenOrthogonalHandleSnappedToForeignCorner =>
        mergeAdjacentLotsOnlyWhenOrthogonalHandleSnappedToForeignCorner;

    [Header("Fusion de lots (murs qui se touchent)")]
    [Tooltip("Marge (m) pour détecter deux rectangles comme collés (épaisseur de mur / imprécision).")]
    public float mergeContactTolerance = 0.2f;
    [Tooltip("Fusion : deux lots doivent partager un côté aligné grille. Écart max = max(plancher, pas_grille × fraction).")]
    [SerializeField, Range(0.0005f, 0.08f)] float flushMergeMaxGapFractionOfCell = 0.022f;
    [Tooltip("Plancher absolu (m) pour l’alignement bord à bord.")]
    [SerializeField, Min(0.0002f)] float flushMergeMaxGapAbsoluteM = 0.005f;
    [Tooltip("Maisons adjacentes: conserve les lots sources (cachés) et met à jour un mur enveloppe extérieur unique au lieu de fusionner destructivement les lots.")]
    [SerializeField] bool designatedHouseLotsUseOuterEnvelopeOnly = true;
    [Tooltip("Obsolète/conservé pour compatibilité scène : la fusion auto maison est toujours active côté code et ne concerne que les lots maison.")]
    [SerializeField] bool mergeDrawnOrPresetShapesWithAdjacentDesignatedHouseOnCommit = true;

    [Header("Duplication (Ctrl+C / Ctrl+V)")]
    [Tooltip("Si désactivé, les raccourcis ne sont pas traités.")]
    public bool enableClipboardDuplicate = true;
    [Tooltip("Décalage monde XZ appliqué au mur dupliqué (évite la superposition exacte).")]
    public Vector2 pasteOffsetXZ = new Vector2(1f, 1f);
    [Tooltip("Plancher par défaut au collage d’une maison multi-sources si la copie n’a pas de matériau (enveloppe sans parquet, etc.). Sinon le premier LotBuildMenuUI.defaultParquetMaterial dans la scène.")]
    [SerializeField] Material defaultHouseParquetMaterialForPaste;

    [Header("Menu lot maison — Ajouter un mur")]
    [Tooltip("Demi-longueur (m) du segment ouvert créé ; longueur totale ≈ 2 × cette valeur.")]
    [SerializeField, Min(0.05f)] float houseMenuSpawnWallHalfLengthM = 1.25f;
    [Tooltip("Déplacement vertical (m) par unité Unity de la molette pour un mur avec élévation (voir WallEditShape.allowVerticalScrollElevation).")]
    [SerializeField, Min(0.01f)] float verticalScrollElevationMetersPerWheelUnit = 5f;
    [Header("Menu lot maison — Ajouter un étage")]
    [Tooltip("Hauteur (m) ajoutée au périmètre du lot et aux murs intérieurs rattachés à chaque « Ajouter un étage » (sans dupliquer le contour).")]
    [SerializeField, Min(0.1f)] float addFloorHeightMeters = 2.5f;

    private readonly List<WallObject> _walls = new List<WallObject>();
    readonly List<WallObject> _wallGatherScratch = new List<WallObject>(32);
    readonly List<Vector3> _ringScratchA = new List<Vector3>(64);
    readonly List<Vector3> _ringScratchB = new List<Vector3>(64);
    readonly List<Vector2> _lotFootprintRingScratch = new List<Vector2>(128);

    List<Vector3> _clipboardPath;
    WallDrawInput.DetectedShapeKind _clipboardKind;
    bool _clipboardHasData;
    float _clipboardHeight = 2.5f;
    float _clipboardThickness = 0.25f;
    bool _clipboardAllowVerticalScrollElevation;
    WallEditShape _clipboardInteriorLotForConstraint;
    bool _clipboardPasteClosedFreeAsMergedLotOutline;

    /// <summary>Maison multi-plans : plusieurs lots sources + enveloppe recalculée au collage (pas un seul polyline).</summary>
    bool _clipboardIsMultiSourceHouseBundle;
    readonly List<ClipboardHouseSourceSnapshot> _clipboardHouseSourceSnapshots = new List<ClipboardHouseSourceSnapshot>(8);
    float _clipboardHouseEnvelopeHeight;
    bool _clipboardHouseIndependentHandles;
    bool _clipboardHouseEnvelopeHadParquet;
    Material _clipboardHouseEnvelopeParquetMaterial;
    float _clipboardHouseEnvelopeParquetUv;
    float _clipboardHouseEnvelopeParquetY;

    sealed class ClipboardHouseSourceSnapshot
    {
        public List<Vector3> pathWorld;
        public WallDrawInput.DetectedShapeKind kind;
        public float height;
        public float thickness;
    }

    bool _verticalScrollElevationUndoArmed = true;
    WallObject _lastWallForVerticalScrollElevation;

    public IReadOnlyList<WallObject> Walls => _walls;
    public WallObject SelectedWall { get; private set; }

    /// <summary>Hauteur d’un étage (m) pour dalles empilées / murs intérieurs par niveau — aligner <see cref="HouseParquetFloor.storeyHeightMeters"/>.</summary>
    public float AddFloorHeightMeters => addFloorHeightMeters;

    static void RunWithCladdingRebuildSuspended(Action action)
    {
        WallCladdingGenerator.SetGlobalRebuildSuspended(true);
        try
        {
            action?.Invoke();
        }
        finally
        {
            WallCladdingGenerator.SetGlobalRebuildSuspended(false);
        }
    }

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
    }

    void OnEnable()
    {
        if (drawInput != null)
            drawInput.OnShapeCommittedDetailed += HandleShapeCommittedDetailed;
    }

    void OnDisable()
    {
        if (drawInput != null)
            drawInput.OnShapeCommittedDetailed -= HandleShapeCommittedDetailed;
    }

    void Update()
    {
        CleanupNullWalls();

        HandleVerticalScrollElevationOnSelectedWall();

        if (enableClipboardDuplicate && wallPrefab != null)
            HandleClipboardDuplicateInput();

        if (!handleSelectionInput || cam == null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.GetMouseButtonDown(0))
            TrySelectWallUnderMouse();
    }

    void HandleVerticalScrollElevationOnSelectedWall()
    {
        if (SelectedWall == null)
        {
            _lastWallForVerticalScrollElevation = null;
            return;
        }

        if (SelectedWall != _lastWallForVerticalScrollElevation)
        {
            _lastWallForVerticalScrollElevation = SelectedWall;
            _verticalScrollElevationUndoArmed = true;
        }

        WallEditShape edit = SelectedWall.GetComponent<WallEditShape>();
        if (edit == null || !edit.allowVerticalScrollElevation)
            return;

        if (ControlPointHandleUI.SelectedProvider == edit && ControlPointHandleUI.SelectedIndex >= 0)
            return;

        if (MergedLotShapePivotHandleUI.ActivePivotForScroll != null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (IsTypingInUiInputField())
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-5f)
        {
            _verticalScrollElevationUndoArmed = true;
            return;
        }

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null && _verticalScrollElevationUndoArmed)
        {
            undoManager.RecordSnapshot("Wall elevation");
            _verticalScrollElevationUndoArmed = false;
        }

        float delta = scroll * verticalScrollElevationMetersPerWheelUnit;
        edit.OffsetShapeWorldY(delta);
        ControlPointHandleUI.BlockCameraZoomFromWallShapeScroll = true;
    }

    /// <summary>
    /// Depuis le menu du pivot violet : crée un mur ouvert (segment) au centre du lot, réglable en hauteur à la molette.
    /// Un exemplaire par niveau (hauteur d’étage = <see cref="addFloorHeightMeters"/>), aligné sur la hauteur totale du lot.
    /// </summary>
    public void SpawnOpenWallFromHouseMenu(WallObject referenceLot)
    {
        if (wallPrefab == null || referenceLot == null)
            return;

        WallEditShape refEdit = referenceLot.GetComponent<WallEditShape>();
        if (refEdit == null || !refEdit.TryGetHouseLotSpawnCenterWorld(out Vector3 center))
            return;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Add wall from house menu");

        float story = Mathf.Max(0.1f, addFloorHeightMeters);
        int floorCount = Mathf.Max(1, Mathf.RoundToInt(referenceLot.height / story));
        const int maxFloorCountForSingleAction = 24;
        if (floorCount > maxFloorCountForSingleAction)
        {
            if (logDebug)
                Debug.LogWarning($"[WallBuildController] SpawnOpenWallFromHouseMenu capped floors from {floorCount} to {maxFloorCountForSingleAction}.", this);
            floorCount = maxFloorCountForSingleAction;
        }

        WallObject lastWall = null;
        RunWithCladdingRebuildSuspended(() =>
        {
            for (int k = 0; k < floorCount; k++)
            {
                Vector3 centerK = new Vector3(center.x, refEdit.shapeY + k * story, center.z);
                Vector3 a;
                Vector3 b;
                if (!TryBuildOpenSegmentAcrossReferenceLot(refEdit, centerK, out a, out b))
                {
                    float half = Mathf.Max(0.05f, houseMenuSpawnWallHalfLengthM);
                    float y = refEdit.shapeY + k * story;
                    a = new Vector3(center.x - half, y, center.z);
                    b = new Vector3(center.x + half, y, center.z);
                }

                var segment = new List<Vector3>(2) { a, b };

                WallObject wall = Instantiate(wallPrefab);
                wall.transform.position = Vector3.zero;
                wall.height = story;
                wall.thickness = referenceLot.thickness;

                WallEditShape editShape = wall.GetComponent<WallEditShape>();
                if (editShape == null)
                    editShape = wall.gameObject.AddComponent<WallEditShape>();

                editShape.wall = wall;
                editShape.allowVerticalScrollElevation = true;
                editShape.interiorWallsStayInsideLot = refEdit;
                editShape.InitFromPath(segment);

                if (drawInput != null)
                {
                    var pathForMainGrid = new List<Vector3>(wall.Points);
                    bool loopClosed = wall.closedLoop;
                    drawInput.SnapCommittedPathToMainGridInPlace(pathForMainGrid, loopClosed);
                    wall.SetPath(pathForMainGrid);
                    editShape.InitFromPath(pathForMainGrid);
                }

                editShape.ClampInteriorWallToLotFootprintIfConfigured();

                WallSelectable selectable = wall.GetComponent<WallSelectable>();
                if (selectable == null)
                    selectable = wall.gameObject.AddComponent<WallSelectable>();

                selectable.providerBehaviour = editShape;

                if (defaultWallStyle != null)
                    WallStyleApplier.Apply(wall, defaultWallStyle);

                EnsureWallStoneCladdingEnabled(wall);
                RegisterExistingWall(wall);
                RequestDeferredCladdingRefresh(wall);
                lastWall = wall;
            }
        });

        if (lastWall != null)
            ForceSelectWall(lastWall);

        if (logDebug)
            Debug.Log("[WallBuildController] Spawned open wall(s) from house menu (per storey).");
    }

    /// <summary>
    /// Un seul lot fermé : hauteur + dalle + duplication des murs intérieurs du dernier niveau.
    /// Pas d’undo ni de <see cref="RunWithCladdingRebuildSuspended"/> — l’appelant s’en charge.
    /// </summary>
    void ApplyAddFloorToSingleClosedLot(WallObject referenceLot, float story)
    {
        WallEditShape lotEdit = referenceLot.GetComponent<WallEditShape>();
        if (lotEdit == null || !lotEdit.IsClosedLoopPath)
            return;

        HouseParquetFloor parquet = referenceLot.GetComponent<HouseParquetFloor>();
        if (parquet != null)
            parquet.storeyHeightMeters = story;

        ExtendLotWallHeightOnly(referenceLot, story);

        if (TryGetMaxInteriorShapeYAttachedToLot(lotEdit, out float maxInteriorY))
            DuplicateInteriorWallsOnTopFloor(lotEdit, maxInteriorY, story);

        lotEdit.ApplyToWall();
    }

    /// <summary>
    /// Depuis le menu du pivot violet : augmente la hauteur du périmètre du lot (un étage), ajoute une dalle au niveau,
    /// duplique les murs intérieurs du dernier niveau vers le haut — sans dupliquer le contour du lot.
    /// </summary>
    public WallObject AddFloorFromHouseMenu(WallObject referenceLot)
    {
        if (referenceLot == null)
            return null;

        WallEditShape lotEdit = referenceLot.GetComponent<WallEditShape>();
        if (lotEdit == null || !lotEdit.IsClosedLoopPath)
            return null;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Add floor from house menu");

        float story = Mathf.Max(0.1f, addFloorHeightMeters);

        RunWithCladdingRebuildSuspended(() => ApplyAddFloorToSingleClosedLot(referenceLot, story));

        // Maison multi-plans : hauteur commune = min(sources) sur l’enveloppe (tout le bas du pourtour). Les lots
        // plus hauts qu’un seul côté ont la pierre du haut générée <b>uniquement</b> sur le contour de ce lot (bande
        // haute, sans doubler l’enveloppe sur 0..min, voir <see cref="HouseEnvelopeBundledSourceVisuals"/>).
        TrySyncBundledShellHeightsAfterIndividualStoreyAddOnSource(referenceLot);

        ForceSelectWall(referenceLot);

        if (logDebug)
            Debug.Log("[WallBuildController] Extended wall height for add floor.");

        return referenceLot;
    }

    /// <summary>
    /// Enveloppe maison + ≥2 lots : l’enveloppe reste à <c>min</c> (hauteur commune) sur tout l’emprise fusionnée ;
    /// chaque lot plus haut reçoit la pierre extérieure du « surplus » de hauteur uniquement sur <b>son</b> périmètre
    /// (le bas reste couvert par l’enveloppe seule, sans 2e couche de pierre).
    /// </summary>
    void TrySyncBundledShellHeightsAfterIndividualStoreyAddOnSource(WallObject lotAfterStoreyAdd)
    {
        if (lotAfterStoreyAdd == null)
            return;

        WallObject env = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(lotAfterStoreyAdd, true);
        if (env == null || env == lotAfterStoreyAdd)
            return;

        HouseExteriorEnvelopeSources meta = env.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null)
            return;

        IReadOnlyList<GameObject> gos = meta.SourceLotObjects;
        if (gos == null)
            return;

        float minH = env.height;
        for (int i = 0; i < gos.Count; i++)
        {
            if (gos[i] == null)
                continue;
            WallObject w = gos[i].GetComponent<WallObject>();
            if (w == null)
                continue;
            minH = Mathf.Min(minH, w.height);
        }

        if (minH < env.height - 0.0001f)
        {
            env.SetHeight(minH);
            EnsureWallStoneCladdingEnabled(env);
            if (isActiveAndEnabled)
                StartCoroutine(CoRefreshCladdingAfterLotMerge(env));
        }
        else if (isActiveAndEnabled)
            StartCoroutine(CoRefreshCladdingAfterLotMerge(env));

        for (int i = 0; i < gos.Count; i++)
        {
            if (gos[i] == null)
                continue;
            WallObject w = gos[i].GetComponent<WallObject>();
            if (w == null)
                continue;

            if (w.height > minH + 0.01f)
            {
                HouseEnvelopeBundledSourceVisuals.ApplyTallerSourceUpperBandExteriorCladdingOnly(w, minH);

                WallEditShape ed = w.GetComponent<WallEditShape>();
                HouseParquetFloor pf = w.GetComponent<HouseParquetFloor>();
                if (pf != null && ed != null)
                    ApplyHouseParquetForDesignatedClosedLot(pf, w, ed);

                EnsureWallStoneCladdingEnabled(w);
                if (isActiveAndEnabled)
                    StartCoroutine(CoRefreshCladdingAfterLotMerge(w));
            }
            else
                HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(w, true);
        }

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>(FindObjectsInactive.Include);
        if (overlay != null)
            overlay.RebuildOverlay();
    }

    /// <summary>
    /// Depuis le pivot violet (enveloppe multi-plans) : +1 étage sur l’enveloppe <b>et</b> sur chaque lot source lié
    /// sur <see cref="HouseExteriorEnvelopeSources"/>, un seul snapshot undo. Si pas de composant / pas de sources, équivalent à <see cref="AddFloorFromHouseMenu"/>.
    /// </summary>
    public WallObject AddFloorToEntireLinkedHouseEnsemble(WallObject envelopeWall)
    {
        if (envelopeWall == null)
            return null;

        WallEditShape envEdit = envelopeWall.GetComponent<WallEditShape>();
        if (envEdit == null || !envEdit.IsClosedLoopPath)
            return null;

        HouseExteriorEnvelopeSources hes = envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
        if (hes == null || hes.SourceLotObjects == null || hes.SourceLotObjects.Count == 0)
            return AddFloorFromHouseMenu(envelopeWall);

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Add floor (toute la maison liée)");

        float story = Mathf.Max(0.1f, addFloorHeightMeters);

        var targets = new HashSet<WallObject> { envelopeWall };
        IReadOnlyList<GameObject> srcGos = hes.SourceLotObjects;
        for (int i = 0; i < srcGos.Count; i++)
        {
            GameObject go = srcGos[i];
            if (go == null)
                continue;
            WallObject w = go.GetComponent<WallObject>();
            if (w == null || w == envelopeWall)
                continue;
            WallEditShape e = w.GetComponent<WallEditShape>();
            if (e != null && e.IsClosedLoopPath)
                targets.Add(w);
        }

        RunWithCladdingRebuildSuspended(() =>
        {
            foreach (WallObject w in targets)
                ApplyAddFloorToSingleClosedLot(w, story);
        });

        ForceSelectWall(envelopeWall);

        if (logDebug)
            Debug.Log("[WallBuildController] Add floor ensemble: " + targets.Count + " lots.");

        return envelopeWall;
    }

    /// <summary>
    /// Augmente uniquement la hauteur du mur périphérique du lot (pas les murs intérieurs : un segment par niveau).
    /// </summary>
    void ExtendLotWallHeightOnly(WallObject lotWall, float deltaH)
    {
        if (lotWall == null || deltaH < 1e-6f)
            return;

        lotWall.SetHeight(lotWall.height + deltaH);
    }

    bool TryGetMaxInteriorShapeYAttachedToLot(WallEditShape lotEdit, out float maxY)
    {
        maxY = float.NegativeInfinity;
        bool any = false;
        CleanupNullWalls();
        for (int i = 0; i < _walls.Count; i++)
        {
            WallObject w = _walls[i];
            if (w == null)
                continue;

            WallEditShape e = w.GetComponent<WallEditShape>();
            if (e == null || e.interiorWallsStayInsideLot != lotEdit)
                continue;

            any = true;
            maxY = Mathf.Max(maxY, e.shapeY);
        }

        return any;
    }

    void DuplicateInteriorWallsOnTopFloor(WallEditShape lotEdit, float maxInteriorY, float storyH)
    {
        const float yEps = 0.07f;
        const int maxDuplicatedInteriorWallsPerFloorAdd = 64;
        int duplicated = 0;
        CleanupNullWalls();
        for (int i = 0; i < _walls.Count; i++)
        {
            if (duplicated >= maxDuplicatedInteriorWallsPerFloorAdd)
            {
                if (logDebug)
                    Debug.LogWarning($"[WallBuildController] DuplicateInteriorWallsOnTopFloor cap reached ({maxDuplicatedInteriorWallsPerFloorAdd}).", this);
                break;
            }

            WallObject w = _walls[i];
            if (w == null)
                continue;

            WallEditShape e = w.GetComponent<WallEditShape>();
            if (e == null || e.interiorWallsStayInsideLot != lotEdit)
                continue;

            if (Mathf.Abs(e.shapeY - maxInteriorY) > yEps)
                continue;

            DuplicateInteriorWallAtShapeY(w, e, lotEdit, maxInteriorY + storyH);
            duplicated++;
        }
    }

    void DuplicateInteriorWallAtShapeY(WallObject sourceWall, WallEditShape sourceEdit, WallEditShape lotEdit, float newShapeY)
    {
        if (wallPrefab == null || sourceWall == null || sourceEdit == null || lotEdit == null)
            return;

        List<Vector3> src = sourceEdit.GetClipboardDuplicatePathWorld();
        if (src == null || src.Count < 2)
            return;

        var newPath = new List<Vector3>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            Vector3 p = src[i];
            newPath.Add(new Vector3(p.x, newShapeY, p.z));
        }

        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.height = Mathf.Max(0.1f, addFloorHeightMeters);
        wall.thickness = sourceWall.thickness;

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape == null)
            editShape = wall.gameObject.AddComponent<WallEditShape>();

        editShape.wall = wall;
        editShape.allowVerticalScrollElevation = true;
        editShape.interiorWallsStayInsideLot = lotEdit;

        WallDrawInput.DetectedShapeKind kind = sourceEdit.GetClipboardDetectedKind();
        if (sourceEdit.ShouldPasteClosedFreeAsMergedLotOutline && newPath.Count >= 4)
        {
            bool snapOutline = drawInput != null && drawInput.enableGridSnap && drawInput.snapToHierarchicalVisualGrid;
            editShape.InitFromMergedLotOutline(newPath, drawInput, snapOutline);
        }
        else
        {
            editShape.InitFromDetectedPath(newPath, kind);
            if (wall.Points == null || wall.Points.Count < 2)
                editShape.InitFromPath(newPath);

            if (drawInput != null)
            {
                var pathForMainGrid = new List<Vector3>(wall.Points);
                bool loopClosed = wall.closedLoop;
                drawInput.SnapCommittedPathToMainGridInPlace(pathForMainGrid, loopClosed);
                wall.SetPath(pathForMainGrid);
                editShape.InitFromDetectedPath(pathForMainGrid, kind);
                if (wall.Points == null || wall.Points.Count < 2)
                    editShape.InitFromPath(pathForMainGrid);
            }
        }

        editShape.ClampInteriorWallToLotFootprintIfConfigured();

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable == null)
            selectable = wall.gameObject.AddComponent<WallSelectable>();

        selectable.providerBehaviour = editShape;

        if (defaultWallStyle != null)
            WallStyleApplier.Apply(wall, defaultWallStyle);

        EnsureWallStoneCladdingEnabled(wall);
        RegisterExistingWall(wall);
        RequestDeferredCladdingRefresh(wall);
    }

    bool TryBuildOpenSegmentAcrossReferenceLot(WallEditShape referenceEdit, Vector3 center, out Vector3 a, out Vector3 b)
    {
        a = default;
        b = default;
        if (referenceEdit == null)
            return false;

        _lotFootprintRingScratch.Clear();
        bool haveRing = referenceEdit.TryGetClosedLotFootprintRingXZ(_lotFootprintRingScratch) &&
                        _lotFootprintRingScratch.Count >= 3;

        float clipInset = 0f;
        if (haveRing)
        {
            float t = referenceEdit.wall != null ? referenceEdit.wall.thickness : 0.25f;
            clipInset = WallEditShape.ClampInsetToFeasibleRingXZ(
                _lotFootprintRingScratch,
                WallEditShape.ComputeOpenInteriorWallFootprintInsetMeters(t, t));
        }

        List<Vector3> path = referenceEdit.GetPreviewPathWorld();
        if (path == null || path.Count < 2)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;
        if (n < 2)
            return false;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float spanX = maxX - minX;
        float spanZ = maxZ - minZ;
        if (spanX < 0.05f && spanZ < 0.05f)
            return false;

        float y = center.y;

        bool TryClipCandidate(Vector3 ca, Vector3 cb, out Vector3 oa, out Vector3 ob)
        {
            oa = ca;
            ob = cb;
            if (!haveRing)
                return (cb - ca).sqrMagnitude > 1e-6f;

            if (!WallEditShape.TryClipOpenWorldSegmentToLotRingXZ(ca, cb, _lotFootprintRingScratch, out oa, out ob, clipInset))
                return false;

            return (ob - oa).sqrMagnitude > 0.01f;
        }

        void BuildHorizontalAabb(out Vector3 ca, out Vector3 cb)
        {
            float inset = Mathf.Min(0.2f, spanX * 0.15f);
            float x0 = minX + inset;
            float x1 = maxX - inset;
            if (x1 - x0 < 0.1f)
            {
                x0 = minX;
                x1 = maxX;
            }

            ca = new Vector3(x0, y, center.z);
            cb = new Vector3(x1, y, center.z);
        }

        void BuildVerticalAabb(out Vector3 ca, out Vector3 cb)
        {
            float inset = Mathf.Min(0.2f, spanZ * 0.15f);
            float z0 = minZ + inset;
            float z1 = maxZ - inset;
            if (z1 - z0 < 0.1f)
            {
                z0 = minZ;
                z1 = maxZ;
            }

            ca = new Vector3(center.x, y, z0);
            cb = new Vector3(center.x, y, z1);
        }

        if (spanX >= spanZ)
        {
            BuildHorizontalAabb(out Vector3 h0, out Vector3 h1);
            if (TryClipCandidate(h0, h1, out a, out b))
                return true;

            BuildVerticalAabb(out Vector3 v0, out Vector3 v1);
            if (TryClipCandidate(v0, v1, out a, out b))
                return true;
        }
        else
        {
            BuildVerticalAabb(out Vector3 v0, out Vector3 v1);
            if (TryClipCandidate(v0, v1, out a, out b))
                return true;

            BuildHorizontalAabb(out Vector3 h0, out Vector3 h1);
            if (TryClipCandidate(h0, h1, out a, out b))
                return true;
        }

        if (!haveRing)
        {
            if (spanX >= spanZ)
                BuildHorizontalAabb(out a, out b);
            else
                BuildVerticalAabb(out a, out b);

            return true;
        }

        float half = Mathf.Max(0.05f, houseMenuSpawnWallHalfLengthM);
        Vector3 fa = new Vector3(center.x - half, y, center.z);
        Vector3 fb = new Vector3(center.x + half, y, center.z);
        if (WallEditShape.TryClipOpenWorldSegmentToLotRingXZ(fa, fb, _lotFootprintRingScratch, out a, out b, clipInset))
            return (b - a).sqrMagnitude > 1e-6f;

        return false;
    }

    static bool IsTypingInUiInputField()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
            return false;

        GameObject go = EventSystem.current.currentSelectedGameObject;
        return go.GetComponent<InputField>() != null || go.GetComponent<TMP_InputField>() != null;
    }

    void HandleClipboardDuplicateInput()
    {
        if (IsTypingInUiInputField())
            return;

        bool ctrlOrCmd = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                         Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (!ctrlOrCmd)
            return;

        if (Input.GetKeyDown(KeyCode.C))
            TryCopySelectedWallToClipboard();

        if (Input.GetKeyDown(KeyCode.V))
            TryPasteWallFromClipboard();
    }

    /// <summary>
    /// Mur à dupliquer : <see cref="SelectedWall"/> peut être null si la sélection vient seulement des poignées UI
    /// (sans nouveau raycast sur le mesh).
    /// </summary>
    bool TryResolveWallForClipboard(out WallObject wall, out WallEditShape edit)
    {
        wall = null;
        edit = null;

        if (SelectedWall != null)
        {
            wall = SelectedWall;
            edit = wall.GetComponent<WallEditShape>();
            if (edit != null)
                return true;
        }

        if (ControlPointHandleUI.SelectedProvider is WallEditShape wes && wes.wall != null)
        {
            edit = wes;
            wall = wes.wall;
            return true;
        }

        if (overlay != null && overlay.targetProviderBehaviour is WallEditShape wes2 && wes2.wall != null)
        {
            edit = wes2;
            wall = wes2.wall;
            return true;
        }

        return false;
    }

    bool TryCopyMultiSourceHouseToClipboard(WallObject envelopeWall, HouseExteriorEnvelopeSources meta)
    {
        if (envelopeWall == null || meta == null || !meta.HasMultipleSourceLots)
            return false;

        _clipboardHouseSourceSnapshots.Clear();

        IReadOnlyList<GameObject> srcGos = meta.SourceLotObjects;
        for (int i = 0; i < srcGos.Count; i++)
        {
            GameObject go = srcGos[i];
            if (go == null)
                continue;

            WallObject wo = go.GetComponent<WallObject>();
            WallEditShape wes = wo != null ? wo.GetComponent<WallEditShape>() : null;
            if (wo == null || wes == null)
                continue;

            List<Vector3> path = wes.GetClipboardDuplicatePathWorld();
            if (path == null || path.Count < 2)
                continue;

            var snap = new ClipboardHouseSourceSnapshot();
            snap.pathWorld = new List<Vector3>(path.Count);
            for (int j = 0; j < path.Count; j++)
                snap.pathWorld.Add(path[j]);
            snap.kind = wes.GetClipboardDetectedKind();
            snap.height = wo.height;
            snap.thickness = wo.thickness;
            _clipboardHouseSourceSnapshots.Add(snap);
        }

        if (_clipboardHouseSourceSnapshots.Count < 2)
        {
            _clipboardHouseSourceSnapshots.Clear();
            return false;
        }

        _clipboardHouseEnvelopeHeight = envelopeWall.height;
        _clipboardHouseIndependentHandles = meta.UseIndependentSourceHandlesForHouseEnvelope;
        _clipboardHouseEnvelopeHadParquet = false;
        _clipboardHouseEnvelopeParquetMaterial = null;

        HouseParquetFloor envPf = envelopeWall.GetComponent<HouseParquetFloor>();
        if (envPf != null && envPf.parquetMaterial != null)
        {
            _clipboardHouseEnvelopeHadParquet = true;
            _clipboardHouseEnvelopeParquetMaterial = envPf.parquetMaterial;
            _clipboardHouseEnvelopeParquetUv = envPf.uvMetersPerTile;
            _clipboardHouseEnvelopeParquetY = envPf.yOffsetAboveBase;
        }

        // Souvent le parquet vit sur les lots sources ; l’enveloppe n’a que le mesh pierre.
        if (!_clipboardHouseEnvelopeHadParquet && meta.SourceLotObjects != null)
        {
            for (int si = 0; si < meta.SourceLotObjects.Count; si++)
            {
                GameObject sgo = meta.SourceLotObjects[si];
                if (sgo == null)
                    continue;
                WallObject swo = sgo.GetComponent<WallObject>();
                if (swo == null)
                    continue;
                HouseParquetFloor pf = swo.GetComponent<HouseParquetFloor>();
                if (pf != null && pf.parquetMaterial != null)
                {
                    _clipboardHouseEnvelopeHadParquet = true;
                    _clipboardHouseEnvelopeParquetMaterial = pf.parquetMaterial;
                    _clipboardHouseEnvelopeParquetUv = pf.uvMetersPerTile;
                    _clipboardHouseEnvelopeParquetY = pf.yOffsetAboveBase;
                    break;
                }
            }
        }

        ClipboardHouseSourceSnapshot first = _clipboardHouseSourceSnapshots[0];
        _clipboardPath = new List<Vector3>(first.pathWorld.Count);
        for (int i = 0; i < first.pathWorld.Count; i++)
            _clipboardPath.Add(first.pathWorld[i]);
        _clipboardKind = first.kind;
        _clipboardHeight = _clipboardHouseEnvelopeHeight;
        _clipboardThickness = envelopeWall.thickness;
        _clipboardAllowVerticalScrollElevation = false;
        _clipboardInteriorLotForConstraint = null;
        _clipboardPasteClosedFreeAsMergedLotOutline = false;

        _clipboardIsMultiSourceHouseBundle = true;
        _clipboardHasData = true;

        if (logDebug)
            Debug.Log($"[WallBuildController] Copied multi-source house ({_clipboardHouseSourceSnapshots.Count} source lots).");

        return true;
    }

    Material ResolveDefaultParquetMaterialForHousePaste()
    {
        if (defaultHouseParquetMaterialForPaste != null)
            return defaultHouseParquetMaterialForPaste;

        LotBuildMenuUI lotUi = FindFirstObjectByType<LotBuildMenuUI>(FindObjectsInactive.Include);
        if (lotUi != null && lotUi.defaultParquetMaterial != null)
            return lotUi.defaultParquetMaterial;

        // Dernier recours : réutiliser le matériau d’un lot maison déjà en scène (copie sans métadonnées valides, menu absent, etc.).
        HouseParquetFloor[] floors = FindObjectsByType<HouseParquetFloor>(FindObjectsSortMode.None);
        for (int i = 0; i < floors.Length; i++)
        {
            HouseParquetFloor pf = floors[i];
            if (pf != null && pf.parquetMaterial != null)
                return pf.parquetMaterial;
        }

        return null;
    }

    void TryCopySelectedWallToClipboard()
    {
        if (!TryResolveWallForClipboard(out WallObject wall, out WallEditShape edit))
            return;

        if (SelectedWall != wall)
            ForceSelectWall(wall);

        HouseEnvelopeBundledSourceTag bundledTag = wall.GetComponent<HouseEnvelopeBundledSourceTag>();
        if (bundledTag != null && bundledTag.envelopeWall != null)
        {
            HouseExteriorEnvelopeSources envFromBundled = bundledTag.envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
            if (envFromBundled != null &&
                envFromBundled.HasMultipleSourceLots &&
                TryCopyMultiSourceHouseToClipboard(bundledTag.envelopeWall, envFromBundled))
                return;
        }

        HouseExteriorEnvelopeSources directEnv = wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (directEnv != null && directEnv.HasMultipleSourceLots && TryCopyMultiSourceHouseToClipboard(wall, directEnv))
            return;

        _clipboardIsMultiSourceHouseBundle = false;
        _clipboardHouseSourceSnapshots.Clear();

        List<Vector3> path = edit.GetClipboardDuplicatePathWorld();
        if (path == null || path.Count < 2)
            return;

        _clipboardPath = new List<Vector3>(path.Count);
        for (int i = 0; i < path.Count; i++)
            _clipboardPath.Add(path[i]);

        _clipboardKind = edit.GetClipboardDetectedKind();
        _clipboardHeight = wall.height;
        _clipboardThickness = wall.thickness;
        _clipboardAllowVerticalScrollElevation = edit.allowVerticalScrollElevation;
        _clipboardInteriorLotForConstraint = edit.interiorWallsStayInsideLot;
        _clipboardPasteClosedFreeAsMergedLotOutline = edit.ShouldPasteClosedFreeAsMergedLotOutline;
        _clipboardHasData = true;

        if (logDebug)
            Debug.Log($"[WallBuildController] Copied wall path ({_clipboardPath.Count} pts), kind={_clipboardKind}.");
    }

    void TryPasteMultiSourceHouseFromClipboard()
    {
        if (wallPrefab == null || _clipboardHouseSourceSnapshots == null || _clipboardHouseSourceSnapshots.Count < 2)
            return;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Duplicate multi-source house");

        float ox = pasteOffsetXZ.x;
        float oz = pasteOffsetXZ.y;

        // Même critère que la fusion : <see cref="WallCountsAsDesignatedHouse"/> (parquetMaterial sur le lot).
        Material parquetMat = _clipboardHouseEnvelopeParquetMaterial;
        bool parquetFromClipboard = _clipboardHouseEnvelopeHadParquet && parquetMat != null;
        if (parquetMat == null)
            parquetMat = ResolveDefaultParquetMaterialForHousePaste();
        float parquetUv = parquetFromClipboard ? _clipboardHouseEnvelopeParquetUv : 0.45f;
        float parquetY = parquetFromClipboard ? _clipboardHouseEnvelopeParquetY : 0.003f;

        var sourceWalls = new List<WallObject>(_clipboardHouseSourceSnapshots.Count);

        for (int s = 0; s < _clipboardHouseSourceSnapshots.Count; s++)
        {
            ClipboardHouseSourceSnapshot snap = _clipboardHouseSourceSnapshots[s];
            if (snap.pathWorld == null || snap.pathWorld.Count < 2)
                continue;

            List<Vector3> offsetPath = new List<Vector3>(snap.pathWorld.Count);
            for (int i = 0; i < snap.pathWorld.Count; i++)
            {
                Vector3 p = snap.pathWorld[i];
                offsetPath.Add(new Vector3(p.x + ox, p.y, p.z + oz));
            }

            WallObject srcWall = CreateWallFromShapePathInternal(offsetPath, snap.kind, registerAndSelect: false);
            if (srcWall == null)
                continue;

            srcWall.SetHeight(snap.height);
            srcWall.thickness = snap.thickness;
            WallEditShape srcEdit = srcWall.GetComponent<WallEditShape>();
            if (srcEdit != null)
                srcEdit.ApplyToWall();

            if (parquetMat != null && srcEdit != null && srcEdit.IsClosedLoopPath)
            {
                HouseParquetFloor srcFloor = srcWall.GetComponent<HouseParquetFloor>();
                if (srcFloor == null)
                    srcFloor = srcWall.gameObject.AddComponent<HouseParquetFloor>();
                srcFloor.parquetMaterial = parquetMat;
                srcFloor.uvMetersPerTile = parquetUv;
                srcFloor.yOffsetAboveBase = parquetY;
                srcFloor.storeyHeightMeters = addFloorHeightMeters;
                ApplyHouseParquetForDesignatedClosedLot(srcFloor, srcWall, srcEdit);
            }

            RegisterExistingWall(srcWall);
            sourceWalls.Add(srcWall);
        }

        if (sourceWalls.Count < 2)
        {
            if (logDebug)
                Debug.LogWarning("[WallBuildController] Multi-source paste: not enough valid source walls spawned.");
            return;
        }

        WallObject envelope = Instantiate(wallPrefab);
        envelope.transform.position = Vector3.zero;
        envelope.SetHeight(_clipboardHouseEnvelopeHeight);
        envelope.thickness = _clipboardThickness;

        WallEditShape envEdit = envelope.GetComponent<WallEditShape>();
        if (envEdit == null)
            envEdit = envelope.gameObject.AddComponent<WallEditShape>();
        envEdit.wall = envelope;

        HouseExteriorEnvelopeSources envMeta = envelope.GetComponent<HouseExteriorEnvelopeSources>();
        if (envMeta == null)
            envMeta = envelope.gameObject.AddComponent<HouseExteriorEnvelopeSources>();
        envMeta.RestoreUndoState(_clipboardHouseIndependentHandles, sourceWalls);

        WallSelectable envSelectable = envelope.GetComponent<WallSelectable>();
        if (envSelectable == null)
            envSelectable = envelope.gameObject.AddComponent<WallSelectable>();
        envSelectable.providerBehaviour = envEdit;

        if (defaultWallStyle != null)
            WallStyleApplier.Apply(envelope, defaultWallStyle);

        RegisterExistingWall(envelope);

        TryRebuildHouseOuterEnvelopeFromSources(
            envelope,
            snapMergedOutlineToGrid: true,
            refreshControlPointOverlay: false,
            recordUndoSnapshotWhenAutoSplit: false,
            immediateFullCladdingRefresh: true,
            preferSelectSourceWallAfterSplit: null);

        envEdit = envelope.GetComponent<WallEditShape>();
        envMeta = envelope.GetComponent<HouseExteriorEnvelopeSources>();

        if (envMeta != null)
        {
            for (int i = 0; i < sourceWalls.Count; i++)
            {
                WallObject sw = sourceWalls[i];
                if (sw == null || sw == envelope)
                    continue;

                if (envMeta.UseIndependentSourceHandlesForHouseEnvelope)
                {
                    HouseEnvelopeBundledSourceTag tag = sw.GetComponent<HouseEnvelopeBundledSourceTag>();
                    if (tag == null)
                        tag = sw.gameObject.AddComponent<HouseEnvelopeBundledSourceTag>();
                    tag.envelopeWall = envelope;
                    HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(sw, true);
                }
                else
                    sw.gameObject.SetActive(false);
            }
        }

        if (parquetMat != null && envEdit != null)
        {
            HouseParquetFloor floor = envelope.GetComponent<HouseParquetFloor>();
            if (floor == null)
                floor = envelope.gameObject.AddComponent<HouseParquetFloor>();
            floor.parquetMaterial = parquetMat;
            floor.uvMetersPerTile = parquetUv;
            floor.yOffsetAboveBase = parquetY;
            floor.storeyHeightMeters = addFloorHeightMeters;
            ApplyHouseParquetForDesignatedClosedLot(floor, envelope, envEdit);
        }

        EnsureWallStoneCladdingEnabled(envelope);
        StartCoroutine(CoRefreshCladdingAfterLotMerge(envelope));

        if (envEdit != null)
            ControlPointHandleUI.ApplyEditingSelectionAfterHouseEnvelopeMerge(envEdit);

        if (overlay != null)
            overlay.RebuildOverlay();

        MergedLotShapePivotHandleUI.RefreshAllPivotVisualStates();

        ForceSelectWall(envelope);

        if (logDebug)
            Debug.Log("[WallBuildController] Pasted multi-source house.");

        // Même frame : enveloppe / pierre peuvent laisser le path ou le mesh un tick derrière ; réapplique le parquet au frame suivant.
        StartCoroutine(CoDeferredHouseParquetAfterMultiSourcePaste(envelope, sourceWalls, parquetMat, parquetUv, parquetY));
    }

    IEnumerator CoDeferredHouseParquetAfterMultiSourcePaste(
        WallObject envelope,
        List<WallObject> sourceWalls,
        Material parquetMat,
        float parquetUv,
        float parquetY)
    {
        yield return null;

        Material mat = parquetMat;
        if (mat == null)
            mat = ResolveDefaultParquetMaterialForHousePaste();
        if (mat == null)
            yield break;

        void ApplyOne(WallObject w)
        {
            if (w == null)
                return;
            WallEditShape ed = w.GetComponent<WallEditShape>();
            if (ed == null || !ed.IsClosedLoopPath)
                return;
            HouseParquetFloor f = w.GetComponent<HouseParquetFloor>();
            if (f == null)
                f = w.gameObject.AddComponent<HouseParquetFloor>();
            f.parquetMaterial = mat;
            f.uvMetersPerTile = parquetUv;
            f.yOffsetAboveBase = parquetY;
            f.storeyHeightMeters = addFloorHeightMeters;
            ApplyHouseParquetForDesignatedClosedLot(f, w, ed);
        }

        ApplyOne(envelope);
        if (sourceWalls != null)
        {
            for (int i = 0; i < sourceWalls.Count; i++)
                ApplyOne(sourceWalls[i]);
        }

        MergedLotShapePivotHandleUI.RefreshAllPivotVisualStates();
    }

    /// <summary>
    /// Fusion par chevauchement / enveloppe : même frame, <see cref="ApplyHouseParquetForDesignatedClosedLot"/> peut échouer
    /// si le contour ou le mesh n’est pas encore aligné — réapplique au frame suivant (comme le collage multi-sources).
    /// </summary>
    IEnumerator CoDeferredParquetAfterOverlapMerge(
        WallObject wall,
        Material parquetMat,
        float parquetUv,
        float parquetY)
    {
        // Un seul WaitForEndOfFrame + un frame : mesh / preview parfois encore en file après InitFromMergedLotOutline.
        yield return null;
        yield return new WaitForEndOfFrame();

        if (wall == null)
            yield break;

        WallEditShape ed = wall.GetComponent<WallEditShape>();
        if (ed == null || !ed.IsClosedLoopPath)
        {
            MergedLotShapePivotHandleUI.RefreshAllPivotVisualStates();
            yield break;
        }

        ed.ApplyToWall();

        Material mat = parquetMat;
        if (mat == null)
            mat = ResolveDefaultParquetMaterialForHousePaste();
        if (mat == null)
        {
            HouseParquetFloor epf = wall.GetComponent<HouseParquetFloor>();
            if (epf != null && epf.parquetMaterial != null)
                mat = epf.parquetMaterial;
        }

        HouseParquetFloor f = wall.GetComponent<HouseParquetFloor>();
        if (f == null)
            f = wall.gameObject.AddComponent<HouseParquetFloor>();
        if (mat != null)
            f.parquetMaterial = mat;
        f.uvMetersPerTile = parquetUv;
        f.yOffsetAboveBase = parquetY;
        f.storeyHeightMeters = addFloorHeightMeters;
        ApplyHouseParquetForDesignatedClosedLot(f, wall, ed);
        MergedLotShapePivotHandleUI.RefreshAllPivotVisualStates();
    }

    void TryPasteWallFromClipboard()
    {
        if (!_clipboardHasData || wallPrefab == null)
            return;

        if (_clipboardIsMultiSourceHouseBundle && _clipboardHouseSourceSnapshots != null && _clipboardHouseSourceSnapshots.Count >= 2)
        {
            TryPasteMultiSourceHouseFromClipboard();
            return;
        }

        if (_clipboardPath == null || _clipboardPath.Count < 2)
            return;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Duplicate Wall");

        List<Vector3> pasted = new List<Vector3>(_clipboardPath.Count);
        float ox = pasteOffsetXZ.x;
        float oz = pasteOffsetXZ.y;
        for (int i = 0; i < _clipboardPath.Count; i++)
        {
            Vector3 p = _clipboardPath[i];
            pasted.Add(new Vector3(p.x + ox, p.y, p.z + oz));
        }

        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.height = _clipboardHeight;
        wall.thickness = _clipboardThickness;

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape == null)
            editShape = wall.gameObject.AddComponent<WallEditShape>();

        editShape.wall = wall;
        editShape.interiorWallsStayInsideLot = _clipboardInteriorLotForConstraint;
        editShape.allowVerticalScrollElevation = _clipboardAllowVerticalScrollElevation;

        if (editShape.interiorWallsStayInsideLot != null && pasted.Count >= 2)
        {
            _lotFootprintRingScratch.Clear();
            if (editShape.interiorWallsStayInsideLot.TryGetClosedLotFootprintRingXZ(_lotFootprintRingScratch) &&
                _lotFootprintRingScratch.Count >= 3)
            {
                if (pasted.Count == 2)
                {
                    float tl = editShape.interiorWallsStayInsideLot.wall != null
                        ? editShape.interiorWallsStayInsideLot.wall.thickness
                        : 0.25f;
                    float ti = editShape.wall != null ? editShape.wall.thickness : 0.25f;
                    float pasteInset = WallEditShape.ClampInsetToFeasibleRingXZ(
                        _lotFootprintRingScratch,
                        WallEditShape.ComputeOpenInteriorWallFootprintInsetMeters(tl, ti));

                    if (!WallEditShape.TryClipOpenWorldSegmentToLotRingXZ(
                            pasted[0], pasted[1], _lotFootprintRingScratch, out Vector3 ca, out Vector3 cb, pasteInset))
                    {
                        if (editShape.interiorWallsStayInsideLot.TryGetHouseLotSpawnCenterWorld(out Vector3 lotCenter) &&
                            TryBuildOpenSegmentAcrossReferenceLot(editShape.interiorWallsStayInsideLot, lotCenter, out ca, out cb))
                        {
                            pasted[0] = ca;
                            pasted[1] = cb;
                        }
                    }
                    else
                    {
                        pasted[0] = ca;
                        pasted[1] = cb;
                    }
                }
            }
        }

        bool mergedOutlinePaste = _clipboardPasteClosedFreeAsMergedLotOutline && pasted.Count >= 4;
        if (mergedOutlinePaste)
        {
            bool snapOutline = drawInput != null && drawInput.enableGridSnap && drawInput.snapToHierarchicalVisualGrid;
            editShape.InitFromMergedLotOutline(pasted, drawInput, snapOutline);
        }
        else
        {
            editShape.InitFromDetectedPath(pasted, _clipboardKind);

            if (wall.Points == null || wall.Points.Count < 2)
                editShape.InitFromPath(pasted);

            if (drawInput != null)
            {
                var pathForMainGrid = new List<Vector3>(wall.Points);
                bool loopClosed = wall.closedLoop;
                drawInput.SnapCommittedPathToMainGridInPlace(pathForMainGrid, loopClosed);
                wall.SetPath(pathForMainGrid);
                editShape.InitFromDetectedPath(pathForMainGrid, _clipboardKind);
                if (wall.Points == null || wall.Points.Count < 2)
                    editShape.InitFromPath(pathForMainGrid);
            }
        }

        if (editShape.allowVerticalScrollElevation)
            editShape.ClampInteriorWallToLotFootprintIfConfigured();

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable == null)
            selectable = wall.gameObject.AddComponent<WallSelectable>();

        selectable.providerBehaviour = editShape;

        if (defaultWallStyle != null)
            WallStyleApplier.Apply(wall, defaultWallStyle);

        EnsureWallStoneCladdingEnabled(wall);
        RequestDeferredCladdingRefresh(wall);
        RegisterExistingWall(wall);
        ForceSelectWall(wall);

        if (logDebug)
            Debug.Log($"[WallBuildController] Pasted duplicate wall, kind={_clipboardKind}, mergedOutline={mergedOutlinePaste}.");
    }

    void HandleShapeCommittedDetailed(List<Vector3> points, WallDrawInput.DetectedShapeKind detectedKind, string detectedName)
    {
        if (wallPrefab == null)
            return;

        if (points == null || points.Count < 2)
            return;

        // Toujours tenter : TryMergeCommittedShapeIntoHouse ne fait rien s'il n'y a pas de voisin en contact.
        // Ne pas dépendre de la valeur sérialisée du champ ci-dessus, qui peut rester false sur des scènes existantes.
        if (TryMergeCommittedShapeIntoHouse(points, detectedKind, null, requireDesignatedHouseLot: true))
            return;

        CommitNewWallFromShapePath(points, detectedKind, detectedName);
    }

    WallObject CreateWallFromShapePathInternal(List<Vector3> points, WallDrawInput.DetectedShapeKind detectedKind, bool registerAndSelect)
    {
        if (wallPrefab == null || points == null || points.Count < 2)
            return null;

        WallObject wall = Instantiate(wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.SetPath(points);

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape == null)
            editShape = wall.gameObject.AddComponent<WallEditShape>();

        editShape.wall = wall;
        editShape.InitFromDetectedPath(points, detectedKind);

        if (drawInput != null)
        {
            var pathForMainGrid = new List<Vector3>(wall.Points);
            bool loopClosed = wall.closedLoop;
            drawInput.SnapCommittedPathToMainGridInPlace(pathForMainGrid, loopClosed);
            wall.SetPath(pathForMainGrid);
            editShape.InitFromDetectedPath(pathForMainGrid, detectedKind);
        }

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable == null)
            selectable = wall.gameObject.AddComponent<WallSelectable>();
        selectable.providerBehaviour = editShape;

        if (defaultWallStyle != null)
            WallStyleApplier.Apply(wall, defaultWallStyle);

        EnsureWallStoneCladdingEnabled(wall);
        // Même hors RunWithCladdingRebuildSuspended : budget global / intervalle min peuvent repousser
        // le 1er ForceRebuild en LateUpdate — une 2e passe frame suivante stabilise pierres + teintes.
        RequestDeferredCladdingRefresh(wall);

        if (registerAndSelect)
        {
            RegisterExistingWall(wall);
            ForceSelectWall(wall);
        }

        return wall;
    }

    void CommitNewWallFromShapePath(List<Vector3> points, WallDrawInput.DetectedShapeKind detectedKind, string detectedName)
    {
        if (wallPrefab == null || points == null || points.Count < 2)
            return;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Create Wall");

        WallObject wall = CreateWallFromShapePathInternal(points, detectedKind, registerAndSelect: true);
        if (wall == null)
            return;

        if (logDebug)
            Debug.Log($"[WallBuildController] Spawned wall '{wall.name}' from detected shape '{detectedName}'.");
    }

    Vector3 ResolveUiPresetSpawnCenterWorld()
    {
        if (drawInput == null)
            return Vector3.zero;

        if (!drawInput.TryGetWorldPointFromViewport(0.5f, 0.5f, out Vector3 center))
            center = Vector3.zero;

        if (drawInput.enableGridSnap)
            center = drawInput.SnapWorldPointForEditing(center);

        return center;
    }

    /// <summary>
    /// Fusion menu lot / pivot / pan : <see cref="TryMergeCommittedShapeIntoHouse"/> avec référence lot / enveloppe.
    /// Sans référence : fusion au relâchement lorsque le BFS détecte un contact avec un lot « maison »
    /// (même sans mur déplacé).
    /// </summary>
    bool TryPresetShapeMergeIntoHouse(
        List<Vector3> path,
        WallDrawInput.DetectedShapeKind kind,
        WallObject referenceLotOrNull)
    {
        if (path == null)
            return false;

        if (referenceLotOrNull != null)
        {
            if (TryMergeCommittedShapeIntoHouse(path, kind, referenceLotOrNull, requireDesignatedHouseLot: true))
                return true;
        }

        if (TryMergeCommittedShapeIntoHouse(path, kind, null, requireDesignatedHouseLot: true))
            return true;
        return false;
    }

    /// <summary>
    /// Pour qu’un preset soit traité comme « deux lots qui se touchent » : instancier le mur, copier parquet / hauteur
    /// depuis le lot de référence, puis <see cref="TryMergeWallWithAdjacentLots"/> (contour union, enveloppe, sol).
    /// Retourne faux seulement si <see cref="CreateWallFromShapePathInternal"/> échoue.
    /// </summary>
    bool TrySpawnClosedPresetWallAtReferenceLotAndMerge(
        List<Vector3> path,
        WallDrawInput.DetectedShapeKind kind,
        WallObject referenceLot,
        string undoSnapshotLabel)
    {
        if (path == null || path.Count < 3 || referenceLot == null || wallPrefab == null)
            return false;

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot(undoSnapshotLabel);

        WallObject newWall = CreateWallFromShapePathInternal(path, kind, registerAndSelect: false);
        if (newWall == null)
            return false;

        CopyDesignatedHouseAppearanceFromReference(referenceLot, newWall);
        RegisterExistingWall(newWall);

        if (TryMergeWallWithAdjacentLots(newWall))
            return true;

        ForceSelectWall(newWall);
        return true;
    }

    void CopyDesignatedHouseAppearanceFromReference(WallObject reference, WallObject target)
    {
        if (reference == null || target == null)
            return;

        target.SetHeight(reference.height);
        target.thickness = reference.thickness;

        HouseParquetFloor srcPf = reference.GetComponent<HouseParquetFloor>();
        if (srcPf == null || !srcPf.IsDesignatedHouseLot)
            return;

        HouseParquetFloor dstPf = target.GetComponent<HouseParquetFloor>();
        if (dstPf == null)
            dstPf = target.gameObject.AddComponent<HouseParquetFloor>();
        dstPf.parquetMaterial = srcPf.parquetMaterial;
        dstPf.uvMetersPerTile = srcPf.uvMetersPerTile;
        dstPf.yOffsetAboveBase = srcPf.yOffsetAboveBase;
        dstPf.storeyHeightMeters = Mathf.Approximately(0f, srcPf.storeyHeightMeters)
            ? addFloorHeightMeters
            : srcPf.storeyHeightMeters;

        WallEditShape ed = target.GetComponent<WallEditShape>();
        if (ed != null)
            ApplyHouseParquetForDesignatedClosedLot(dstPf, target, ed);
    }

    /// <summary>Pan « Wall draw » : crée un cercle au centre de l’écran (projection sol).</summary>
    public void SpawnUiPresetCircle(float radiusMeters = 2f) =>
        SpawnUiPresetCircleAtWorldCenter(ResolveUiPresetSpawnCenterWorld(), radiusMeters);

    /// <summary>Cercle au centre XZ d’un lot fermé (menu lot / pivot), même logique de fusion que <see cref="SpawnUiPresetCircle"/>.</summary>
    public void SpawnUiPresetCircleAtReferenceLotCenter(WallObject referenceLot, float radiusMeters = 2f)
    {
        if (referenceLot == null)
            return;
        WallEditShape ed = referenceLot.GetComponent<WallEditShape>();
        if (ed == null || !ed.TryGetHouseLotSpawnCenterWorld(out Vector3 c))
            return;

        if (drawInput == null || wallPrefab == null)
            return;

        List<Vector3> path = drawInput.BuildUiPresetClosedCircle(c, radiusMeters);
        if (path == null || path.Count < 3)
            return;

        if (TrySpawnClosedPresetWallAtReferenceLotAndMerge(path, WallDrawInput.DetectedShapeKind.Circle, referenceLot, "Preset circle at lot"))
            return;

        if (TryPresetShapeMergeIntoHouse(path, WallDrawInput.DetectedShapeKind.Circle, referenceLot))
            return;

        CommitNewWallFromShapePath(path, WallDrawInput.DetectedShapeKind.Circle, "Circle");
    }

    void SpawnUiPresetCircleAtWorldCenter(Vector3 centerWorld, float radiusMeters)
    {
        if (drawInput == null || wallPrefab == null)
            return;

        List<Vector3> path = drawInput.BuildUiPresetClosedCircle(centerWorld, radiusMeters);
        if (path == null || path.Count < 3)
            return;

        if (TryPresetShapeMergeIntoHouse(path, WallDrawInput.DetectedShapeKind.Circle, null))
            return;

        CommitNewWallFromShapePath(path, WallDrawInput.DetectedShapeKind.Circle, "Circle");
    }

    /// <summary>Pan « Wall draw » : crée un carré au centre de l’écran.</summary>
    public void SpawnUiPresetSquare(float sideLengthMeters = 3f)
    {
        if (drawInput == null || wallPrefab == null)
            return;

        Vector3 c = ResolveUiPresetSpawnCenterWorld();
        List<Vector3> path = drawInput.BuildUiPresetClosedSquare(c, sideLengthMeters);
        if (path == null || path.Count < 3)
            return;

        if (TryPresetShapeMergeIntoHouse(path, WallDrawInput.DetectedShapeKind.Square, null))
            return;

        CommitNewWallFromShapePath(path, WallDrawInput.DetectedShapeKind.Square, "Square");
    }

    /// <summary>Pan « Wall draw » : crée un triangle équilatéral au centre de l’écran.</summary>
    public void SpawnUiPresetTriangle(float sideLengthMeters = 3f) =>
        SpawnUiPresetTriangleAtWorldCenter(ResolveUiPresetSpawnCenterWorld(), sideLengthMeters);

    /// <summary>Triangle équilatéral au centre XZ d’un lot fermé (menu lot / pivot).</summary>
    public void SpawnUiPresetTriangleAtReferenceLotCenter(WallObject referenceLot, float sideLengthMeters = 3f)
    {
        if (referenceLot == null)
            return;
        WallEditShape ed = referenceLot.GetComponent<WallEditShape>();
        if (ed == null || !ed.TryGetHouseLotSpawnCenterWorld(out Vector3 c))
            return;

        if (drawInput == null || wallPrefab == null)
            return;

        List<Vector3> path = drawInput.BuildUiPresetClosedTriangle(c, sideLengthMeters);
        if (path == null || path.Count < 3)
            return;

        if (TrySpawnClosedPresetWallAtReferenceLotAndMerge(path, WallDrawInput.DetectedShapeKind.Triangle, referenceLot, "Preset triangle at lot"))
            return;

        if (TryPresetShapeMergeIntoHouse(path, WallDrawInput.DetectedShapeKind.Triangle, referenceLot))
            return;

        CommitNewWallFromShapePath(path, WallDrawInput.DetectedShapeKind.Triangle, "Triangle");
    }

    void SpawnUiPresetTriangleAtWorldCenter(Vector3 centerWorld, float sideLengthMeters)
    {
        if (drawInput == null || wallPrefab == null)
            return;

        List<Vector3> path = drawInput.BuildUiPresetClosedTriangle(centerWorld, sideLengthMeters);
        if (path == null || path.Count < 3)
            return;

        if (TryPresetShapeMergeIntoHouse(path, WallDrawInput.DetectedShapeKind.Triangle, null))
            return;

        CommitNewWallFromShapePath(path, WallDrawInput.DetectedShapeKind.Triangle, "Triangle");
    }

    /// <summary>
    /// Carré centré sur un lot (enveloppe multi-plans, lot source, rectangle, triangle, ellipse…), avec fusion comme les autres presets.
    /// </summary>
    public void SpawnUiPresetSquareAtReferenceLotCenter(WallObject referenceLot, float sideLengthMeters = 3f)
    {
        if (referenceLot == null)
            return;
        WallEditShape ed = referenceLot.GetComponent<WallEditShape>();
        if (ed == null || !ed.TryGetHouseLotSpawnCenterWorld(out Vector3 c))
            return;

        if (drawInput == null || wallPrefab == null)
            return;

        List<Vector3> path = drawInput.BuildUiPresetClosedSquare(c, sideLengthMeters);
        if (path == null || path.Count < 3)
            return;

        if (TrySpawnClosedPresetWallAtReferenceLotAndMerge(path, WallDrawInput.DetectedShapeKind.Square, referenceLot, "Preset square at lot"))
            return;

        if (TryPresetShapeMergeIntoHouse(path, WallDrawInput.DetectedShapeKind.Square, referenceLot))
            return;

        CommitNewWallFromShapePath(path, WallDrawInput.DetectedShapeKind.Square, "Square");
    }

    /// <summary>
    /// Après déplacement : union du périmètre extérieur uniquement si le mur déplacé et tous les voisins
    /// en contact sont des lots désignés « maison » (<see cref="HouseParquetFloor.IsDesignatedHouseLot"/>).
    /// </summary>
    public bool TryMergeWallWithAdjacentLots(WallObject wall)
    {
        if (wall == null)
            return false;

        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit == null || !edit.IsClosedLoopPath)
            return false;

        if (edit.shapeKind != WallEditShape.ShapeKind.Rectangle &&
            edit.shapeKind != WallEditShape.ShapeKind.Free &&
            edit.shapeKind != WallEditShape.ShapeKind.Ellipse &&
            edit.shapeKind != WallEditShape.ShapeKind.Triangle)
            return false;

        WallDrawInput.DetectedShapeKind kind = edit.GetClipboardDetectedKind();
        if (kind == WallDrawInput.DetectedShapeKind.Square)
            kind = WallDrawInput.DetectedShapeKind.Rectangle;

        List<Vector3> path = edit.GetPreviewPathWorld();
        // Après fusion / snap : le contour peut être Free orthogonal alors que c’est encore un cercle visuellement —
        // ne pas perdre le kind Circle (sinon fusion suivante → InitFromDetectedPath(Rectangle) et « carré parfait »).
        if (kind == WallDrawInput.DetectedShapeKind.Free && path != null)
        {
            if (TryFitCircleXZFromClosedPath(path, out _, out _, out _))
                kind = WallDrawInput.DetectedShapeKind.Circle;
            else if (path.Count >= 24 && edit.shapeKind == WallEditShape.ShapeKind.Free &&
                     EstimateClosedPathCircleLikeness(path) > 0.72f)
                kind = WallDrawInput.DetectedShapeKind.Circle;
        }

        return TryMergeCommittedShapeIntoHouse(path, kind, wall, requireDesignatedHouseLot: true);
    }

    /// <summary>
    /// Vrai si une fusion « maison ↔ maison » serait possible au relâchement avec la position actuelle (sans modifier la scène).
    /// Sert au pivot rose pendant le déplacement.
    /// </summary>
    public bool DesignatedHouseMergeContactPossibleNow(WallObject wall)
    {
        if (wall == null)
            return false;

        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit == null || !edit.IsClosedLoopPath)
            return false;

        if (!WallCountsAsDesignatedHouse(wall))
            return false;

        if (edit.shapeKind != WallEditShape.ShapeKind.Rectangle &&
            edit.shapeKind != WallEditShape.ShapeKind.Free &&
            edit.shapeKind != WallEditShape.ShapeKind.Ellipse &&
            edit.shapeKind != WallEditShape.ShapeKind.Triangle)
            return false;

        WallDrawInput.DetectedShapeKind kind = edit.GetClipboardDetectedKind();
        if (kind == WallDrawInput.DetectedShapeKind.Square)
            kind = WallDrawInput.DetectedShapeKind.Rectangle;

        List<Vector3> path = edit.GetPreviewPathWorld();
        if (kind == WallDrawInput.DetectedShapeKind.Free && path != null)
        {
            if (TryFitCircleXZFromClosedPath(path, out _, out _, out _))
                kind = WallDrawInput.DetectedShapeKind.Circle;
            else if (path.Count >= 24 && edit.shapeKind == WallEditShape.ShapeKind.Free &&
                     EstimateClosedPathCircleLikeness(path) > 0.72f)
                kind = WallDrawInput.DetectedShapeKind.Circle;
        }

        return TryMergeCommittedShapeIntoHouse(path, kind, wall, requireDesignatedHouseLot: true, dryRun: true);
    }

    /// <summary>
    /// Recalcule le contour de l'enveloppe extérieure à partir des lots sources (après translation d'un plan rose).
    /// </summary>
    /// <remarks>
    /// Si les empreintes des lots sources ne forment plus qu’<b>un seul morceau connexe</b> (contact par arête / chevauchement),
    /// l’enveloppe est recalculée. Sinon, <see cref="TrySplitHouseEnvelopeIntoSourceLots"/> est appelée : un mur extérieur par lot.
    /// Interactions : <see cref="WallCladdingGenerator"/> peut retarder la régénération des pierres pendant un drag si
    /// l’option « rebuild during handle drag » est désactivée sur le composant enveloppe ; le mur vectoriel suit quand même.
    /// <see cref="WallCladdingGenerator.IsGlobalRebuildSuspended"/> bloque les ForceRebuild (undo, <c>RunWithCladdingRebuildSuspended</c>).
    /// <see cref="EnsureWallStoneCladdingEnabled"/> invalide le hash et force le habillage sauf suspension globale.
    /// Avec <paramref name="immediateFullCladdingRefresh"/> à <c>false</c>, ce forçage et le parquet sont sautés pendant le drag.
    /// </remarks>
    /// <param name="immediateFullCladdingRefresh">
    /// Si faux (drag interactif) : met à jour le contour/mesh du mur sans <see cref="EnsureWallStoneCladdingEnabled"/> ni coroutine
    /// de secours — évite un <c>ForceRebuild</c> complet des pierres à chaque frame. Le habillage suit via <c>MarkDirty</c> (intervalle drag).
    /// </param>
    /// <param name="preferSelectSourceWallAfterSplit">Lot source à sélectionner après une séparation auto (souvent celui qu’on déplaçait).</param>
    public bool TryRebuildHouseOuterEnvelopeFromSources(
        WallObject envelopeWall,
        bool snapMergedOutlineToGrid,
        bool refreshControlPointOverlay = true,
        bool recordUndoSnapshotWhenAutoSplit = true,
        bool immediateFullCladdingRefresh = true,
        WallObject preferSelectSourceWallAfterSplit = null)
    {
        if (envelopeWall == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null)
            return false;

        IReadOnlyList<GameObject> srcGos = meta.SourceLotObjects;
        if (srcGos == null || srcGos.Count == 0)
            return false;

        float tolMerge = Mathf.Max(mergeContactTolerance, 0.08f);
        var unionRects = new List<WallOrthoMergeUtility.RectXZ>(srcGos.Count * 2);
        var rectsForSplitCheck = new List<WallOrthoMergeUtility.RectXZ>(srcGos.Count * 2);
        var ringsForBooleanUnion = new List<List<Vector3>>(srcGos.Count);
        float yRef = envelopeWall.transform.position.y;
        bool useClipperBooleanRingUnion = false;

        for (int i = 0; i < srcGos.Count; i++)
        {
            GameObject go = srcGos[i];
            if (go == null)
                continue;

            WallObject w = go.GetComponent<WallObject>();
            WallEditShape ed = w != null ? w.GetComponent<WallEditShape>() : null;
            if (ed == null || !ed.IsClosedLoopPath)
                continue;

            List<Vector3> path = ed.GetPreviewPathWorld();
            if (path == null || path.Count < 2)
                continue;

            yRef = path[0].y;

            if (!TryGetLotFootprintForMerge(path, tolMerge, out _, out List<WallOrthoMergeUtility.RectXZ> footprint))
            {
                if (!TryGetAabbOnlyFootprintFromPreviewPath(path, out _, out footprint))
                    continue;
            }

            // Même ordre / même filtre que unionRects : l'union booléenne doit recevoir exactement ces lots.
            ringsForBooleanUnion.Add(new List<Vector3>(path));
            if (ed.shapeKind == WallEditShape.ShapeKind.Triangle || ed.shapeKind == WallEditShape.ShapeKind.Ellipse)
                useClipperBooleanRingUnion = true;

            for (int k = 0; k < footprint.Count; k++)
                unionRects.Add(footprint[k]);

            if (ed.shapeKind == WallEditShape.ShapeKind.Triangle || ed.shapeKind == WallEditShape.ShapeKind.Ellipse)
            {
                if (TryGetAabbOnlyFootprintFromPreviewPath(path, out _, out List<WallOrthoMergeUtility.RectXZ> splitAabb) &&
                    splitAabb != null)
                {
                    for (int k = 0; k < splitAabb.Count; k++)
                        rectsForSplitCheck.Add(splitAabb[k]);
                }
                else
                {
                    for (int k = 0; k < footprint.Count; k++)
                        rectsForSplitCheck.Add(footprint[k]);
                }
            }
            else
            {
                for (int k = 0; k < footprint.Count; k++)
                    rectsForSplitCheck.Add(footprint[k]);
            }
        }

        if (unionRects.Count == 0)
            return false;

        // Plusieurs lots mais empreintes en îlots séparés : on ne casse plus tout le groupe ici.
        // Si c'est le lot en cours de déplacement, on le retire seul de la maison et on garde le reste groupé.
        if (meta.HasMultipleSourceLots && WallOrthoMergeUtility.IsRectUnionDisconnectedFourWay(rectsForSplitCheck) &&
            preferSelectSourceWallAfterSplit != null &&
            SourceLotListContains(meta, preferSelectSourceWallAfterSplit))
        {
            return TryDetachOneSourceFromHouseEnvelope(
                envelopeWall,
                preferSelectSourceWallAfterSplit,
                snapMergedOutlineToGrid,
                refreshControlPointOverlay,
                recordUndoSnapshotWhenAutoSplit,
                immediateFullCladdingRefresh);
        }

        if (meta.HasMultipleSourceLots && WallOrthoMergeUtility.IsRectUnionDisconnectedFourWay(rectsForSplitCheck))
            return TrySplitHouseEnvelopeIntoSourceLots(
                envelopeWall,
                recordUndoSnapshotWhenAutoSplit,
                preferSelectSourceWallAfterSplit);

        RectBounds newAabb = UnionBoundsFromRects(unionRects, yRef);

        List<Vector3> mergedPath = null;
        bool isFilledRectangle = false;
        bool mergedFromClipperRings = false;
        bool builtPoly = false;

        // Triangle : arêtes obliques. Cercle / ellipse : courbe (l’emp. « carré r±r » pour les rects donne
        // un mur enveloppe en coudes H/V + zigzag). Ici on unionne les vrais anneaux d’échantillonnage.
        if (useClipperBooleanRingUnion && ringsForBooleanUnion.Count > 0 &&
            WallPolygonBooleanUnion.TryUnionClosedRingsWorldXZ(
                ringsForBooleanUnion, yRef, out mergedPath, out isFilledRectangle) &&
            mergedPath != null &&
            mergedPath.Count >= 3)
        {
            builtPoly = true;
            mergedFromClipperRings = true;
        }

        if (!builtPoly)
        {
            builtPoly = WallOrthoMergeUtility.TryBuildMergedClosedPathFromRectUnionWorld(
                unionRects,
                newAabb.y,
                out mergedPath,
                out isFilledRectangle);
        }

        if (!builtPoly || mergedPath == null)
        {
            if (TryGetMergeGridParameters(out float cellStep, out Vector2 gridOrigin))
            {
                builtPoly = WallOrthoMergeUtility.TryBuildMergedClosedPath(
                    unionRects,
                    cellStep,
                    gridOrigin,
                    newAabb.y,
                    out mergedPath,
                    out isFilledRectangle);
            }
        }

        // Dernier recours : rectangle englobant (évite enveloppe figée si la fusion polygone échoue encore).
        if (!builtPoly || mergedPath == null)
        {
            mergedPath = WallOrthoMergeUtility.BuildAxisAlignedRectLoopWorld(
                newAabb.minX, newAabb.maxX, newAabb.minZ, newAabb.maxZ, newAabb.y);
            isFilledRectangle = true;
            builtPoly = mergedPath != null && mergedPath.Count >= 4;
        }

        if (!builtPoly || mergedPath == null)
            return false;

        if (drawInput != null && snapMergedOutlineToGrid && isFilledRectangle)
            drawInput.SnapCommittedPathToMainGridInPlace(mergedPath, closed: true);

        if (isFilledRectangle && !mergedFromClipperRings &&
            WallOrthoMergeUtility.TryExpandFilledRectanglePathWithInternalPartitionSpikes(
                mergedPath,
                unionRects,
                newAabb.y,
                out List<Vector3> partitionSpikedPath) &&
            partitionSpikedPath != null &&
            partitionSpikedPath.Count >= 4)
        {
            mergedPath = partitionSpikedPath;
            isFilledRectangle = false;
            if (drawInput != null && snapMergedOutlineToGrid)
                drawInput.SnapCommittedPathToMainGridInPlace(mergedPath, closed: true);
        }

        WallEditShape envelopeEdit = envelopeWall.GetComponent<WallEditShape>();
        if (envelopeEdit == null)
            envelopeEdit = envelopeWall.gameObject.AddComponent<WallEditShape>();
        envelopeEdit.wall = envelopeWall;

        if (isFilledRectangle)
            envelopeEdit.InitFromDetectedPath(mergedPath, WallDrawInput.DetectedShapeKind.Rectangle);
        else
            envelopeEdit.InitFromMergedLotOutline(mergedPath, drawInput, snapMergedOutlineToGrid);

        if (immediateFullCladdingRefresh)
        {
            HouseParquetFloor envelopeFloor = envelopeWall.GetComponent<HouseParquetFloor>();
            if (envelopeFloor != null && envelopeFloor.parquetMaterial != null)
                ApplyHouseParquetForDesignatedClosedLot(envelopeFloor, envelopeWall, envelopeEdit);

            EnsureWallStoneCladdingEnabled(envelopeWall);
            StartCoroutine(CoRefreshCladdingAfterLotMerge(envelopeWall));
        }

        if (refreshControlPointOverlay && overlay != null)
            overlay.RebuildOverlay();

        return true;
    }

    /// <summary>
    /// Détruit le mur enveloppe et réactive les lots sources (fusion enveloppe uniquement).
    /// </summary>
    /// <param name="recordUndoSnapshot">Faux lors d’une séparation déclenchée pendant un drag déjà couvert par un snapshot.</param>
    /// <param name="preferSelectSourceWall">Si non null et présent dans les sources, ce mur est sélectionné (ex. lot qu’on écartait avec la rose).</param>
    public bool TrySplitHouseEnvelopeIntoSourceLots(WallObject envelopeWall, bool recordUndoSnapshot = true, WallObject preferSelectSourceWall = null)
    {
        if (envelopeWall == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || meta.SourceLotObjects == null || meta.SourceLotObjects.Count == 0)
            return false;

        var sources = new List<WallObject>(meta.SourceLotObjects.Count);
        for (int i = 0; i < meta.SourceLotObjects.Count; i++)
        {
            GameObject go = meta.SourceLotObjects[i];
            if (go == null)
                continue;
            WallObject wo = go.GetComponent<WallObject>();
            if (wo != null)
                sources.Add(wo);
        }

        if (sources.Count == 0)
            return false;

        EnvelopePinkDragCapture pinkSplitResume = default;
        bool wantPinkSplitResume = !recordUndoSnapshot && HouseEnvelopeSourceHandleUI.ActiveDragInstance != null;
        if (wantPinkSplitResume)
            pinkSplitResume = HouseEnvelopeSourceHandleUI.ActiveDragInstance.CaptureForSplitResume();

        if (recordUndoSnapshot)
        {
            if (undoManager == null)
                undoManager = FindFirstObjectByType<WallUndoManager>();
            if (undoManager != null)
                undoManager.RecordSnapshot("Split house envelope lots");
        }

        UnregisterWall(envelopeWall);
        if (Application.isPlaying)
            Destroy(envelopeWall.gameObject);
        else
            DestroyImmediate(envelopeWall.gameObject);

        for (int i = 0; i < sources.Count; i++)
        {
            WallObject wo = sources[i];
            if (wo == null)
                continue;

            HouseEnvelopeBundledSourceTag bundledTag = wo.GetComponent<HouseEnvelopeBundledSourceTag>();
            if (bundledTag != null)
            {
                if (Application.isPlaying)
                    Destroy(bundledTag);
                else
                    DestroyImmediate(bundledTag);
            }

            HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(wo, false);

            wo.gameObject.SetActive(true);

            WallEditShape ed = wo.GetComponent<WallEditShape>();
            if (ed != null)
                ed.ApplyToWall();

            RegisterExistingWall(wo);

            HouseParquetFloor pf = wo.GetComponent<HouseParquetFloor>();
            if (pf != null && ed != null && ed.IsClosedLoopPath && pf.parquetMaterial != null)
                ApplyHouseParquetForDesignatedClosedLot(pf, wo, ed);

            EnsureWallStoneCladdingEnabled(wo);
            StartCoroutine(CoRefreshCladdingAfterLotMerge(wo));
        }

        WallObject pick = null;
        if (preferSelectSourceWall != null)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i] == preferSelectSourceWall)
                {
                    pick = sources[i];
                    break;
                }
            }
        }

        if (pick == null)
            pick = sources[0];

        WallEditShape pickedEdit = pick != null ? pick.GetComponent<WallEditShape>() : null;

        ControlPointHandleUI.ClearStaleWallSelectionState();
        ControlPointHandleUI.ForceCancelActivePointerDrag();

        if (pickedEdit != null)
            ControlPointHandleUI.ApplyEditingSelectionAfterHouseEnvelopeMerge(pickedEdit);

        HouseEnvelopeSourceHandleUI.ClearPinkHighlightTracking();

        WallSelectable ws = pick != null ? pick.GetComponent<WallSelectable>() : null;
        if (ws != null)
            ws.AutoFindProvider();

        ForceSelectWall(pick);

        if (Application.isPlaying && wantPinkSplitResume && pinkSplitResume.IsValid && pick != null)
            StartCoroutine(CoResumeMergedPivotBulkDragAfterSplitDeferred(pick, pinkSplitResume));

        return true;
    }

    static bool SourceLotListContains(HouseExteriorEnvelopeSources meta, WallObject wall)
    {
        if (meta == null || wall == null)
            return false;
        IReadOnlyList<GameObject> list = meta.SourceLotObjects;
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            GameObject go = list[i];
            if (go == null)
                continue;
            if (go == wall.gameObject)
                return true;
        }
        return false;
    }

    static void UnbundleHouseSourceLotForStandalone(WallObject sourceWall)
    {
        if (sourceWall == null)
            return;
        HouseEnvelopeBundledSourceTag bundledTag = sourceWall.GetComponent<HouseEnvelopeBundledSourceTag>();
        if (bundledTag != null)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(bundledTag);
            else
                UnityEngine.Object.DestroyImmediate(bundledTag);
        }
        HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(sourceWall, false);
        sourceWall.gameObject.SetActive(true);
    }

    /// <summary>
    /// Un seul lot source s'est écarté : le retirer du groupe, le reste reste une maison enveloppée.
    /// </summary>
    bool TryDetachOneSourceFromHouseEnvelope(
        WallObject envelopeWall,
        WallObject detachedWall,
        bool snapMergedOutlineToGrid,
        bool refreshControlPointOverlay,
        bool recordUndoSnapshot,
        bool immediateFullCladdingRefresh)
    {
        if (envelopeWall == null || detachedWall == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || meta.SourceLotObjects == null)
            return false;

        EnvelopePinkDragCapture pinkSplitResume = default;
        bool wantPinkSplitResume = !recordUndoSnapshot && HouseEnvelopeSourceHandleUI.ActiveDragInstance != null;
        if (wantPinkSplitResume)
            pinkSplitResume = HouseEnvelopeSourceHandleUI.ActiveDragInstance.CaptureForSplitResume();

        var remaining = new List<WallObject>(meta.SourceLotObjects.Count);
        for (int i = 0; i < meta.SourceLotObjects.Count; i++)
        {
            GameObject go = meta.SourceLotObjects[i];
            if (go == null)
                continue;
            WallObject wo = go.GetComponent<WallObject>();
            if (wo == null || wo == detachedWall)
                continue;
            remaining.Add(wo);
        }

        if (recordUndoSnapshot)
        {
            if (undoManager == null)
                undoManager = FindFirstObjectByType<WallUndoManager>();
            if (undoManager != null)
                undoManager.RecordSnapshot("Detach one house source from envelope");
        }

        UnbundleHouseSourceLotForStandalone(detachedWall);
        WallEditShape detachedEdit = detachedWall.GetComponent<WallEditShape>();
        if (detachedEdit != null)
            detachedEdit.ApplyToWall();
        RegisterExistingWall(detachedWall);
        HouseParquetFloor dpf = detachedWall.GetComponent<HouseParquetFloor>();
        if (dpf != null && detachedEdit != null && detachedEdit.IsClosedLoopPath && dpf.parquetMaterial != null)
            ApplyHouseParquetForDesignatedClosedLot(dpf, detachedWall, detachedEdit);
        EnsureWallStoneCladdingEnabled(detachedWall);
        StartCoroutine(CoRefreshCladdingAfterLotMerge(detachedWall));

        if (remaining.Count == 0)
        {
            UnregisterWall(envelopeWall);
            if (Application.isPlaying)
                Destroy(envelopeWall.gameObject);
            else
                DestroyImmediate(envelopeWall.gameObject);
            PostDetachReselectEnvelopeAndDetached(
                null,
                detachedWall,
                detachedEdit,
                refreshControlPointOverlay,
                wantPinkSplitResume,
                pinkSplitResume);
            return true;
        }

        if (remaining.Count == 1)
        {
            WallObject only = remaining[0];
            UnbundleHouseSourceLotForStandalone(only);
            WallEditShape onlyEd = only.GetComponent<WallEditShape>();
            if (onlyEd != null)
                onlyEd.ApplyToWall();
            UnregisterWall(envelopeWall);
            if (Application.isPlaying)
                Destroy(envelopeWall.gameObject);
            else
                DestroyImmediate(envelopeWall.gameObject);
            RegisterExistingWall(only);
            HouseParquetFloor opf = only.GetComponent<HouseParquetFloor>();
            if (opf != null && onlyEd != null && onlyEd.IsClosedLoopPath && opf.parquetMaterial != null)
                ApplyHouseParquetForDesignatedClosedLot(opf, only, onlyEd);
            EnsureWallStoneCladdingEnabled(only);
            StartCoroutine(CoRefreshCladdingAfterLotMerge(only));
            PostDetachReselectEnvelopeAndDetached(
                null,
                detachedWall,
                detachedEdit,
                refreshControlPointOverlay,
                wantPinkSplitResume,
                pinkSplitResume);
            return true;
        }

        meta.SetSources(remaining);
        for (int r = 0; r < remaining.Count; r++)
        {
            WallObject rw = remaining[r];
            if (rw == null)
                continue;
            if (meta.UseIndependentSourceHandlesForHouseEnvelope)
            {
                HouseEnvelopeBundledSourceTag tag = rw.GetComponent<HouseEnvelopeBundledSourceTag>();
                if (tag == null)
                    tag = rw.gameObject.AddComponent<HouseEnvelopeBundledSourceTag>();
                tag.envelopeWall = envelopeWall;
                HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(rw, true);
            }
            else
                rw.gameObject.SetActive(false);
        }

        bool rebuilt = TryRebuildHouseOuterEnvelopeFromSources(
            envelopeWall,
            snapMergedOutlineToGrid,
            refreshControlPointOverlay,
            recordUndoSnapshotWhenAutoSplit: false,
            immediateFullCladdingRefresh,
            preferSelectSourceWallAfterSplit: null);
        PostDetachReselectEnvelopeAndDetached(
            envelopeWall,
            detachedWall,
            detachedEdit,
            refreshControlPointOverlay,
            wantPinkSplitResume,
            pinkSplitResume);
        return rebuilt;
    }

    void PostDetachReselectEnvelopeAndDetached(
        WallObject envelopeWall,
        WallObject detachedWall,
        WallEditShape detachedEdit,
        bool refreshControlPointOverlay,
        bool wantPinkSplitResume,
        EnvelopePinkDragCapture pinkSplitResume)
    {
        if (wantPinkSplitResume && pinkSplitResume.IsValid && detachedWall != null && Application.isPlaying)
        {
            ControlPointHandleUI.ClearStaleWallSelectionState();
            ControlPointHandleUI.ForceCancelActivePointerDrag();

            if (detachedEdit != null)
                ControlPointHandleUI.ApplyEditingSelectionAfterHouseEnvelopeMerge(detachedEdit);

            HouseEnvelopeSourceHandleUI.ClearPinkHighlightTracking();

            WallSelectable wsE = envelopeWall != null ? envelopeWall.GetComponent<WallSelectable>() : null;
            if (wsE != null)
                wsE.AutoFindProvider();
            WallSelectable wsD = detachedWall.GetComponent<WallSelectable>();
            if (wsD != null)
                wsD.AutoFindProvider();

            ForceSelectWall(detachedWall);

            if (refreshControlPointOverlay && overlay != null)
                overlay.RebuildOverlay();

            StartCoroutine(CoResumeMergedPivotBulkDragAfterSplitDeferred(detachedWall, pinkSplitResume));
            return;
        }

        HouseEnvelopeSourceHandleUI.ClearPinkHighlightTracking();
        WallSelectable wsE2 = envelopeWall != null ? envelopeWall.GetComponent<WallSelectable>() : null;
        if (wsE2 != null)
            wsE2.AutoFindProvider();
        WallSelectable wsD2 = detachedWall != null ? detachedWall.GetComponent<WallSelectable>() : null;
        if (wsD2 != null)
            wsD2.AutoFindProvider();
        ForceSelectWall(detachedWall);
        if (refreshControlPointOverlay && overlay != null)
            overlay.RebuildOverlay();
    }

    IEnumerator CoResumeMergedPivotBulkDragAfterSplitDeferred(WallObject wall, EnvelopePinkDragCapture capture)
    {
        yield return null;
        MergedLotShapePivotHandleUI.ApplySplitResumeToWallIfPresent(wall, capture);
    }

    /// <summary>
    /// Sommets des contours + intersections d'arêtes entre murs (jonctions en T, croix), pour aimantation au dessin.
    /// Les seuls sommets ne suffisent pas : le contact d'un coin sur le milieu d'un autre mur n'est pas un sommet du grand polygone.
    /// </summary>
    public void GatherExistingWallCornerWorldPoints(List<Vector3> outPoints, float dedupeEps = 0.02f)
    {
        if (outPoints == null)
            return;

        float dedupeSq = dedupeEps * dedupeEps;
        CleanupNullWalls();

        _wallGatherScratch.Clear();
        for (int i = 0; i < _walls.Count; i++)
        {
            if (_walls[i] != null)
                _wallGatherScratch.Add(_walls[i]);
        }

        if (_wallGatherScratch.Count == 0)
        {
            WallObject[] all = FindObjectsByType<WallObject>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    _wallGatherScratch.Add(all[i]);
            }
        }

        for (int i = 0; i < _wallGatherScratch.Count; i++)
            AppendWallOutlineVerticesDeduped(_wallGatherScratch[i], outPoints, dedupeSq);

        AppendOrthogonalWallPairEdgeIntersections(_wallGatherScratch, outPoints, dedupeSq);
    }

    void AppendOrthogonalWallPairEdgeIntersections(
        List<WallObject> walls,
        List<Vector3> acc,
        float dedupeSq)
    {
        if (walls == null || walls.Count < 2 || acc == null)
            return;

        for (int a = 0; a < walls.Count; a++)
        {
            if (!TryFillWallRingForSnap(walls[a], _ringScratchA))
                continue;
            int na = _ringScratchA.Count;

            for (int b = a + 1; b < walls.Count; b++)
            {
                if (!TryFillWallRingForSnap(walls[b], _ringScratchB))
                    continue;
                int nb = _ringScratchB.Count;

                for (int ia = 0; ia < na; ia++)
                {
                    Vector3 a0 = _ringScratchA[ia];
                    Vector3 a1 = _ringScratchA[(ia + 1) % na];
                    for (int ib = 0; ib < nb; ib++)
                    {
                        Vector3 b0 = _ringScratchB[ib];
                        Vector3 b1 = _ringScratchB[(ib + 1) % nb];
                        if (TryAxisAlignedSegmentIntersectionXZ(a0, a1, b0, b1, out Vector3 hit))
                            TryAppendDedupXZ(acc, hit, dedupeSq);
                    }
                }
            }
        }
    }

    bool TryFillWallRingForSnap(WallObject wall, List<Vector3> ringOut)
    {
        ringOut.Clear();

        if (wall == null)
            return false;

        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit == null || !edit.IsClosedLoopPath)
            return false;

        if (edit.shapeKind != WallEditShape.ShapeKind.Rectangle &&
            edit.shapeKind != WallEditShape.ShapeKind.Free)
            return false;

        List<Vector3> path = edit.GetPreviewPathWorld();
        if (path == null || path.Count < 2)
            return false;

        for (int i = 0; i < path.Count; i++)
            ringOut.Add(path[i]);

        if (ringOut.Count >= 2 && Vector3.Distance(ringOut[0], ringOut[ringOut.Count - 1]) < 0.001f)
            ringOut.RemoveAt(ringOut.Count - 1);

        return ringOut.Count >= 2;
    }

    static void TryAppendDedupXZ(List<Vector3> acc, Vector3 v, float dedupeSq)
    {
        for (int j = 0; j < acc.Count; j++)
        {
            float dx = acc[j].x - v.x;
            float dz = acc[j].z - v.z;
            if (dx * dx + dz * dz <= dedupeSq)
                return;
        }

        acc.Add(v);
    }

    /// <summary>Intersection propre de deux segments axis-aligned (XZ), y compris jonction en T sur le milieu d’un mur.</summary>
    static bool TryAxisAlignedSegmentIntersectionXZ(
        Vector3 a0,
        Vector3 a1,
        Vector3 b0,
        Vector3 b1,
        out Vector3 hit)
    {
        const float axisEps = 0.00035f;
        const float pad = 0.0005f;

        hit = default;

        float dxA = Mathf.Abs(a1.x - a0.x);
        float dzA = Mathf.Abs(a1.z - a0.z);
        float dxB = Mathf.Abs(b1.x - b0.x);
        float dzB = Mathf.Abs(b1.z - b0.z);

        bool aHor = dzA <= axisEps && dxA > axisEps;
        bool aVer = dxA <= axisEps && dzA > axisEps;
        bool bHor = dzB <= axisEps && dxB > axisEps;
        bool bVer = dxB <= axisEps && dzB > axisEps;

        if (aHor && bVer)
        {
            float z = a0.z;
            float x = b0.x;
            float minAx = Mathf.Min(a0.x, a1.x) - pad;
            float maxAx = Mathf.Max(a0.x, a1.x) + pad;
            float minBz = Mathf.Min(b0.z, b1.z) - pad;
            float maxBz = Mathf.Max(b0.z, b1.z) + pad;
            if (x >= minAx && x <= maxAx && z >= minBz && z <= maxBz)
            {
                hit = new Vector3(x, a0.y, z);
                return true;
            }

            return false;
        }

        if (aVer && bHor)
        {
            float x = a0.x;
            float z = b0.z;
            float minAz = Mathf.Min(a0.z, a1.z) - pad;
            float maxAz = Mathf.Max(a0.z, a1.z) + pad;
            float minBx = Mathf.Min(b0.x, b1.x) - pad;
            float maxBx = Mathf.Max(b0.x, b1.x) + pad;
            if (z >= minAz && z <= maxAz && x >= minBx && x <= maxBx)
            {
                hit = new Vector3(x, a0.y, z);
                return true;
            }

            return false;
        }

        return false;
    }

    static void AppendWallOutlineVerticesDeduped(WallObject wall, List<Vector3> acc, float dedupeSq)
    {
        if (wall == null)
            return;

        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit == null || !edit.IsClosedLoopPath)
            return;

        if (edit.shapeKind != WallEditShape.ShapeKind.Rectangle &&
            edit.shapeKind != WallEditShape.ShapeKind.Free)
            return;

        List<Vector3> path = edit.GetPreviewPathWorld();
        if (path == null || path.Count < 2)
            return;

        for (int k = 0; k < path.Count; k++)
        {
            Vector3 v = path[k];
            bool dup = false;
            for (int j = 0; j < acc.Count; j++)
            {
                if ((acc[j] - v).sqrMagnitude <= dedupeSq)
                {
                    dup = true;
                    break;
                }
            }

            if (!dup)
                acc.Add(v);
        }
    }

    /// <summary>
    /// Si <paramref name="world"/> est proche d’un sommet de mur existant, le remplace par ce sommet exact.
    /// </summary>
    public bool TrySnapWorldPointToExistingWallCorners(ref Vector3 world, float radius = -1f)
    {
        if (!snapDrawToExistingWallCorners)
            return false;

        float r = radius > 0f ? radius : wallCornerSnapRadius;
        float rSq = r * r;

        _cornerSnapScratch.Clear();
        GatherExistingWallCornerWorldPoints(_cornerSnapScratch, dedupeEps: 0.02f);
        if (_cornerSnapScratch.Count == 0)
            return false;

        Vector3 best = world;
        float bestSq = rSq;
        for (int i = 0; i < _cornerSnapScratch.Count; i++)
        {
            Vector3 c = _cornerSnapScratch[i];
            float dx = c.x - world.x;
            float dz = c.z - world.z;
            float s = dx * dx + dz * dz;
            if (s < bestSq)
            {
                bestSq = s;
                best = c;
            }
        }

        if (bestSq >= rSq - 1e-8f)
            return false;

        world = new Vector3(best.x, world.y, best.z);
        return true;
    }

    /// <summary>
    /// Après projection sur la grille, recolle les sommets proches des coins de murs existants.
    /// </summary>
    public void SnapPathVerticesToExistingWallCornersInPlace(List<Vector3> path, float radius = -1f)
    {
        if (path == null || path.Count == 0 || !snapDrawToExistingWallCorners)
            return;

        float r = radius > 0f ? radius : wallCornerSnapRadius;
        float rSq = r * r;

        _cornerSnapScratch.Clear();
        GatherExistingWallCornerWorldPoints(_cornerSnapScratch, dedupeEps: 0.02f);
        if (_cornerSnapScratch.Count == 0)
            return;

        for (int p = 0; p < path.Count; p++)
        {
            Vector3 w = path[p];
            for (int i = 0; i < _cornerSnapScratch.Count; i++)
            {
                Vector3 c = _cornerSnapScratch[i];
                float dx = c.x - w.x;
                float dz = c.z - w.z;
                if (dx * dx + dz * dz <= rSq)
                {
                    path[p] = new Vector3(c.x, w.y, c.z);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Drag d’une poignée sur un lot orthogonal fusionné : recolle au <b>même</b> XZ qu’une autre poignée du contour
    /// ou qu’un coin / intersection d’un autre mur (liste complète comme au dessin).
    /// </summary>
    public bool TrySnapWorldPointForOrthogonalHandleDrag(ref Vector3 world, WallEditShape edit, int draggedControlIndex, out bool snapTargetIsExternalToEditedControlRing, float radius = -1f)
    {
        snapTargetIsExternalToEditedControlRing = false;

        if (!snapOrthogonalEditHandlesToWallCorners || edit == null || !edit.UsesMergedLotOrthogonalHandles)
            return false;

        float r = radius > 0f ? radius : wallCornerSnapRadius;
        float rSq = r * r;
        const float dedupeEps = 0.015f;
        float dedupeSq = dedupeEps * dedupeEps;

        _cornerSnapScratch.Clear();
        // Coin rentrant collé au saillant : pas d’aimantation vers les autres murs (évite enchaînements),
        // mais on garde le recollement aux autres poignées du même contour.
        if (!edit.ShouldSuppressInterWallSnapAndLotMergeAtIndex(draggedControlIndex))
            GatherExistingWallCornerWorldPoints(_cornerSnapScratch, dedupeEps);

        int nCtrl = edit.ControlPointCount;
        for (int j = 0; j < nCtrl; j++)
        {
            if (j == draggedControlIndex)
                continue;
            Vector3 q = edit.GetControlPointWorld(j);
            TryAppendDedupXZ(_cornerSnapScratch, new Vector3(q.x, world.y, q.z), dedupeSq);
        }

        if (_cornerSnapScratch.Count == 0)
            return false;

        Vector3 best = world;
        float bestSq = rSq;
        for (int i = 0; i < _cornerSnapScratch.Count; i++)
        {
            Vector3 c = _cornerSnapScratch[i];
            float dx = c.x - world.x;
            float dz = c.z - world.z;
            float s = dx * dx + dz * dz;
            if (s < bestSq)
            {
                bestSq = s;
                best = c;
            }
        }

        if (bestSq >= rSq - 1e-8f)
            return false;

        world = new Vector3(best.x, world.y, best.z);
        // Cible hors des poignées du lot édité = coin d’un autre mur ou intersection T (intention de raccord).
        snapTargetIsExternalToEditedControlRing = !IsWorldPointNearAnyEditControlPointXZ(edit, best, 0.0025f);
        return true;
    }

    static bool IsWorldPointNearAnyEditControlPointXZ(WallEditShape edit, Vector3 p, float eps)
    {
        int n = edit.ControlPointCount;
        for (int j = 0; j < n; j++)
        {
            Vector3 q = edit.GetControlPointWorld(j);
            if (Mathf.Abs(q.x - p.x) <= eps && Mathf.Abs(q.z - p.z) <= eps)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Première fusion : pas encore de rose touchée — <see cref="HouseEnvelopeSourceHandleUI.LastInteractedSourceLotIndex"/> reste -1.
    /// On aligne la surbrillance sur le lot déplacé (<paramref name="mergeSurvivorHint"/>) dans la liste des sources.
    /// </summary>
    static int ResolvePendingHouseEnvelopePinkHighlightIndex(HouseExteriorEnvelopeSources envelopeMeta, WallObject mergeSurvivorHint)
    {
        if (mergeSurvivorHint != null && envelopeMeta != null)
        {
            IReadOnlyList<GameObject> srcList = envelopeMeta.SourceLotObjects;
            for (int i = 0; i < srcList.Count; i++)
            {
                GameObject go = srcList[i];
                if (go == null)
                    continue;
                WallObject w = go.GetComponent<WallObject>();
                if (w != null && w == mergeSurvivorHint)
                    return i;
            }
        }

        return HouseEnvelopeSourceHandleUI.LastInteractedSourceLotIndex;
    }

    static int NormalizeIndependentEnvelopeSourceFocus(HouseExteriorEnvelopeSources envelopeMeta, int candidate)
    {
        if (envelopeMeta == null || !envelopeMeta.HasMultipleSourceLots || !envelopeMeta.UseIndependentSourceHandlesForHouseEnvelope)
            return -1;
        IReadOnlyList<GameObject> src = envelopeMeta.SourceLotObjects;
        if (src == null || src.Count == 0)
            return -1;
        if (candidate < 0)
            return 0;
        if (candidate >= src.Count)
            return src.Count - 1;
        return candidate;
    }

    readonly List<Vector3> _cornerSnapScratch = new List<Vector3>(64);

    /// <summary>
    /// Score 0–1 : 1 = périmètre à rayon quasi constant (cercle / ellipse échantillonnés même si le fit analytique échoue).
    /// </summary>
    static float EstimateClosedPathCircleLikeness(List<Vector3> path)
    {
        if (path == null || path.Count < 12)
            return 0f;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;

        if (n < 12)
            return 0f;

        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++)
            c += new Vector2(path[i].x, path[i].z);
        c /= n;

        float mean = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            mean += Vector2.Distance(p, c);
        }

        mean /= Mathf.Max(1, n);
        if (mean < 0.04f)
            return 0f;

        float var = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            float d = Vector2.Distance(p, c);
            float t = d - mean;
            var += t * t;
        }

        var /= Mathf.Max(1, n);
        float cv = Mathf.Sqrt(var) / mean;
        return Mathf.Clamp01(1f - cv * 6.5f);
    }

    /// <summary>
    /// Vrai seulement pour un rectangle aligné axes à exactement 4 sommets distincts.
    /// </summary>
    static bool IsStrictAxisAlignedRectangleFourCornerLoop(List<Vector3> path, float tol)
    {
        if (path == null || path.Count < 4)
            return false;

        var pts = new List<Vector3>(path);
        if (pts.Count >= 2 && Vector3.Distance(pts[0], pts[pts.Count - 1]) < tol)
            pts.RemoveAt(pts.Count - 1);

        if (pts.Count != 4)
            return false;

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % 4];
            bool horiz = Mathf.Abs(a.z - b.z) <= tol;
            bool vert = Mathf.Abs(a.x - b.x) <= tol;
            if (!horiz && !vert)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Union booléenne 2D (Clipper2) sur les contours réels — remplace l’union de rectangles / grille pour les formes courbes.
    /// </summary>
    bool TryBuildMergedPathWithClipperBoolean(
        List<Vector3> committedPoints,
        WallObject mergeSurvivorHint,
        HashSet<WallObject> mergeSet,
        float yWorld,
        out List<Vector3> mergedPath,
        out bool isFilledRectangle)
    {
        mergedPath = null;
        isFilledRectangle = false;
        if (committedPoints == null || committedPoints.Count < 3 || mergeSet == null)
            return false;

        var rings = new List<List<Vector3>> { new List<Vector3>(committedPoints) };

        foreach (WallObject w in mergeSet)
        {
            if (w == null)
                continue;
            if (mergeSurvivorHint != null && w == mergeSurvivorHint)
                continue;

            WallEditShape ed = w.GetComponent<WallEditShape>();
            if (ed == null || !ed.IsClosedLoopPath)
                continue;

            List<Vector3> pw = ed.GetPreviewPathWorld();
            if (pw == null || pw.Count < 3)
                continue;

            rings.Add(new List<Vector3>(pw));
        }

        return WallPolygonBooleanUnion.TryUnionClosedRingsWorldXZ(rings, yWorld, out mergedPath, out isFilledRectangle);
    }

    bool TryMergeCommittedShapeIntoHouse(
        List<Vector3> committedPoints,
        WallDrawInput.DetectedShapeKind detectedKind,
        WallObject mergeSurvivorHint,
        bool requireDesignatedHouseLot,
        bool dryRun = false)
    {
        // Rectangle / carré / free : empreinte orthogonale classique. Cercle : boîte carrée englobante pour l’union
        // (le contour est ensuite adouci par un arc sur le segment plat du bossage).
        if (detectedKind != WallDrawInput.DetectedShapeKind.Rectangle &&
            detectedKind != WallDrawInput.DetectedShapeKind.Square &&
            detectedKind != WallDrawInput.DetectedShapeKind.Free &&
            detectedKind != WallDrawInput.DetectedShapeKind.Circle &&
            detectedKind != WallDrawInput.DetectedShapeKind.Triangle)
            return false;

        float tolMerge = Mathf.Max(mergeContactTolerance, 0.08f);
        RectBounds newAabb;
        List<WallOrthoMergeUtility.RectXZ> newFootprint;
        if (detectedKind == WallDrawInput.DetectedShapeKind.Circle)
        {
            if (!TryGetCircleSquareAabbFootprintForMerge(committedPoints, tolMerge, out newAabb, out newFootprint) &&
                !TryGetAabbOnlyFootprintFromPreviewPath(committedPoints, out newAabb, out newFootprint))
                return false;
        }
        else if (!TryGetLotFootprintForMerge(committedPoints, tolMerge, out newAabb, out newFootprint) &&
                 !TryGetAabbOnlyFootprintFromPreviewPath(committedPoints, out newAabb, out newFootprint))
        {
            return false;
        }

        CleanupNullWalls();

        var lots = new List<LotMergeInfo>();
        var seenWalls = new HashSet<WallObject>();

        void TryAddLotCandidate(WallObject wall)
        {
            if (wall == null || !seenWalls.Add(wall))
                return;

            // Sources déjà "bundlées" à une enveloppe : ne pas les traiter comme lots voisins autonomes
            // lors d'une fusion normale, sinon le mergeSet peut embarquer des murs cachés historiques.
            if (mergeSurvivorHint == null || wall != mergeSurvivorHint)
            {
                WallObject bundledEnv = HouseEnvelopeBundledSourceTag.GetEnvelopeIfBundled(wall);
                if (bundledEnv != null)
                    return;
            }

            // Enveloppe maison multi-plans : les lots sources restent dans la scène (masqués) et touchent encore
            // l’empreinte fusionnée — ne pas les traiter comme des voisins à re-fusionner avec l’enveloppe.
            if (mergeSurvivorHint != null)
            {
                HouseExteriorEnvelopeSources envHint = mergeSurvivorHint.GetComponent<HouseExteriorEnvelopeSources>();
                if (envHint != null && envHint.SourceLotObjects != null)
                {
                    IReadOnlyList<GameObject> srcGos = envHint.SourceLotObjects;
                    for (int si = 0; si < srcGos.Count; si++)
                    {
                        GameObject g = srcGos[si];
                        if (g == null)
                            continue;
                        WallObject srcWo = g.GetComponent<WallObject>();
                        if (srcWo != null && srcWo == wall)
                            return;
                    }
                }
            }

            if (!wall.gameObject.activeInHierarchy)
                return;

            WallEditShape edit = wall.GetComponent<WallEditShape>();
            if (edit == null || !edit.IsClosedLoopPath)
                return;

            bool rectOrFree = edit.shapeKind == WallEditShape.ShapeKind.Rectangle ||
                              edit.shapeKind == WallEditShape.ShapeKind.Free;
            bool ellipseHouse = edit.shapeKind == WallEditShape.ShapeKind.Ellipse &&
                                 WallCountsAsDesignatedHouse(wall);
            bool triangleHouse = edit.shapeKind == WallEditShape.ShapeKind.Triangle &&
                                 WallCountsAsDesignatedHouse(wall);
            if (!rectOrFree && !ellipseHouse && !triangleHouse)
                return;

            List<Vector3> path = edit.GetPreviewPathWorld();
            if (path == null || path.Count < 2)
                return;

            RectBounds aabb;
            List<WallOrthoMergeUtility.RectXZ> footprint;
            if (!TryGetLotFootprintForMerge(path, tolMerge, out aabb, out footprint))
            {
                if (!WallCountsAsDesignatedHouse(wall))
                    return;
                if (!TryGetAabbOnlyFootprintFromPreviewPath(path, out aabb, out footprint))
                    return;
            }

            lots.Add(new LotMergeInfo
            {
                wall = wall,
                edit = edit,
                aabb = aabb,
                footprint = footprint
            });
        }

        for (int i = 0; i < _walls.Count; i++)
            TryAddLotCandidate(_walls[i]);

        WallObject[] allSceneWalls = FindObjectsByType<WallObject>(FindObjectsSortMode.None);
        for (int i = 0; i < allSceneWalls.Length; i++)
            TryAddLotCandidate(allSceneWalls[i]);

        if (lots.Count == 0)
            return false;

        // Le contact visible des murs peut laisser un petit écart entre les empreintes XZ (épaisseur / snap / pierres).
        // La grille donne un minimum très strict ; mergeContactTolerance est la marge métier exposée dans l'inspector.
        float flushGap = Mathf.Max(ComputeFlushMergeMaxGap(), tolMerge);
        float footprintArea = Mathf.Max(1e-12f, (newAabb.maxX - newAabb.minX) * (newAabb.maxZ - newAabb.minZ));
        // Chevauchement 2D (rectangles qui se recouvrent sans être « bord à bord ») : nécessaire pour fusionner
        // deux lots maison qui se superposent ; sans cela seuls cercle + adjacence flush étaient pris en compte.
        float minFootprintOverlapArea = Mathf.Max(1e-6f, footprintArea * 0.00025f);
        if (!TryBuildMergeSetFlushAdjacent(
                lots,
                newFootprint,
                newAabb,
                mergeSurvivorHint,
                flushGap,
                minFootprintOverlapArea,
                out HashSet<WallObject> mergeSet))
            return false;

        // Sans second lot, le seul candidat est souvent le mur déplacé (auto-chevauchement empreinte / AABB).
        // La suite reconstruit alors l’union rectangulaire → périmètre = boîte englobante du cercle (carré « parfait »)
        // et perte ellipse / fusion/split incohérente. Pas de fusion réelle → ne rien appliquer.
        if (mergeSurvivorHint != null &&
            mergeSet.Count == 1 &&
            mergeSet.Contains(mergeSurvivorHint))
            return false;

        if (requireDesignatedHouseLot && designatedHouseLotsUseOuterEnvelopeOnly)
            TryExpandMergeSetWithExistingHouseEnvelopeSourceLots(ref mergeSet, mergeSurvivorHint);

        if (requireDesignatedHouseLot)
        {
            // Sans hint (tracé/preset) : mergeSet prouve qu'un voisin maison est en contact.
            // Avec hint (drag) : le mur déplacé doit lui-même être un lot maison.
            if (mergeSurvivorHint == null)
            {
                if (mergeSet == null || mergeSet.Count < 1)
                    return false;
            }
            else
            {
                if (!WallCountsAsDesignatedHouse(mergeSurvivorHint))
                    return false;
            }

            // N'embarquer que des maisons. Un mur non-maison voisin ne doit pas bloquer une fusion maison valide.
            var designatedOnly = new HashSet<WallObject>();
            foreach (WallObject w in mergeSet)
            {
                if (w != null && WallCountsAsDesignatedHouse(w))
                    designatedOnly.Add(w);
            }
            if (mergeSurvivorHint != null && WallCountsAsDesignatedHouse(mergeSurvivorHint))
                designatedOnly.Add(mergeSurvivorHint);
            if (designatedOnly.Count == 0)
                return false;

            mergeSet = designatedOnly;

            // Après filtrage, s'il ne reste que le mur hint, pas de vraie fusion à effectuer.
            if (mergeSurvivorHint != null &&
                mergeSet.Count == 1 &&
                mergeSet.Contains(mergeSurvivorHint))
                return false;
        }

        WallObject targetWall = SelectMergeSurvivorWall(lots, mergeSet, mergeSurvivorHint);
        WallEditShape targetEdit = targetWall != null ? targetWall.GetComponent<WallEditShape>() : null;

        if (targetWall == null || targetEdit == null)
            return false;

        var unionRects = new List<WallOrthoMergeUtility.RectXZ>(newFootprint.Count + mergeSet.Count * 4);
        for (int i = 0; i < newFootprint.Count; i++)
            unionRects.Add(newFootprint[i]);

        foreach (LotMergeInfo h in lots)
        {
            if (!mergeSet.Contains(h.wall))
                continue;
            for (int k = 0; k < h.footprint.Count; k++)
                unionRects.Add(h.footprint[k]);
        }

        // Valeurs par défaut : le compilateur n’infère pas toujours l’assignation des `out`
        // quand l’appel est dans un `if` (TryGetMergeGridParameters peut être false).
        List<Vector3> mergedPath = null;
        bool isFilledRectangle = false;

        bool mergedWithClipper = TryBuildMergedPathWithClipperBoolean(
            committedPoints,
            mergeSurvivorHint,
            mergeSet,
            newAabb.y,
            out mergedPath,
            out isFilledRectangle);

        bool builtPoly = mergedWithClipper && mergedPath != null;

        // Repli : union de rectangles sur grille (ortho / ancien cercle en boîte).
        if (!builtPoly || mergedPath == null)
        {
            builtPoly = WallOrthoMergeUtility.TryBuildMergedClosedPathFromRectUnionWorld(
                unionRects,
                newAabb.y,
                out mergedPath,
                out isFilledRectangle);

            if (!builtPoly || mergedPath == null)
            {
                if (TryGetMergeGridParameters(out float cellStep, out Vector2 gridOrigin))
                {
                    builtPoly = WallOrthoMergeUtility.TryBuildMergedClosedPath(
                        unionRects,
                        cellStep,
                        gridOrigin,
                        newAabb.y,
                        out mergedPath,
                        out isFilledRectangle);
                }
            }
        }

        if (!builtPoly || mergedPath == null)
            return false;

        if (dryRun)
            return mergeSet != null && mergeSet.Count > 0;

        // Toujours lancer ces passes : si l’union passe par Clipper, le contour peut encore avoir des segments
        // plats sur le carré englobant du cercle ; avant elles ne tournaient que sans Clipper → cercle « en carré ».
        // Pas seulement detectedKind Circle : un lot ellipse peut être classé Free après snap / édition.
        if (TryFitCircleXZFromClosedPath(committedPoints, out Vector2 circleCxz, out float circleR, out _) &&
            circleR > 0.15f &&
            TryComputeHouseFootprintCentroidXZ(lots, mergeSet, out Vector2 houseCentroidXz))
        {
            for (int arcPass = 0; arcPass < 28; arcPass++)
            {
                if (!TryReplaceCircleBumpFlatEdgeWithArc(mergedPath, circleCxz, circleR, houseCentroidXz))
                    break;
            }
        }

        TryBeautifyMergedPathWithEllipseSourceLots(mergedPath, lots, mergeSet);

        if (TryResolveCircleCenterRadiusForBumpSnap(committedPoints, lots, mergeSet, out Vector2 bumpC, out float bumpR))
            SnapCircleBboxStrutsOntoCircleRing(mergedPath, bumpC.x, bumpC.y, bumpR);

        EnsureMergedOutlineClosedIfNearOpen(mergedPath, Mathf.Max(0.055f, tolMerge * 4f));

        // Snap hiérarchique sur un L peut déformer le contour et réintroduire un segment « mur intérieur ».
        if (drawInput != null && isFilledRectangle)
            drawInput.SnapCommittedPathToMainGridInPlace(mergedPath, closed: true);

        // Rectangle plein après fusion : les cloisons (arête partagée par deux lots) ne sont pas sur le
        // périmètre extérieur — sans ça, InitFromDetectedPath(Rectangle) ne garde que 4 coins et le mur disparaît.
        if (isFilledRectangle &&
            WallOrthoMergeUtility.TryExpandFilledRectanglePathWithInternalPartitionSpikes(
                mergedPath,
                unionRects,
                newAabb.y,
                out List<Vector3> partitionSpikedPath) &&
            partitionSpikedPath != null &&
            partitionSpikedPath.Count >= 4)
        {
            mergedPath = partitionSpikedPath;
            isFilledRectangle = false;
            if (drawInput != null)
                drawInput.SnapCommittedPathToMainGridInPlace(mergedPath, closed: true);
        }

        Material parquetMat = null;
        float parquetUv = 0.45f;
        float parquetY = 0.003f;
        bool anyParquet = false;
        foreach (WallObject w in mergeSet)
        {
            if (w == null)
                continue;
            HouseParquetFloor pf = w.GetComponent<HouseParquetFloor>();
            if (pf == null)
                continue;
            anyParquet = true;
            parquetMat = pf.parquetMaterial;
            parquetUv = pf.uvMetersPerTile;
            parquetY = pf.yOffsetAboveBase;
            break;
        }

        if (!anyParquet && mergeSurvivorHint != null)
        {
            HouseParquetFloor pf = mergeSurvivorHint.GetComponent<HouseParquetFloor>();
            if (pf != null)
            {
                anyParquet = true;
                parquetMat = pf.parquetMaterial;
                parquetUv = pf.uvMetersPerTile;
                parquetY = pf.yOffsetAboveBase;
            }
        }

        if (requireDesignatedHouseLot && designatedHouseLotsUseOuterEnvelopeOnly)
        {
            if (undoManager == null)
                undoManager = FindFirstObjectByType<WallUndoManager>();
            if (undoManager != null)
                undoManager.RecordSnapshot("Update house outer envelope");

            WallObject envelopeWall = null;
            if (mergeSurvivorHint != null && mergeSurvivorHint.GetComponent<HouseExteriorEnvelopeSources>() != null)
                envelopeWall = mergeSurvivorHint;

            if (envelopeWall == null)
            {
                foreach (WallObject w in mergeSet)
                {
                    if (w != null && w.GetComponent<HouseExteriorEnvelopeSources>() != null)
                    {
                        envelopeWall = w;
                        break;
                    }
                }
            }

            // Lot source déjà rattaché à une enveloppe : réutiliser ce mur (sinon CreateWallFromShapePathInternal à chaque tentative).
            if (envelopeWall == null)
            {
                foreach (WallObject w in mergeSet)
                {
                    if (w == null)
                        continue;
                    WallObject envFromSource = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(w);
                    if (envFromSource != null)
                    {
                        envelopeWall = envFromSource;
                        break;
                    }
                }
            }

            if (envelopeWall == null)
            {
                envelopeWall = CreateWallFromShapePathInternal(
                    mergedPath,
                    isFilledRectangle ? WallDrawInput.DetectedShapeKind.Rectangle : WallDrawInput.DetectedShapeKind.Free,
                    registerAndSelect: false);
                if (envelopeWall == null)
                    return false;
                RegisterExistingWall(envelopeWall);
            }

            WallEditShape envelopeEdit = envelopeWall.GetComponent<WallEditShape>();
            if (envelopeEdit == null)
                envelopeEdit = envelopeWall.gameObject.AddComponent<WallEditShape>();
            envelopeEdit.wall = envelopeWall;

            if (isFilledRectangle)
                envelopeEdit.InitFromDetectedPath(mergedPath, WallDrawInput.DetectedShapeKind.Rectangle);
            else
                envelopeEdit.InitFromMergedLotOutline(mergedPath, drawInput, false);

            HouseParquetFloor envelopeFloor = envelopeWall.GetComponent<HouseParquetFloor>();
            if (anyParquet)
            {
                if (envelopeFloor == null)
                    envelopeFloor = envelopeWall.gameObject.AddComponent<HouseParquetFloor>();
                envelopeFloor.parquetMaterial = parquetMat;
                envelopeFloor.uvMetersPerTile = parquetUv;
                envelopeFloor.yOffsetAboveBase = parquetY;
                ApplyHouseParquetForDesignatedClosedLot(envelopeFloor, envelopeWall, envelopeEdit);
            }

            HouseExteriorEnvelopeSources envelopeMeta = envelopeWall.GetComponent<HouseExteriorEnvelopeSources>();
            if (envelopeMeta == null)
                envelopeMeta = envelopeWall.gameObject.AddComponent<HouseExteriorEnvelopeSources>();

            // Tracé/preset sans mur déplacé : committedPoints a participé au mergedPath, mais aucun WallObject source
            // n'existe encore pour ce nouveau lot. Sans cette création, l'enveloppe peut s'agrandir une frame puis
            // revenir aux deux sources précédentes au prochain recalcul.
            if (mergeSurvivorHint == null)
            {
                WallObject committedSource = CreateCommittedHouseSourceForEnvelopeMerge(
                    committedPoints,
                    detectedKind,
                    envelopeWall,
                    mergeSet);
                if (committedSource != null)
                    mergeSet.Add(committedSource);
            }
            else if (mergeSurvivorHint != envelopeWall &&
                     mergeSurvivorHint.GetComponent<HouseExteriorEnvelopeSources>() == null)
            {
                // Drag/preset avec un vrai mur maison déjà créé : committedPoints met bien à jour le contour,
                // mais ce mur doit aussi devenir une source de l'enveloppe. Sinon on reste bloqué aux 2 sources
                // précédentes et la 3e maison reste séparée / réapparaît au prochain recalcul.
                mergeSet.Add(mergeSurvivorHint);
            }

            envelopeMeta.SetSourcesMergingWithMergeSet(mergeSet, envelopeWall);

            IReadOnlyList<GameObject> bundledSources = envelopeMeta.SourceLotObjects;
            for (int bi = 0; bi < bundledSources.Count; bi++)
            {
                GameObject bgo = bundledSources[bi];
                if (bgo == null)
                    continue;
                WallObject w = bgo.GetComponent<WallObject>();
                if (w == null)
                    continue;

                if (envelopeMeta.UseIndependentSourceHandlesForHouseEnvelope)
                {
                    HouseEnvelopeBundledSourceTag tag = w.GetComponent<HouseEnvelopeBundledSourceTag>();
                    if (tag == null)
                        tag = w.gameObject.AddComponent<HouseEnvelopeBundledSourceTag>();
                    tag.envelopeWall = envelopeWall;

                    HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(w, true);
                }
                else
                {
                    w.gameObject.SetActive(false);
                }
            }

            EnsureWallStoneCladdingEnabled(envelopeWall);
            StartCoroutine(CoRefreshCladdingAfterLotMerge(envelopeWall));
            StartCoroutine(CoDeferredParquetAfterOverlapMerge(envelopeWall, parquetMat, parquetUv, parquetY));

            ControlPointHandleUI.ApplyEditingSelectionAfterHouseEnvelopeMerge(envelopeEdit);
            int pendingPinkLot = ResolvePendingHouseEnvelopePinkHighlightIndex(envelopeMeta, mergeSurvivorHint);
            pendingPinkLot = NormalizeIndependentEnvelopeSourceFocus(envelopeMeta, pendingPinkLot);
            HouseEnvelopeSourceHandleUI.PendingHighlightSourceLotIndex = pendingPinkLot;

            WallSelectable wsEnvelope = envelopeWall.GetComponent<WallSelectable>();
            if (wsEnvelope != null)
                wsEnvelope.AutoFindProvider();

            // Garder le même plan source mis en avant (rose orange) via SetTarget — ne pas SetFocusPink ici :
            // sinon le pivot violet ne reçoit aucun raycast jusqu’à changement de mur / reload (voir EnvelopeOverlayHandleFocus).
            if (pendingPinkLot >= 0 &&
                envelopeMeta.HasMultipleSourceLots &&
                envelopeMeta.UseIndependentSourceHandlesForHouseEnvelope)
                ForceSelectWall(envelopeWall, null, pendingPinkLot);
            else
                ForceSelectWall(envelopeWall);

            StartCoroutine(CoDeferredEnvelopeMergeHandleColorRefresh());

            return true;
        }

        if (undoManager == null)
            undoManager = FindFirstObjectByType<WallUndoManager>();
        if (undoManager != null)
            undoManager.RecordSnapshot("Merge lots");

        targetEdit.wall = targetWall;
        // Contour union déjà densifié (arcs) : ne pas repasser par « poignées préservées » qui dédupliquent en ~4 sommets.
        // Inclure Clipper / cercle / périmètre dense : sinon isFilledRectangle + kind Free peut déclencher
        // InitFromDetectedPath(Rectangle) et transformer un cercle fusionné en « carré parfait ».
        bool mergedPathFitsCircle =
            mergedPath != null && TryFitCircleXZFromClosedPath(mergedPath, out _, out _, out _);
        bool committedFitsCircle =
            committedPoints != null && TryFitCircleXZFromClosedPath(committedPoints, out _, out _, out _);
        bool mergedOutlineDenseOrCurved = mergedPath != null &&
            (mergedPath.Count > 14 || EstimateClosedPathCircleLikeness(mergedPath) > 0.58f);

        bool preferDenseMergedOutline = MergeSetHasEllipseDesignatedHouse(mergeSet, lots) ||
                                        detectedKind == WallDrawInput.DetectedShapeKind.Circle ||
                                        mergedWithClipper ||
                                        mergedPathFitsCircle ||
                                        committedFitsCircle ||
                                        mergedOutlineDenseOrCurved;

        if (isFilledRectangle && !preferDenseMergedOutline &&
            IsStrictAxisAlignedRectangleFourCornerLoop(mergedPath, tolMerge))
            targetEdit.InitFromDetectedPath(mergedPath, WallDrawInput.DetectedShapeKind.Rectangle);
        else
        {
            List<Vector3> preservedOutline = null;
            if (!preferDenseMergedOutline)
            {
                preservedOutline = TryBuildPreservedMergeHandleOutline(
                    mergedPath,
                    mergeSet,
                    targetWall,
                    mergeSurvivorHint,
                    newAabb,
                    mergeSurvivorHint == null,
                    tolMerge);
            }

            if (preservedOutline != null && preservedOutline.Count >= 3)
            {
                var closedPreserved = new List<Vector3>(preservedOutline.Count + 1);
                closedPreserved.AddRange(preservedOutline);
                closedPreserved.Add(preservedOutline[0]);
                targetEdit.InitFromMergedLotOutline(closedPreserved, drawInput, false);
            }
            else
                targetEdit.InitFromMergedLotOutline(mergedPath, drawInput, false);
        }

        HouseParquetFloor targetFloor = targetWall.GetComponent<HouseParquetFloor>();
        if (anyParquet)
        {
            if (targetFloor == null)
                targetFloor = targetWall.gameObject.AddComponent<HouseParquetFloor>();
            targetFloor.parquetMaterial = parquetMat;
            targetFloor.uvMetersPerTile = parquetUv;
            targetFloor.yOffsetAboveBase = parquetY;
            if (!targetEdit.IsClosedLoopPath)
                targetFloor.ClearFloor();
            else
                ApplyHouseParquetForDesignatedClosedLot(targetFloor, targetWall, targetEdit);
        }
        else if (targetFloor != null)
            targetFloor.ClearFloor();

        foreach (WallObject wall in mergeSet)
        {
            if (wall == null || wall == targetWall)
                continue;

            UnregisterWall(wall);
            if (Application.isPlaying)
                Destroy(wall.gameObject);
            else
                DestroyImmediate(wall.gameObject);
        }

        // Mur déplacé absorbé par la cible (ex. carré normal → maison) : hors mergeSet, donc pas détruit plus haut.
        if (mergeSurvivorHint != null && mergeSurvivorHint != targetWall && !mergeSet.Contains(mergeSurvivorHint))
        {
            UnregisterWall(mergeSurvivorHint);
            if (Application.isPlaying)
                Destroy(mergeSurvivorHint.gameObject);
            else
                DestroyImmediate(mergeSurvivorHint.gameObject);
        }

        EnsureWallStoneCladdingEnabled(targetWall);
        // Second passage frame suivante : path/mesh mur parfois encore en cours de synchro après fusion.
        StartCoroutine(CoRefreshCladdingAfterLotMerge(targetWall));
        if (requireDesignatedHouseLot)
            StartCoroutine(CoDeferredParquetAfterOverlapMerge(targetWall, parquetMat, parquetUv, parquetY));

        RegisterExistingWall(targetWall);

        WallEditShape survivorEdit = targetWall.GetComponent<WallEditShape>();
        ControlPointHandleUI.ResyncSelectionAfterMergeIntoSurvivor(survivorEdit);

        WallSelectable ws = targetWall.GetComponent<WallSelectable>();
        if (ws != null)
            ws.AutoFindProvider();

        ForceSelectWall(targetWall);

        if (overlay != null)
            overlay.RebuildOverlay();

        return true;
    }

    static void EnsureWallStoneCladdingEnabled(WallObject wall)
    {
        if (wall == null)
            return;

        WallCladdingGenerator gen = wall.GetComponent<WallCladdingGenerator>();
        if (gen != null)
            gen.EnsureStoneCladdingEnabledAndRefresh();
    }

    void RequestDeferredCladdingRefresh(WallObject wall)
    {
        if (wall == null || !isActiveAndEnabled)
            return;

        StartCoroutine(CoRefreshCladdingAfterLotMerge(wall));
    }

    IEnumerator CoRefreshCladdingAfterLotMerge(WallObject wall)
    {
        yield return null;
        EnsureWallStoneCladdingEnabled(wall);
    }

    IEnumerator CoDeferredEnvelopeMergeHandleColorRefresh()
    {
        yield return null;
        HouseEnvelopeSourceHandleUI.RefreshAllPinkHandleVisuals();
        MergedLotShapePivotHandleUI.RefreshAllPivotVisualStates();
    }

    /// <summary>
    /// Après PointerUp sur une rose enveloppe : le contour est déjà recalculé sans overlay/pierre lourde ;
    /// reporte overlay + parquet + pierre pour éviter le gel (EventSystem + pic sur la même frame).
    /// </summary>
    public void ScheduleEnvelopePinkReleaseVisualFollowup(WallObject envelopeWall)
    {
        if (envelopeWall == null || !isActiveAndEnabled)
            return;
        StartCoroutine(CoEnvelopePinkReleaseVisualFollowup(envelopeWall));
    }

    IEnumerator CoEnvelopePinkReleaseVisualFollowup(WallObject envelopeWall)
    {
        yield return null;

        if (overlay != null)
            overlay.RebuildOverlay();

        if (envelopeWall == null)
            yield break;

        WallEditShape envelopeEdit = envelopeWall.GetComponent<WallEditShape>();
        if (envelopeEdit == null)
            yield break;

        HouseParquetFloor envelopeFloor = envelopeWall.GetComponent<HouseParquetFloor>();
        if (envelopeFloor != null && envelopeFloor.parquetMaterial != null)
            ApplyHouseParquetForDesignatedClosedLot(envelopeFloor, envelopeWall, envelopeEdit);

        EnsureWallStoneCladdingEnabled(envelopeWall);
        yield return StartCoroutine(CoRefreshCladdingAfterLotMerge(envelopeWall));
    }

    float ComputeFlushMergeMaxGap()
    {
        if (TryGetMergeGridParameters(out float cellStep, out _))
            return Mathf.Max(flushMergeMaxGapAbsoluteM, cellStep * flushMergeMaxGapFractionOfCell);
        return Mathf.Max(flushMergeMaxGapAbsoluteM, 0.004f);
    }

    /// <summary>
    /// Voisins reliés uniquement par un côté commun aligné (pas de trou d’une maille, pas simple chevauchement flou).
    /// </summary>
    static bool AreRectsFlushAdjacentForMerge(RectBounds a, RectBounds b, float maxGap)
    {
        float minOverlapAlongEdge = Mathf.Max(Mathf.Min(maxGap * 2f, 0.12f), 0.035f);

        bool ZSpanLongEnough()
        {
            float len = Mathf.Min(a.maxZ, b.maxZ) - Mathf.Max(a.minZ, b.minZ);
            return len >= minOverlapAlongEdge - maxGap * 2f;
        }

        bool XSpanLongEnough()
        {
            float len = Mathf.Min(a.maxX, b.maxX) - Mathf.Max(a.minX, b.minX);
            return len >= minOverlapAlongEdge - maxGap * 2f;
        }

        if (Mathf.Abs(a.maxX - b.minX) <= maxGap && ZSpanLongEnough())
            return true;
        if (Mathf.Abs(b.maxX - a.minX) <= maxGap && ZSpanLongEnough())
            return true;
        if (Mathf.Abs(a.maxZ - b.minZ) <= maxGap && XSpanLongEnough())
            return true;
        if (Mathf.Abs(b.maxZ - a.minZ) <= maxGap && XSpanLongEnough())
            return true;
        return false;
    }

    static bool RectFootprintsCoincident(RectBounds a, RectBounds b, float maxGap)
    {
        return Mathf.Abs(a.minX - b.minX) <= maxGap &&
               Mathf.Abs(a.maxX - b.maxX) <= maxGap &&
               Mathf.Abs(a.minZ - b.minZ) <= maxGap &&
               Mathf.Abs(a.maxZ - b.maxZ) <= maxGap;
    }

    WallObject CreateCommittedHouseSourceForEnvelopeMerge(
        List<Vector3> committedPoints,
        WallDrawInput.DetectedShapeKind detectedKind,
        WallObject envelopeWall,
        HashSet<WallObject> mergeSet)
    {
        if (committedPoints == null || committedPoints.Count < 3 || envelopeWall == null)
            return null;

        WallObject source = CreateWallFromShapePathInternal(committedPoints, detectedKind, registerAndSelect: false);
        if (source == null)
            return null;

        WallObject reference = envelopeWall;
        if (!WallCountsAsDesignatedHouse(reference) && mergeSet != null)
        {
            foreach (WallObject w in mergeSet)
            {
                if (WallCountsAsDesignatedHouse(w))
                {
                    reference = w;
                    break;
                }
            }
        }

        CopyDesignatedHouseAppearanceFromReference(reference, source);
        RegisterExistingWall(source);
        return source;
    }

    /// <summary>
    /// Quand un 3ᵉ (ou Nᵉ) carré ne touche l’enveloppe qu’en bordure, le BFS peut ne relier qu’enveloppe + nouveau lot
    /// sans remonter aux carrés sources distants. On les ré-injecte depuis <see cref="HouseExteriorEnvelopeSources"/>
    /// et on retire l’enveloppe du <paramref name="mergeSet"/> pour l’union de rectangles / Clipper.
    /// </summary>
    void TryExpandMergeSetWithExistingHouseEnvelopeSourceLots(
        ref HashSet<WallObject> mergeSet,
        WallObject mergeSurvivorHint)
    {
        if (mergeSet == null || mergeSet.Count == 0)
            return;

        WallObject envelopeW = null;
        if (mergeSurvivorHint != null)
        {
            envelopeW = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(mergeSurvivorHint);
            if (envelopeW == null && mergeSurvivorHint.GetComponent<HouseExteriorEnvelopeSources>() != null)
                envelopeW = mergeSurvivorHint;
        }

        if (envelopeW == null)
        {
            foreach (WallObject w in mergeSet)
            {
                if (w == null)
                    continue;
                if (w.GetComponent<HouseExteriorEnvelopeSources>() != null)
                {
                    envelopeW = w;
                    break;
                }
            }
        }

        if (envelopeW == null)
        {
            foreach (WallObject w in mergeSet)
            {
                if (w == null)
                    continue;
                envelopeW = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(w);
                if (envelopeW != null)
                    break;
            }
        }

        if (envelopeW == null)
            return;

        HouseExteriorEnvelopeSources meta = envelopeW.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || meta.SourceLotObjects == null)
            return;

        IReadOnlyList<GameObject> srcGos = meta.SourceLotObjects;
        for (int i = 0; i < srcGos.Count; i++)
        {
            GameObject go = srcGos[i];
            if (go == null)
                continue;
            WallObject srcW = go.GetComponent<WallObject>();
            if (srcW == null)
                continue;
            mergeSet.Add(srcW);
        }

        if (mergeSet.Contains(envelopeW))
            mergeSet.Remove(envelopeW);
    }

    bool TryBuildMergeSetFlushAdjacent(
        List<LotMergeInfo> lots,
        List<WallOrthoMergeUtility.RectXZ> newFootprint,
        RectBounds newAabb,
        WallObject mergeSurvivorHint,
        float flushGap,
        float minFootprintOverlapArea,
        out HashSet<WallObject> mergeSet)
    {
        mergeSet = new HashSet<WallObject>();
        var q = new Queue<WallObject>();

        // Ne pas exclure mergeSurvivorHint : sinon un preset (cercle/triangle) centré sur ce lot avec
        // TryMergeCommittedShapeIntoHouse(hint) ne met jamais le voisin dans mergeSet → fusion impossible,
        // deux murs séparés et artefacts (pierres/sol/pivot).
        foreach (LotMergeInfo h in lots)
        {
            bool overlapOk = false;
            if (!float.IsPositiveInfinity(minFootprintOverlapArea))
            {
                overlapOk = AnyFootprintOverlapArea(newFootprint, h.footprint, minFootprintOverlapArea);
                if (!overlapOk)
                {
                    float nearOverlapInflate = Mathf.Min(flushGap * 2f, 0.04f);
                    List<WallOrthoMergeUtility.RectXZ> inflated = InflateFootprintXZ(newFootprint, nearOverlapInflate);
                    overlapOk = AnyFootprintOverlapArea(inflated, h.footprint, 1e-8f);
                }
            }

            if (!AnyFootprintFlushAdjacent(newFootprint, h.footprint, flushGap) &&
                !RectFootprintsCoincident(newAabb, h.aabb, flushGap) &&
                !overlapOk)
                continue;

            mergeSet.Add(h.wall);
            q.Enqueue(h.wall);
        }

        while (q.Count > 0)
        {
            WallObject w0 = q.Dequeue();

            List<WallOrthoMergeUtility.RectXZ> fp0 = null;
            for (int i = 0; i < lots.Count; i++)
            {
                if (lots[i].wall == w0)
                {
                    fp0 = lots[i].footprint;
                    break;
                }
            }

            if (fp0 == null)
                continue;

            foreach (LotMergeInfo h in lots)
            {
                if (mergeSet.Contains(h.wall))
                    continue;

                bool linked = AnyFootprintFlushAdjacent(fp0, h.footprint, flushGap);
                if (!linked)
                {
                    linked = AnyFootprintOverlapArea(fp0, h.footprint, minFootprintOverlapArea);
                    if (!linked)
                    {
                        float nearOverlapInflate = Mathf.Min(flushGap * 2f, 0.04f);
                        List<WallOrthoMergeUtility.RectXZ> inflated = InflateFootprintXZ(fp0, nearOverlapInflate);
                        linked = AnyFootprintOverlapArea(inflated, h.footprint, 1e-8f);
                    }
                }

                if (!linked)
                    continue;

                mergeSet.Add(h.wall);
                q.Enqueue(h.wall);
            }
        }

        return mergeSet.Count > 0;
    }

    static bool AnyFootprintFlushAdjacent(
        List<WallOrthoMergeUtility.RectXZ> a,
        List<WallOrthoMergeUtility.RectXZ> b,
        float gap)
    {
        if (a == null || b == null)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            for (int j = 0; j < b.Count; j++)
            {
                if (AreRectXZFlushAdjacent(a[i], b[j], gap))
                    return true;
            }
        }

        return false;
    }

    static bool AreRectXZFlushAdjacent(WallOrthoMergeUtility.RectXZ a, WallOrthoMergeUtility.RectXZ b, float maxGap)
    {
        var ra = new RectBounds
        {
            minX = a.minX,
            maxX = a.maxX,
            minZ = a.minZ,
            maxZ = a.maxZ,
            y = 0f
        };
        var rb = new RectBounds
        {
            minX = b.minX,
            maxX = b.maxX,
            minZ = b.minZ,
            maxZ = b.maxZ,
            y = 0f
        };
        return AreRectsFlushAdjacentForMerge(ra, rb, maxGap);
    }

    static bool WallCountsAsDesignatedHouse(WallObject wall)
    {
        if (wall == null)
            return false;

        HouseParquetFloor f = wall.GetComponent<HouseParquetFloor>();
        if (f != null && f.IsDesignatedHouseLot)
            return true;

        // Lot source caché d'une enveloppe : peut ne plus avoir de mesh parquet (ClearFloor lors du masquage),
        // mais doit rester éligible aux fusions maison.
        HouseEnvelopeBundledSourceTag bundled = wall.GetComponent<HouseEnvelopeBundledSourceTag>();
        if (bundled != null && bundled.envelopeWall != null)
            return true;

        // Mur enveloppe lui-même.
        return wall.GetComponent<HouseExteriorEnvelopeSources>() != null;
    }

    static WallObject SelectMergeSurvivorWall(
        List<LotMergeInfo> lots,
        HashSet<WallObject> mergeSet,
        WallObject mergeSurvivorHint)
    {
        for (int i = 0; i < lots.Count; i++)
        {
            WallObject w = lots[i].wall;
            if (w != null && mergeSet.Contains(w) && WallCountsAsDesignatedHouse(w))
                return w;
        }

        // Maison déplacée vers un lot normal : la maison n’est pas dans mergeSet (seulement les voisins).
        if (mergeSurvivorHint != null && WallCountsAsDesignatedHouse(mergeSurvivorHint))
            return mergeSurvivorHint;

        if (mergeSurvivorHint != null && mergeSet.Contains(mergeSurvivorHint))
            return mergeSurvivorHint;

        for (int i = 0; i < lots.Count; i++)
        {
            if (mergeSet.Contains(lots[i].wall))
                return lots[i].wall;
        }

        return null;
    }

    static WallOrthoMergeUtility.RectXZ ToRectXZ(RectBounds r)
    {
        return new WallOrthoMergeUtility.RectXZ
        {
            minX = r.minX,
            maxX = r.maxX,
            minZ = r.minZ,
            maxZ = r.maxZ
        };
    }

    static RectBounds UnionBoundsFromRects(List<WallOrthoMergeUtility.RectXZ> rects, float y)
    {
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (r.minX < minX) minX = r.minX;
            if (r.maxX > maxX) maxX = r.maxX;
            if (r.minZ < minZ) minZ = r.minZ;
            if (r.maxZ > maxZ) maxZ = r.maxZ;
        }

        return new RectBounds
        {
            minX = minX,
            maxX = maxX,
            minZ = minZ,
            maxZ = maxZ,
            y = y
        };
    }

    static float OverlapAreaRectXZ(WallOrthoMergeUtility.RectXZ a, WallOrthoMergeUtility.RectXZ b)
    {
        float ix = Mathf.Min(a.maxX, b.maxX) - Mathf.Max(a.minX, b.minX);
        float iz = Mathf.Min(a.maxZ, b.maxZ) - Mathf.Max(a.minZ, b.minZ);
        if (ix <= 0f || iz <= 0f)
            return 0f;
        return ix * iz;
    }

    static bool AnyFootprintOverlapArea(
        List<WallOrthoMergeUtility.RectXZ> a,
        List<WallOrthoMergeUtility.RectXZ> b,
        float minArea)
    {
        if (a == null || b == null || minArea <= 0f)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            for (int j = 0; j < b.Count; j++)
            {
                if (OverlapAreaRectXZ(a[i], b[j]) >= minArea)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Plancher maison : rectangle, contour fusionné libre, ou contour fermé courbe (cercle, triangle).</summary>
    static void ApplyHouseParquetForDesignatedClosedLot(HouseParquetFloor floor, WallObject wall, WallEditShape edit)
    {
        // parquetMaterial peut être null : <see cref="HouseParquetFloor.BuildMultiStoreyExtrudedFloors"/> applique alors un Lit/Standard beige runtime.
        if (floor == null || wall == null || edit == null || !edit.IsClosedLoopPath)
            return;

        if (edit.shapeKind == WallEditShape.ShapeKind.Rectangle)
            floor.ApplyOrRefresh(wall, edit);
        else if (edit.shapeKind == WallEditShape.ShapeKind.Free)
            floor.ApplyOrRefreshClosedFreeLoop(wall, edit);
        else if (edit.shapeKind == WallEditShape.ShapeKind.Ellipse || edit.shapeKind == WallEditShape.ShapeKind.Triangle)
            floor.ApplyOrRefreshFromClosedPreviewPath(wall, edit);
        else
            floor.ClearFloor();
    }

    /// <summary>
    /// Repli pour lot maison : AABB XZ du polyline (un seul rectangle), si l’empreinte orthogonale échoue.
    /// </summary>
    static bool TryGetAabbOnlyFootprintFromPreviewPath(
        List<Vector3> path,
        out RectBounds aabb,
        out List<WallOrthoMergeUtility.RectXZ> footprint)
    {
        footprint = null;
        aabb = default;
        if (path == null || path.Count < 2)
            return false;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float y = path[0].y;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
            y = p.y;
        }

        if (!(maxX > minX && maxZ > minZ))
            return false;

        var rect = new WallOrthoMergeUtility.RectXZ
        {
            minX = minX,
            maxX = maxX,
            minZ = minZ,
            maxZ = maxZ
        };
        footprint = new List<WallOrthoMergeUtility.RectXZ>(1) { rect };
        aabb = new RectBounds
        {
            minX = minX,
            maxX = maxX,
            minZ = minZ,
            maxZ = maxZ,
            y = y
        };
        return true;
    }

    static List<WallOrthoMergeUtility.RectXZ> InflateFootprintXZ(
        List<WallOrthoMergeUtility.RectXZ> src,
        float pad)
    {
        if (src == null || pad <= 0f)
            return src;

        var r = new List<WallOrthoMergeUtility.RectXZ>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            WallOrthoMergeUtility.RectXZ q = src[i];
            r.Add(new WallOrthoMergeUtility.RectXZ
            {
                minX = q.minX - pad,
                maxX = q.maxX + pad,
                minZ = q.minZ - pad,
                maxZ = q.maxZ + pad
            });
        }

        return r;
    }

    /// <summary>
    /// Contour fusionné presque fermé (jeu flottant / arcs) : recolle le dernier point au premier pour éviter
    /// <see cref="WallEditShape.IsClosedLoopPath"/> faux → pas de parquet, habillage brisé.
    /// </summary>
    static void EnsureMergedOutlineClosedIfNearOpen(List<Vector3> path, float maxAutoCloseGapMeters)
    {
        if (path == null || path.Count < 3)
            return;
        Vector3 a = path[0];
        Vector3 b = path[path.Count - 1];
        float d = Vector3.Distance(a, b);
        if (d <= 1e-5f)
            return;
        if (d <= maxAutoCloseGapMeters)
            path.Add(a);
    }

    /// <summary>
    /// Centre / rayon du cercle pour lissage : tracé « cercle » en cours de fusion, sinon premier lot ellipse du mergeSet.
    /// </summary>
    static bool TryResolveCircleCenterRadiusForBumpSnap(
        List<Vector3> committedPoints,
        List<LotMergeInfo> lots,
        HashSet<WallObject> mergeSet,
        out Vector2 centerXZ,
        out float radius)
    {
        centerXZ = default;
        radius = 0f;
        if (committedPoints != null &&
            TryFitCircleXZFromClosedPath(committedPoints, out Vector2 c0, out float r0, out _) &&
            r0 > 0.12f)
        {
            centerXZ = c0;
            radius = r0;
            return true;
        }

        if (lots == null || mergeSet == null)
            return false;
        for (int i = 0; i < lots.Count; i++)
        {
            if (!mergeSet.Contains(lots[i].wall) || lots[i].edit == null)
                continue;
            if (lots[i].edit.shapeKind != WallEditShape.ShapeKind.Ellipse)
                continue;
            if (!lots[i].edit.TryGetEllipseCircleApproxXZForLotMerge(out Vector2 c1, out float r1) || r1 < 0.12f)
                continue;
            centerXZ = c1;
            radius = r1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sommets encore sur le bord du carré englobant [cx±r]×[cz±r] mais au-delà du disque (bossage « à coins ») :
    /// projection radiale sur le cercle — complète les remplacements d’arête quand l’union laisse des points cardinaux.
    /// </summary>
    static void SnapCircleBboxStrutsOntoCircleRing(List<Vector3> path, float cx, float cz, float r)
    {
        if (path == null || path.Count < 4 || r < 0.1f)
            return;

        float y0 = path[0].y;
        var open = new List<Vector3>(path);
        if (open.Count >= 2 && Vector3.Distance(open[0], open[open.Count - 1]) < 0.001f)
            open.RemoveAt(open.Count - 1);

        float lineTol = Mathf.Max(0.035f, r * 0.03f);
        float insideEps = Mathf.Max(0.0015f, r * 0.0006f);

        for (int i = 0; i < open.Count; i++)
        {
            float px = open[i].x;
            float pz = open[i].z;
            float dx = px - cx;
            float dz = pz - cz;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 1e-5f || d <= r + insideEps)
                continue;

            bool onN = Mathf.Abs(pz - (cz + r)) < lineTol && px >= cx - r - lineTol && px <= cx + r + lineTol;
            bool onS = Mathf.Abs(pz - (cz - r)) < lineTol && px >= cx - r - lineTol && px <= cx + r + lineTol;
            bool onE = Mathf.Abs(px - (cx + r)) < lineTol && pz >= cz - r - lineTol && pz <= cz + r + lineTol;
            bool onW = Mathf.Abs(px - (cx - r)) < lineTol && pz >= cz - r - lineTol && pz <= cz + r + lineTol;
            if (!onN && !onS && !onE && !onW)
                continue;

            float nx = cx + dx / d * r;
            float nz = cz + dz / d * r;
            open[i] = new Vector3(nx, y0, nz);
        }

        path.Clear();
        for (int i = 0; i < open.Count; i++)
            path.Add(open[i]);
        if (path.Count >= 2 && Vector3.Distance(path[0], path[path.Count - 1]) > 1e-5f)
            path.Add(path[0]);
    }

    /// <summary>
    /// Cercle dessiné / préréglé : empreinte fusion = carré axes [cx±r]×[cz±r] pour l’union avec le lot maison.
    /// </summary>
    static bool TryGetCircleSquareAabbFootprintForMerge(
        List<Vector3> path,
        float tolAbs,
        out RectBounds aabb,
        out List<WallOrthoMergeUtility.RectXZ> footprint)
    {
        footprint = null;
        aabb = default;
        if (path == null || path.Count < 6)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;

        if (n < 6)
            return false;

        float y = path[0].y;
        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++)
            c += new Vector2(path[i].x, path[i].z);
        c /= n;

        float rAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            rAcc += Vector2.Distance(p, c);
        }

        float r = rAcc / n;
        if (r < 0.12f)
            return false;

        float maxErr = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            maxErr = Mathf.Max(maxErr, Mathf.Abs(Vector2.Distance(p, c) - r));
        }

        if (maxErr > Mathf.Max(tolAbs, r * 0.36f))
            return false;

        var rect = new WallOrthoMergeUtility.RectXZ
        {
            minX = c.x - r,
            maxX = c.x + r,
            minZ = c.y - r,
            maxZ = c.y + r,
        };

        footprint = new List<WallOrthoMergeUtility.RectXZ>(1) { rect };
        aabb = new RectBounds
        {
            minX = rect.minX,
            maxX = rect.maxX,
            minZ = rect.minZ,
            maxZ = rect.maxZ,
            y = y
        };
        return true;
    }

    static bool TryFitCircleXZFromClosedPath(List<Vector3> path, out Vector2 centerXZ, out float radius, out float y)
    {
        centerXZ = default;
        radius = 0f;
        y = 0f;
        if (path == null || path.Count < 6)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;

        if (n < 6)
            return false;

        y = path[0].y;
        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++)
            c += new Vector2(path[i].x, path[i].z);
        c /= n;

        float rAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            rAcc += Vector2.Distance(p, c);
        }

        radius = rAcc / n;
        if (radius < 0.12f)
            return false;

        float maxErr = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            maxErr = Mathf.Max(maxErr, Mathf.Abs(Vector2.Distance(p, c) - radius));
        }

        if (maxErr > Mathf.Max(0.07f, radius * 0.22f))
            return false;

        centerXZ = c;
        return true;
    }

    static bool MergeSetHasEllipseDesignatedHouse(HashSet<WallObject> mergeSet, List<LotMergeInfo> lots)
    {
        if (mergeSet == null || lots == null)
            return false;
        for (int i = 0; i < lots.Count; i++)
        {
            if (!mergeSet.Contains(lots[i].wall) || lots[i].edit == null)
                continue;
            if (lots[i].edit.shapeKind != WallEditShape.ShapeKind.Ellipse)
                continue;
            if (WallCountsAsDesignatedHouse(lots[i].wall))
                return true;
        }

        return false;
    }

    static bool TryComputeHouseFootprintCentroidXZ(
        List<LotMergeInfo> lots,
        HashSet<WallObject> mergeSet,
        out Vector2 centroidXZ)
    {
        centroidXZ = default;
        float sx = 0f;
        float sz = 0f;
        int count = 0;

        for (int i = 0; i < lots.Count; i++)
        {
            if (!mergeSet.Contains(lots[i].wall))
                continue;

            for (int r = 0; r < lots[i].footprint.Count; r++)
            {
                var b = lots[i].footprint[r];
                sx += (b.minX + b.maxX) * 0.5f;
                sz += (b.minZ + b.maxZ) * 0.5f;
                count++;
            }
        }

        if (count == 0)
            return false;

        centroidXZ = new Vector2(sx / count, sz / count);
        return true;
    }

    /// <summary>
    /// Quand le mur déplacé n’est pas classé « cercle » (ex. rectangle) mais qu’un voisin est encore une ellipse maison,
    /// remplace sur le contour union les segments plats du carré englobant de ce cercle par l’arc.
    /// </summary>
    static void TryBeautifyMergedPathWithEllipseSourceLots(
        List<Vector3> mergedPath,
        List<LotMergeInfo> lots,
        HashSet<WallObject> mergeSet)
    {
        if (mergedPath == null || mergedPath.Count < 4 || lots == null || mergeSet == null || mergeSet.Count == 0)
            return;
        if (!TryComputeHouseFootprintCentroidXZ(lots, mergeSet, out Vector2 houseCentroidXz))
            return;

        for (int i = 0; i < lots.Count; i++)
        {
            LotMergeInfo lm = lots[i];
            if (!mergeSet.Contains(lm.wall) || lm.edit == null)
                continue;
            if (lm.edit.shapeKind != WallEditShape.ShapeKind.Ellipse)
                continue;
            if (!WallCountsAsDesignatedHouse(lm.wall))
                continue;
            if (!lm.edit.TryGetEllipseCircleApproxXZForLotMerge(out Vector2 cxz, out float r) || r < 0.15f)
                continue;

            for (int arcPass = 0; arcPass < 28; arcPass++)
            {
                if (!TryReplaceCircleBumpFlatEdgeWithArc(mergedPath, cxz, r, houseCentroidXz))
                    break;
            }
        }
    }

    /// <summary>
    /// Remplace un segment horizontal ou vertical ~2r sur le bord du carré englobant du cercle par des points le long de l’arc extérieur.
    /// Retourne false si aucun segment n’a été remplacé (contour inchangé).
    /// </summary>
    static bool TryReplaceCircleBumpFlatEdgeWithArc(
        List<Vector3> path,
        Vector2 circleCenterXZ,
        float r,
        Vector2 houseInteriorHintXZ)
    {
        if (path == null || path.Count < 5 || r < 0.1f)
            return false;

        float cx = circleCenterXZ.x;
        float cz = circleCenterXZ.y;
        float yRef = path[0].y;
        float epsZ = Mathf.Max(0.015f, r * 0.025f);
        float epsX = Mathf.Max(0.015f, r * 0.025f);
        float band = Mathf.Max(0.14f, r * 0.4f);

        var open = new List<Vector3>(path);
        if (open.Count >= 2 && Vector3.Distance(open[0], open[open.Count - 1]) < 0.001f)
            open.RemoveAt(open.Count - 1);

        int n = open.Count;
        if (n < 4)
            return false;

        int bestI = -1;
        float bestL = -1f;

        float bboxPad = band * 2.5f;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a = open[i];
            Vector3 b = open[j];

            bool horiz = Mathf.Abs(a.z - b.z) <= epsZ;
            bool vert = Mathf.Abs(a.x - b.x) <= epsX;
            if (!horiz && !vert)
                continue;

            float L = horiz ? Mathf.Abs(a.x - b.x) : Mathf.Abs(a.z - b.z);
            // Côtés partiels après union carré+cercle : ne pas exiger ~2r ; les « coins » cardinaux viennent souvent
            // de plats courts ou décalés (midX loin de cx) qu’on ne remplaçait pas à cause du filtre midX.
            float Lmin = Mathf.Max(0.03f, Mathf.Min(r * 0.18f, 0.75f));
            if (L < Lmin || L > r * 3.15f)
                continue;

            float midX = (a.x + b.x) * 0.5f;
            float midZ = (a.z + b.z) * 0.5f;
            Vector2 chordMid = new Vector2(midX, midZ);
            Vector2 toHint = houseInteriorHintXZ - chordMid;

            if (horiz)
            {
                float zRun = (a.z + b.z) * 0.5f;
                bool nearNorth = Mathf.Abs(zRun - (cz + r)) < band;
                bool nearSouth = Mathf.Abs(zRun - (cz - r)) < band;
                if (!nearNorth && !nearSouth)
                    continue;

                float xmin = Mathf.Min(a.x, b.x);
                float xmax = Mathf.Max(a.x, b.x);
                if (xmax < cx - r - bboxPad || xmin > cx + r + bboxPad)
                    continue;

                Vector2 outward = new Vector2(0f, nearNorth ? 1f : -1f);
                if (toHint.sqrMagnitude > 1e-8f && Vector2.Dot(outward, toHint) > 0f)
                    continue;
            }
            else
            {
                float xRun = (a.x + b.x) * 0.5f;
                bool nearEast = Mathf.Abs(xRun - (cx + r)) < band;
                bool nearWest = Mathf.Abs(xRun - (cx - r)) < band;
                if (!nearEast && !nearWest)
                    continue;

                float zmin = Mathf.Min(a.z, b.z);
                float zmax = Mathf.Max(a.z, b.z);
                if (zmax < cz - r - bboxPad || zmin > cz + r + bboxPad)
                    continue;

                Vector2 outward = new Vector2(nearEast ? 1f : -1f, 0f);
                if (toHint.sqrMagnitude > 1e-8f && Vector2.Dot(outward, toHint) > 0f)
                    continue;
            }

            // Remplacer d’abord les plus longs plats sur la boîte (cordes majeures), puis les morceaux restants.
            if (L > bestL)
            {
                bestL = L;
                bestI = i;
            }
        }

        if (bestI < 0)
            return false;

        int jEnd = (bestI + 1) % n;
        Vector3 pA = open[bestI];
        Vector3 pB = open[jEnd];

        float angA = Mathf.Atan2(pA.z - cz, pA.x - cx);
        float angB = Mathf.Atan2(pB.z - cz, pB.x - cx);

        float shortStep = Mathf.DeltaAngle(angA * Mathf.Rad2Deg, angB * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float longStep = shortStep > 0f ? shortStep - Mathf.PI * 2f : shortStep + Mathf.PI * 2f;

        float midShort = angA + shortStep * 0.5f;
        float midLong = angA + longStep * 0.5f;
        Vector2 qS = new Vector2(cx + Mathf.Cos(midShort) * r, cz + Mathf.Sin(midShort) * r);
        Vector2 qL = new Vector2(cx + Mathf.Cos(midLong) * r, cz + Mathf.Sin(midLong) * r);
        bool useLong = (qL - houseInteriorHintXZ).sqrMagnitude > (qS - houseInteriorHintXZ).sqrMagnitude;
        float arcStep = useLong ? longStep : shortStep;

        float absArc = Mathf.Abs(arcStep);
        // Densité ~0,7°–2,5° selon rayon : contour visible lisse (pas seulement quelques points cardinaux).
        float degPerSeg = Mathf.Lerp(1.85f, 0.5f, Mathf.InverseLerp(0.15f, 8f, r));
        int segments = Mathf.Clamp(
            Mathf.CeilToInt((absArc * Mathf.Rad2Deg) / Mathf.Max(0.28f, degPerSeg)),
            36,
            360);
        var insert = new List<Vector3>(Mathf.Max(0, segments - 1));
        for (int s = 1; s < segments; s++)
        {
            float t = s / (float)segments;
            float ang = angA + arcStep * t;
            insert.Add(new Vector3(cx + Mathf.Cos(ang) * r, yRef, cz + Mathf.Sin(ang) * r));
        }

        // Reconstruire le contour sans l’arête droite (bestI → jEnd). Ne pas utiliser open[0]..open[bestI] quand
        // bestI == n-1 et jEnd == 0 : on dupliquerait tout l’anneau et on casserait la fermeture (forme en C, pas de sol).
        var rebuilt = new List<Vector3>(n + insert.Count + 1);
        for (int k = 0; k < bestI; k++)
            rebuilt.Add(open[k]);
        rebuilt.Add(open[bestI]);
        rebuilt.AddRange(insert);
        if (jEnd > bestI)
        {
            for (int k = jEnd; k < n; k++)
                rebuilt.Add(open[k]);
        }
        else
        {
            // Arête qui referme l’anneau (dernier sommet → premier) : suffixe linéaire open[jEnd..] recollerait tout le début.
            rebuilt.Add(open[jEnd]);
        }

        if (rebuilt.Count < 3)
            return false;

        rebuilt.Add(rebuilt[0]);
        path.Clear();
        path.AddRange(rebuilt);
        return true;
    }

    /// <summary>
    /// Empreinte union de rectangles pour fusion : un rectangle par lot, ou décomposition d’un contour orthogonal fermé (L, U, …).
    /// </summary>
    static bool TryGetLotFootprintForMerge(
        List<Vector3> path,
        float tol,
        out RectBounds aabb,
        out List<WallOrthoMergeUtility.RectXZ> footprint)
    {
        footprint = null;
        aabb = default;
        if (path == null || path.Count < 2)
            return false;

        if (TryGetAxisAlignedClosedRectBounds(path, out RectBounds rb))
        {
            aabb = rb;
            footprint = new List<WallOrthoMergeUtility.RectXZ>(1) { ToRectXZ(rb) };
            return true;
        }

        if (TryGetClosedLoopAxisAlignedRectByPerimeterDistance(path, tol, out rb))
        {
            aabb = rb;
            footprint = new List<WallOrthoMergeUtility.RectXZ>(1) { ToRectXZ(rb) };
            return true;
        }

        if (WallOrthoMergeUtility.TryDecomposeOrthogonalClosedLoopToRects(path, out List<WallOrthoMergeUtility.RectXZ> dec) &&
            dec != null &&
            dec.Count > 0)
        {
            footprint = dec;
            aabb = UnionBoundsFromRects(dec, path[0].y);
            return true;
        }

        // Cercle / ellipse : contour courbe → pas de décomposition orthogonale ; même empreinte carré [cx±r] que
        // <see cref="TryGetCircleSquareAabbFootprintForMerge"/> pour le tracé validé « cercle » au commit.
        if (TryGetCircleSquareAabbFootprintForMerge(path, Mathf.Max(tol, 0.06f), out aabb, out footprint))
            return true;

        return false;
    }

    bool TryGetMergeGridParameters(out float cellStep, out Vector2 gridOriginXZ)
    {
        cellStep = 1f;
        gridOriginXZ = Vector2.zero;

        if (drawInput != null && drawInput.TryGetHierarchicalCellStepAndOrigin(out cellStep, out gridOriginXZ))
            return true;

        HierarchicalGridManager mgr = drawInput != null && drawInput.hierarchicalGrid != null
            ? drawInput.hierarchicalGrid
            : FindFirstObjectByType<HierarchicalGridManager>();

        if (mgr != null && mgr.settings != null)
        {
            cellStep = Mathf.Max(0.01f, mgr.settings.minCellSize);
            gridOriginXZ = mgr.settings.gridWorldCenterXZ;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Même idée que <see cref="WallDrawInput.TryBuildGridRectangleFromPoints"/> : AABB + distance des
    /// échantillons au périmètre, avec une tolérance plus large que le test « coin sur arête » strict.
    /// </summary>
    static bool TryGetClosedLoopAxisAlignedRectByPerimeterDistance(List<Vector3> path, float tol, out RectBounds rect)
    {
        rect = default;
        if (path == null || path.Count < 2)
            return false;

        int nUnique = path.Count;
        if (nUnique >= 2 && Vector3.Distance(path[0], path[nUnique - 1]) < 0.001f)
            nUnique--;
        if (nUnique < 3)
            return false;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float y = path[0].y;

        for (int i = 0; i < nUnique; i++)
        {
            Vector3 p = path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float w = maxX - minX;
        float h = maxZ - minZ;
        if (w < 0.02f || h < 0.02f)
            return false;

        float diag = Mathf.Sqrt(w * w + h * h);
        float useTol = Mathf.Max(tol, diag * 0.0005f);

        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0 && i == path.Count - 1 && Vector3.Distance(path[0], path[i]) < 0.0001f)
                continue;

            Vector2 p = new Vector2(path[i].x, path[i].z);
            float d = DistancePointToAxisAlignedRectPerimeter(p, minX, maxX, minZ, maxZ);
            if (d > useTol)
                return false;
        }

        rect.minX = minX;
        rect.maxX = maxX;
        rect.minZ = minZ;
        rect.maxZ = maxZ;
        rect.y = y;
        return true;
    }

    static float DistancePointToAxisAlignedRectPerimeter(Vector2 p, float minX, float maxX, float minZ, float maxZ)
    {
        if (p.x < minX || p.x > maxX || p.y < minZ || p.y > maxZ)
        {
            float cx = Mathf.Clamp(p.x, minX, maxX);
            float cy = Mathf.Clamp(p.y, minZ, maxZ);
            return Vector2.Distance(p, new Vector2(cx, cy));
        }

        float dx = Mathf.Min(p.x - minX, maxX - p.x);
        float dy = Mathf.Min(p.y - minZ, maxZ - p.y);
        return Mathf.Min(dx, dy);
    }

    static bool TryGetAxisAlignedClosedRectBounds(List<Vector3> path, out RectBounds rect)
    {
        rect = default;
        if (path == null || path.Count < 4)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;
        if (n < 4)
            return false;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float y = path[0].y;

        for (int i = 0; i < n; i++)
        {
            Vector3 p = path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float diag = Mathf.Sqrt((maxX - minX) * (maxX - minX) + (maxZ - minZ) * (maxZ - minZ));
        float borderEps = Mathf.Max(0.02f, diag * 1e-4f);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = path[i];
            bool onX = Mathf.Abs(p.x - minX) <= borderEps || Mathf.Abs(p.x - maxX) <= borderEps;
            bool onZ = Mathf.Abs(p.z - minZ) <= borderEps || Mathf.Abs(p.z - maxZ) <= borderEps;
            if (!onX && !onZ)
                return false;
        }

        if (maxX - minX <= borderEps || maxZ - minZ <= borderEps)
            return false;

        rect.minX = minX;
        rect.maxX = maxX;
        rect.minZ = minZ;
        rect.maxZ = maxZ;
        rect.y = y;
        return true;
    }

    void TrySelectWallUnderMouse()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, wallRaycastMask, QueryTriggerInteraction.Ignore))
            return;

        WallObject wall = hit.collider.GetComponentInParent<WallObject>();
        if (wall == null)
            return;

        RegisterExistingWall(wall);
        ForceSelectWall(wall);
    }

    MonoBehaviour ResolveBestProvider(WallObject wall)
    {
        if (wall == null)
            return null;

        WallEditShape editShape = wall.GetComponent<WallEditShape>();
        if (editShape != null)
            return editShape;

        WallSelectable selectable = wall.GetComponent<WallSelectable>();
        if (selectable != null)
        {
            if (selectable.providerBehaviour == null)
                selectable.AutoFindProvider();

            if (selectable.providerBehaviour != null)
                return selectable.providerBehaviour;
        }

        MonoBehaviour[] monos = wall.GetComponents<MonoBehaviour>();
        for (int i = 0; i < monos.Length; i++)
        {
            if (monos[i] is IControlPointProvider)
                return monos[i];
        }

        return null;
    }

    public void ForceSelectWall(WallObject wall, Vector3? envelopeClickHitWorld = null, int? independentHouseEnvelopeSourceLotOverride = null)
    {
        SelectedWall = wall;

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>(FindObjectsInactive.Include);

        if (wall == null)
        {
            if (overlay != null)
                overlay.ClearTarget();
            return;
        }

        MonoBehaviour provider = ResolveBestProvider(wall);
        if (overlay != null)
        {
            if (provider != null)
            {
                int envelopeSourceFocus = -1;
                if (independentHouseEnvelopeSourceLotOverride.HasValue)
                    envelopeSourceFocus = independentHouseEnvelopeSourceLotOverride.Value;
                else if (envelopeClickHitWorld.HasValue &&
                    provider is WallEditShape wes &&
                    wes.wall == wall)
                {
                    HouseExteriorEnvelopeSources env = wall.GetComponent<HouseExteriorEnvelopeSources>();
                    if (env != null &&
                        env.HasMultipleSourceLots &&
                        env.UseIndependentSourceHandlesForHouseEnvelope)
                    {
                        if (env.TryResolveSourceLotIndexForEnvelopeClick(envelopeClickHitWorld.Value, out int resolved))
                            envelopeSourceFocus = resolved;
                        else
                            envelopeSourceFocus = 0;
                    }
                }

                if (provider is WallEditShape wesFocus &&
                    wesFocus.wall == wall)
                {
                    HouseExteriorEnvelopeSources env = wall.GetComponent<HouseExteriorEnvelopeSources>();
                    envelopeSourceFocus = NormalizeIndependentEnvelopeSourceFocus(env, envelopeSourceFocus);
                }

                overlay.SetTarget(provider, envelopeSourceFocus);
            }
            else
                overlay.ClearTarget();
        }
    }

    public void RegisterExistingWall(WallObject wall)
    {
        if (wall == null)
            return;

        CleanupNullWalls();

        if (_walls.Contains(wall))
            return;

        _walls.Add(wall);
    }

    /// <summary>
    /// Déplace les murs ouverts intérieurs dont <see cref="WallEditShape.interiorWallsStayInsideLot"/> pointe vers ce lot
    /// (même translation XZ que le déplacement global de la forme).
    /// </summary>
    public void MoveInteriorWallsAttachedToLotXZ(WallEditShape lotEdit, Vector3 deltaXZ)
    {
        if (lotEdit == null)
            return;

        deltaXZ.y = 0f;
        if (deltaXZ.sqrMagnitude < 1e-16f)
            return;

        CleanupNullWalls();

        for (int i = 0; i < _walls.Count; i++)
        {
            WallObject w = _walls[i];
            if (w == null)
                continue;

            WallEditShape e = w.GetComponent<WallEditShape>();
            if (e == null)
                continue;

            if (e.interiorWallsStayInsideLot != lotEdit)
                continue;

            e.TranslateOpenFreeInteriorWallXZ(deltaXZ);
        }
    }

    public bool UnregisterWall(WallObject wall)
    {
        if (wall == null)
            return false;

        bool removed = _walls.Remove(wall);

        if (SelectedWall == wall)
            ForceSelectWall(null);

        return removed;
    }

    public void ClearManagedWalls()
    {
        CleanupNullWalls();
        _walls.Clear();

        if (SelectedWall != null)
            ForceSelectWall(null);
    }

    public void ClearManagedWalls(bool destroyWallObjects)
    {
        if (destroyWallObjects)
        {
            for (int i = _walls.Count - 1; i >= 0; i--)
            {
                WallObject wall = _walls[i];
                if (wall == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(wall.gameObject);
                else
                    DestroyImmediate(wall.gameObject);
            }
        }

        ClearManagedWalls();
    }

    void CleanupNullWalls()
    {
        for (int i = _walls.Count - 1; i >= 0; i--)
        {
            if (_walls[i] == null)
                _walls.RemoveAt(i);
        }
    }

    /// <summary>
    /// Rebuilds a Free-loop outline for a merged L/U shape using perimeter handles from each participating lot
    /// (rectangle 0–7 + free points + committed AABB mids) so users keep the same edge/corner grips as before.
    /// Points far from the merged exterior (e.g. removed partition) are dropped; order follows the outer ring CCW.
    /// </summary>
    List<Vector3> TryBuildPreservedMergeHandleOutline(
        List<Vector3> mergedClosedPath,
        HashSet<WallObject> mergeSet,
        WallObject targetWall,
        WallObject mergeSurvivorHint,
        RectBounds newAabb,
        bool includeNewDrawnRectPerimeter8,
        float mergeTol)
    {
        if (mergedClosedPath == null || mergedClosedPath.Count < 3)
            return null;

        var borderOpen = new List<Vector3>(mergedClosedPath);
        if (borderOpen.Count >= 2 &&
            Vector3.SqrMagnitude(borderOpen[0] - borderOpen[borderOpen.Count - 1]) < 1e-6f)
            borderOpen.RemoveAt(borderOpen.Count - 1);

        if (borderOpen.Count < 3)
            return null;

        if (SignedAreaXZPolygonOpen(borderOpen) < 0f)
            borderOpen.Reverse();

        float yOut = mergedClosedPath[0].y;

        var wallSet = new HashSet<WallObject>();
        foreach (WallObject w in mergeSet)
        {
            if (w != null)
                wallSet.Add(w);
        }
        if (targetWall != null)
            wallSet.Add(targetWall);
        if (mergeSurvivorHint != null)
            wallSet.Add(mergeSurvivorHint);

        var raw = new List<Vector3>(64);
        foreach (WallObject wo in wallSet)
        {
            if (wo == null)
                continue;
            WallEditShape edit = wo.GetComponent<WallEditShape>();
            if (edit == null || !edit.IsClosedLoopPath)
                continue;
            if (edit.shapeKind == WallEditShape.ShapeKind.Rectangle)
                edit.AppendRectanglePerimeterHandlesTo(raw);
            else if (edit.shapeKind == WallEditShape.ShapeKind.Ellipse)
                edit.AppendEllipsePerimeterHandlesTo(raw);
            else if (edit.shapeKind == WallEditShape.ShapeKind.Triangle)
                edit.AppendTrianglePerimeterHandlesTo(raw);
            else if (edit.shapeKind == WallEditShape.ShapeKind.Free && edit.freeControlPoints != null)
            {
                for (int i = 0; i < edit.freeControlPoints.Count; i++)
                    raw.Add(edit.freeControlPoints[i]);
            }
        }

        if (includeNewDrawnRectPerimeter8)
            AppendWorldAxisAlignedRectPerimeter8(raw, newAabb);

        float exteriorTol = Mathf.Max(mergeTol * 1.35f, 0.11f);
        float exteriorTolSq = exteriorTol * exteriorTol;
        var onBorder = new List<Vector3>();
        for (int i = 0; i < raw.Count; i++)
        {
            Vector3 p = raw[i];
            if (MinDistanceSqPointToClosedPolylineXZ(p, borderOpen) > exteriorTolSq)
                continue;
            onBorder.Add(new Vector3(p.x, yOut, p.z));
        }

        if (onBorder.Count < 3)
            return null;

        bool hasEllipseSource = false;
        foreach (WallObject wo in wallSet)
        {
            if (wo == null)
                continue;
            WallEditShape ed = wo.GetComponent<WallEditShape>();
            if (ed != null && ed.shapeKind == WallEditShape.ShapeKind.Ellipse)
            {
                hasEllipseSource = true;
                break;
            }
        }

        // Échantillons d’arc proches : 0,038 m fusionnait trop de points → contour « carré » à quelques poignées.
        float dedupeEps = hasEllipseSource ? 0.0105f : 0.038f;
        onBorder = SpatialDedupeAverageXZ(onBorder, dedupeEps);
        if (onBorder.Count < 3)
            return null;

        float perimeter = PerimeterClosedLoopXZ(borderOpen);
        if (perimeter < 1e-4f)
            return null;

        var withArc = new List<(float arc, Vector3 p)>(onBorder.Count);
        for (int i = 0; i < onBorder.Count; i++)
        {
            float arc = ArclengthOfClosestPointOnClosedLoopXZ(borderOpen, onBorder[i]);
            withArc.Add((arc, onBorder[i]));
        }

        withArc.Sort((a, b) => a.arc.CompareTo(b.arc));
        return OrderPreservedHandlesByRingArclength(withArc, perimeter);
    }

    static List<Vector3> OrderPreservedHandlesByRingArclength(List<(float arc, Vector3 p)> sortedByArc, float perimeter)
    {
        int n = sortedByArc.Count;
        var ordered = new List<Vector3>(n);
        if (n == 0)
            return ordered;
        if (n == 1)
        {
            ordered.Add(sortedByArc[0].p);
            return ordered;
        }

        int start = 0;
        float maxGap = -1f;
        for (int i = 0; i < n; i++)
        {
            float cur = sortedByArc[i].arc;
            float nxt = (i + 1 < n) ? sortedByArc[i + 1].arc : sortedByArc[0].arc + perimeter;
            float gap = nxt - cur;
            if (gap > maxGap)
            {
                maxGap = gap;
                start = (i + 1) % n;
            }
        }

        for (int k = 0; k < n; k++)
            ordered.Add(sortedByArc[(start + k) % n].p);
        return ordered;
    }

    static float SignedAreaXZPolygonOpen(List<Vector3> ringOpen)
    {
        int n = ringOpen.Count;
        double a = 0.0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += (double)ringOpen[i].x * ringOpen[j].z - (double)ringOpen[j].x * ringOpen[i].z;
        }
        return (float)a;
    }

    static float PerimeterClosedLoopXZ(List<Vector3> ringOpen)
    {
        int n = ringOpen.Count;
        float p = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = ringOpen[i];
            Vector3 b = ringOpen[(i + 1) % n];
            float dx = b.x - a.x, dz = b.z - a.z;
            p += Mathf.Sqrt(dx * dx + dz * dz);
        }
        return p;
    }

    static float MinDistanceSqPointToClosedPolylineXZ(Vector3 p, List<Vector3> ringOpen)
    {
        int n = ringOpen.Count;
        float best = float.PositiveInfinity;
        float px = p.x, pz = p.z;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = ringOpen[i];
            Vector3 b = ringOpen[(i + 1) % n];
            float d = DistanceSqPointToSegmentXZ(px, pz, a.x, a.z, b.x, b.z);
            if (d < best)
                best = d;
        }
        return best;
    }

    static float DistanceSqPointToSegmentXZ(float px, float pz, float ax, float az, float bx, float bz)
    {
        float abx = bx - ax, abz = bz - az;
        float apx = px - ax, apz = pz - az;
        float abLenSq = abx * abx + abz * abz;
        float t = abLenSq < 1e-20f ? 0f : Mathf.Clamp01((apx * abx + apz * abz) / abLenSq);
        float qx = ax + t * abx;
        float qz = az + t * abz;
        float dx = px - qx, dz = pz - qz;
        return dx * dx + dz * dz;
    }

    static float ArclengthOfClosestPointOnClosedLoopXZ(List<Vector3> ringOpen, Vector3 p)
    {
        int n = ringOpen.Count;
        float px = p.x, pz = p.z;
        float bestDsq = float.PositiveInfinity;
        float bestArc = 0f;
        float accum = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = ringOpen[i];
            Vector3 b = ringOpen[(i + 1) % n];
            float abx = b.x - a.x, abz = b.z - a.z;
            float apx = px - a.x, apz = pz - a.z;
            float abLenSq = abx * abx + abz * abz;
            float segLen = Mathf.Sqrt(abLenSq);
            float t = abLenSq < 1e-20f ? 0f : Mathf.Clamp01((apx * abx + apz * abz) / abLenSq);
            float qx = a.x + t * abx;
            float qz = a.z + t * abz;
            float dx = px - qx, dz = pz - qz;
            float dsq = dx * dx + dz * dz;
            if (dsq < bestDsq)
            {
                bestDsq = dsq;
                bestArc = accum + t * segLen;
            }
            accum += segLen;
        }
        return bestArc;
    }

    static void AppendWorldAxisAlignedRectPerimeter8(List<Vector3> dst, RectBounds b)
    {
        float y = b.y;
        Vector3 tl = new Vector3(b.minX, y, b.maxZ);
        Vector3 tr = new Vector3(b.maxX, y, b.maxZ);
        Vector3 br = new Vector3(b.maxX, y, b.minZ);
        Vector3 bl = new Vector3(b.minX, y, b.minZ);
        dst.Add(tl);
        dst.Add((tl + tr) * 0.5f);
        dst.Add(tr);
        dst.Add((tr + br) * 0.5f);
        dst.Add(br);
        dst.Add((br + bl) * 0.5f);
        dst.Add(bl);
        dst.Add((bl + tl) * 0.5f);
    }

    static List<Vector3> SpatialDedupeAverageXZ(List<Vector3> pts, float eps)
    {
        float epsSq = eps * eps;
        var clusters = new List<Vector3>();
        var counts = new List<int>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            int hit = -1;
            for (int c = 0; c < clusters.Count; c++)
            {
                Vector3 q = clusters[c];
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz <= epsSq)
                {
                    hit = c;
                    break;
                }
            }
            if (hit < 0)
            {
                clusters.Add(p);
                counts.Add(1);
            }
            else
            {
                Vector3 q = clusters[hit];
                int cn = counts[hit];
                counts[hit] = cn + 1;
                clusters[hit] = new Vector3((q.x * cn + p.x) / (cn + 1f), p.y, (q.z * cn + p.z) / (cn + 1f));
            }
        }
        return clusters;
    }
}
