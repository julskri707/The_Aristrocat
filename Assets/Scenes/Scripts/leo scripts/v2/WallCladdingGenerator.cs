using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(WallObject))]
[RequireComponent(typeof(WallCladdingRuntime))]
[DefaultExecutionOrder(3200)]
public sealed partial class WallCladdingGenerator : MonoBehaviour
{
    private static int s_GlobalRebuildSuspendCounter;
    private static int s_LastBudgetFrame = -1;
    private static int s_RebuildsThisFrame = 0;

    [Header("Profile")]
    [SerializeField] private WallCladdingProfile defaultProfile;
    [SerializeField] private bool autoRegenerate = true;

    [Header("Sides")]
    [SerializeField] private bool generateOutside = true;
    [SerializeField] private bool generateInside = false;

    [Header("Base Wall")]
    [SerializeField] private bool applyFallbackWallMaterial = true;
    [SerializeField] private bool clearWhenProfileMissing = true;

    [Header("Debug")]
    [Tooltip("Logs détaillés (rebuild, combine, garde-fous). Très verbeux si activé pendant un drag — coupé automatiquement pour ce mur pendant le déplacement.")]
    [SerializeField] private bool logDebug = false;

    // Profiler: high Rendering.Batches with one draw per stone is expected until combineGeneratedStonesPerSide is on.
    // Unity's "Material Count" in Memory/Rendering can reflect editor/session scope — use Batches/SetPass as the batching metric.
    [Header("Performance")]
    [Tooltip("In Play Mode, skip the heavy stone rebuild in OnEnable and wait a few frames — avoids freezing right after drawing a new wall.")]
    [SerializeField] private bool deferFirstCladdingRebuildInPlayMode = true;
    [SerializeField, Min(0)] private int deferFirstRebuildExtraFrames = 2;
    [SerializeField, Min(0f)] private float deferFirstRebuildExtraSeconds = 0.08f;
    [SerializeField, Min(0f)] private float minRebuildInterval = 0.18f;
    [SerializeField, Min(0f)] private float geometryHashPollInterval = 0.18f;
    [SerializeField, Min(0f)] private float rebuildAfterDragDelay = 0.08f;
    [Tooltip("Pendant le drag d’une poignée ou du pivot du lot : régénère le habillage pour que les pierres suivent le mur en direct. Désactiver pour retrouver l’ancien comportement (rebuild surtout après relâchement), moins coûteux.")]
    [SerializeField] private bool rebuildCladdingDuringHandleDrag = true;
    [Tooltip("Si activé : limite la fréquence des rebuilds pierre pendant le drag (utilise le délai ci‑dessous). Sinon : rebuild chaque frame tant que la géométrie change — pierres collées au mur, plus coûteux.")]
    [SerializeField] private bool throttleHandleDragCladdingRebuild = false;
    [Tooltip("Utilisé seulement si « throttle » ci‑dessus est activé. Sinon ignoré (intervalle 0 pendant le drag).")]
    [SerializeField, Min(0f)] private float minRebuildIntervalDuringHandleDrag = 0.08f;
    [Tooltip("Pendant le drag : génère les pierres sans CombineMeshes (souvent le coût dominant). Au relâchement, un rebuild complet refusionne le mesh.")]
    [SerializeField] private bool deferCombineMeshesDuringHandleDrag = true;
    [SerializeField, Min(1)] private int globalRebuildBudgetPerFrame = 2;
    [Tooltip("After each generated stone mesh is built on the CPU, upload it to the GPU and release the CPU copy (Mesh.UploadMeshData). Placement logic still runs on the CPU; this cuts RAM and upload overhead. Safe for cladding stones (no MeshCollider). Ignored when Combine Per Side is enabled (meshes must stay CPU-readable for CombineMeshes).")]
    [SerializeField] private bool uploadGeneratedStoneMeshesToGpu = true;
    [Tooltip("Recommended ON for shipping: merges each side into one mesh + one renderer (far fewer Batches/SetPass). Disables per-stone runtime LOD swap; per-stone tint is baked into vertex colors (see Resources/Shaders/WallStoneVertexTintLit). Far \"box\" LOD at generation is also skipped so the merged mesh keeps full stone shape. Turn OFF for editing with per-stone LOD/color.")]
    [SerializeField] private bool combineGeneratedStonesPerSide = true;
    [Tooltip("When Combine Per Side is ON: skip CPU distance/frustum/side toggles on MeshRenderers each tick — use Unity/GPU visibility instead (frustum, Hi-Z). Slightly more GPU work when off-screen; much less CPU. Shader Graph can use globals from WallCladdingGpuGlobals.")]
    [SerializeField] private bool preferGpuDrivenVisibility = true;
    [Tooltip("Push LOD / distance globals for Shader Graph (WallCladdingGpuLibrary.hlsl). Disable if this wall uses a stock material and you want zero extra SetGlobal calls.")]
    [SerializeField] private bool pushShaderGraphGpuGlobals = true;
    [Header("Camera optimization")]
    [Tooltip("When enabled, generation can skip stones too far from camera or outside frustum, and use simpler meshes for far stones.")]
    [SerializeField] private bool enableCameraStoneOptimization = true;
    [Tooltip("Skip creating stones outside the current camera frustum during rebuild. Enable only if rebuilds are frequent enough for your camera movement.")]
    [SerializeField] private bool cullOffscreenStones = false;
    [Tooltip("Skip creating stones farther than this distance from camera.")]
    [SerializeField, Min(1f)] private float stoneMaxGenerationDistance = 120f;
    [Tooltip("Use a low-detail box-like mesh for far stones.")]
    [SerializeField] private bool useFarLodMesh = true;
    [SerializeField, Min(1f)] private float stoneFarLodDistance = 7f;
    [Header("Render distance (runtime)")]
    [Tooltip("Runtime visibility tuning: frustum culling, optional wall-side culling, and either LOD mesh swap or legacy hide/decimate for merged meshes.")]
    [SerializeField] private bool enableDynamicRenderDistance = true;
    [SerializeField, Min(8f)] private float renderDistance = 140f;
    [SerializeField, Min(4f)] private float renderDistanceHysteresis = 10f;
    [SerializeField, Min(4f)] private float horizonStartDistance = 10f;
    [Range(1, 8)] [SerializeField] private int horizonDecimationStep = 3;
    [SerializeField] private bool renderDistanceUseFrustum = true;
    [Tooltip("If the camera is closer than this (m) to the renderer bounds center, never hide the wall only because it left the frustum (fast camera spins stay solid). When farther, off-screen frustum culling applies as before, together with render distance.")]
    [SerializeField, Min(0f)] private float preserveWallFrustumWithinDistance = 42f;
    [SerializeField, Min(0.02f)] private float renderDistanceUpdateInterval = 0.35f;
    [Tooltip("Distant-Horizons style: do not disable distant stones; swap each field stone to a low-triangle mesh, and end/corner quoins to a 12-triangle box matching their bounds. Ignored when Combine Per Side is on (single merged mesh).")]
    [SerializeField] private bool useDistanceLodInsteadOfDisabling = true;
    [Tooltip("Below this distance (m) from the camera, prefer the full-detail stone mesh (when hysteresis allows).")]
    [SerializeField, Min(2f)] private float lodFullDetailDistance = 6f;
    [Tooltip("Beyond this distance (m), prefer the simplified stone mesh.")]
    [SerializeField, Min(2f)] private float lodLowDetailBeyondDistance = 10f;
    [Tooltip("If both outside and inside cladding are generated, disable renderers on the side of the wall facing away from the camera.")]
    [SerializeField] private bool hideWallSideFacingAwayFromCamera = true;
    [Tooltip("Optional hard cutoff (m): hide stones beyond this even in LOD mode. 0 = disabled.")]
    [SerializeField, Min(0f)] private float hardMaxStoneRenderDistance = 0f;

    [Header("Stone mesh topology")]
    [Tooltip("When ON (default): full stone volume (rear cap + full LOD box); backs render from real geometry + material cull off — no extra triangle shell, so lighting and normal maps stay correct. When ON, the two options below are ignored.")]
    [SerializeField] private bool keepFullStoneGeometryBothSides = true;
    [Tooltip("Full-detail stones: skip the rear cap (into the wall) to save triangles. Ignored if Keep Full Stone Geometry is ON.")]
    [SerializeField] private bool fullDetailOmitBackCap = false;
    [Tooltip("Low-detail LOD: only one façade polygon. Ignored if Keep Full Stone Geometry is ON.")]
    [SerializeField] private bool lowDetailStoneFrontFaceOnly = false;
    [Tooltip("URP _Cull Off on a runtime material copy. If Keep Full Stone Geometry is OFF and stones use hollow/front-only meshes, also duplicates triangles (~2× tris) so both sides draw — that path can flatten shading; prefer keeping full geometry ON.")]
    [SerializeField] private bool forceDoubleSidedStoneMaterials = true;
    [Tooltip("Half-round terminals: higher = smoother arc (more segments). Down to ~0.12 = coarsest arc (3 segments). Field extrusions unchanged.")]
    [SerializeField, Range(0.12f, 1f)] private float stoneMeshTriangleRetention = 0.52f;
    [Tooltip("Quoin relief faces: 5 = max detail, 4 = fewer triangles with still-readable relief.")]
    [SerializeField, Range(2, 5)] private int stoneReliefFaceGrid = 4;
    [Tooltip("Field stones: OFF = carved 8-vertex silhouette (bevels). ON = cheap flat boxes (no bevels).")]
    [SerializeField] private bool useSimpleRectangularFieldStones = false;
    [Tooltip("Multiplies profile UV scale (lower = tighter tiling, richer texture/normal detail on the same mesh). 1 = profile value only.")]
    [SerializeField, Range(0.35f, 1.25f)] private float fieldStoneUvTilingBoost = 0.78f;

    [SerializeField, Min(64)] private int maxGeneratedStonesPerSide = 3000;
    [SerializeField, Min(1)] private int maxRowsPerSide = 120;
    [SerializeField, Min(8)] private int maxStonesPerRow = 128;
    [SerializeField, Min(1)] private int maxTailGapFillStonesPerRow = 48;

    [Header("Path (closed loop)")]
    [Tooltip("If the closed wall path has more corners than this, it is evenly resampled for cladding (same perimeter, fewer segments). Skipped for axis-aligned orthogonal rings so stones follow the same polyline as the wall mesh. 0 = no resampling.")]
    [SerializeField, Range(0, 256)] private int maxCladdingClosedLoopPathVertices = 64;

    [Header("Interior wall floor clearance")]
    [Tooltip("Extra bottom clearance (m) for decorative interior walls (WallEditShape.interiorWallsStayInsideLot != null) so stones do not intersect floor slabs.")]
    [SerializeField, Min(0f)] private float interiorDecorativeStoneFloorClearance = 0.035f;
    [Tooltip("Top safety margin (m) for decorative interior walls so cladding never protrudes above wall top.")]
    [SerializeField, Min(0f)] private float interiorDecorativeStoneTopClearance = 0.02f;
    [Tooltip("Top row only (decorative interior walls): vertical scale for stones. < 1 = flatter stones.")]
    [SerializeField, Range(0.55f, 1f)] private float interiorDecorativeTopRowHeightScale = 0.82f;
    [Tooltip("Top row only (decorative interior walls): horizontal scale for stones. > 1 = longer stones, fewer per row.")]
    [SerializeField, Range(1f, 2f)] private float interiorDecorativeTopRowWidthScale = 1.26f;
    [Tooltip("Decorative interior walls: remove the top cladding row entirely.")]
    [SerializeField] private bool interiorDecorativeRemoveTopRow = true;
    [Tooltip("Decorative interior walls: width scale for the row just below removed top row.")]
    [SerializeField, Range(1f, 2.4f)] private float interiorDecorativeCompensationRowWidthScale = 1.45f;

    [Header("Bundled house — upper storey (extérieur seul)")]
    [Tooltip("Pierres extérieures uniquement à partir de cette cote (m) depuis la base du mur, en coordonnées locales. " +
        "0 = toute la hauteur. Utilisé quand un seul lot source a un étage de plus que l’enveloppe basse (évite de doubler la base avec l’enveloppe).")]
    [SerializeField, Min(0f)] private float exteriorCladMinYFromWallBaseMeters = 0f;

    [Header("Connector Stones")]
    [SerializeField] private float connectorRightShift = 0.10f;
    [SerializeField] private float cornerSideExtensionMultiplier = 0f;
    [SerializeField] private float cornerFaceReferenceShift = 0.03f;
    [SerializeField] private bool alignExteriorCornerColumn = true;
    [SerializeField] private bool alignCornerLateralStack = true;
    [SerializeField] private bool invertOtherWallCornerColumn = true;
    [SerializeField] private bool growOppositeVoidLateralFace = false;
    [SerializeField] private float cornerStackColumnOffset = -0.18f;
    [SerializeField] private bool randomizeSingleCornerLateralFace = true;
    [SerializeField] private float cornerSingleFaceExtraMin = 0.02f;
    [SerializeField] private float cornerSingleFaceExtraMax = 0.60f;
    [SerializeField] private float cornerSingleFaceExtraHardCap = 0.85f;

    [Header("Corner quoin — réglage sur ce composant")]
    [Tooltip("S’ajoute au profil : décalage local pierre (m). X = mesh right (décaler le long de la face), Y = haut, Z = normale mur (ressortir).")]
    [SerializeField] private Vector3 cornerQuoinExtraLocalMeters = Vector3.zero;
    [Tooltip("S’ajoute après le local : décalage en espace monde (m), pratique pour vue de dessus (X/Z).")]
    [SerializeField] private Vector3 cornerQuoinExtraWorldMeters = Vector3.zero;

    [Header("Coin rentrant interne (~270°) — déplacement libre du quoin")]
    [Tooltip("Uniquement angles rentrants : décalage local (m). Z négatif = enfoncer dans le mur / traverser l’épaisseur ; Z positif = ressortir davantage.")]
    [SerializeField] private Vector3 reflexCornerQuoinFreeLocalMeters = Vector3.zero;
    [Tooltip("Uniquement angles rentrants : décalage monde (m), en plus du local.")]
    [SerializeField] private Vector3 reflexCornerQuoinFreeWorldMeters = Vector3.zero;

    private Vector3 _reflexCornerQuoinRuntimeLocalMeters;
    private Vector3 _reflexCornerQuoinRuntimeWorldMeters;

    private const bool forceCornerSideExtensionFromCode = false;
    private const float forcedCornerSideExtensionMultiplier = 0f;
    private const bool forceCornerStackColumnOffsetFromCode = false;
    private const float forcedCornerStackColumnOffset = -0.18f;

    [Header("Triangle Acute Bollard Geometry")]
    [SerializeField, Min(0f)] private float triangleBollardMinWallParallelLeg = 0.45f;
    [SerializeField, Min(0f)] private float triangleBollardMaxWallParallelLeg = 0.85f;
    [SerializeField] private float triangleBollardColumnCompStartAngleDeg = 25f;
    [SerializeField] private float triangleBollardColumnCompEndAngleDeg = 5f;
    [SerializeField] private float triangleBollardColumnSmallAngleScale = 2.10f;
    private float triangleBollardAngleOffsetMin = 0.45f;
    private float triangleBollardAngleOffsetMax = 0.60f;
    private float triangleBollardAngleOffsetResponse = 1.35f;
#pragma warning disable CS0414
    [SerializeField] private float triangleBollardLateralClampMinAngleDeg = 8f;
#pragma warning restore CS0414
    [SerializeField] private float triangleBollardLateralFollowMinAngleDeg = 8f;

    private WallObject wall;
    private WallEditShape wallEdit;
    private WallCladdingRuntime runtime;

    private readonly List<WallStoneModuleDefinition> allModules = new List<WallStoneModuleDefinition>(16);
    private readonly Dictionary<WallStoneModuleDefinition, int> usageCounts = new Dictionary<WallStoneModuleDefinition, int>();
    private MaterialPropertyBlock propertyBlock;

    private readonly List<QuoinRowSpan> startQuoinSpans = new List<QuoinRowSpan>(32);
    private readonly List<QuoinRowSpan> endQuoinSpans = new List<QuoinRowSpan>(32);


    private WallStoneModuleDefinition lastUsed;
    private WallStoneModuleDefinition secondLastUsed;
    private float _lastRebuildTime = -999f;
    private float _nextHashCheckTime;
    private float _dragCooldownUntil;
    private bool _wasDraggingThisWallForLiveCladding;
    private bool _needsFullCombineAfterInteractiveDrag;
    private bool _suppressStoneCombineForCurrentRebuild;
    /// <summary>
    /// Aligné sur le <c>runCombine</c> réel du dernier rebuild (peut être faux si combine désactivé faute de shader vertex tint).
    /// Sert à synchroniser MPB vs vertex colors : si ce flag est encore vrai alors qu’on ne merge pas, le MPB était sauté à tort.
    /// </summary>
    private bool _effectiveCombineStonesThisRebuild;
    private bool _warnedMissingVertexTintShaderForCombine;
    private float _nextWarnPathTooShortTime;
    private bool _waitingDeferredFirstPlayModeRebuild;
    private int _firstRebuildNotBeforeFrame;
    private float _firstRebuildNotBeforeUnscaledTime;
    private Camera _optCamera;
    private bool _hasOptCameraContext;
    private Vector3 _optCameraPos;
    private Plane[] _optFrustumPlanes;
    private readonly List<MeshRenderer> _cachedGeneratedRenderers = new List<MeshRenderer>(256);
    private bool _rendererCacheDirty = true;
    private float _nextRenderDistanceUpdateTime;
    private bool _renderDistanceApplied;
    private Material _runtimeStoneMaterialSource;
    private Material _runtimeStoneMaterialInstance;

    /// <summary>Reuse path samples for side-facing camera cull when wall geometry hash unchanged (less CPU each render-distance tick).</summary>
    private int _sideCullPathSamplesCacheHash = int.MinValue;
    private List<PathSample> _sideCullPathSamplesCache;

    private Camera _cachedMainCamera;

    /// <summary>Detected once per rebuild for closed-loop walls; drives which corner / edge rules run.</summary>
    private WallLoopShapeKind loopShapeKind = WallLoopShapeKind.Unknown;
    private readonly List<float> rectangleCornerDistances = new List<float>(8);

    /// <summary>Buffered field-stone placements for one row on closed-loop walls; seam gap is absorbed into widths before spawning.</summary>
    private readonly List<StonePlacement> _closedLoopRowPlacements = new List<StonePlacement>(64);

    private enum WallLoopShapeKind
    {
        Unknown = 0,
        OpenPolyline = 1,
        GenericClosedPolygon = 2,
        /// <summary>Exactly 4 segments, ~90° at each vertex (square or rectangle).</summary>
        Rectangle = 3,
        Triangle = 4,
        /// <summary>Many short edges, approximately constant radius from centroid (XZ).</summary>
        CircleLike = 5,
    }

    public static bool IsGlobalRebuildSuspended => s_GlobalRebuildSuspendCounter > 0;

    public static void SetGlobalRebuildSuspended(bool suspended)
    {
        if (suspended)
        {
            s_GlobalRebuildSuspendCounter++;
            return;
        }

        if (s_GlobalRebuildSuspendCounter > 0)
            s_GlobalRebuildSuspendCounter--;
    }

    private bool TryConsumeGlobalRebuildBudget()
    {
        int frame = Time.frameCount;
        if (s_LastBudgetFrame != frame)
        {
            s_LastBudgetFrame = frame;
            s_RebuildsThisFrame = 0;
        }

        int budget = Mathf.Max(1, globalRebuildBudgetPerFrame);
        if (s_RebuildsThisFrame >= budget)
            return false;

        s_RebuildsThisFrame++;
        return true;
    }

    /// <summary>
    /// Si <see cref="logDebug"/> est activé, évite d'inonder la console pendant un drag (rebuilds très fréquents sur ce mur).
    /// </summary>
    bool ShouldLogCladdingDebug()
    {
        if (!logDebug)
            return false;
        if (!ControlPointHandleUI.IsDraggingAnyHandle)
            return true;

        WallObject dragWall = ControlPointHandleUI.TryGetWallObjectForDraggedProvider();
        if (dragWall != null)
            return wall != dragWall;

        if (ControlPointHandleUI.SelectedProvider is Component mb && mb.gameObject == gameObject)
            return false;

        return true;
    }

    private float EffectiveCornerSideExtensionMultiplier()
    {
        return forceCornerSideExtensionFromCode
            ? forcedCornerSideExtensionMultiplier
            : cornerSideExtensionMultiplier;
    }

    private float EffectiveCornerStackColumnOffset()
    {
        return forceCornerStackColumnOffsetFromCode
            ? forcedCornerStackColumnOffset
            : cornerStackColumnOffset;
    }

    private float ApplyCornerLateralStackAlignment(float anchorX)
    {
        if (!alignCornerLateralStack)
            return anchorX;

        // Force both alternating corner stones to share one lateral column,
        // so rows look stacked on top of each other.
        return EffectiveCornerStackColumnOffset();
    }

    private float ResolveOtherWallColumnOffset(bool useA, float baseOffset)
    {
        if (!invertOtherWallCornerColumn)
            return baseOffset;

        // Mirror column offset for the opposite wall set (B rows).
        return useA ? baseOffset : -baseOffset;
    }

    private struct PathSample
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 tangent;
        public float length;
        public float startDistance;
        public float endDistance;
    }

    private struct WallFrame
    {
        public Vector3 centerline;
        public Vector3 tangent;
        public Vector3 faceNormal;
    }


    private struct QuoinRowSpan
    {
        public float yMin;
        public float yMax;
        public float innerLimit;
    }

    private struct StonePlacement
    {
        public WallStoneModuleDefinition module;
        public float centerDistance;
        public float centerY;
        public float width;
        public float height;
        public float depth;
        public float protrusion;
        public float embed;
        public bool useTerminalHalfRound;
        public bool terminalRoundTowardPositiveDistance;
    }

    private void Awake()
    {
        CacheRefs();
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        DestroyRuntimeStoneMaterialInstance();
    }

    private void OnEnable()
    {
        CacheRefs();

        if (Application.isPlaying && deferFirstCladdingRebuildInPlayMode && autoRegenerate && !IsGlobalRebuildSuspended)
        {
            runtime?.MarkDirty();
            _waitingDeferredFirstPlayModeRebuild = true;
            _firstRebuildNotBeforeFrame = Time.frameCount + Mathf.Max(0, deferFirstRebuildExtraFrames);
            _firstRebuildNotBeforeUnscaledTime = Time.unscaledTime + Mathf.Max(0f, deferFirstRebuildExtraSeconds);
            _nextHashCheckTime = Mathf.Max(_nextHashCheckTime, _firstRebuildNotBeforeUnscaledTime);
            return;
        }

        if (autoRegenerate && !IsGlobalRebuildSuspended)
            ForceRebuild();
        else
            runtime?.MarkDirty();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        lodFullDetailDistance = Mathf.Max(2f, lodFullDetailDistance);
        lodLowDetailBeyondDistance = Mathf.Max(lodFullDetailDistance + 0.25f, lodLowDetailBeyondDistance);
        stoneMeshTriangleRetention = Mathf.Clamp(stoneMeshTriangleRetention, 0.12f, 1f);
        stoneReliefFaceGrid = Mathf.Clamp(stoneReliefFaceGrid, 2, 5);
        fieldStoneUvTilingBoost = Mathf.Clamp(fieldStoneUvTilingBoost, 0.35f, 1.25f);
        maxCladdingClosedLoopPathVertices = Mathf.Clamp(maxCladdingClosedLoopPathVertices, 0, 256);

        // Ancien prefab avait connectorRightShift: 10 (au lieu de 0,1 m) — évite des dérives énormes sur les joints.
        if (Mathf.Abs(connectorRightShift) > 1f)
            connectorRightShift = 0.1f;

        CacheRefs();
        if (!autoRegenerate || runtime == null)
            return;

        runtime.MarkDirty();

        if (Application.isPlaying)
            return;

        // In edit mode there is no LateUpdate-driven rebuild, so serialized value tweaks
        // (like cornerSideExtensionMultiplier) must trigger an explicit refresh.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled || !autoRegenerate)
                return;

            CacheRefs();
            if (runtime != null && !IsGlobalRebuildSuspended)
                ForceRebuild();
        };
    }

    [ContextMenu("Reset corner quoin tuning (this component)")]
    private void ContextMenuResetCornerQuoinTuning()
    {
        cornerQuoinExtraLocalMeters = Vector3.zero;
        cornerQuoinExtraWorldMeters = Vector3.zero;
        reflexCornerQuoinFreeLocalMeters = Vector3.zero;
        reflexCornerQuoinFreeWorldMeters = Vector3.zero;
        ClearReflexCornerQuoinRuntimeFreeOffset();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        CacheRefs();
        runtime?.MarkDirty();
        if (!Application.isPlaying && autoRegenerate && !IsGlobalRebuildSuspended)
            ForceRebuild();
    }
#endif

    private void LateUpdate()
    {
        if (Application.isPlaying)
        {
            WallCladdingGpuGlobals.PushOncePerFrame();
            if (pushShaderGraphGpuGlobals)
            {
                float lodNear = Mathf.Max(2f, lodFullDetailDistance);
                float lodFar = Mathf.Max(lodNear + 0.5f, lodLowDetailBeyondDistance);
                float maxDist = Mathf.Max(8f, renderDistance);
                float hStart = Mathf.Clamp(horizonStartDistance, 0f, maxDist);
                float hyster = Mathf.Max(0f, renderDistanceHysteresis);
                WallCladdingGpuGlobals.PushGeneratorParams(lodNear, lodFar, maxDist, hStart, hyster);
            }
        }

        UpdateRuntimeRenderDistance(false);

        if (!autoRegenerate)
            return;

        float now = Time.unscaledTime;
        bool draggingThisWall = false;
        bool willDeferCombine = rebuildCladdingDuringHandleDrag && deferCombineMeshesDuringHandleDrag && combineGeneratedStonesPerSide;

        // Only the wall being edited should go dirty during handle drag — not every WallCladdingGenerator in the scene.
        if (ControlPointHandleUI.IsDraggingAnyHandle)
        {
            CacheRefs();
            if (wall == null || runtime == null)
                return;

            WallObject dragWall = ControlPointHandleUI.TryGetWallObjectForDraggedProvider();
            if (dragWall != null)
            {
                if (wall != dragWall)
                {
                    _wasDraggingThisWallForLiveCladding = false;
                    return;
                }

                draggingThisWall = true;
            }
            else if (ControlPointHandleUI.SelectedProvider is Component mb && mb.gameObject != gameObject)
            {
                _wasDraggingThisWallForLiveCladding = false;
                return;
            }
            else
                draggingThisWall = true;

            runtime.MarkDirty();
            if (!rebuildCladdingDuringHandleDrag)
            {
                _wasDraggingThisWallForLiveCladding = false;
                _dragCooldownUntil = now + Mathf.Max(0f, rebuildAfterDragDelay);
                return;
            }
        }
        else
        {
            if (_wasDraggingThisWallForLiveCladding && willDeferCombine)
            {
                CacheRefs();
                if (runtime != null)
                {
                    runtime.MarkDirty();
                    _needsFullCombineAfterInteractiveDrag = true;
                }
            }

            if (now < _dragCooldownUntil)
                return;
        }

        _wasDraggingThisWallForLiveCladding = draggingThisWall && willDeferCombine;

        if (!draggingThisWall && !_needsFullCombineAfterInteractiveDrag)
        {
            if (now < _nextHashCheckTime)
                return;
            _nextHashCheckTime = now + geometryHashPollInterval;
        }

        CacheRefs();
        if (wall == null || runtime == null)
            return;

        if (IsGlobalRebuildSuspended)
        {
            runtime.MarkDirty();
            return;
        }

        WallCladdingProfile profile = runtime.CurrentProfile != null ? runtime.CurrentProfile : defaultProfile;
        if (profile == null)
        {
            if (clearWhenProfileMissing)
                ClearGenerated();
            return;
        }

        if (runtime.IsDirty)
        {
            if (_waitingDeferredFirstPlayModeRebuild)
            {
                if (Time.frameCount < _firstRebuildNotBeforeFrame || now < _firstRebuildNotBeforeUnscaledTime)
                    return;
            }

            int nextHash = ComputeGeometryHash();
            if (nextHash == runtime.LastGeometryHash && !_needsFullCombineAfterInteractiveDrag)
            {
                runtime.MarkClean();
                _waitingDeferredFirstPlayModeRebuild = false;
                return;
            }

            float minIv = draggingThisWall
                ? (throttleHandleDragCladdingRebuild ? minRebuildIntervalDuringHandleDrag : 0f)
                : minRebuildInterval;
            if (!_needsFullCombineAfterInteractiveDrag && now - _lastRebuildTime < minIv)
                return;

            if (!draggingThisWall && !_needsFullCombineAfterInteractiveDrag && !TryConsumeGlobalRebuildBudget())
                return;

            if (ShouldLogCladdingDebug())
                Debug.Log($"[WallCladdingGenerator] LateUpdate rebuild on {name} (dirty={runtime.IsDirty})", this);

            _suppressStoneCombineForCurrentRebuild = draggingThisWall && willDeferCombine;
            ForceRebuildWithHash(nextHash);
            _suppressStoneCombineForCurrentRebuild = false;
            _waitingDeferredFirstPlayModeRebuild = false;
        }
    }

    public void ForceRebuild()
    {
        ForceRebuildWithHash(null);
    }

    void ForceRebuildWithHash(int? precomputedGeometryHash)
    {
        CacheRefs();
        if (wall == null || runtime == null)
            return;

        if (IsGlobalRebuildSuspended)
        {
            runtime.MarkDirty();
            return;
        }

        WallCladdingProfile profile = runtime.CurrentProfile != null ? runtime.CurrentProfile : defaultProfile;
        if (profile == null)
        {
            if (ShouldLogCladdingDebug())
                Debug.LogWarning("[WallCladdingGenerator] No profile assigned.", this);

            if (clearWhenProfileMissing)
                ClearGenerated();

            return;
        }

        runtime.SetProfile(profile, runtime.CurrentSeed != 0 ? runtime.CurrentSeed : ComputeStableSeed(profile));
        ApplyFallbackMaterial(profile);
        profile = CreateRuntimeScaledProfileIfNeeded(profile);

        List<Vector3> path = GetWallPath();
        if (path == null || path.Count < 2)
        {
            if (ShouldLogCladdingDebug() && Time.unscaledTime >= _nextWarnPathTooShortTime)
            {
                Debug.LogWarning("[WallCladdingGenerator] Wall path is null or too short.", this);
                _nextWarnPathTooShortTime = Time.unscaledTime + 1f;
            }

            ClearGenerated();
            return;
        }

        List<PathSample> samples = BuildPathSamples(path);
        if (samples.Count == 0)
        {
            if (ShouldLogCladdingDebug())
                Debug.LogWarning("[WallCladdingGenerator] No valid path samples.", this);

            ClearGenerated();
            return;
        }

        GatherModules(profile);
        if (allModules.Count == 0)
        {
            if (ShouldLogCladdingDebug())
                Debug.LogWarning("[WallCladdingGenerator] No stone modules found in profile.", this);

            ClearGenerated();
            return;
        }

        ResetUsage();
        runtime.ClearRoot(true);
        runtime.ClearRoot(false);

        System.Random rng = new System.Random(runtime.CurrentSeed);
        RefreshCameraOptimizationContext();

        Profiler.BeginSample("WallCladding.GenerateAndCombine");

        bool runCombine = combineGeneratedStonesPerSide && !_suppressStoneCombineForCurrentRebuild;
        if (runCombine && !CanCombineWithPerStoneTint())
            runCombine = false;

        _effectiveCombineStonesThisRebuild = runCombine;

        if (generateOutside)
        {
            GenerateStoneSide(profile, samples, true, +1f, rng);
            if (runCombine)
                TryCombineGeneratedStonesForRoot(runtime.GetOrCreateRoot(true));
        }

        if (generateInside)
        {
            GenerateStoneSide(profile, samples, false, -1f, rng);
            if (runCombine)
                TryCombineGeneratedStonesForRoot(runtime.GetOrCreateRoot(false));
        }

        Profiler.EndSample();

        runtime.LastGeometryHash = precomputedGeometryHash ?? ComputeGeometryHash();
        runtime.MarkClean();
        _lastRebuildTime = Time.unscaledTime;
        if (runCombine)
            _needsFullCombineAfterInteractiveDrag = false;

        if (ShouldLogCladdingDebug())
            Debug.Log($"[WallCladdingGenerator] Rebuild OK on {name} (cornerSideExtensionMultiplier={EffectiveCornerSideExtensionMultiplier():0.###}, cornerStackColumnOffset={EffectiveCornerStackColumnOffset():0.###})", this);

        _rendererCacheDirty = true;
        UpdateRuntimeRenderDistance(true);
    }

    public void MarkDirty()
    {
        CacheRefs();
        runtime?.MarkDirty();
    }

    public void SetExteriorCladdingMinHeightFromWallBaseMeters(float meters)
    {
        exteriorCladMinYFromWallBaseMeters = Mathf.Max(0f, meters);
        if (runtime != null)
            runtime.LastGeometryHash = int.MinValue;
        MarkDirty();
    }

    public void ClearExteriorCladdingMinHeightFromWallBaseMeters()
    {
        exteriorCladMinYFromWallBaseMeters = 0f;
        if (runtime != null)
            runtime.LastGeometryHash = int.MinValue;
        MarkDirty();
    }

    bool CanCombineWithPerStoneTint()
    {
        Shader vertexTintShader = Resources.Load<Shader>("Shaders/WallStoneVertexTintLit");
        if (vertexTintShader == null)
            vertexTintShader = Shader.Find("TinyGlade/WallStoneVertexTintLit");

        if (vertexTintShader != null)
            return true;

        if (!_warnedMissingVertexTintShaderForCombine && ShouldLogCladdingDebug())
        {
            _warnedMissingVertexTintShaderForCombine = true;
            Debug.LogWarning(
                "[WallCladdingGenerator] Vertex tint shader missing; combine-per-side disabled to preserve per-stone tint.",
                this);
        }

        return false;
    }

    /// <summary>
    /// Réactive le habillage pierre standard (extérieur) et déclenche une régénération.
    /// Utile après des fusions anciennes qui avaient coupé <c>autoRegenerate</c>/<c>generateOutside</c>.
    /// Appelle <see cref="ForceRebuild"/> en éditeur et en jeu : le rebuild différé via <c>LateUpdate</c>
    /// peut sinon être sauté (hash inchangé, intervalle, budget, premier frame play).
    /// </summary>
    public void EnsureStoneCladdingEnabledAndRefresh()
    {
        CacheRefs();
        autoRegenerate = true;
        generateOutside = true;
        if (!enabled)
            enabled = true;

        // Débloquer les garde-fous qui empêchent un rebuild juste après fusion / auto-connect.
        _waitingDeferredFirstPlayModeRebuild = false;
        _dragCooldownUntil = 0f;
        _nextHashCheckTime = 0f;
        _lastRebuildTime = -999f;

        if (runtime != null)
        {
            runtime.MarkDirty();
            runtime.LastGeometryHash = int.MinValue;
        }

        if (IsGlobalRebuildSuspended)
            return;

        ForceRebuild();
    }

    float GetEffectiveBuildingScale()
    {
        WallBuildController controller = FindFirstObjectByType<WallBuildController>(FindObjectsInactive.Include);
        return controller != null ? Mathf.Max(0.01f, controller.GetEffectiveBuildingScale()) : 1f;
    }

    WallCladdingProfile CreateRuntimeScaledProfileIfNeeded(WallCladdingProfile source)
    {
        float scale = GetEffectiveBuildingScale();
        if (source == null || Mathf.Approximately(scale, 1f))
            return source;

        WallCladdingProfile p = ScriptableObject.CreateInstance<WallCladdingProfile>();
        p.hideFlags = HideFlags.DontSave;
        p.profileId = source.profileId;
        p.displayName = source.displayName + " (BuildingScale)";
        p.icon = source.icon;
        p.mode = source.mode;
        p.fallbackWallMaterial = source.fallbackWallMaterial;
        p.stoneMaterial = source.stoneMaterial;
        p.general = CopyScaledGeneral(source.general, scale);
        p.stone = CopyScaledStone(source.stone, scale);
        p.brick = source.brick;
        CopyScaledModules(source.stoneLargeModules, p.stoneLargeModules, scale);
        CopyScaledModules(source.stoneMediumModules, p.stoneMediumModules, scale);
        CopyScaledModules(source.stoneSmallModules, p.stoneSmallModules, scale);
        Debug.Log($"[BuildingScale] wall cladding will use scale={scale:F3}", this);
        return p;
    }

    static WallCladdingGeneralSettings CopyScaledGeneral(WallCladdingGeneralSettings s, float scale)
    {
        if (s == null)
            return new WallCladdingGeneralSettings();
        return new WallCladdingGeneralSettings
        {
            sideInset = s.sideInset * scale,
            depthOffset = s.depthOffset * scale,
            randomSeedOffset = s.randomSeedOffset
        };
    }

    static StoneCladdingSettings CopyScaledStone(StoneCladdingSettings s, float scale)
    {
        if (s == null)
            return new StoneCladdingSettings();

        StoneCladdingSettings d = new StoneCladdingSettings
        {
            targetRowHeight = s.targetRowHeight * scale,
            rowHeightJitter = s.rowHeightJitter,
            horizontalSpacing = s.horizontalSpacing * scale,
            verticalSpacing = s.verticalSpacing * scale,
            staggerFraction = s.staggerFraction,
            minStoneWidth = s.minStoneWidth * scale,
            maxStoneWidth = s.maxStoneWidth * scale,
            minStoneHeight = s.minStoneHeight * scale,
            maxStoneHeight = s.maxStoneHeight * scale,
            minWidthVsHeight = s.minWidthVsHeight,
            maxWidthVsHeight = s.maxWidthVsHeight,
            nearCornerMaxWidthVsHeight = s.nearCornerMaxWidthVsHeight,
            embedDepth = s.embedDepth * scale,
            surfaceProtrusion = s.surfaceProtrusion * scale,
            minStoneDepth = s.minStoneDepth * scale,
            maxStoneDepth = s.maxStoneDepth * scale,
            widthJitter = s.widthJitter,
            heightJitter = s.heightJitter,
            depthJitter = s.depthJitter,
            scaleJitter = s.scaleJitter,
            minWidthScale = s.minWidthScale,
            maxWidthScale = s.maxWidthScale,
            minHeightScale = s.minHeightScale,
            maxHeightScale = s.maxHeightScale,
            minDepthScale = s.minDepthScale,
            maxDepthScale = s.maxDepthScale,
            maxScaleAspectRatio = s.maxScaleAspectRatio,
            positionJitter = s.positionJitter * scale,
            randomYaw = s.randomYaw,
            randomPitch = s.randomPitch,
            randomRoll = s.randomRoll,
            smallStoneFillChance = s.smallStoneFillChance,
            preferSmallModulesNearCorners = s.preferSmallModulesNearCorners,
            cornerSmallModuleZone = s.cornerSmallModuleZone * scale,
            minRowUsableWidth = s.minRowUsableWidth * scale,
            endGapTolerance = s.endGapTolerance * scale,
            rejectSliverGapBelow = s.rejectSliverGapBelow * scale,
            facePlaneJitter = s.facePlaneJitter * scale,
            uvMetersPerUnit = s.uvMetersPerUnit * scale,
            enablePerStoneColorVariation = s.enablePerStoneColorVariation,
            hueJitter = s.hueJitter,
            saturationJitter = s.saturationJitter,
            valueJitter = s.valueJitter,
            uvOffsetJitter = s.uvOffsetJitter,
            baseTint = s.baseTint,
            useSeparateTintForQuoins = s.useSeparateTintForQuoins,
            quoinBaseTint = s.quoinBaseTint,
            endQuoins = CopyScaledEndQuoins(s.endQuoins, scale)
        };
        return d;
    }

    static EndQuoinSettings CopyScaledEndQuoins(EndQuoinSettings s, float scale)
    {
        if (s == null)
            return new EndQuoinSettings();
        return new EndQuoinSettings
        {
            enabled = s.enabled,
            reserveWidth = s.reserveWidth * scale,
            targetHeight = s.targetHeight * scale,
            rowHeightJitter = s.rowHeightJitter,
            minLength = s.minLength * scale,
            maxLength = s.maxLength * scale,
            lengthJitter = s.lengthJitter,
            extraOutsideDepth = s.extraOutsideDepth * scale,
            alternateShortScale = s.alternateShortScale,
            alternateLongScale = s.alternateLongScale,
            edgeInset = s.edgeInset * scale,
            verticalSpacing = s.verticalSpacing * scale,
            cornerLDepthMul = s.cornerLDepthMul,
            cornerQuoinOutwardOffsetMeters = s.cornerQuoinOutwardOffsetMeters * scale,
            cornerQuoinLocalOffsetMeters = s.cornerQuoinLocalOffsetMeters * scale,
            reflexCornerQuoinOutwardOffsetMeters = s.reflexCornerQuoinOutwardOffsetMeters * scale,
            reflexCornerQuoinLocalOffsetMeters = s.reflexCornerQuoinLocalOffsetMeters * scale,
            useGridRightAngleCornerQuoins = s.useGridRightAngleCornerQuoins
        };
    }

    static void CopyScaledModules(List<WallStoneModuleDefinition> source, List<WallStoneModuleDefinition> dest, float scale)
    {
        if (source == null || dest == null)
            return;
        for (int i = 0; i < source.Count; i++)
        {
            WallStoneModuleDefinition s = source[i];
            if (s == null)
                continue;
            WallStoneModuleDefinition d = ScriptableObject.CreateInstance<WallStoneModuleDefinition>();
            d.hideFlags = HideFlags.DontSave;
            d.displayName = s.displayName;
            d.sizeClass = s.sizeClass;
            d.weight = s.weight;
            d.probability = s.probability;
            d.canUseNearCorners = s.canUseNearCorners;
            d.preferAsGapFiller = s.preferAsGapFiller;
            d.minWidthToHeight = s.minWidthToHeight;
            d.maxWidthToHeight = s.maxWidthToHeight;
            d.minCornerCut = s.minCornerCut;
            d.maxCornerCut = s.maxCornerCut;
            d.frontRelief = s.frontRelief * scale;
            d.depthMultiplier = s.depthMultiplier;
            d.verticalEdgeLean = s.verticalEdgeLean;
            d.horizontalEdgeLean = s.horizontalEdgeLean;
            dest.Add(d);
        }
    }

    /// <summary>
    /// Sends mesh data to the GPU and optionally drops the CPU copy. Does not replace CPU-side generation.
    /// Call only after all CPU edits to that mesh are finished (including debug color splits).
    /// </summary>
    void FinalizeGeneratedMeshForGpu(Mesh mesh)
    {
        if (mesh == null || mesh.vertexCount <= 0)
            return;

        // Only duplicate geometry when the mesh can be one-sided (hollow / front-only LOD). Full-volume stones use real back faces + _Cull Off — doubling would stack two shells and break normals/tangents (flat shading).
        if (ShouldDuplicateStoneTrianglesForRendering() && mesh.subMeshCount <= 1)
            EnsureMeshDoubleSided(mesh);

        if (combineGeneratedStonesPerSide)
            return;

        if (!uploadGeneratedStoneMeshesToGpu || !Application.isPlaying)
            return;

        mesh.UploadMeshData(true);
    }

    bool IncludeStoneBackCapInExtrusion() =>
        keepFullStoneGeometryBothSides || !fullDetailOmitBackCap;

    bool UseLowDetailFrontFaceOnlyNow() =>
        !keepFullStoneGeometryBothSides && lowDetailStoneFrontFaceOnly;

    bool ShouldDuplicateStoneTrianglesForRendering() =>
        forceDoubleSidedStoneMaterials && !keepFullStoneGeometryBothSides;

    /// <summary>Same conditions as per-stone LOD on field stones: runtime mesh swap when not merging per side.</summary>
    bool ShouldAttachPerStoneRuntimeLod() =>
        Application.isPlaying
        && useDistanceLodInsteadOfDisabling
        && !combineGeneratedStonesPerSide
        && enableDynamicRenderDistance;

    /// <summary>
    /// Axis-aligned box (6 quads, 12 tris) matching the high mesh AABB in local space — used for end/corner quoins at distance.
    /// </summary>
    Mesh BuildQuoinRuntimeLodBoxMesh(Mesh highDetailMesh, float uvMetersPerUnit)
    {
        if (highDetailMesh == null || highDetailMesh.vertexCount <= 0)
            return null;

        highDetailMesh.RecalculateBounds();
        Bounds b = highDetailMesh.bounds;
        Vector3 mn = b.min;
        Vector3 mx = b.max;
        if (b.size.x < 1e-5f || b.size.y < 1e-5f || b.size.z < 1e-5f)
            return null;

        var verts = new List<Vector3>(24);
        var tris = new List<int>(36);
        var uvs = new List<Vector2>(24);

        // Wind each face CCW when viewed from outside (Unity default front face).
        AddQuad(verts, tris, uvs,
            new Vector3(mx.x, mn.y, mn.z),
            new Vector3(mx.x, mx.y, mn.z),
            new Vector3(mx.x, mx.y, mx.z),
            new Vector3(mx.x, mn.y, mx.z),
            uvMetersPerUnit);
        AddQuad(verts, tris, uvs,
            new Vector3(mn.x, mn.y, mx.z),
            new Vector3(mn.x, mx.y, mx.z),
            new Vector3(mn.x, mx.y, mn.z),
            new Vector3(mn.x, mn.y, mn.z),
            uvMetersPerUnit);
        AddQuad(verts, tris, uvs,
            new Vector3(mn.x, mx.y, mn.z),
            new Vector3(mn.x, mx.y, mx.z),
            new Vector3(mx.x, mx.y, mx.z),
            new Vector3(mx.x, mx.y, mn.z),
            uvMetersPerUnit);
        AddQuad(verts, tris, uvs,
            new Vector3(mn.x, mn.y, mn.z),
            new Vector3(mx.x, mn.y, mn.z),
            new Vector3(mx.x, mn.y, mx.z),
            new Vector3(mn.x, mn.y, mx.z),
            uvMetersPerUnit);
        AddQuad(verts, tris, uvs,
            new Vector3(mn.x, mn.y, mx.z),
            new Vector3(mx.x, mn.y, mx.z),
            new Vector3(mx.x, mx.y, mx.z),
            new Vector3(mn.x, mx.y, mx.z),
            uvMetersPerUnit);
        AddQuad(verts, tris, uvs,
            new Vector3(mx.x, mn.y, mn.z),
            new Vector3(mn.x, mn.y, mn.z),
            new Vector3(mn.x, mx.y, mn.z),
            new Vector3(mx.x, mx.y, mn.z),
            uvMetersPerUnit);

        var mesh = new Mesh { name = "QuoinRuntimeLodBox" };
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void AttachQuoinRuntimeLodIfEnabled(GameObject go, MeshFilter mf, Mesh meshHigh, float uvMetersPerUnit)
    {
        if (mf == null || meshHigh == null)
            return;

        if (!ShouldAttachPerStoneRuntimeLod())
        {
            FinalizeGeneratedMeshForGpu(meshHigh);
            return;
        }

        Mesh meshLow = BuildQuoinRuntimeLodBoxMesh(meshHigh, uvMetersPerUnit);
        if (meshLow == null || meshLow.vertexCount <= 0)
        {
            DestroyObjectSafe(meshLow);
            FinalizeGeneratedMeshForGpu(meshHigh);
            return;
        }

        var lod = go.AddComponent<WallCladdingStoneLod>();
        lod.Initialize(mf, meshHigh, meshLow);
        FinalizeGeneratedMeshForGpu(meshHigh);
        FinalizeGeneratedMeshForGpu(meshLow);
    }

    /// <summary>Duplicates all triangles with reversed winding and a second vertex copy with negated normals (solid from both sides).</summary>
    static void EnsureMeshDoubleSided(Mesh mesh)
    {
        if (mesh == null)
            return;

        int[] tris = mesh.triangles;
        if (tris == null || tris.Length < 3)
            return;

        Vector3[] verts = mesh.vertices;
        if (verts == null || verts.Length == 0)
            return;

        Vector2[] uv = mesh.uv;
        if (uv == null || uv.Length != verts.Length)
            uv = new Vector2[verts.Length];

        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != verts.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        int vCount = verts.Length;
        int tCount = tris.Length;

        var dsVerts = new Vector3[vCount * 2];
        var dsUv = new Vector2[vCount * 2];
        var dsNormals = new Vector3[vCount * 2];

        for (int i = 0; i < vCount; i++)
        {
            dsVerts[i] = verts[i];
            dsUv[i] = uv[i];
            dsNormals[i] = normals[i];

            int bi = i + vCount;
            dsVerts[bi] = verts[i];
            dsUv[bi] = uv[i];
            dsNormals[bi] = -normals[i];
        }

        var dsTris = new int[tCount * 2];
        for (int i = 0; i < tCount; i++)
            dsTris[i] = tris[i];

        int w = tCount;
        for (int i = 0; i < tCount; i += 3)
        {
            int a = tris[i] + vCount;
            int b = tris[i + 1] + vCount;
            int c = tris[i + 2] + vCount;
            dsTris[w++] = c;
            dsTris[w++] = b;
            dsTris[w++] = a;
        }

        mesh.Clear();
        if (dsVerts.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = dsVerts;
        mesh.uv = dsUv;
        mesh.normals = dsNormals;
        mesh.triangles = dsTris;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
    }

    void DestroyRuntimeStoneMaterialInstance()
    {
        if (_runtimeStoneMaterialInstance == null)
        {
            _runtimeStoneMaterialSource = null;
            return;
        }

        if (Application.isPlaying)
            Destroy(_runtimeStoneMaterialInstance);
        else
            DestroyImmediate(_runtimeStoneMaterialInstance);

        _runtimeStoneMaterialInstance = null;
        _runtimeStoneMaterialSource = null;
    }

    /// <summary>
    /// Stone material for generation: optional runtime instance with double-sided culling (URP-style _Cull).
    /// </summary>
    Material ResolveStoneMaterialForCladding(WallCladdingProfile profile)
    {
        Material src = profile != null && profile.stoneMaterial != null ? profile.stoneMaterial : profile?.fallbackWallMaterial;
        if (src == null)
        {
            DestroyRuntimeStoneMaterialInstance();
            return null;
        }

        if (!forceDoubleSidedStoneMaterials)
        {
            DestroyRuntimeStoneMaterialInstance();
            return src;
        }

        if (_runtimeStoneMaterialInstance != null && _runtimeStoneMaterialSource == src)
            return _runtimeStoneMaterialInstance;

        DestroyRuntimeStoneMaterialInstance();
        _runtimeStoneMaterialSource = src;
        _runtimeStoneMaterialInstance = new Material(src);
        ApplyMaterialDoubleSided(_runtimeStoneMaterialInstance);
        return _runtimeStoneMaterialInstance;
    }

    static readonly int CullPropertyId = Shader.PropertyToID("_Cull");
    static readonly int CullModePropertyId = Shader.PropertyToID("_CullMode");

    static void ApplyMaterialDoubleSided(Material mat)
    {
        if (mat == null)
            return;

        int cullOff = (int)CullMode.Off;
        // URP Lit: _Cull 0 = Off (both sides). Use PropertyToID + Int+Float so batching/variants pick it up reliably.
        if (mat.HasProperty(CullPropertyId))
        {
            mat.SetInt(CullPropertyId, cullOff);
            mat.SetFloat(CullPropertyId, cullOff);
        }

        if (mat.HasProperty(CullModePropertyId))
        {
            mat.SetInt(CullModePropertyId, cullOff);
            mat.SetFloat(CullModePropertyId, cullOff);
        }

        mat.doubleSidedGI = true;
        mat.enableInstancing = true;
    }

    void RefreshCameraOptimizationContext()
    {
        _hasOptCameraContext = false;
        if (!enableCameraStoneOptimization)
            return;

        if (_cachedMainCamera == null || !_cachedMainCamera.isActiveAndEnabled)
            _cachedMainCamera = Camera.main;

        _optCamera = _cachedMainCamera;
        if (_optCamera == null)
            return;

        _optCameraPos = _optCamera.transform.position;
        _hasOptCameraContext = true;

        if (cullOffscreenStones)
        {
            if (_optFrustumPlanes == null || _optFrustumPlanes.Length != 6)
                _optFrustumPlanes = new Plane[6];
            GeometryUtility.CalculateFrustumPlanes(_optCamera, _optFrustumPlanes);
        }
    }

    bool ShouldSkipStoneFromCamera(Vector3 worldCenter, float approxRadius)
    {
        if (!enableCameraStoneOptimization || !_hasOptCameraContext)
            return false;

        float dist = Vector3.Distance(_optCameraPos, worldCenter);
        if (!useDistanceLodInsteadOfDisabling)
        {
            float maxDist = Mathf.Max(1f, stoneMaxGenerationDistance);
            if (dist > maxDist)
                return true;
        }

        if (cullOffscreenStones && _optFrustumPlanes != null)
        {
            float size = Mathf.Max(0.12f, approxRadius * 2f);
            Bounds b = new Bounds(worldCenter, new Vector3(size, size, size));
            if (!GeometryUtility.TestPlanesAABB(_optFrustumPlanes, b))
            {
                if (preserveWallFrustumWithinDistance <= 0f || dist > preserveWallFrustumWithinDistance)
                    return true;
            }
        }

        return false;
    }

    bool ShouldUseFarLod(Vector3 worldCenter)
    {
        if (!enableCameraStoneOptimization || !useFarLodMesh || !_hasOptCameraContext)
            return false;

        // Merged side = one mesh, no per-stone runtime LOD swap. Baking far "box" stones at gen time
        // (camera-dependent) permanently uglifies the combined mesh; keep full detail and rely on batching.
        if (combineGeneratedStonesPerSide)
            return false;

        return Vector3.Distance(_optCameraPos, worldCenter) >= Mathf.Max(1f, stoneFarLodDistance);
    }

    private void CacheRefs()
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (wallEdit == null)
            wallEdit = GetComponent<WallEditShape>();

        if (runtime == null)
            runtime = GetComponent<WallCladdingRuntime>();

        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
    }

    float GetEffectiveBottomInset(WallCladdingProfile profile)
    {
        float yMin = Mathf.Max(0f, profile.general.sideInset);
        if (wallEdit != null && wallEdit.interiorWallsStayInsideLot != null)
            yMin += Mathf.Max(0f, interiorDecorativeStoneFloorClearance);
        return yMin;
    }

    float GetEffectiveTopLimit(WallCladdingProfile profile, float yMin)
    {
        float wallHeight = Mathf.Max(0.1f, wall != null ? wall.height : 2.5f);
        float yMax = Mathf.Max(yMin + 0.05f, wallHeight - profile.general.sideInset);

        // Toit : la semelle du maillage commence au-dessus du « haut mur » (lift + offset). Sans prolonger la pierre,
        // un vide horizontal reste visible entre la dernière assise et la sous-face du toit.
        if (!IsInteriorDecorativeWall() && wall != null)
        {
            HouseRoofSystem roof = wall.GetComponent<HouseRoofSystem>();
            if (roof != null)
                yMax += HouseRoofSystem.RoofBuiltInVerticalLiftMeters + Mathf.Max(0f, roof.yOffsetAboveWallTop);
        }

        if (IsInteriorDecorativeWall())
            yMax = Mathf.Max(yMin + 0.05f, yMax - Mathf.Max(0f, interiorDecorativeStoneTopClearance));
        return yMax;
    }

    bool IsInteriorDecorativeWall()
    {
        return wallEdit != null && wallEdit.interiorWallsStayInsideLot != null;
    }

    float GetInteriorDecorativeMaxStoneEmbed()
    {
        // Keep decorative stones anchored on one face only: never let them cross through the wall thickness.
        float t = Mathf.Max(0.01f, wall != null ? wall.thickness : 0.25f);
        return Mathf.Max(0.012f, t * 0.42f);
    }

    /// <summary>Smaller value → more UV repeats per meter → richer albedo/normal read without extra geometry.</summary>
    float GetEffectiveUvMetersPerUnit(WallCladdingProfile profile)
    {
        float baseUv = profile != null ? profile.stone.uvMetersPerUnit : 0.42f;
        return Mathf.Max(0.05f, baseUv * fieldStoneUvTilingBoost);
    }

    private void ClearGenerated()
    {
        if (runtime == null)
            return;

        runtime.ClearRoot(true);
        runtime.ClearRoot(false);
        runtime.MarkClean();
        _rendererCacheDirty = true;
        _renderDistanceApplied = false;
        _sideCullPathSamplesCacheHash = int.MinValue;
        _sideCullPathSamplesCache = null;
    }

    void UpdateRuntimeRenderDistance(bool force)
    {
        if (!Application.isPlaying)
            return;

        if (combineGeneratedStonesPerSide && preferGpuDrivenVisibility)
        {
            if (_renderDistanceApplied)
            {
                EnsureGeneratedRendererCache();
                for (int i = 0; i < _cachedGeneratedRenderers.Count; i++)
                {
                    MeshRenderer mr = _cachedGeneratedRenderers[i];
                    if (mr != null && !mr.enabled)
                        mr.enabled = true;
                }
                _renderDistanceApplied = false;
            }
            return;
        }

        if (!enableDynamicRenderDistance)
        {
            if (_renderDistanceApplied)
            {
                EnsureGeneratedRendererCache();
                for (int i = 0; i < _cachedGeneratedRenderers.Count; i++)
                {
                    MeshRenderer mr = _cachedGeneratedRenderers[i];
                    if (mr != null && !mr.enabled)
                        mr.enabled = true;
                }
                _renderDistanceApplied = false;
            }
            return;
        }

        float now = Time.unscaledTime;
        if (!force && now < _nextRenderDistanceUpdateTime)
            return;
        _nextRenderDistanceUpdateTime = now + Mathf.Max(0.02f, renderDistanceUpdateInterval);

        if (_cachedMainCamera == null || !_cachedMainCamera.isActiveAndEnabled)
            _cachedMainCamera = Camera.main;

        Camera cam = _cachedMainCamera;
        if (cam == null)
            return;

        EnsureGeneratedRendererCache();
        if (_cachedGeneratedRenderers.Count == 0)
            return;

        Vector3 camPos = cam.transform.position;
        float maxDist = Mathf.Max(8f, renderDistance);
        float hyster = Mathf.Max(0f, renderDistanceHysteresis);
        float horizonStart = Mathf.Clamp(horizonStartDistance, 0f, maxDist);
        int step = Mathf.Clamp(horizonDecimationStep, 1, 8);
        float hardMax = Mathf.Max(0f, hardMaxStoneRenderDistance);
        float lodNear = Mathf.Max(2f, lodFullDetailDistance);
        float lodFar = Mathf.Max(lodNear + 0.5f, lodLowDetailBeyondDistance);

        Plane[] planes = null;
        if (renderDistanceUseFrustum)
            planes = GeometryUtility.CalculateFrustumPlanes(cam);

        bool applySideCull = hideWallSideFacingAwayFromCamera
            && generateOutside
            && generateInside
            && runtime != null
            && runtime.OutsideRoot != null
            && runtime.InsideRoot != null;

        bool cameraSeesOutside = true;
        if (applySideCull && runtime != null)
        {
            int gh = runtime.LastGeometryHash;
            if (_sideCullPathSamplesCache == null || _sideCullPathSamplesCacheHash != gh)
            {
                List<Vector3> path = GetWallPath();
                _sideCullPathSamplesCache = path != null && path.Count >= 2 ? BuildPathSamples(path) : null;
                _sideCullPathSamplesCacheHash = gh;
            }

            if (_sideCullPathSamplesCache != null
                && TryClosestDistanceAlongPath(_sideCullPathSamplesCache, camPos, out float dAlong))
            {
                WallFrame frame = GetFrameAtDistance(_sideCullPathSamplesCache, dAlong, +1f);
                Vector3 toCam = camPos - frame.centerline;
                cameraSeesOutside = Vector3.Dot(frame.faceNormal, toCam) > 0f;
            }
        }

        bool usePerStoneLod = useDistanceLodInsteadOfDisabling && !combineGeneratedStonesPerSide;

        for (int i = 0; i < _cachedGeneratedRenderers.Count; i++)
        {
            MeshRenderer mr = _cachedGeneratedRenderers[i];
            if (mr == null)
                continue;

            if (applySideCull)
            {
                Transform tr = mr.transform;
                if (tr.IsChildOf(runtime.InsideRoot) && cameraSeesOutside)
                {
                    if (mr.enabled)
                        mr.enabled = false;
                    continue;
                }

                if (tr.IsChildOf(runtime.OutsideRoot) && !cameraSeesOutside)
                {
                    if (mr.enabled)
                        mr.enabled = false;
                    continue;
                }
            }

            Bounds b = mr.bounds;
            float d = Vector3.Distance(camPos, b.center);

            if (renderDistanceUseFrustum && planes != null && !GeometryUtility.TestPlanesAABB(planes, b))
            {
                bool frustumCanHide =
                    preserveWallFrustumWithinDistance <= 0f || d > preserveWallFrustumWithinDistance;
                if (frustumCanHide)
                {
                    if (mr.enabled)
                        mr.enabled = false;
                    continue;
                }
            }

            WallCladdingStoneLod stoneLod = usePerStoneLod ? mr.GetComponent<WallCladdingStoneLod>() : null;
            if (stoneLod != null)
            {
                if (hardMax > 0f && d > hardMax)
                {
                    if (mr.enabled)
                        mr.enabled = false;
                    continue;
                }

                if (!mr.enabled)
                    mr.enabled = true;

                bool horizonLow = step > 1 && d > horizonStart && (StableRenderBucket(b.center) % step) != 0;
                stoneLod.ApplyLod(d, lodNear, lodFar, horizonLow);
                continue;
            }

            // Legacy / merged mesh: optional distance + horizon hide, no mesh swap.
            bool enable = mr.enabled;
            float offThreshold = maxDist + hyster;
            float onThreshold = Mathf.Max(0f, maxDist - hyster);
            if (enable)
            {
                if (d > offThreshold)
                    enable = false;
            }
            else
            {
                if (d <= onThreshold)
                    enable = true;
            }

            if (enable && step > 1 && d > horizonStart)
            {
                int bucket = StableRenderBucket(b.center);
                if ((bucket % step) != 0)
                    enable = false;
            }

            if (hardMax > 0f && d > hardMax)
                enable = false;

            if (mr.enabled != enable)
                mr.enabled = enable;
        }

        _renderDistanceApplied = true;
    }

    static bool TryClosestDistanceAlongPath(List<PathSample> samples, Vector3 worldQuery, out float distanceAlong)
    {
        distanceAlong = 0f;
        if (samples == null || samples.Count == 0)
            return false;

        float bestDistSq = float.MaxValue;
        float bestAlong = 0f;

        for (int i = 0; i < samples.Count; i++)
        {
            PathSample s = samples[i];
            Vector3 ab = s.b - s.a;
            float lenSq = ab.sqrMagnitude;
            float t = lenSq > 1e-10f ? Mathf.Clamp01(Vector3.Dot(worldQuery - s.a, ab) / lenSq) : 0f;
            Vector3 p = s.a + ab * t;
            float d2 = (worldQuery - p).sqrMagnitude;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestAlong = s.startDistance + t * s.length;
            }
        }

        distanceAlong = bestAlong;
        return true;
    }

    void EnsureGeneratedRendererCache()
    {
        if (!_rendererCacheDirty)
            return;

        _rendererCacheDirty = false;
        _cachedGeneratedRenderers.Clear();
        if (runtime == null)
            return;

        Transform outRoot = runtime.OutsideRoot;
        Transform inRoot = runtime.InsideRoot;
        CollectRenderersRecursive(outRoot, _cachedGeneratedRenderers);
        CollectRenderersRecursive(inRoot, _cachedGeneratedRenderers);
    }

    static void CollectRenderersRecursive(Transform root, List<MeshRenderer> list)
    {
        if (root == null || list == null)
            return;

        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer mr = renderers[i];
            if (mr != null)
                list.Add(mr);
        }
    }

    static int StableRenderBucket(Vector3 worldPos)
    {
        unchecked
        {
            int x = Mathf.RoundToInt(worldPos.x * 2f);
            int y = Mathf.RoundToInt(worldPos.y * 2f);
            int z = Mathf.RoundToInt(worldPos.z * 2f);
            int h = 17;
            h = h * 31 + x;
            h = h * 31 + y;
            h = h * 31 + z;
            return h & 0x7fffffff;
        }
    }

    private void ApplyFallbackMaterial(WallCladdingProfile profile)
    {
        if (!applyFallbackWallMaterial || profile == null || profile.fallbackWallMaterial == null || wall == null)
            return;

        wall.wallMaterial = profile.fallbackWallMaterial;

        MeshRenderer mr = wall.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sharedMaterial = profile.fallbackWallMaterial;
    }

    private List<Vector3> GetWallPath()
    {
        List<Vector3> path = wall.GetPreviewPathWorld();
        if (path == null || path.Count < 2)
        {
            IReadOnlyList<Vector3> pts = wall.Points;
            if (pts == null || pts.Count < 2)
                return null;
            path = new List<Vector3>(pts);
        }

        // Match WallObject.RebuildMesh polyline so cladding walks the same edges as the base mesh (avoids skipped sub-1mm segments).
        path = WallObject.GetRenderablePolylineXZ(path, wall.closedLoop);
        if (path == null || path.Count < 2)
            return path;

        if (wall == null || !wall.closedLoop || maxCladdingClosedLoopPathVertices <= 0 || path.Count < 3)
            return path;

        var work = new List<Vector3>(path);
        if (work.Count > 2 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count > maxCladdingClosedLoopPathVertices &&
            !WallObject.IsClosedLoopOrthogonalAxisAlignedXZ(work))
            return WallObject.ResampleClosedLoopEvenly(work, maxCladdingClosedLoopPathVertices);

        return path;
    }

    private List<PathSample> BuildPathSamples(List<Vector3> path)
    {
        List<PathSample> result = new List<PathSample>(path.Count);
        if (path == null || path.Count < 2)
            return result;

        List<Vector3> work = new List<Vector3>(path);
        if (work.Count > 2 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        float distance = 0f;

        for (int i = 0; i < work.Count - 1; i++)
        {
            Vector3 a = work[i];
            Vector3 b = work[i + 1];

            Vector3 tangent = b - a;
            tangent.y = 0f;
            float len = tangent.magnitude;

            // True degenerate only (polyline is already merged in GetWallPath to match WallObject).
            if (len < 1e-6f)
                continue;

            tangent /= len;

            result.Add(new PathSample
            {
                a = a,
                b = b,
                tangent = tangent,
                length = len,
                startDistance = distance,
                endDistance = distance + len,
            });

            distance += len;
        }

        if (wall != null && wall.closedLoop && work.Count > 2)
        {
            Vector3 a = work[work.Count - 1];
            Vector3 b = work[0];

            Vector3 tangent = b - a;
            tangent.y = 0f;
            float len = tangent.magnitude;
            if (len >= 1e-6f)
            {
                tangent /= len;
                result.Add(new PathSample
                {
                    a = a,
                    b = b,
                    tangent = tangent,
                    length = len,
                    startDistance = distance,
                    endDistance = distance + len,
                });
            }
        }

        return result;
    }

    private WallLoopShapeKind DetectClosedLoopShape(List<PathSample> samples)
    {
        if (wall == null || !wall.closedLoop || samples == null)
            return WallLoopShapeKind.GenericClosedPolygon;
        if (samples.Count < 3)
            return WallLoopShapeKind.GenericClosedPolygon;

        int n = samples.Count;
        if (n == 3)
            return WallLoopShapeKind.Triangle;
        if (n == 4 && IsRectangleFourSegmentLoop(samples))
            return WallLoopShapeKind.Rectangle;
        if (n >= 10 && IsCircleLikeLoop(samples))
            return WallLoopShapeKind.CircleLike;

        return WallLoopShapeKind.GenericClosedPolygon;
    }

    private static bool IsRectangleFourSegmentLoop(List<PathSample> samples)
    {
        if (samples == null || samples.Count != 4)
            return false;

        for (int i = 0; i < 4; i++)
        {
            PathSample prev = samples[i];
            PathSample next = samples[(i + 1) % 4];
            float dot = Vector3.Dot(prev.tangent, next.tangent);
            if (Mathf.Abs(dot) > 0.28f)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Déplacement horizontal (XZ) le long de la normale extérieure du mur pour régler la saillie d’un quoin d’angle.
    /// <paramref name="cornerRotation"/> : typiquement <c>Quaternion.LookRotation(outward, Vector3.up)</c> avec <c>forward</c> = normale du mur actif.
    /// </summary>
    public static Vector3 ComputeCornerQuoinOutwardWorldOffset(Quaternion cornerRotation, float outwardMeters)
    {
        if (Mathf.Abs(outwardMeters) < 1e-8f)
            return Vector3.zero;
        Vector3 n = cornerRotation * Vector3.forward;
        n.y = 0f;
        if (n.sqrMagnitude < 1e-14f)
            return Vector3.zero;
        return n.normalized * outwardMeters;
    }

    /// <summary>
    /// Profil (End Quoins) + champs <see cref="cornerQuoinExtraLocalMeters"/> / <see cref="cornerQuoinExtraWorldMeters"/> sur ce composant.
    /// Local : repère du quoin (<c>rot</c> = LookRotation vers la normale extérieure du mur actif).
    /// <paramref name="isReflexCorner"/> : coin rentrant interne (~270°) — utilise les champs <c>reflexCornerQuoin*</c> du profil au lieu de <c>cornerQuoin*</c>.
    /// <paramref name="useWallAForZipRow"/> : rangée du zip (mur A vs B) — pour les reflex, <c>local.x</c> est inversé une rangée sur deux car <c>rot</c> change de mur.
    /// </summary>
    private void ApplyCornerQuoinUserOffsets(ref Vector3 centerWorld, Quaternion rot, EndQuoinSettings settings, bool isReflexCorner, bool useWallAForZipRow)
    {
        Vector3 local = Vector3.zero;
        if (settings != null)
        {
            if (isReflexCorner)
            {
                local = settings.reflexCornerQuoinLocalOffsetMeters;
                local.z += settings.reflexCornerQuoinOutwardOffsetMeters;
                local.x *= useWallAForZipRow ? 1f : -1f;
            }
            else
            {
                local = settings.cornerQuoinLocalOffsetMeters;
                local.z += settings.cornerQuoinOutwardOffsetMeters;
            }
        }

        centerWorld += rot * local;
        centerWorld += rot * cornerQuoinExtraLocalMeters;
        centerWorld += cornerQuoinExtraWorldMeters;
    }

    /// <summary>
    /// Décalage additionnel réservé aux <b>coins rentrants</b> (angle interne ~270°) : inspecteur + runtime (voir <see cref="SetReflexCornerQuoinFreeOffset"/>).
    /// Même règle de signe sur <c>local.x</c> que le profil reflex (zip mur A / B).
    /// </summary>
    private void ApplyReflexCornerQuoinFreeOffsets(ref Vector3 centerWorld, Quaternion rot, bool useWallAForZipRow)
    {
        Vector3 freeLocal = reflexCornerQuoinFreeLocalMeters;
        freeLocal.x *= useWallAForZipRow ? 1f : -1f;
        centerWorld += rot * freeLocal;
        centerWorld += reflexCornerQuoinFreeWorldMeters;
        Vector3 runtimeLocal = _reflexCornerQuoinRuntimeLocalMeters;
        runtimeLocal.x *= useWallAForZipRow ? 1f : -1f;
        centerWorld += rot * runtimeLocal;
        centerWorld += _reflexCornerQuoinRuntimeWorldMeters;
    }

    /// <summary>
    /// Déplace librement le caillou de quoin au <b>coin interne</b> (rentrant). S’ajoute aux champs sérialisés du même nom.
    /// Local : X = largeur le long de la face, Y = haut, Z = normale extérieure du mur actif (négatif = vers l’intérieur du mur / traverse la masse).
    /// </summary>
    public void SetReflexCornerQuoinFreeOffset(Vector3 extraLocalMeters, Vector3 extraWorldMeters)
    {
        _reflexCornerQuoinRuntimeLocalMeters = extraLocalMeters;
        _reflexCornerQuoinRuntimeWorldMeters = extraWorldMeters;
        MarkDirty();
    }

    /// <summary>Réinitialise uniquement la partie runtime de <see cref="SetReflexCornerQuoinFreeOffset"/> (les valeurs Inspector sont conservées).</summary>
    public void ClearReflexCornerQuoinRuntimeFreeOffset()
    {
        _reflexCornerQuoinRuntimeLocalMeters = Vector3.zero;
        _reflexCornerQuoinRuntimeWorldMeters = Vector3.zero;
        MarkDirty();
    }

    /// <summary>True when consecutive segments meet at ~90° in XZ (grid-style corners), excluding straight joints.</summary>
    private static bool IsApproximatelyRightAngleBetweenSegments(PathSample prev, PathSample next, float angleToleranceDeg = 16f)
    {
        Vector3 t0 = prev.tangent;
        Vector3 t1 = next.tangent;
        t0.y = 0f;
        t1.y = 0f;
        if (t0.sqrMagnitude < 1e-10f || t1.sqrMagnitude < 1e-10f)
            return false;
        float angle = Vector3.Angle(t0, t1);
        return Mathf.Abs(angle - 90f) <= angleToleranceDeg;
    }

    private static bool ShouldPlaceRectangleStyleCornerQuoin(PathSample prev, PathSample next, EndQuoinSettings settings)
    {
        float cornerDot = Vector3.Dot(prev.tangent, next.tangent);
        if (cornerDot > 0.965f)
            return false;
        if (settings != null && settings.useGridRightAngleCornerQuoins)
            return IsApproximatelyRightAngleBetweenSegments(prev, next);
        return true;
    }

    private static bool IsCircleLikeLoop(List<PathSample> samples)
    {
        if (samples == null || samples.Count < 8)
            return false;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < samples.Count; i++)
            sum += samples[i].b;
        sum /= samples.Count;

        float meanR = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 p = samples[i].b;
            float dx = p.x - sum.x;
            float dz = p.z - sum.z;
            meanR += Mathf.Sqrt(dx * dx + dz * dz);
        }

        meanR /= samples.Count;
        if (meanR < 0.02f)
            return false;

        float acc = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 p = samples[i].b;
            float dx = p.x - sum.x;
            float dz = p.z - sum.z;
            float r = Mathf.Sqrt(dx * dx + dz * dz);
            float d = r - meanR;
            acc += d * d;
        }

        acc /= samples.Count;
        float rel = Mathf.Sqrt(acc) / meanR;
        return rel < 0.085f;
    }

    private void RefreshRectangleCornerDistances(List<PathSample> samples)
    {
        rectangleCornerDistances.Clear();
        if (samples == null || samples.Count == 0)
            return;

        for (int i = 0; i < samples.Count; i++)
        {
            float d = samples[i].endDistance;
            if (d > 0.001f)
                rectangleCornerDistances.Add(d);
        }
    }

    private void GatherModules(WallCladdingProfile profile)
    {
        allModules.Clear();
        AddUnique(allModules, profile.stoneLargeModules);
        AddUnique(allModules, profile.stoneMediumModules);
        AddUnique(allModules, profile.stoneSmallModules);
    }

    private static void AddUnique(List<WallStoneModuleDefinition> target, List<WallStoneModuleDefinition> source)
    {
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            WallStoneModuleDefinition m = source[i];
            if (m == null)
                continue;

            if (!target.Contains(m))
                target.Add(m);
        }
    }

    private void ResetUsage()
    {
        usageCounts.Clear();
        lastUsed = null;
        secondLastUsed = null;
    }

    private void RegisterUsage(WallStoneModuleDefinition module)
    {
        if (module == null)
            return;

        usageCounts.TryGetValue(module, out int count);
        usageCounts[module] = count + 1;
        secondLastUsed = lastUsed;
        lastUsed = module;
    }

    private int GetUsageCount(WallStoneModuleDefinition module)
    {
        return module != null && usageCounts.TryGetValue(module, out int count) ? count : 0;
    }

    private void GenerateStoneSide(WallCladdingProfile profile, List<PathSample> samples, bool outside, float sideSign, System.Random rng)
    {
        Transform root = runtime.GetOrCreateRoot(outside);
        Material stoneMat = ResolveStoneMaterialForCladding(profile);
        if (stoneMat == null)
            return;

        Profiler.BeginSample(outside ? "WallCladding.GenerateOutside" : "WallCladding.GenerateInside");

        float totalLength = samples[samples.Count - 1].endDistance;
        float wallHeight = Mathf.Max(0.1f, wall.height);
        float yMin = GetEffectiveBottomInset(profile);
        if (outside && exteriorCladMinYFromWallBaseMeters > 0.0001f)
            yMin = Mathf.Max(yMin, exteriorCladMinYFromWallBaseMeters);
        float yMax = GetEffectiveTopLimit(profile, yMin);

        int stoneIndex = 0;

        startQuoinSpans.Clear();
        endQuoinSpans.Clear();

        loopShapeKind = WallLoopShapeKind.OpenPolyline;
        rectangleCornerDistances.Clear();
        if (wall != null && wall.closedLoop && samples != null && samples.Count >= 3)
        {
            loopShapeKind = DetectClosedLoopShape(samples);
            if (loopShapeKind == WallLoopShapeKind.Rectangle)
                RefreshRectangleCornerDistances(samples);
        }

        if (ShouldLogCladdingDebug())
            Debug.Log($"[WallCladdingGenerator] loop shape = {loopShapeKind}", this);

        if (outside)
        {
            if (wall != null && wall.closedLoop)
            {
                if (profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
                {
                    EndQuoinSettings eq = profile.stone.endQuoins;
                    bool wantRectStyleCornerQuoins = eq.useGridRightAngleCornerQuoins
                        || loopShapeKind == WallLoopShapeKind.Rectangle;
                    if (loopShapeKind != WallLoopShapeKind.Triangle)
                        TryEmitHouseEnvelopeSourceTriangleAcuteBollardsAtMatchingCorners(
                            profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
                    switch (loopShapeKind)
                    {
                        case WallLoopShapeKind.Rectangle:
                        case WallLoopShapeKind.GenericClosedPolygon:
                            if (wantRectStyleCornerQuoins)
                                GenerateClosedLoopCornerQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
                            break;
                        case WallLoopShapeKind.CircleLike:
                            break;
                        case WallLoopShapeKind.Triangle:
                            GenerateClosedLoopTriangleEndQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
                            break;
                    }
                }
            }
            else
            {
                GenerateOpenEndQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
                if (profile.stone.endQuoins != null && profile.stone.endQuoins.enabled && profile.stone.endQuoins.useGridRightAngleCornerQuoins)
                    GenerateOpenPolylineRightAngleCornerQuoins(profile, root, stoneMat, samples, sideSign, yMin, yMax, rng, ref stoneIndex);
            }
        }

        float rowBottom = yMin;
        int rowIndex = 0;

        while (rowBottom < yMax - profile.stone.minStoneHeight)
        {
            if (rowIndex >= maxRowsPerSide || stoneIndex >= maxGeneratedStonesPerSide)
                break;

            float rowHeight = BuildRowHeight(profile, yMax - rowBottom, rng);
            if (rowHeight < profile.stone.minStoneHeight)
                break;

            bool isTopRow = (rowBottom + rowHeight + profile.stone.verticalSpacing) >= (yMax - profile.stone.minStoneHeight * 0.35f);
            if (isTopRow && IsInteriorDecorativeWall() && interiorDecorativeRemoveTopRow)
                break;

            bool compensateRemovedTopRow = false;
            if (!isTopRow && IsInteriorDecorativeWall() && interiorDecorativeRemoveTopRow)
            {
                float nextRowBottom = rowBottom + rowHeight + profile.stone.verticalSpacing * 1.28f;
                if (nextRowBottom < yMax - profile.stone.minStoneHeight)
                {
                    float nextProbeHeight = Mathf.Clamp(
                        profile.stone.targetRowHeight,
                        profile.stone.minStoneHeight,
                        profile.stone.maxStoneHeight);
                    bool nextWouldBeTop = (nextRowBottom + nextProbeHeight + profile.stone.verticalSpacing) >=
                                          (yMax - profile.stone.minStoneHeight * 0.35f);
                    compensateRemovedTopRow = nextWouldBeTop;
                }
            }

            if (isTopRow)
            {
                float topCover = IsInteriorDecorativeWall()
                    ? 0f
                    : Mathf.Max(wall.thickness * 0.18f, profile.stone.surfaceProtrusion * 1.45f, 0.04f);
                rowHeight = Mathf.Min(
                    (yMax - rowBottom) + topCover,
                    Mathf.Max(profile.stone.maxStoneHeight * 1.28f, rowHeight));
            }

            float rowCenterY = rowBottom + rowHeight * 0.5f;
            GenerateRow(profile, root, stoneMat, samples, totalLength, outside, rowIndex, rowCenterY, rowHeight, sideSign, rng, ref stoneIndex, maxGeneratedStonesPerSide, isTopRow, compensateRemovedTopRow);

            rowBottom += rowHeight + profile.stone.verticalSpacing * 1.28f;
            rowIndex++;
        }

        Profiler.EndSample();
    }

    private float BuildRowHeight(WallCladdingProfile profile, float remainingHeight, System.Random rng)
    {
        float h = profile.stone.targetRowHeight * RandomRange(rng, 1f - profile.stone.rowHeightJitter, 1f + profile.stone.rowHeightJitter);
        h = Mathf.Clamp(h, profile.stone.minStoneHeight, profile.stone.maxStoneHeight);
        return Mathf.Min(h, remainingHeight);
    }

    /// <summary>
    /// Closed-loop rows are packed on [usableStart, usableEnd] like an open segment, which leaves a seam gap
    /// (before the first stone + after the last) that meets in world space. Absorb that gap by widening stones
    /// uniformly — avoids FillTailGapWithGeneratedStones here, which forces nearCorner modules and stacks ugly slivers.
    /// </summary>
    private static void ApplyClosedLoopClosureToPackedRow(List<StonePlacement> row, float closureGap, WallCladdingProfile profile)
    {
        int n = row.Count;
        if (n == 0 || Mathf.Abs(closureGap) < 0.00008f)
            return;

        float per = closureGap / n;
        float minW = Mathf.Max(profile.stone.minStoneWidth * 0.32f, 0.024f);
        float maxW = profile.stone.maxStoneWidth;

        for (int i = 0; i < n; i++)
        {
            StonePlacement p = row[i];
            p.width = Mathf.Clamp(p.width + per, minW, maxW);
            row[i] = p;
        }
    }

    /// <summary>
    /// Linear walk matching <see cref="EmitClosedLoopPackedRow"/> — seam gap = tail after last stone + head before rowPackStart.
    /// </summary>
    private static float MeasureClosedLoopClosureGap(
        List<StonePlacement> row,
        float usableStart,
        float usableEnd,
        float rowPackStart,
        float mortarPx)
    {
        if (row == null || row.Count == 0)
            return 0f;

        float cur = rowPackStart;
        float lastTrailing = rowPackStart;
        for (int i = 0; i < row.Count; i++)
        {
            StonePlacement p = row[i];
            lastTrailing = cur + p.width;
            float advance = p.width + mortarPx * 1.16f;
            if (advance < 0.0005f)
                advance = 0.0005f;
            cur += advance;
        }

        return (usableEnd - lastTrailing) + (rowPackStart - usableStart);
    }

    private void EmitClosedLoopPackedRow(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float rowPackStart,
        float rowCenterY,
        float mortarPx,
        System.Random rng,
        ref int stoneIndex,
        List<StonePlacement> row)
    {
        float cur = rowPackStart;
        for (int i = 0; i < row.Count; i++)
        {
            StonePlacement p = row[i];
            p.centerDistance = cur + p.width * 0.5f;
            p.centerY = rowCenterY;
            CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, p, rng, stoneIndex++, false);
            RegisterUsage(p.module);
            float advance = p.width + mortarPx * 1.16f;
            if (advance < 0.0005f)
                advance = 0.0005f;
            cur += advance;
        }
    }

    private void GenerateRow(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float totalLength,
        bool outside,
        int rowIndex,
        float rowCenterY,
        float rowHeight,
        float sideSign,
        System.Random rng,
        ref int stoneIndex,
        int maxStoneBudget,
        bool isTopRow,
        bool compensateRemovedTopRow)
    {
        // Spacing clamped: if horizontalSpacing is 0 (allowed by profile), cursor += spacing * k is 0 and the
        // main packing loop can spin forever when placements are repeatedly rejected as "too narrow".
        const float minMortar = 0.0001f;
        float mortarPx = Mathf.Max(minMortar, profile.stone.horizontalSpacing);

        float usableStart = Mathf.Max(0f, profile.general.sideInset);
        float usableEnd = Mathf.Max(usableStart, totalLength - profile.general.sideInset);

        float startGapMin = 0f;
        float startGapMax = 0f;
        float endGapMin = 0f;
        float endGapMax = 0f;
        float startBoundaryDistance = 0f;
        float endBoundaryDistance = 0f;
        bool hasStartBoundaryZone = false;
        bool hasEndBoundaryZone = false;

        if (outside && !wall.closedLoop && profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
        {
            startBoundaryDistance = GetCachedQuoinInnerLimit(rowCenterY, true);
            float endInnerLimit = GetCachedQuoinInnerLimit(rowCenterY, false);
            endBoundaryDistance = totalLength - endInnerLimit;

            float gapHalfWidth = Mathf.Max(profile.stone.minStoneWidth * 0.85f, mortarPx * 2.0f);
            float safetyInset  = Mathf.Max(mortarPx * 0.5f, 0.008f);
            float clippingGuard = Mathf.Max(mortarPx * 2.10f, profile.stone.minStoneWidth * 0.30f);

            startGapMin = Mathf.Max(0f, startBoundaryDistance - safetyInset);
            startGapMax = startBoundaryDistance + gapHalfWidth + clippingGuard;

            endGapMin = endBoundaryDistance - gapHalfWidth - clippingGuard;
            endGapMax = Mathf.Min(totalLength, endBoundaryDistance + safetyInset);

            usableStart = Mathf.Max(usableStart, startGapMax);
            usableEnd   = Mathf.Min(usableEnd,   endGapMin);

            hasStartBoundaryZone = startBoundaryDistance > 0.001f;
            hasEndBoundaryZone   = endInnerLimit > 0.001f;
        }

        if (hasStartBoundaryZone)
            GenerateBoundaryBlendStone(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, startBoundaryDistance, startGapMin, startGapMax, true, rng, ref stoneIndex);

        float usableLength = usableEnd - usableStart;
        bool skipTopTailFillForInterior = isTopRow && IsInteriorDecorativeWall();
        if (usableLength > profile.stone.minRowUsableWidth)
        {
            float stagger = ((rowIndex & 1) == 1) ? rowHeight * profile.stone.staggerFraction : 0f;
            float rowPackStart = Mathf.Min(usableEnd, usableStart + stagger);
            float cursor = rowPackStart;
            int rowStoneCount = 0;
            bool stoppedByBudget = false;
            int packLoopGuard = 0;

            bool bufferClosedLoopRow = wall != null && wall.closedLoop;
            if (bufferClosedLoopRow)
                _closedLoopRowPlacements.Clear();

            while (cursor < usableEnd - profile.stone.minRowUsableWidth)
            {
                if (++packLoopGuard > 20000)
                {
                    if (ShouldLogCladdingDebug())
                        Debug.LogWarning("[WallCladdingGenerator] Row packing stopped: iteration guard (check horizontalSpacing / end quoin zones).", this);
                    break;
                }

                if (stoneIndex >= maxStoneBudget || rowStoneCount >= maxStonesPerRow)
                {
                    stoppedByBudget = true;
                    break;
                }

                float remaining = usableEnd - cursor;

                bool nearCorner;
                if (!profile.stone.preferSmallModulesNearCorners)
                    nearCorner = false;
                else if (wall != null && wall.closedLoop)
                    nearCorner = IsDistanceNearAnyPathVertex(samples, cursor, totalLength, profile.stone.cornerSmallModuleZone);
                else
                    nearCorner =
                        cursor - usableStart < profile.stone.cornerSmallModuleZone ||
                        remaining < profile.stone.cornerSmallModuleZone;

                if (!ChoosePlacement(profile, rowHeight, remaining, nearCorner, rng, out StonePlacement placement))
                    break;

                placement.centerDistance = cursor + placement.width * 0.5f;
                placement.centerY = rowCenterY;
                placement.useTerminalHalfRound = false;
                placement.terminalRoundTowardPositiveDistance = false;

                if (isTopRow && IsInteriorDecorativeWall())
                {
                    // Keep top decorative row readable: fewer/longer stones and slightly flatter vertically.
                    float scaledHeight = placement.height * Mathf.Clamp(interiorDecorativeTopRowHeightScale, 0.55f, 1f);
                    placement.height = Mathf.Clamp(
                        scaledHeight,
                        Mathf.Max(profile.stone.minStoneHeight * 0.58f, 0.035f),
                        profile.stone.maxStoneHeight);

                    float maxWidthForRemaining = Mathf.Max(
                        profile.stone.minStoneWidth * 0.40f,
                        remaining - mortarPx * 0.22f);

                    float scaledWidth = placement.width * Mathf.Max(1f, interiorDecorativeTopRowWidthScale);
                    placement.width = Mathf.Clamp(
                        scaledWidth,
                        Mathf.Max(profile.stone.minStoneWidth * 0.58f, 0.06f),
                        maxWidthForRemaining);

                    placement.centerDistance = cursor + placement.width * 0.5f;
                }
                else if (compensateRemovedTopRow && IsInteriorDecorativeWall())
                {
                    // Top row removed: stretch the row below so it reads as a strong crown band.
                    float maxWidthForRemaining = Mathf.Max(
                        profile.stone.minStoneWidth * 0.45f,
                        remaining - mortarPx * 0.22f);
                    float scaledWidth = placement.width * Mathf.Max(1f, interiorDecorativeCompensationRowWidthScale);
                    placement.width = Mathf.Clamp(
                        scaledWidth,
                        Mathf.Max(profile.stone.minStoneWidth * 0.62f, 0.075f),
                        maxWidthForRemaining);
                    placement.centerDistance = cursor + placement.width * 0.5f;
                }

                if (outside && !wall.closedLoop && profile.stone.endQuoins != null && profile.stone.endQuoins.enabled)
                {
                    ApplyCachedEndQuoinClearance(profile, totalLength, rowCenterY, ref placement);
                    // Hard clamp: keep first-pass cladding out of connector/filler zones.
                    float mortar = Mathf.Max(mortarPx * 1.52f, 0.0075f);
                    float startLimit = GetCachedQuoinInnerLimit(rowCenterY, true) + mortar;
                    float endLimit = totalLength - GetCachedQuoinInnerLimit(rowCenterY, false) - mortar;
                    if (endLimit > startLimit + profile.stone.minStoneWidth * 0.35f)
                    {
                        float maxAllowedWidth = Mathf.Max(profile.stone.minStoneWidth * 0.30f, endLimit - startLimit);
                        placement.width = Mathf.Min(placement.width, maxAllowedWidth);
                        placement.centerDistance = Mathf.Clamp(
                            placement.centerDistance,
                            startLimit + placement.width * 0.5f,
                            endLimit - placement.width * 0.5f);
                    }
                }

                if (placement.width < profile.stone.minStoneWidth * 0.35f)
                {
                    cursor += Mathf.Max(mortarPx * 0.8f, profile.stone.minStoneWidth * 0.02f);
                    continue;
                }

                if (outside && wall != null && wall.closedLoop && loopShapeKind == WallLoopShapeKind.Rectangle &&
                    profile.stone.endQuoins != null && profile.stone.endQuoins.enabled &&
                    rectangleCornerDistances != null && rectangleCornerDistances.Count > 0 && totalLength > 0.001f)
                {
                    float nearestCornerDist = float.MaxValue;
                    for (int c = 0; c < rectangleCornerDistances.Count; c++)
                    {
                        float d = Mathf.Abs(placement.centerDistance - rectangleCornerDistances[c]);
                        d = Mathf.Min(d, totalLength - d);
                        if (d < nearestCornerDist)
                            nearestCornerDist = d;
                    }

                    float softenZone = GetRectangleCornerHalfZone(profile) + Mathf.Max(mortarPx * 1.10f, 0.02f);
                    if (nearestCornerDist < softenZone)
                    {
                        float sideExtrusionT = EvaluateCornerExtrusionStrength(EffectiveCornerSideExtensionMultiplier());
                        float tCorner = 1f - Mathf.Clamp01(nearestCornerDist / Mathf.Max(0.0001f, softenZone));
                        float targetScaleAtCorner = sideExtrusionT <= 1f
                            ? Mathf.Lerp(0.90f, 1.22f, sideExtrusionT)
                            : 1.22f + (sideExtrusionT - 1f) * 0.16f;
                        float protrusionScale = Mathf.Lerp(1f, targetScaleAtCorner, tCorner);
                        placement.protrusion *= protrusionScale;
                        placement.protrusion = Mathf.Clamp(
                            placement.protrusion,
                            0.006f,
                            Mathf.Max(0.006f, profile.stone.surfaceProtrusion * (1.40f + sideExtrusionT * 0.85f)));
                    }
                }

                // No terminal half-round stones on closed-loop wall rows: they duplicated old D-end geometry
                // and z-fought with dedicated corner quoins / triangle bollards.

                if (bufferClosedLoopRow)
                {
                    _closedLoopRowPlacements.Add(placement);
                    rowStoneCount++;
                }
                else
                {
                    CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, false);
                    rowStoneCount++;
                    RegisterUsage(placement.module);
                }

                float advance = placement.width + mortarPx * 1.16f;
                if (advance < 0.0005f)
                    advance = 0.0005f;
                cursor += advance;
            }

            // If budget cap stops row generation early, continue with real
            // generated stones (small/medium modules), not stretched fillers.
            if (bufferClosedLoopRow && _closedLoopRowPlacements.Count > 0)
            {
                float closureGap = MeasureClosedLoopClosureGap(
                    _closedLoopRowPlacements,
                    usableStart,
                    usableEnd,
                    rowPackStart,
                    mortarPx);
                ApplyClosedLoopClosureToPackedRow(_closedLoopRowPlacements, closureGap, profile);

                float residual = MeasureClosedLoopClosureGap(
                    _closedLoopRowPlacements,
                    usableStart,
                    usableEnd,
                    rowPackStart,
                    mortarPx);
                if (Mathf.Abs(residual) > 0.0012f)
                    ApplyClosedLoopClosureToPackedRow(_closedLoopRowPlacements, residual, profile);

                EmitClosedLoopPackedRow(
                    profile,
                    root,
                    stoneMaterial,
                    samples,
                    sideSign,
                    rowPackStart,
                    rowCenterY,
                    mortarPx,
                    rng,
                    ref stoneIndex,
                    _closedLoopRowPlacements);
            }
            else if (!bufferClosedLoopRow &&
                !skipTopTailFillForInterior &&
                cursor < usableEnd - 0.0006f &&
                stoneIndex < maxStoneBudget)
                FillTailGapWithGeneratedStones(
                    profile,
                    root,
                    stoneMaterial,
                    samples,
                    sideSign,
                    rowCenterY,
                    rowHeight,
                    cursor,
                    usableEnd,
                    rng,
                    ref stoneIndex,
                    maxStoneBudget,
                    mortarPx);
        }
        else if (usableLength > 0.0004f &&
            wall != null && !wall.closedLoop &&
            !skipTopTailFillForInterior &&
            stoneIndex < maxStoneBudget)
        {
            FillTailGapWithGeneratedStones(
                profile,
                root,
                stoneMaterial,
                samples,
                sideSign,
                rowCenterY,
                rowHeight,
                usableStart,
                usableEnd,
                rng,
                ref stoneIndex,
                maxStoneBudget,
                mortarPx);
        }

        if (hasEndBoundaryZone)
            GenerateBoundaryBlendStone(profile, root, stoneMaterial, samples, sideSign, rowCenterY, rowHeight, endBoundaryDistance, endGapMin, endGapMax, false, rng, ref stoneIndex);

    }

    private void FillTailGapWithGeneratedStones(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float rowCenterY,
        float rowHeight,
        float gapStart,
        float gapEnd,
        System.Random rng,
        ref int stoneIndex,
        int maxStoneBudget,
        float mortarPx)
    {
        float width = gapEnd - gapStart;
        if (width < 0.0003f)
            return;

        float cursor = gapStart;
        int emitted = 0;
        int maxExtra = Mathf.Max(1, maxTailGapFillStonesPerRow);
        int tailGuard = 0;
        // Allow filling sub-minRowUsable gaps; last resort handles what ChoosePlacement refuses.
        const float minTailStrut = 0.00025f;
        while (cursor < gapEnd - minTailStrut)
        {
            if (++tailGuard > 12000)
            {
                if (ShouldLogCladdingDebug())
                    Debug.LogWarning("[WallCladdingGenerator] Tail gap fill: iteration guard.", this);
                break;
            }

            if (stoneIndex >= maxStoneBudget || emitted >= maxExtra)
                break;

            float remaining = gapEnd - cursor;
            if (remaining < profile.stone.minStoneWidth * 0.30f - 0.0001f)
                break;

            if (!ChoosePlacement(profile, rowHeight, remaining, true, rng, out StonePlacement placement))
                break;

            if (placement.width < profile.stone.minStoneWidth * 0.30f)
                break;

            placement.centerDistance = cursor + placement.width * 0.5f;
            placement.centerY = rowCenterY;
            placement.useTerminalHalfRound = false;
            placement.terminalRoundTowardPositiveDistance = false;

            CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, false);
            RegisterUsage(placement.module);

            float advance = placement.width + mortarPx * 1.08f;
            if (advance < 0.0005f)
                advance = 0.0005f;
            cursor += advance;
            emitted++;
        }

        if (cursor < gapEnd - 0.0002f && stoneIndex < maxStoneBudget)
        {
            TryEmitLastResortNarrowSliverInGap(
                profile,
                root,
                stoneMaterial,
                samples,
                sideSign,
                rowCenterY,
                rowHeight,
                cursor,
                gapEnd,
                rng,
                ref stoneIndex);
        }
    }

    /// <summary>
    /// When normal packing and <see cref="ChoosePlacement"/> leave a strip (often &lt; minRowUsableWidth or too narrow
    /// for the elongated-rectangle rule), place one filler stone without the face-aspect minimum — avoids a bare
    /// white capping band on a rare edge segment.
    /// </summary>
    private bool TryEmitLastResortNarrowSliverInGap(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float rowCenterY,
        float rowHeight,
        float gapStart,
        float gapEnd,
        System.Random rng,
        ref int stoneIndex)
    {
        float w = gapEnd - gapStart;
        if (w < 0.00035f)
            return false;

        WallStoneModuleDefinition module = PickGapFillerModule(profile, rng);
        if (module == null)
        {
            module = PickWeightedModule(profile != null ? profile.stoneSmallModules : null, rng);
            if (module == null)
                module = PickWeightedModule(profile != null ? profile.stoneMediumModules : null, rng);
        }

        if (module == null)
            return false;

        float wallBottom = GetEffectiveBottomInset(profile);
        float wallTop = GetEffectiveTopLimit(profile, wallBottom);
        bool nearTopRow = (rowCenterY + rowHeight * 0.5f) >=
            (wallTop - Mathf.Max(rowHeight * 0.55f, 0.02f));

        float useW = Mathf.Max(
            w * 0.96f,
            Mathf.Max(profile.stone.minStoneWidth * 0.06f, 0.0042f));
        useW = Mathf.Min(useW, w * 0.998f);
        if (useW < 0.0005f)
            return false;

        float minH = Mathf.Max(profile.stone.minStoneHeight * 0.50f, 0.02f);
        float targetH = rowHeight * RandomRange(rng, 0.88f, 1.02f);
        targetH = Mathf.Clamp(targetH, minH, profile.stone.maxStoneHeight);
        if (useW * 1.35f < targetH)
            targetH = Mathf.Max(minH * 0.72f, useW * 1.18f);
        if (useW * 0.5f > targetH + 0.0001f)
        {
            targetH = Mathf.Max(minH, Mathf.Min(targetH, useW * 0.62f));
        }

        float topOvershoot = nearTopRow
            ? (IsInteriorDecorativeWall() ? 0f : Mathf.Max(wall.thickness * 0.16f, profile.stone.surfaceProtrusion * 1.35f, 0.03f))
            : 0f;
        float allowedTop = wallTop + topOvershoot;
        float maxH = Mathf.Max(minH, allowedTop - wallBottom - 0.0018f);
        targetH = Mathf.Min(targetH, maxH);
        if (targetH < minH * 0.65f)
            return false;

        float halfW = useW * 0.5f;
        float centerDistance = gapStart + halfW;
        centerDistance = Mathf.Clamp(centerDistance, gapStart + halfW, gapEnd - halfW);

        float centerY = rowCenterY;
        float topLimit = allowedTop - targetH * 0.5f - 0.0012f;
        float bottomLimit = wallBottom + targetH * 0.5f + 0.0012f;
        centerY = Mathf.Clamp(centerY, bottomLimit, topLimit);

        float protrusion = Mathf.Max(profile.stone.surfaceProtrusion * RandomRange(rng, 0.92f, 1.02f), 0.012f);
        if (nearTopRow)
        {
            float topTarget = Mathf.Max(profile.stone.surfaceProtrusion * 1.1f, protrusion);
            protrusion = Mathf.Min(topTarget, profile.stone.surfaceProtrusion * 1.32f);
        }

        float embedMortar = Mathf.Max(profile.stone.horizontalSpacing * 0.7f, 0.0028f);
        float depth = Mathf.Lerp(profile.stone.minStoneDepth, profile.stone.maxStoneDepth, 0.52f) * module.depthMultiplier;
        depth = Mathf.Clamp(
            depth * RandomRange(rng, 0.9f, 1.1f),
            profile.stone.minStoneDepth,
            profile.stone.maxStoneDepth);
        float through = Mathf.Max(wall.thickness + protrusion + embedMortar * 0.3f, profile.stone.minStoneDepth * 1.05f);

        StonePlacement placement = new StonePlacement
        {
            module = module,
            centerDistance = centerDistance,
            centerY = centerY,
            width = useW,
            height = targetH,
            depth = through,
            protrusion = protrusion,
            embed = through
        };

        CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, true);
        RegisterUsage(module);
        return true;
    }

    private bool GenerateBoundaryBlendStone(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float rowCenterY,
        float rowHeight,
        float boundaryDistance,
        float zoneMin,
        float zoneMax,
        bool startBoundary,
        System.Random rng,
        ref int stoneIndex,
        bool allowThinCornerInfill = false)
    {
        float zoneWidth = zoneMax - zoneMin;
        float zoneMinFactor = allowThinCornerInfill ? 0.16f : 0.30f;
        if (zoneWidth < profile.stone.minStoneWidth * zoneMinFactor)
            return false;

        float mortarGap = Mathf.Clamp(
            profile.stone.horizontalSpacing * 0.48f,
            0.0022f,
            Mathf.Max(0.0022f, profile.stone.minStoneWidth * 0.08f));

        float workingMin = zoneMin + mortarGap * 0.35f;
        float workingMax = zoneMax - mortarGap * 0.35f;
        if (workingMax <= workingMin + 0.001f)
            return false;

        float availableWidth = workingMax - workingMin;
        float hardMinWidth = allowThinCornerInfill
            ? Mathf.Max(profile.stone.minStoneWidth * 0.20f, 0.018f)
            : Mathf.Max(profile.stone.minStoneWidth * 0.38f, 0.036f);
        if (availableWidth < hardMinWidth)
            return false;

        WallStoneModuleDefinition module = PickGapFillerModule(profile, rng);
        if (module == null)
            module = PickEndQuoinModule(profile, rng);
        if (module == null)
            return false;

        float wallBottom = GetEffectiveBottomInset(profile);
        float wallTop = GetEffectiveTopLimit(profile, wallBottom);
        float maxHeightInWall = Mathf.Max(profile.stone.minStoneHeight * 0.72f, wallTop - wallBottom - 0.002f);

        float minHeight = Mathf.Max(profile.stone.minStoneHeight * 0.72f, 0.045f);
        float targetHeight = Mathf.Clamp(
            rowHeight + profile.stone.verticalSpacing * 0.62f,
            minHeight,
            Mathf.Min(profile.stone.maxStoneHeight, maxHeightInWall));
        float width = availableWidth;
        float minWidthFromHeightRatio = allowThinCornerInfill ? 0.34f : 0.72f;
        float minWidthFromHeight = targetHeight * minWidthFromHeightRatio;

        int reductionSteps = 0;
        while (width < minWidthFromHeight && targetHeight > minHeight + 0.001f && reductionSteps < 8)
        {
            targetHeight = Mathf.Max(minHeight, targetHeight * 0.86f);
            minWidthFromHeight = targetHeight * minWidthFromHeightRatio;
            reductionSteps++;
        }

        if (width < minWidthFromHeight)
        {
            if (!allowThinCornerInfill)
                return false;

            // Last fallback for tight corner mortar gaps: keep a slim infiller instead of dropping it.
            targetHeight = Mathf.Max(minHeight * 0.70f, width / Mathf.Max(0.001f, minWidthFromHeightRatio));
        }

        float sideContactNudge = Mathf.Clamp(mortarGap * 0.92f, 0.0012f, 0.0058f);
        width += sideContactNudge * 2f;

        bool nearTopRow = (rowCenterY + rowHeight * 0.5f) >= (wallTop - Mathf.Max(rowHeight * 0.55f, 0.02f));
        float topOvershoot = nearTopRow
            ? (IsInteriorDecorativeWall()
                ? 0f
                : Mathf.Max(wall.thickness * 0.16f, profile.stone.surfaceProtrusion * 1.35f, 0.03f))
            : 0f;
        float allowedTop = wallTop + topOvershoot;

        if (nearTopRow)
        {
            float boostedTopHeight = Mathf.Max(
                targetHeight,
                Mathf.Min(rowHeight * 1.08f, profile.stone.maxStoneHeight * 1.24f));
            targetHeight = Mathf.Min(boostedTopHeight, allowedTop - wallBottom - 0.002f);
        }

        float centerY = rowCenterY;
        float topLimit = allowedTop - targetHeight * 0.5f - 0.0015f;
        float bottomLimit = wallBottom + targetHeight * 0.5f + 0.0015f;
        centerY = Mathf.Clamp(centerY, bottomLimit, topLimit);

        float protrusion = Mathf.Max(profile.stone.surfaceProtrusion * RandomRange(rng, 0.94f, 1.03f), 0.014f);
        if (nearTopRow)
        {
            // Keep top connector stones flush with the top row read.
            float topTarget = Mathf.Max(profile.stone.surfaceProtrusion * 1.14f, protrusion);
            protrusion = Mathf.Min(topTarget, profile.stone.surfaceProtrusion * 1.30f);
        }
        float embedMortarRef = Mathf.Max(profile.stone.horizontalSpacing * 0.75f, 0.0030f);
        float throughWallEmbed = Mathf.Max(
            wall.thickness + protrusion + embedMortarRef * 0.35f,
            profile.stone.minStoneDepth * 1.10f);

        float stableShiftSeed = Mathf.Sin((rowCenterY + boundaryDistance) * 17.123f);
        float stableShift = Mathf.Clamp(stableShiftSeed * mortarGap * 0.40f, -mortarGap * 0.45f, mortarGap * 0.45f);
        float seamBias = Mathf.Clamp(Mathf.Abs(connectorRightShift) * 0.12f, 0f, availableWidth * 0.04f);
        float centerDistance = (workingMin + workingMax) * 0.5f + stableShift + (startBoundary ? seamBias : -seamBias);

        float halfWidth = width * 0.5f;
        float minCenter = zoneMin + halfWidth - sideContactNudge;
        float maxCenter = zoneMax - halfWidth + sideContactNudge;
        if (maxCenter >= minCenter)
            centerDistance = Mathf.Clamp(centerDistance, minCenter, maxCenter);
        else
            centerDistance = (zoneMin + zoneMax) * 0.5f;

        StonePlacement placement = new StonePlacement
        {
            module = module,
            centerDistance = centerDistance,
            centerY = centerY,
            width = width,
            height = targetHeight,
            depth = throughWallEmbed,
            protrusion = protrusion,
            embed = throughWallEmbed
        };

        CreateStoneObject(profile, root, stoneMaterial, samples, sideSign, placement, rng, stoneIndex++, true);
        RegisterUsage(module);
        return true;
    }

    private WallStoneModuleDefinition PickGapFillerModule(WallCladdingProfile profile, System.Random rng)
    {
        WallStoneModuleDefinition best = PickPreferredGapFiller(profile != null ? profile.stoneSmallModules : null, rng);
        if (best != null)
            return best;

        best = PickPreferredGapFiller(profile != null ? profile.stoneMediumModules : null, rng);
        if (best != null)
            return best;

        best = PickWeightedModule(profile != null ? profile.stoneSmallModules : null, rng);
        if (best != null)
            return best;

        return PickWeightedModule(profile != null ? profile.stoneMediumModules : null, rng);
    }

    private WallStoneModuleDefinition PickPreferredGapFiller(List<WallStoneModuleDefinition> list, System.Random rng)
    {
        if (list == null || list.Count == 0)
            return null;

        List<WallStoneModuleDefinition> preferred = null;
        for (int i = 0; i < list.Count; i++)
        {
            WallStoneModuleDefinition m = list[i];
            if (m == null || m.weight <= 0f || m.probability <= 0f)
                continue;

            if (!m.preferAsGapFiller)
                continue;

            preferred ??= new List<WallStoneModuleDefinition>();
            preferred.Add(m);
        }

        if (preferred == null || preferred.Count == 0)
            return null;

        return PickWeightedModule(preferred, rng);
    }

    private bool ChoosePlacement(WallCladdingProfile profile, float rowHeight, float remainingWidth, bool nearCorner, System.Random rng, out StonePlacement result)
    {
        result = default;

        WallStoneModuleDefinition best = null;
        float bestScore = float.MinValue;
        float bestWidth = 0f;
        float bestHeight = 0f;
        float bestDepth = 0f;
        float desiredWidth = ComputeDesiredWidth(profile, rowHeight, remainingWidth, nearCorner, rng);

        for (int i = 0; i < allModules.Count; i++)
        {
            WallStoneModuleDefinition m = allModules[i];
            if (m == null || m.probability <= 0f || m.weight <= 0f)
                continue;

            if (nearCorner && !m.canUseNearCorners)
                continue;

            if (RandomValue(rng) > m.probability)
                continue;

            float ratio = RandomRange(rng, m.minWidthToHeight, m.maxWidthToHeight);
            float width = Mathf.Clamp(
                rowHeight * ratio * RandomRange(rng, 1f - profile.stone.widthJitter, 1f + profile.stone.widthJitter),
                profile.stone.minStoneWidth,
                profile.stone.maxStoneWidth);

            width = Mathf.Min(width, remainingWidth);

            if (width < profile.stone.minRowUsableWidth)
                continue;

            float height = Mathf.Clamp(
                rowHeight * RandomRange(rng, 1f - profile.stone.heightJitter, 1f + profile.stone.heightJitter),
                profile.stone.minStoneHeight,
                profile.stone.maxStoneHeight);

            float depth = Mathf.Lerp(profile.stone.minStoneDepth, profile.stone.maxStoneDepth, 0.5f) * m.depthMultiplier;
            depth *= RandomRange(rng, 1f - profile.stone.depthJitter, 1f + profile.stone.depthJitter);
            depth = Mathf.Clamp(depth, profile.stone.minStoneDepth, profile.stone.maxStoneDepth);

            float widthFit = 1f - Mathf.Clamp01(Mathf.Abs(width - desiredWidth) / Mathf.Max(0.001f, desiredWidth));
            float usagePenalty = 1f / (1f + GetUsageCount(m) * 0.35f);
            float repeatPenalty = (m == lastUsed) ? 0.35f : (m == secondLastUsed ? 0.70f : 1f);

            float sliverPenalty = 1f;
            float after = remainingWidth - width - profile.stone.horizontalSpacing;
            if (after > 0f && after < profile.stone.rejectSliverGapBelow)
                sliverPenalty = 0.55f;

            float classBias = 1f;
            if (nearCorner)
            {
                if (m.sizeClass == StoneModuleSizeClass.Small) classBias *= 1.15f;
                if (m.sizeClass == StoneModuleSizeClass.Large) classBias *= 0.88f;
            }
            else
            {
                if (remainingWidth > rowHeight * 2f && m.sizeClass == StoneModuleSizeClass.Large) classBias *= 1.08f;
                if (remainingWidth < rowHeight * 1.25f && (m.sizeClass == StoneModuleSizeClass.Small || m.preferAsGapFiller)) classBias *= 1.12f;
            }

            float score = (widthFit * 2.2f + usagePenalty * 0.8f) * repeatPenalty * sliverPenalty * classBias * m.weight;
            score *= RandomRange(rng, 0.97f, 1.03f);

            if (score > bestScore)
            {
                bestScore = score;
                best = m;
                bestWidth = width;
                bestHeight = height;
                bestDepth = depth;
            }
        }

        if (best == null)
            return false;

        ClampWallFaceStoneToElongatedRectangle(profile, remainingWidth, rng, ref bestWidth, ref bestHeight);

        if (bestWidth < profile.stone.minRowUsableWidth || bestHeight < profile.stone.minStoneHeight * 0.75f)
            return false;

        result.module = best;
        result.width = bestWidth;
        result.height = bestHeight;
        result.depth = bestDepth;
        result.protrusion = Mathf.Min(profile.stone.surfaceProtrusion, bestDepth * 0.45f);
        result.embed = Mathf.Min(profile.stone.embedDepth, bestDepth * 0.65f);
        return true;
    }

    /// <summary>
    /// Face stones stay long rectangles along the wall run: width (path) must stay clearly above height (row band).
    /// </summary>
    private void ClampWallFaceStoneToElongatedRectangle(
        WallCladdingProfile profile,
        float remainingWidth,
        System.Random rng,
        ref float width,
        ref float height)
    {
        float minWidthOverHeight = 1.28f;
        if (width >= height * minWidthOverHeight - 0.0001f)
            return;

        float targetW = height * minWidthOverHeight * RandomRange(rng, 1.0f, 1.16f);
        if (targetW <= remainingWidth + 0.0001f)
        {
            width = Mathf.Min(remainingWidth, targetW);
            return;
        }

        width = remainingWidth;
        float maxH = width / minWidthOverHeight;
        height = Mathf.Max(
            profile.stone.minStoneHeight * 0.78f,
            Mathf.Min(height, maxH));
    }

    private float ComputeDesiredWidth(WallCladdingProfile profile, float rowHeight, float remainingWidth, bool nearCorner, System.Random rng)
    {
        float ratioMin = Mathf.Max(profile.stone.minWidthVsHeight, 1.18f);
        float ratioMax = nearCorner ? profile.stone.nearCornerMaxWidthVsHeight : profile.stone.maxWidthVsHeight;
        if (ratioMax < ratioMin)
            ratioMax = ratioMin;

        float desired = rowHeight * RandomRange(rng, ratioMin, ratioMax);
        desired = Mathf.Clamp(desired, profile.stone.minStoneWidth, profile.stone.maxStoneWidth);
        return Mathf.Min(desired, remainingWidth);
    }

    private void AddQuoinSpan(bool startEnd, float yMin, float yMax, float innerLimit)
    {
        QuoinRowSpan span = new QuoinRowSpan
        {
            yMin = yMin,
            yMax = yMax,
            innerLimit = Mathf.Max(0f, innerLimit)
        };

        if (startEnd)
            startQuoinSpans.Add(span);
        else
            endQuoinSpans.Add(span);
    }

    private float GetRectangleCornerHalfZone(WallCladdingProfile profile)
    {
        float cornerReserve = Mathf.Max(
            profile.stone.endQuoins != null ? profile.stone.endQuoins.reserveWidth * 0.68f : 0f,
            profile.stone.minStoneWidth * 0.72f,
            wall != null ? wall.thickness * 0.28f : 0f);
        float cornerGap = Mathf.Max(profile.stone.horizontalSpacing * 1.08f, 0.008f);
        return cornerReserve + cornerGap;
    }

    private bool TryGetNearestCornerAngleAtDistance(
        List<PathSample> samples,
        float totalLength,
        float distance,
        out float signedDeltaToCorner,
        out float cornerAngleDeg)
    {
        signedDeltaToCorner = 0f;
        cornerAngleDeg = 180f;
        if (samples == null || samples.Count < 2)
            return false;

        bool closed = wall != null && wall.closedLoop;
        int cornerCount = closed ? samples.Count : samples.Count - 1;
        if (cornerCount <= 0)
            return false;

        float bestAbsDelta = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < cornerCount; i++)
        {
            int next = i + 1;
            if (next >= samples.Count)
            {
                if (!closed)
                    break;
                next = 0;
            }

            Vector3 a = samples[i].tangent;
            Vector3 b = samples[next].tangent;
            float angle = Vector3.Angle(a, b);
            if (angle < 0.001f)
                continue;

            float cornerDistance = samples[i].endDistance;
            float delta = cornerDistance - distance;
            if (closed && totalLength > 0.001f)
            {
                while (delta > totalLength * 0.5f) delta -= totalLength;
                while (delta < -totalLength * 0.5f) delta += totalLength;
            }

            float absDelta = Mathf.Abs(delta);
            if (absDelta < bestAbsDelta)
            {
                bestAbsDelta = absDelta;
                signedDeltaToCorner = delta;
                cornerAngleDeg = angle;
                found = true;
            }
        }

        return found;
    }

    private float GetCachedQuoinInnerLimit(float y, bool startEnd)
    {
        List<QuoinRowSpan> spans = startEnd ? startQuoinSpans : endQuoinSpans;
        float best = 0f;

        for (int i = 0; i < spans.Count; i++)
        {
            QuoinRowSpan span = spans[i];
            if (y >= span.yMin && y <= span.yMax)
                return span.innerLimit;

            if (Mathf.Abs(y - (span.yMin + span.yMax) * 0.5f) < 0.12f)
                best = Mathf.Max(best, span.innerLimit);
        }

        return best;
    }

    private void ApplyCachedEndQuoinClearance(WallCladdingProfile profile, float totalLength, float rowCenterY, ref StonePlacement placement)
    {
        float startLimit = GetCachedQuoinInnerLimit(rowCenterY, true);
        float endLimit = totalLength - GetCachedQuoinInnerLimit(rowCenterY, false);

        float stoneLeft = placement.centerDistance - placement.width * 0.5f;
        float stoneRight = placement.centerDistance + placement.width * 0.5f;

        float distToStart = stoneLeft - startLimit;
        float distToEnd = endLimit - stoneRight;

        float blendWidth = Mathf.Max(0.20f, profile.stone.minStoneWidth * 1.70f);

        if (distToStart < blendWidth)
        {
            float t = Mathf.Clamp01(Mathf.Max(0f, distToStart) / blendWidth);
            placement.width *= Mathf.Lerp(0.70f, 1f, t);
        }

        if (distToEnd < blendWidth)
        {
            float t = Mathf.Clamp01(Mathf.Max(0f, distToEnd) / blendWidth);
            placement.width *= Mathf.Lerp(0.70f, 1f, t);
        }

        placement.protrusion = Mathf.Max(0.0065f, placement.protrusion);
        placement.embed = Mathf.Max(profile.stone.minStoneDepth * 0.30f, placement.embed);
        placement.width = Mathf.Max(profile.stone.minStoneWidth * 0.65f, placement.width);
    }

    private float GetReservedEndQuoinWidth(WallCladdingProfile profile)
    {
        if (profile == null || profile.stone == null || profile.stone.endQuoins == null || !profile.stone.endQuoins.enabled)
            return 0f;

        return Mathf.Max(profile.stone.endQuoins.reserveWidth, profile.stone.endQuoins.maxLength + profile.stone.horizontalSpacing);
    }

    private void GenerateOpenEndQuoins(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        float yMin,
        float yMax,
        System.Random rng,
        ref int stoneIndex)
    {
        if (profile == null || profile.stone == null || profile.stone.endQuoins == null)
            return;

        EndQuoinSettings settings = profile.stone.endQuoins;
        if (!settings.enabled || wall == null || wall.closedLoop || samples == null || samples.Count == 0)
            return;

        PathSample first = samples[0];
        PathSample last = samples[samples.Count - 1];

        GenerateSingleEndQuoinStack(profile, root, stoneMaterial, first.a, first.tangent, sideSign, true, yMin, yMax, settings, rng, ref stoneIndex);
        GenerateSingleEndQuoinStack(profile, root, stoneMaterial, last.b, last.tangent, sideSign, false, yMin, yMax, settings, rng, ref stoneIndex);
    }


    private void GenerateSingleEndQuoinStack(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        Vector3 endPoint,
        Vector3 segmentTangent,
        float sideSign,
        bool startEnd,
        float yMin,
        float yMax,
        EndQuoinSettings settings,
        System.Random rng,
        ref int stoneIndex)
    {
        Vector3 tangent = segmentTangent.normalized;
        if (tangent.sqrMagnitude < 0.000001f)
            return;

        Vector3 inwardTangent = startEnd ? tangent : -tangent;
        Vector3 outwardNormal = Vector3.Cross(Vector3.up, tangent).normalized * sideSign;

        float rowBottom = yMin;
        int rowIndex = 0;

        while (rowBottom < yMax - 0.10f)
        {
            float rowHeight = settings.targetHeight * RandomRange(rng, 1f - settings.rowHeightJitter, 1f + settings.rowHeightJitter);
            rowHeight = Mathf.Clamp(
                rowHeight,
                profile.stone.minStoneHeight * 1.15f,
                Mathf.Max(profile.stone.minStoneHeight * 1.25f, profile.stone.maxStoneHeight * 1.75f));
            bool isLastQuoinRow = (rowBottom + rowHeight + settings.verticalSpacing) >= yMax;
            float topOvershoot = isLastQuoinRow ? Mathf.Max(wall.thickness * 0.18f, profile.stone.surfaceProtrusion * 1.45f, 0.04f) : 0f;
            rowHeight = Mathf.Min(rowHeight, yMax - rowBottom + topOvershoot);

            if (rowHeight < 0.10f)
                break;

            float baseLength = RandomRange(rng, settings.minLength, settings.maxLength);
            float altScale = ((rowIndex & 1) == 0) ? settings.alternateLongScale : settings.alternateShortScale;
            float length = baseLength * altScale * 1.08f * RandomRange(rng, 1f - settings.lengthJitter, 1f + settings.lengthJitter);
            length = Mathf.Clamp(length, settings.minLength * 0.85f, settings.maxLength * 1.35f);

            float revealAtWallEnd = Mathf.Clamp(
                Mathf.Max(wall.thickness * 0.10f, settings.extraOutsideDepth * 0.55f),
                0.02f,
                Mathf.Max(0.02f, length * 0.20f));

            float inwardCoverage = Mathf.Max(0f, length - settings.edgeInset - revealAtWallEnd);
            AddQuoinSpan(startEnd, rowBottom, rowBottom + rowHeight, inwardCoverage);

            // Make end quoins read as structural pillars: +2 cm protrusion
            // on both front and back faces.
            float fullDepth = Mathf.Max(wall.thickness + settings.extraOutsideDepth * 2.0f + 0.04f, wall.thickness + 0.01f);
            float centerY = rowBottom + rowHeight * 0.5f;

            Vector3 center = endPoint;
            center += inwardTangent * Mathf.Max(0f, length * 0.5f - settings.edgeInset - revealAtWallEnd);
            center += Vector3.up * centerY;

            Quaternion rot = Quaternion.LookRotation(outwardNormal, Vector3.up);

            WallStoneModuleDefinition module = PickEndQuoinModule(profile, rng);
            Mesh mesh = BuildEndQuoinMesh(module, length, rowHeight, fullDepth, profile.stone.facePlaneJitter, GetEffectiveUvMetersPerUnit(profile), rng);
            if (mesh != null && mesh.vertexCount > 0)
            {
                GameObject go = new GameObject(startEnd ? $"EndQuoin_Start_{rowIndex:00}" : $"EndQuoin_End_{rowIndex:00}");
                go.transform.SetParent(root, false);
                go.transform.localPosition = transform.InverseTransformPoint(center);
                go.transform.localRotation = Quaternion.LookRotation(
                    transform.InverseTransformDirection(rot * Vector3.forward),
                    transform.InverseTransformDirection(rot * Vector3.up));
                go.transform.localScale = Vector3.one;

                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = mesh;
                mr.sharedMaterial = stoneMaterial;
                if (forceDoubleSidedStoneMaterials && stoneMaterial != null)
                    ApplyMaterialDoubleSided(stoneMaterial);
                ApplyPerStoneMaterialVariation(profile, mr, rng, true);
                AttachQuoinRuntimeLodIfEnabled(go, mf, mesh, GetEffectiveUvMetersPerUnit(profile));
                if (_effectiveCombineStonesThisRebuild && profile != null && mf.sharedMesh != null)
                    ApplyPerStoneTintAsVertexColors(mf.sharedMesh, profile, rng, true);
                stoneIndex++;
            }

            rowBottom += rowHeight + settings.verticalSpacing;
            rowIndex++;
        }
    }

    private WallStoneModuleDefinition PickEndQuoinModule(WallCladdingProfile profile, System.Random rng)
    {
        WallStoneModuleDefinition best = PickWeightedModule(profile != null ? profile.stoneLargeModules : null, rng);
        if (best != null)
            return best;

        best = PickWeightedModule(profile != null ? profile.stoneMediumModules : null, rng);
        if (best != null)
            return best;

        return PickWeightedModule(profile != null ? profile.stoneSmallModules : null, rng);
    }

    private WallStoneModuleDefinition PickWeightedModule(List<WallStoneModuleDefinition> list, System.Random rng)
    {
        if (list == null || list.Count == 0)
            return null;

        float total = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            WallStoneModuleDefinition m = list[i];
            if (m == null || m.weight <= 0f || m.probability <= 0f)
                continue;
            total += m.weight;
        }

        if (total <= 0f)
            return null;

        float roll = RandomRange(rng, 0f, total);
        float acc = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            WallStoneModuleDefinition m = list[i];
            if (m == null || m.weight <= 0f || m.probability <= 0f)
                continue;

            acc += m.weight;
            if (roll <= acc)
                return m;
        }

        return null;
    }

    private Mesh BuildEndQuoinMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float depth,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        if (width <= 0.01f || height <= 0.01f || depth <= 0.01f)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = depth * 0.5f;
        float backZ = -depth * 0.5f;

        float leftX = -halfW;
        float rightX = halfW;
        float totalFrontWidth = width;

        float cutMin = module != null ? module.minCornerCut : 0.05f;
        float cutMax = module != null ? module.maxCornerCut : 0.12f;
        // Emphasize front corner cuts for a clearer beveled-edge read.
        cutMin = Mathf.Clamp01(cutMin * 1.18f);
        cutMax = Mathf.Clamp(cutMax * 1.24f, cutMin, 0.45f);
        float cutBottom = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTop = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutBL = cutBottom;
        float cutBR = cutBottom;
        float cutTR = cutTop;
        float cutTL = cutTop;

        float relief = module != null ? module.frontRelief : 0.025f;
        float frontJitter = planeJitter + relief;

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(leftX + totalFrontWidth * cutBL, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[1] = new Vector3(rightX - totalFrontWidth * cutBR, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[2] = new Vector3(rightX, -halfH + height * cutBR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[3] = new Vector3(rightX,  halfH - height * cutTR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[4] = new Vector3(rightX - totalFrontWidth * cutTR,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[5] = new Vector3(leftX + totalFrontWidth * cutTL,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[6] = new Vector3(leftX,  halfH - height * cutTL, frontZ + RandomRange(rng, 0f, frontJitter));
        front[7] = new Vector3(leftX, -halfH + height * cutBL, frontZ + RandomRange(rng, 0f, frontJitter));

        float backJitterQuoin = frontJitter;
        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            back[i] = new Vector3(
                front[i].x,
                front[i].y,
                backZ - RandomRange(rng, 0f, backJitterQuoin));
        }

        return BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit, includeBackCap: IncludeStoneBackCapInExtrusion());
    }

    /// <summary>
    /// Corner quoin variant with explicit 3D relief on the 4 vertical faces.
    /// Top and bottom remain simple caps (no extra relief intent).
    /// </summary>
    private Mesh BuildCornerFourFaceReliefMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float depth,
        bool widenRightSide,
        float sideExtra,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        if (width <= 0.01f || height <= 0.01f || depth <= 0.01f)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float xLeft = -halfW;
        float xRight = halfW;

        // Widen only the side that has free mortar space.
        if (widenRightSide)
            xRight += sideExtra;
        else
            xLeft -= sideExtra;
        float totalFrontWidth = xRight - xLeft;

        // Corner-only asymmetry:
        // - shorter on front side
        // - longer on back side
        float frontShare = 0.25f;
        float backShare = 0.75f;
        float frontZ = depth * frontShare; // shorter front side
        float backZ = -depth * backShare;  // keep rear side long

        float cutMin = module != null ? module.minCornerCut : 0.05f;
        float cutMax = module != null ? module.maxCornerCut : 0.12f;
        cutMin = Mathf.Clamp01(cutMin * 1.18f);
        cutMax = Mathf.Clamp(cutMax * 1.24f, cutMin, 0.45f);
        float cutBottom = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutTop = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
        float cutBL = cutBottom;
        float cutBR = cutBottom;
        float cutTR = cutTop;
        float cutTL = cutTop;

        float relief = module != null ? module.frontRelief : 0.025f;
        float frontJitter = planeJitter + relief;

        Vector3[] front = new Vector3[8];
        front[0] = new Vector3(xLeft + totalFrontWidth * cutBL, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[1] = new Vector3(xRight - totalFrontWidth * cutBR, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[2] = new Vector3(xRight, -halfH + height * cutBR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[3] = new Vector3(xRight,  halfH - height * cutTR, frontZ + RandomRange(rng, 0f, frontJitter));
        front[4] = new Vector3(xRight - totalFrontWidth * cutTR,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[5] = new Vector3(xLeft + totalFrontWidth * cutTL,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
        front[6] = new Vector3(xLeft,  halfH - height * cutTL, frontZ + RandomRange(rng, 0f, frontJitter));
        front[7] = new Vector3(xLeft, -halfH + height * cutBL, frontZ + RandomRange(rng, 0f, frontJitter));

        Vector3[] back = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            back[i] = new Vector3(
                front[i].x,
                front[i].y,
                backZ - RandomRange(rng, 0f, frontJitter));
        }

        List<Vector3> verts = new List<Vector3>(256);
        List<int> tris = new List<int>(512);
        List<Vector2> uvs = new List<Vector2>(256);

        // Keep top/bottom simple (no relief), like requested for caps.
        AddPolygonFace(verts, tris, uvs, front, true, uvMetersPerUnit);
        AddPolygonFace(verts, tris, uvs, back, false, uvMetersPerUnit);

        // Height-map style relief on every vertical perimeter face (always double-sided so edges stay closed from any view).
        for (int i = 0; i < front.Length; i++)
        {
            int next = (i + 1) % front.Length;
            Vector3 a = front[i];
            Vector3 b = front[next];
            Vector3 c = back[next];
            Vector3 d = back[i];

            Vector3 outward = Vector3.Cross(b - a, d - a).normalized;
            float faceSpan = Mathf.Min((b - a).magnitude, (d - a).magnitude);
            float reliefDepth = Mathf.Clamp((planeJitter + relief) * 1.55f, 0.0015f, Mathf.Max(0.0015f, faceSpan * 0.28f));
            AddDoubleSidedReliefQuad(verts, tris, uvs, a, b, c, d, outward, reliefDepth, uvMetersPerUnit, rng, stoneReliefFaceGrid);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedCornerQuoin4Faces";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (mesh != null)
            mesh.name = "GeneratedCornerQuoin4Faces";
        return mesh;
    }

    private static bool ResolveCornerWidenRightSide(bool useA, float signedMultiplier)
    {
        // Baseline follows row type (A/B), user sign flips it globally.
        bool widenRight = useA;
        if (signedMultiplier < 0f)
            widenRight = !widenRight;
        return widenRight;
    }

    private static float EvaluateCornerExtrusionStrength(float signedMultiplier)
    {
        // 0 => no extra extrusion. Values above 1 keep increasing effect.
        // Capped for geometry stability, but still large enough to be visually obvious.
        return Mathf.Clamp(Mathf.Abs(signedMultiplier), 0f, 6f);
    }

    private static float ResolveCornerSignedSideOffset(bool useA, float magnitude, float signedMultiplier)
    {
        if (magnitude <= 0f || Mathf.Abs(signedMultiplier) < 0.0001f)
            return 0f;

        // A/B rows need mirrored local-X sign; user sign flips both.
        float rowSign = useA ? -1f : 1f;
        float userSign = signedMultiplier >= 0f ? 1f : -1f;
        return magnitude * rowSign * userSign;
    }

    private void ComputeCornerLateralExtension(
        WallCladdingProfile profile,
        EndQuoinSettings settings,
        float baseWidth,
        bool useA,
        System.Random rng,
        out bool widenRightSide,
        out float sideExtra)
    {
        // Side selection remains one-sided (right OR left) depending on corner row/wall orientation.
        widenRightSide = ResolveCornerWidenRightSide(useA, EffectiveCornerSideExtensionMultiplier());

        float maxAllowedWidth = settings.maxLength * 1.60f;
        float available = Mathf.Max(0f, maxAllowedWidth - baseWidth);

        // Keep variation bounded so the opposite face stays stable and stones don't look overly protruded.
        float minExtra = Mathf.Max(0f, Mathf.Min(cornerSingleFaceExtraMin, cornerSingleFaceExtraMax));
        float maxExtraRandom = Mathf.Max(minExtra, Mathf.Max(cornerSingleFaceExtraMin, cornerSingleFaceExtraMax));
        float hardCap = Mathf.Max(0f, cornerSingleFaceExtraHardCap);
        // Keep a fallback budget so effect remains visible even when width allowance is tight.
        float fallbackBudget = Mathf.Max(
            profile.stone.horizontalSpacing * 6.0f,
            Mathf.Max(0.08f, wall != null ? wall.thickness * 0.50f : 0.08f));
        float maxExtra = Mathf.Min(Mathf.Max(available, fallbackBudget), hardCap);
        if (maxExtra <= 0.0001f)
        {
            sideExtra = 0f;
            return;
        }

        float desired = 0f;
        // Avoid constant-size result: clamp the random interval to per-stone local budget.
        float localMin = Mathf.Clamp(minExtra, 0f, maxExtra * 0.85f);
        float localMax = Mathf.Clamp(maxExtraRandom, localMin + 0.0001f, maxExtra);
        if (randomizeSingleCornerLateralFace)
        {
            // Coherent random: variation follows the current stone scale instead of extreme spikes.
            float t = Mathf.Clamp01((float)rng.NextDouble() * 0.78f + (float)rng.NextDouble() * 0.22f);
            float raw = Mathf.Lerp(localMin, localMax, t);
            float proportional = Mathf.Clamp(raw / Mathf.Max(0.0001f, baseWidth), 0.06f, 0.52f);
            desired = baseWidth * proportional * RandomRange(rng, 0.88f, 1.22f);
        }
        else
            desired = Mathf.Lerp(localMin, localMax, 0.60f);

        // Optional extra gain from legacy multiplier (kept for compatibility).
        float multiplier = EvaluateCornerExtrusionStrength(EffectiveCornerSideExtensionMultiplier());
        if (multiplier > 0.0001f)
            desired *= Mathf.Lerp(1f, 3.2f, Mathf.InverseLerp(0f, 6f, multiplier));

        sideExtra = Mathf.Clamp(desired, 0f, maxExtra);
    }

    /// <summary>
    /// Exterior 90° corner: L footprint in local XZ (Unity: +X along first wall arm, +Z along second), Y up.
    /// All vertical perimeter faces get the same relief/UV treatment as flat quoins (top/bottom caps stay flat).
    /// </summary>
    private Mesh BuildCornerLQuoinMesh(
        WallStoneModuleDefinition module,
        float armLength,
        float legThickness,
        float height,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        float L = Mathf.Max(0.02f, armLength);
        float t = Mathf.Clamp(legThickness, 0.02f, Mathf.Max(0.02f, L * 0.72f));
        if (height <= 0.01f)
            return null;

        float halfH = height * 0.5f;
        float relief = module != null ? module.frontRelief : 0.025f;
        // Keep displacement uniform per face so each vertical quad stays planar (per-vertex jitter twists the quad and shatters the mesh).
        float faceOffsetMax = Mathf.Min(planeJitter + relief, Mathf.Min(L, t) * 0.12f);

        // CCW outer boundary in XZ (view from +Y): inner building corner at (0,0).
        Vector2[] xz = new Vector2[6];
        xz[0] = new Vector2(0f, 0f);
        xz[1] = new Vector2(L, 0f);
        xz[2] = new Vector2(L, t);
        xz[3] = new Vector2(t, t);
        xz[4] = new Vector2(t, L);
        xz[5] = new Vector2(0f, L);

        List<Vector3> verts = new List<Vector3>(128);
        List<int> tris = new List<int>(256);
        List<Vector2> uvs = new List<Vector2>(128);

        // Top / bottom caps: two non-overlapping quads (union of horizontal strip + vertical strip above notch).
        void AddCap(float y, bool flipWinding)
        {
            // R1: [0,L]x[0,t] (non-crossed quad order).
            Vector3 r1_00 = new Vector3(0f, y, 0f);
            Vector3 r1_L0 = new Vector3(L, y, 0f);
            Vector3 r1_Lt = new Vector3(L, y, t);
            Vector3 r1_0t = new Vector3(0f, y, t);
            AddQuad(verts, tris, uvs, r1_00, r1_L0, r1_Lt, r1_0t, uvMetersPerUnit, flipWinding);

            // R2: [0,t]×[t,L] (remainder of vertical arm)
            if (L > t + 0.0001f)
            {
                Vector3 r2_0t = new Vector3(0f, y, t);
                Vector3 r2_tt = new Vector3(t, y, t);
                Vector3 r2_tL = new Vector3(t, y, L);
                Vector3 r2_0L = new Vector3(0f, y, L);
                AddQuad(verts, tris, uvs, r2_0t, r2_tt, r2_tL, r2_0L, uvMetersPerUnit, flipWinding);
            }
        }

        // With AddQuad default winding on XZ plane, normal points -Y.
        AddCap(-halfH, false); // bottom -> -Y
        AddCap(halfH, true);   // top -> +Y

        // Vertical perimeter: one quad per edge with outward horizontal displacement (3D stone look).
        for (int i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            Vector2 p0 = xz[i];
            Vector2 p1 = xz[j];
            float dx = p1.x - p0.x;
            float dz = p1.y - p0.y;
            if (dx * dx + dz * dz < 1e-10f)
                continue;

            // CCW footprint in XZ: outward in the horizontal plane is perpendicular to (dx,dz).
            Vector3 outward = new Vector3(dz, 0f, -dx);
            outward.Normalize();

            float rFace = RandomRange(rng, 0f, faceOffsetMax);

            // Planar quad: one offset per face. Vertex order a→b→c→d is CCW on the face when viewed from
            // outside (normal ≈ outward), matching AddQuad's triangulation.
            Vector3 a = new Vector3(p0.x, -halfH, p0.y) + outward * rFace;
            Vector3 b = new Vector3(p0.x, halfH, p0.y) + outward * rFace;
            Vector3 c = new Vector3(p1.x, halfH, p1.y) + outward * rFace;
            Vector3 d = new Vector3(p1.x, -halfH, p1.y) + outward * rFace;

            AddQuad(verts, tris, uvs, a, b, c, d, uvMetersPerUnit);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedCornerLStone";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uvMetersPerUnit,
        bool flipWinding)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        Vector3 edgeU = b - a;
        Vector3 edgeV = d - a;
        float u1 = edgeU.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        float v1 = edgeV.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(u1, 0f));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(0f, v1));

        if (!flipWinding)
        {
            tris.Add(start + 0);
            tris.Add(start + 1);
            tris.Add(start + 2);
            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }
        else
        {
            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);
            tris.Add(start + 0);
            tris.Add(start + 3);
            tris.Add(start + 2);
        }
    }

    private Mesh BuildExtrudedPolygonMesh(Vector3[] front, Vector3[] back, float uvMetersPerUnit, bool includeBackCap = true)
    {
        if (front == null || back == null || front.Length < 3 || back.Length != front.Length)
            return null;

        List<Vector3> verts = new List<Vector3>(front.Length * 10);
        List<int> tris = new List<int>(front.Length * 18);
        List<Vector2> uvs = new List<Vector2>(front.Length * 10);

        AddPolygonFace(verts, tris, uvs, front, true, uvMetersPerUnit);
        if (includeBackCap)
            AddPolygonFace(verts, tris, uvs, back, false, uvMetersPerUnit);

        for (int i = 0; i < front.Length; i++)
        {
            int next = (i + 1) % front.Length;
            AddDoubleSidedQuad(verts, tris, uvs, front[i], front[next], back[next], back[i], uvMetersPerUnit);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedExtrudedStone";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Like <see cref="BuildExtrudedPolygonMesh"/> but vertical bands use the same height-map relief as corner quoins.
    /// Top/bottom caps stay flat triangulated polygons.
    /// </summary>
    private Mesh BuildExtrudedPolygonReliefMesh(
        Vector3[] front,
        Vector3[] back,
        float uvMetersPerUnit,
        float planeJitter,
        float relief,
        System.Random rng,
        bool includeBackCap = true)
    {
        if (front == null || back == null || front.Length < 3 || back.Length != front.Length)
            return null;

        List<Vector3> verts = new List<Vector3>(front.Length * 40);
        List<int> tris = new List<int>(front.Length * 72);
        List<Vector2> uvs = new List<Vector2>(front.Length * 40);

        AddPolygonFace(verts, tris, uvs, front, true, uvMetersPerUnit);
        if (includeBackCap)
            AddPolygonFace(verts, tris, uvs, back, false, uvMetersPerUnit);

        for (int i = 0; i < front.Length; i++)
        {
            int next = (i + 1) % front.Length;
            Vector3 a = front[i];
            Vector3 b = front[next];
            Vector3 c = back[next];
            Vector3 d = back[i];

            Vector3 outward = Vector3.Cross(b - a, d - a).normalized;
            float faceSpan = Mathf.Min((b - a).magnitude, (d - a).magnitude);
            float reliefDepth = Mathf.Clamp((planeJitter + relief) * 1.55f, 0.0015f, Mathf.Max(0.0015f, faceSpan * 0.28f));
            AddDoubleSidedReliefQuad(verts, tris, uvs, a, b, c, d, outward, reliefDepth, uvMetersPerUnit, rng, stoneReliefFaceGrid);
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedExtrudedStoneRelief";
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddPolygonFace(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3[] points,
        bool frontFace,
        float uvMetersPerUnit)
    {
        int start = verts.Count;
        for (int i = 0; i < points.Length; i++)
        {
            verts.Add(points[i]);
            uvs.Add(new Vector2(points[i].x / uvMetersPerUnit, points[i].y / uvMetersPerUnit));
        }

        for (int i = 1; i < points.Length - 1; i++)
        {
            if (frontFace)
            {
                tris.Add(start + 0);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
            else
            {
                tris.Add(start + 0);
                tris.Add(start + i + 1);
                tris.Add(start + i);
            }
        }
    }

    private void AddDoubleSidedQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uvMetersPerUnit)
    {
        AddQuad(verts, tris, uvs, a, b, c, d, uvMetersPerUnit);
        AddQuad(verts, tris, uvs, a, d, c, b, uvMetersPerUnit);
    }

    private void AddDoubleSidedReliefQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 outward,
        float maxRelief,
        float uvMetersPerUnit,
        System.Random rng,
        int reliefGrid)
    {
        AddReliefQuad(verts, tris, uvs, a, b, c, d, outward, maxRelief, uvMetersPerUnit, rng, reliefGrid, false);
        AddReliefQuad(verts, tris, uvs, a, d, c, b, -outward, maxRelief, uvMetersPerUnit, rng, reliefGrid, false);
    }

    private void AddReliefQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 reliefNormal,
        float maxRelief,
        float uvMetersPerUnit,
        System.Random rng,
        int reliefGrid,
        bool flipWinding)
    {
        int grid = Mathf.Clamp(reliefGrid, 2, 5);
        int start = verts.Count;
        float uLen = (b - a).magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        float vLen = (d - a).magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        // Low-frequency per-face noise corners (bilerp) to avoid sharp vertex spikes.
        float n00 = RandomRange(rng, -1f, 1f);
        float n10 = RandomRange(rng, -1f, 1f);
        float n01 = RandomRange(rng, -1f, 1f);
        float n11 = RandomRange(rng, -1f, 1f);

        for (int y = 0; y < grid; y++)
        {
            float v = y / (float)(grid - 1);
            for (int x = 0; x < grid; x++)
            {
                float u = x / (float)(grid - 1);
                Vector3 p = Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);

                // Border pinned to zero so quads stitch watertight.
                float w = 0f;
                if (x > 0 && x < grid - 1 && y > 0 && y < grid - 1)
                {
                    float ux = u * 2f - 1f;
                    float vy = v * 2f - 1f;
                    float radial = Mathf.Clamp01(Mathf.Sqrt(ux * ux + vy * vy));

                    // Broad plateau in the middle + subtle inward ring near border.
                    float plateau = Mathf.Pow(1f - radial, 0.70f) * 0.56f;
                    float ring = -Mathf.Clamp01((radial - 0.48f) / 0.45f) * 0.14f;

                    float nx0 = Mathf.Lerp(n00, n10, u);
                    float nx1 = Mathf.Lerp(n01, n11, u);
                    float noise = Mathf.Lerp(nx0, nx1, v) * 0.11f; // smooth, no single-vertex spike

                    w = plateau + ring + noise;

                    // Explicitly soften center so it never forms a needle.
                    if (x == grid / 2 && y == grid / 2)
                        w *= 0.86f;

                    w = Mathf.Clamp(w, -0.24f, 0.66f);
                }

                p += reliefNormal * (maxRelief * w);
                verts.Add(p);
                uvs.Add(new Vector2(u * uLen, v * vLen));
            }
        }

        for (int y = 0; y < grid - 1; y++)
        {
            for (int x = 0; x < grid - 1; x++)
            {
                int i0 = start + y * grid + x;
                int i1 = i0 + 1;
                int i2 = i0 + grid + 1;
                int i3 = i0 + grid;

                if (!flipWinding)
                {
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i0); tris.Add(i2); tris.Add(i3);
                }
                else
                {
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i0); tris.Add(i3); tris.Add(i2);
                }
            }
        }
    }

    private void AddQuad(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float uvMetersPerUnit)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        Vector3 edgeU = b - a;
        Vector3 edgeV = d - a;
        float u1 = edgeU.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);
        float v1 = edgeV.magnitude / Mathf.Max(0.0001f, uvMetersPerUnit);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(u1, 0f));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(0f, v1));

        tris.Add(start + 0);
        tris.Add(start + 1);
        tris.Add(start + 2);
        tris.Add(start + 0);
        tris.Add(start + 2);
        tris.Add(start + 3);
    }

    private void CreateStoneObject(
        WallCladdingProfile profile,
        Transform root,
        Material stoneMaterial,
        List<PathSample> samples,
        float sideSign,
        StonePlacement placement,
        System.Random rng,
        int index,
        bool rigidFacePlacement = false)
    {
        WallFrame frame = GetFrameAtDistance(samples, placement.centerDistance, sideSign);
        float halfThickness = Mathf.Max(0.01f, wall.thickness) * 0.5f;

        Vector3 wallFacePoint = frame.centerline + Vector3.up * placement.centerY;
        wallFacePoint += frame.faceNormal * (halfThickness - profile.general.sideInset);

        Vector3 center;
        Quaternion rot = Quaternion.LookRotation(frame.faceNormal, Vector3.up);

        float meshEmbed = placement.embed;

        if (!rigidFacePlacement)
        {
            float throughWallEmbed = Mathf.Max(
                placement.embed,
                wall.thickness + Mathf.Max(profile.stone.surfaceProtrusion, 0.02f));
            if (IsInteriorDecorativeWall())
                throughWallEmbed = Mathf.Min(throughWallEmbed, GetInteriorDecorativeMaxStoneEmbed());

            float centerOffset = ((placement.protrusion - placement.embed) * 0.5f) + profile.general.depthOffset;
            center = wallFacePoint + frame.faceNormal * centerOffset;

            center += frame.tangent * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);
            center += Vector3.up * RandomRange(rng, -profile.stone.positionJitter, profile.stone.positionJitter);

            rot *= Quaternion.Euler(
                RandomRange(rng, -profile.stone.randomPitch, profile.stone.randomPitch),
                RandomRange(rng, -profile.stone.randomYaw, profile.stone.randomYaw),
                RandomRange(rng, -profile.stone.randomRoll, profile.stone.randomRoll));

            meshEmbed = throughWallEmbed;
        }
        else
        {
            meshEmbed = Mathf.Max(placement.embed, wall.thickness + Mathf.Max(profile.stone.surfaceProtrusion, 0.02f));
            if (IsInteriorDecorativeWall())
                meshEmbed = Mathf.Min(meshEmbed, GetInteriorDecorativeMaxStoneEmbed());
            // For rigid connector/filler stones, anchor from the wall face
            // to keep a balanced read on both sides.
            float centerOffset = profile.general.depthOffset;
            center = wallFacePoint + frame.faceNormal * centerOffset;
        }

        Vector3 up = rot * Vector3.up;
        Vector3 normal = rot * Vector3.forward;
        float approxRadius = Mathf.Max(0.08f, Mathf.Max(placement.width, placement.height) * 0.6f + Mathf.Max(placement.protrusion, meshEmbed) * 0.25f);
        if (ShouldSkipStoneFromCamera(center, approxRadius))
            return;

        bool dualRuntimeLod = ShouldAttachPerStoneRuntimeLod();

        bool useFarLodAtGen = !dualRuntimeLod && ShouldUseFarLod(center);

        Mesh mesh;
        if (useFarLodAtGen)
        {
            mesh = BuildLowDetailStoneMesh(
                placement.width,
                placement.height,
                placement.protrusion,
                meshEmbed,
                GetEffectiveUvMetersPerUnit(profile));
        }
        else if (!rigidFacePlacement && placement.useTerminalHalfRound)
        {
            Vector3 localRightWorld = rot * Vector3.right;
            bool localRightIsPositiveDistance = Vector3.Dot(localRightWorld, frame.tangent) >= 0f;
            bool roundRightSide = placement.terminalRoundTowardPositiveDistance
                ? localRightIsPositiveDistance
                : !localRightIsPositiveDistance;

            mesh = BuildTerminalHalfRoundStoneMesh(
                placement.module,
                placement.width,
                placement.height,
                placement.protrusion,
                meshEmbed,
                profile.stone.facePlaneJitter,
                GetEffectiveUvMetersPerUnit(profile),
                rng,
                roundRightSide);
        }
        else
        {
            mesh = BuildStoneMesh(
                placement.module,
                placement.width,
                placement.height,
                placement.protrusion,
                meshEmbed,
                profile.stone.facePlaneJitter,
                GetEffectiveUvMetersPerUnit(profile),
                rng);
        }

        if (mesh == null || mesh.vertexCount == 0)
            return;

        Mesh meshLow = null;
        if (dualRuntimeLod)
        {
            meshLow = BuildLowDetailStoneMesh(
                placement.width,
                placement.height,
                placement.protrusion,
                meshEmbed,
                GetEffectiveUvMetersPerUnit(profile));
            if (meshLow == null || meshLow.vertexCount == 0)
            {
                DestroyObjectSafe(meshLow);
                meshLow = null;
            }
        }

        GameObject go = new GameObject($"Stone_{index:000}");
        go.transform.SetParent(root, false);
        go.transform.localPosition = transform.InverseTransformPoint(center);
        go.transform.localRotation = Quaternion.LookRotation(
            transform.InverseTransformDirection(normal),
            transform.InverseTransformDirection(up));
        go.transform.localScale = Vector3.one;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = stoneMaterial;

        if (forceDoubleSidedStoneMaterials && stoneMaterial != null)
            ApplyMaterialDoubleSided(stoneMaterial);

        ApplyPerStoneMaterialVariation(profile, mr, rng, false);

        if (dualRuntimeLod && meshLow != null)
        {
            WallCladdingStoneLod lod = go.AddComponent<WallCladdingStoneLod>();
            lod.Initialize(mf, mesh, meshLow);
            FinalizeGeneratedMeshForGpu(mesh);
            FinalizeGeneratedMeshForGpu(meshLow);
        }
        else
        {
            FinalizeGeneratedMeshForGpu(mesh);
        }

        if (_effectiveCombineStonesThisRebuild && profile != null && mf != null && mf.sharedMesh != null)
            ApplyPerStoneTintAsVertexColors(mf.sharedMesh, profile, rng, false);
    }

    /// <summary>
    /// Same HSV / palette rules as <see cref="ApplyPerStoneMaterialVariation"/> (MPB path when meshes are not combined).
    /// </summary>
    static Color ComputePerStoneTint(WallCladdingProfile profile, System.Random rng, bool isQuoinOrEndCapStone = false)
    {
        Color tint = profile.stone.baseTint;
        if (isQuoinOrEndCapStone && profile.stone.useSeparateTintForQuoins)
            tint = profile.stone.quoinBaseTint;

        if (profile.stone.enablePerStoneColorVariation)
        {
            Color.RGBToHSV(tint, out float h, out float s, out float v);

            float paletteRoll = RandomValue(rng);
            if (paletteRoll < 0.22f)
            {
                s *= 0.70f;
                v += 0.10f;
            }
            else if (paletteRoll < 0.44f)
            {
                s *= 0.85f;
                v += 0.04f;
            }
            else if (paletteRoll < 0.72f)
            {
                v -= 0.02f;
            }
            else
            {
                s += 0.03f;
                v -= 0.05f;
            }

            h = Mathf.Repeat(h + RandomRange(rng, -profile.stone.hueJitter, profile.stone.hueJitter), 1f);
            s = Mathf.Clamp01(s + RandomRange(rng, -profile.stone.saturationJitter, profile.stone.saturationJitter));
            v = Mathf.Clamp01(v + RandomRange(rng, -profile.stone.valueJitter, profile.stone.valueJitter));
            tint = Color.HSVToRGB(h, s, v);
        }

        return tint;
    }

    /// <summary>
    /// Quand le merge par côté est réellement effectif (<see cref="_effectiveCombineStonesThisRebuild"/>), MPB ne peut pas varier par pierre :
    /// on bake la teinte dans les vertex colors (shader lit vertex tint). Sinon la teinte passe par <see cref="ApplyPerStoneMaterialVariation"/>.
    /// </summary>
    static void ApplyPerStoneTintAsVertexColors(Mesh mesh, WallCladdingProfile profile, System.Random rng, bool isQuoinOrEndCapStone = false)
    {
        if (mesh == null || mesh.vertexCount <= 0 || profile == null)
            return;

        Color tint = ComputePerStoneTint(profile, rng, isQuoinOrEndCapStone);
        int n = mesh.vertexCount;
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
            colors[i] = tint;

        mesh.colors = colors;
    }

    private void ApplyPerStoneMaterialVariation(WallCladdingProfile profile, MeshRenderer mr, System.Random rng, bool isQuoinOrEndCapStone)
    {
        if (mr == null || profile == null || propertyBlock == null)
            return;

        // En combine réussi : teinte dans les vertex colors (un mesh fusionné). Sinon : MPB par pierre.
        if (_effectiveCombineStonesThisRebuild)
            return;

        propertyBlock.Clear();
        Color tint = ComputePerStoneTint(profile, rng, isQuoinOrEndCapStone);
        propertyBlock.SetColor("_BaseColor", tint);
        mr.SetPropertyBlock(propertyBlock);
    }

    private Mesh BuildStoneMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float protrusion,
        float embed,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng)
    {
        if (module == null)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = protrusion;
        float backZ = -embed;

        if (useSimpleRectangularFieldStones)
        {
            float frontJitter = planeJitter + module.frontRelief;
            Vector3[] front = new Vector3[4];
            front[0] = new Vector3(-halfW, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[1] = new Vector3( halfW, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[2] = new Vector3( halfW,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[3] = new Vector3(-halfW,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));

            float backJitter = frontJitter;
            Vector3[] back = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                back[i] = new Vector3(
                    front[i].x,
                    front[i].y,
                    backZ + RandomRange(rng, 0f, backJitter));
            }

            Mesh simpleMesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit, includeBackCap: IncludeStoneBackCapInExtrusion());
            if (simpleMesh != null)
                simpleMesh.name = "GeneratedStone_SimpleRect";
            return simpleMesh;
        }
        else
        {
            float cutMin = Mathf.Clamp01(module.minCornerCut * 1.18f);
            float cutMax = Mathf.Clamp(module.maxCornerCut * 1.24f, cutMin, 0.45f);
            float cutBL = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
            float cutBR = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
            float cutTR = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());
            float cutTL = Mathf.Lerp(cutMin, cutMax, (float)rng.NextDouble());

            float frontJitter = planeJitter + module.frontRelief;

            Vector3[] front = new Vector3[8];
            front[0] = new Vector3(-halfW + width * cutBL, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[1] = new Vector3( halfW - width * cutBR, -halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[2] = new Vector3( halfW, -halfH + height * cutBR, frontZ + RandomRange(rng, 0f, frontJitter));
            front[3] = new Vector3( halfW,  halfH - height * cutTR, frontZ + RandomRange(rng, 0f, frontJitter));
            front[4] = new Vector3( halfW - width * cutTR,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[5] = new Vector3(-halfW + width * cutTL,  halfH, frontZ + RandomRange(rng, 0f, frontJitter));
            front[6] = new Vector3(-halfW,  halfH - height * cutTL, frontZ + RandomRange(rng, 0f, frontJitter));
            front[7] = new Vector3(-halfW, -halfH + height * cutBL, frontZ + RandomRange(rng, 0f, frontJitter));

            float backJitter = frontJitter;

            Vector3[] back = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                back[i] = new Vector3(
                    front[i].x,
                    front[i].y,
                    backZ + RandomRange(rng, 0f, backJitter));
            }

            Mesh mesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit, includeBackCap: IncludeStoneBackCapInExtrusion());
            if (mesh != null)
                mesh.name = "GeneratedStone";

            return mesh;
        }
    }

    private Mesh BuildLowDetailStoneMesh(
        float width,
        float height,
        float protrusion,
        float embed,
        float uvMetersPerUnit)
    {
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = protrusion;
        float backZ = -embed;

        Vector3[] front = new Vector3[4];
        front[0] = new Vector3(-halfW, -halfH, frontZ);
        front[1] = new Vector3( halfW, -halfH, frontZ);
        front[2] = new Vector3( halfW,  halfH, frontZ);
        front[3] = new Vector3(-halfW,  halfH, frontZ);

        if (UseLowDetailFrontFaceOnlyNow())
        {
            Mesh meshOnly = BuildPolygonFrontFaceOnlyMesh(front, uvMetersPerUnit);
            if (meshOnly != null)
                meshOnly.name = "GeneratedStone_LOD";
            return meshOnly;
        }

        Vector3[] back = new Vector3[4];
        back[0] = new Vector3(-halfW, -halfH, backZ);
        back[1] = new Vector3( halfW, -halfH, backZ);
        back[2] = new Vector3( halfW,  halfH, backZ);
        back[3] = new Vector3(-halfW,  halfH, backZ);

        Mesh mesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit, includeBackCap: IncludeStoneBackCapInExtrusion());
        if (mesh != null)
            mesh.name = "GeneratedStone_LOD";
        return mesh;
    }

    /// <summary>
    /// Single-sided façade only (+Z normal in stone local space): no rear cap, no extruded rim.
    /// </summary>
    private Mesh BuildPolygonFrontFaceOnlyMesh(Vector3[] front, float uvMetersPerUnit)
    {
        if (front == null || front.Length < 3)
            return null;

        List<Vector3> verts = new List<Vector3>(front.Length);
        List<int> tris = new List<int>((front.Length - 1) * 3);
        List<Vector2> uvs = new List<Vector2>(front.Length);
        AddPolygonFace(verts, tris, uvs, front, true, uvMetersPerUnit);

        Mesh mesh = new Mesh { name = "GeneratedStone_FrontOnly" };
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildTerminalHalfRoundStoneMesh(
        WallStoneModuleDefinition module,
        float width,
        float height,
        float protrusion,
        float embed,
        float planeJitter,
        float uvMetersPerUnit,
        System.Random rng,
        bool roundRightSide)
    {
        if (module == null)
            return null;

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float frontZ = protrusion;
        float backZ = -embed;
        float frontJitter = planeJitter + module.frontRelief;

        float arcRadius = Mathf.Min(halfH, width * 0.42f);
        arcRadius = Mathf.Max(0.008f, arcRadius);
        int arcSegments = Mathf.Clamp(Mathf.Max(3, Mathf.RoundToInt(6f * stoneMeshTriangleRetention)), 3, 8);

        List<Vector2> contour = new List<Vector2>(arcSegments + 8);
        if (roundRightSide)
        {
            contour.Add(new Vector2(-halfW, -halfH));
            contour.Add(new Vector2(halfW - arcRadius, -halfH));
            for (int s = 0; s <= arcSegments; s++)
            {
                float t = s / (float)arcSegments;
                float ang = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                float x = (halfW - arcRadius) + Mathf.Cos(ang) * arcRadius;
                float y = Mathf.Sin(ang) * arcRadius;
                contour.Add(new Vector2(x, y));
            }
            contour.Add(new Vector2(halfW - arcRadius, halfH));
            contour.Add(new Vector2(-halfW, halfH));
        }
        else
        {
            contour.Add(new Vector2(halfW, -halfH));
            contour.Add(new Vector2(-halfW + arcRadius, -halfH));
            for (int s = 0; s <= arcSegments; s++)
            {
                float t = s / (float)arcSegments;
                float ang = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                float x = (-halfW + arcRadius) - Mathf.Cos(ang) * arcRadius;
                float y = Mathf.Sin(ang) * arcRadius;
                contour.Add(new Vector2(x, y));
            }
            contour.Add(new Vector2(-halfW + arcRadius, halfH));
            contour.Add(new Vector2(halfW, halfH));
        }

        int n = contour.Count;
        Vector3[] front = new Vector3[n];
        Vector3[] back = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 p = contour[i];
            front[i] = new Vector3(
                p.x,
                p.y,
                frontZ + RandomRange(rng, 0f, frontJitter));
            back[i] = new Vector3(
                p.x,
                p.y,
                backZ + RandomRange(rng, 0f, frontJitter));
        }

        Mesh mesh = BuildExtrudedPolygonMesh(front, back, uvMetersPerUnit, includeBackCap: IncludeStoneBackCapInExtrusion());
        if (mesh != null)
            mesh.name = "GeneratedStone_TerminalHalfRound";
        return mesh;
    }

    /// <summary>
    /// For closed loops, prefer small modules only near real polyline vertices — not near the
    /// artificial parameter seam (usableStart / usableEnd), which otherwise reads as a "corner"
    /// twice and stacks thin fillers in a vertical line.
    /// </summary>
    private static bool IsDistanceNearAnyPathVertex(
        List<PathSample> samples,
        float distanceAlong,
        float totalLength,
        float zone)
    {
        if (samples == null || samples.Count == 0 || zone <= 0f || totalLength <= 0.0001f)
            return false;

        float d = Mathf.Repeat(distanceAlong, totalLength);
        for (int i = 0; i < samples.Count; i++)
        {
            float vd = Mathf.Repeat(samples[i].startDistance, totalLength);
            float delta = Mathf.Abs(d - vd);
            float wrap = Mathf.Min(delta, totalLength - delta);
            if (wrap < zone)
                return true;
        }

        return false;
    }

    private WallFrame GetFrameAtDistance(List<PathSample> samples, float distance, float sideSign)
    {
        PathSample s = samples[samples.Count - 1];

        for (int i = 0; i < samples.Count; i++)
        {
            if (distance <= samples[i].endDistance || i == samples.Count - 1)
            {
                s = samples[i];
                break;
            }
        }

        float t = Mathf.InverseLerp(s.startDistance, s.endDistance, distance);
        Vector3 center = Vector3.Lerp(s.a, s.b, t);
        Vector3 tangent = s.tangent;
        Vector3 faceNormal = Vector3.Cross(Vector3.up, tangent).normalized * sideSign;

        return new WallFrame
        {
            centerline = center,
            tangent = tangent,
            faceNormal = faceNormal,
        };
    }

    private int ComputeGeometryHash()
    {
        unchecked
        {
            int hash = 17;

            if (wall != null)
            {
                hash = hash * 31 + Mathf.RoundToInt(wall.height * 1000f);
                hash = hash * 31 + Mathf.RoundToInt(wall.thickness * 1000f);
                hash = hash * 31 + (wall.closedLoop ? 1 : 0);
                hash = hash * 31 + Mathf.RoundToInt(exteriorCladMinYFromWallBaseMeters * 1000f);

                IReadOnlyList<Vector3> pts = wall.Points;
                if (pts != null)
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector3 p = pts[i];
                        hash = hash * 31 + Mathf.RoundToInt(p.x * 1000f);
                        hash = hash * 31 + Mathf.RoundToInt(p.y * 1000f);
                        hash = hash * 31 + Mathf.RoundToInt(p.z * 1000f);
                    }
                }
            }

            WallCladdingProfile profile = runtime != null ? runtime.CurrentProfile : defaultProfile;
            hash = hash * 31 + (profile != null ? profile.GetInstanceID() : 0);
            hash = hash * 31 + (generateOutside ? 1 : 0);
            hash = hash * 31 + (generateInside ? 1 : 0);
            hash = hash * 31 + (keepFullStoneGeometryBothSides ? 1 : 0);
            hash = hash * 31 + (fullDetailOmitBackCap ? 1 : 0);
            hash = hash * 31 + (lowDetailStoneFrontFaceOnly ? 1 : 0);
            hash = hash * 31 + (forceDoubleSidedStoneMaterials ? 1 : 0);
            hash = hash * 31 + Mathf.RoundToInt(stoneMeshTriangleRetention * 1000f);
            hash = hash * 31 + stoneReliefFaceGrid;
            hash = hash * 31 + (useSimpleRectangularFieldStones ? 1 : 0);
            hash = hash * 31 + Mathf.RoundToInt(fieldStoneUvTilingBoost * 1000f);
            hash = hash * 31 + maxCladdingClosedLoopPathVertices;
            hash = hash * 31 + Mathf.RoundToInt(GetEffectiveBuildingScale() * 1000f);

            return hash;
        }
    }

    private int ComputeStableSeed(WallCladdingProfile profile)
    {
        unchecked
        {
            int hash = 23;
            hash = hash * 31 + (profile != null && !string.IsNullOrEmpty(profile.profileId) ? profile.profileId.GetHashCode() : 0);
            hash = hash * 31 + Mathf.RoundToInt((profile != null ? profile.general.randomSeedOffset : 0f) * 1000f);
            hash = hash * 31 + Mathf.RoundToInt((wall != null ? wall.height : 0f) * 100f);
            hash = hash * 31 + Mathf.RoundToInt((wall != null ? wall.thickness : 0f) * 100f);
            hash = hash * 31 + gameObject.GetInstanceID();
            return hash;
        }
    }

    private static float RandomRange(System.Random rng, float min, float max)
    {
        if (Mathf.Approximately(min, max))
            return min;

        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }

    private static float RandomValue(System.Random rng)
    {
        return (float)rng.NextDouble();
    }
}
