using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class WallDrawInput : MonoBehaviour
{
    public enum DetectedShapeKind
    {
        Free,
        StraightLine,
        Rectangle,
        Square,
        Circle,
        Triangle,
        /// <summary>Open circular arc (e.g. semi-circle) fitted by auto-open-arc.</summary>
        OpenArc
    }

    [Header("References")]
    public Camera cam;
    public Collider groundCollider;
    [Tooltip("Optionnel : aimantation des tracés sur les sommets des murs existants + fusion des lots au relâchement sur un coin.")]
    public WallBuildController wallBuild;

    [Header("Drawing")]
    [Min(0.01f)] public float pointSpacing = 0.35f;
    [Min(0.01f)] public float snapCloseDistance = 1.0f;
    public bool flattenYToZero = true;

    [Header("Line Preview")]
    [Min(0.001f)] public float lineWidth = 0.12f;

    [Header("Snap — grille grise (HierarchicalGrid)")]
    [Tooltip("Active l’aimantation (désactive tout le snap si off).")]
    public bool enableGridSnap = true;
    [Tooltip("Aimante sur les cellules feuilles visibles : 9 points par carré (pas = côté feuille / 2).")]
    public bool snapToHierarchicalVisualGrid = true;
    [Tooltip("Si vide, premier HierarchicalGridManager dans la scène. Sans manager : pas de snap grille.")]
    public HierarchicalGridManager hierarchicalGrid;
    [Tooltip("Pendant le clic maintenu : pas d’aimantation ; au relâchement, projection sur la grille 9 points des feuilles.")]
    public bool snapOnlyOnCommitWhileDrawing = true;
    [Tooltip("Si vrai : ne coller aux coins des murs existants qu’au relâchement du tracé (EndDraw), pas à chaque pas pendant ContinueDraw — évite que le segment suive un mur voisin avant d’avoir lâché le clic.")]
    public bool snapDrawToWallCornersOnlyOnCommit = true;
    [Tooltip("Rectangles alignés axes : recaler les coins sur la grille 9 points (feuille sous chaque coin).")]
    public bool alignAxisAlignedClosedShapesToMainGridCells = true;

    [Header("Grille — overlay LineRenderer (mailles feuilles)")]
    [Tooltip("Dessine les bords des cellules feuilles autour de la caméra. Masque le maillage hiérarchique pour éviter le double.")]
    public bool showGridInGame = false;
    public bool showGridGizmos = true;
    [Range(4, 200)] public int gridHalfExtent = 25;
    [Range(24, 220)] public int gridMaxLinesPerAxis = 120;
    [Range(0.0005f, 0.20f)] public float gridLineWidth = 0.02f;
    [Range(0.02f, 1.0f)] public float gridInnerAlpha = 0.38f;
    [Range(0.01f, 0.8f)] public float gridOuterAlpha = 0.06f;
    [Range(-0.05f, 0.2f)] public float gridVisualYOffset = 0.01f;

    [Header("Auto Shapes")]
    public bool enableAutoShapes = true;
    public bool useGridShapeDetectionOnlyWhenGridSnap = true;
    public bool requireClosedLoop = true;
    [Range(0.01f, 0.5f)] public float tolerance = 0.12f;
    public bool autoStraightLine = true;
    public bool autoCircle = true;
    public bool autoRectangle = true;
    public bool autoTriangle = true;

    [Header("Straight Line")]
    [Range(0.005f, 0.2f)] public float straightLineToleranceMultiplier = 0.45f;
    [Tooltip("Open strokes: if every point stays within (this × grid size) of the line from first to last point, collapse to that segment. Fixes grid-snapped diagonal staircases (zigzag) before straight-line fitting.")]
    [Range(0.65f, 1.15f)] public float openStrokeChordDeviationGridMul = 0.9f;

    [Header("Open Arc")]
    public bool autoOpenArc = true;
    [Tooltip("Segments for fitted open arcs. Fewer segments = lighter meshes.")]
    [Range(24, 196)] public int openArcResolution = 40;
    [Tooltip("Radial error vs grid: higher = easier to accept hand-drawn / grid-snapped arcs.")]
    [Range(0.02f, 0.55f)] public float openArcFitTolerance = 0.26f;
    [Tooltip("Minimum bulge (sagitta) vs grid for an arc to be considered curved.")]
    [Range(0.08f, 0.75f)] public float openArcMinSagittaGridMul = 0.28f;
    [Range(10f, 220f)] public float openArcMinSweepDeg = 18f;
    [Range(60f, 350f)] public float openArcMaxSweepDeg = 300f;
    [Tooltip("When both line and arc fit, prefer arc if arcErr <= lineErr × this.")]
    [Range(1f, 2f)] public float openArcPreferOverLineMul = 1.32f;

    [Header("Circle")]
    [Tooltip("Vertices on the fitted circle path. Higher = smoother circle but more wall mesh triangles (~linear). Try 24–36 for balance.")]
    [Range(16, 128)] public int circleResolution = 24;
    [Tooltip("Higher = more tolerant radial deviation from a perfect circle (grid / hand-draw).")]
    [Range(0.01f, 0.5f)] public float circleStrictnessMultiplier = 0.24f;
    [Tooltip("Score multiplier for circle candidate vs other closed shapes.")]
    [Range(1.0f, 1.5f)] public float circleDetectionBoost = 1.14f;

    [Header("Rectangle")]
    [Tooltip("Samples per edge on fitted rectangle/square. Lower = fewer mesh segments (4× this vertices along the loop).")]
    [Range(2, 30)] public int rectPointsPerEdge = 4;
    [Tooltip("Max |w−h|/max(w,h) to classify as Square. Lower = fewer false squares.")]
    [Range(0.0f, 0.4f)] public float squareRatioTolerance = 0.095f;
    [Tooltip("When auto-fit classifies a Square, score is multiplied by this so Circle can win a bit more often.")]
    [Range(0.88f, 1f)] public float squareClassificationScoreMul = 0.96f;
    [Range(0.20f, 0.80f)] public float minRectangleProbability = 0.40f;
    [Range(0.20f, 0.98f)] public float rectangleCornerBoost = 0.70f;
    [Range(0.0f, 1.0f)] public float rectangleRoundPenalty = 0.45f;

    [Header("Triangle")]
    [Range(0.5f, 8.0f)] public float triangleToleranceMultiplier = 4.4f;
    [Range(4, 32)] public int roundedTriangleMaxCurvePoints = 8;
    [Range(40f, 170f)] public float roundedTriangleMaxApexAngle = 142f;
    [Range(0.10f, 0.80f)] public float minTriangleProbability = 0.18f;
    [Tooltip("When stroke is radially round, triangle score scales toward this factor (vs 1). Lower = less triangle vs circles.")]
    [Range(0.55f, 1f)] public float triangleMinScoreWhenStrokeIsCircular = 0.72f;

    [Header("Shape Decision")]
    [Range(0.0f, 1.0f)] public float minClosedShapeConfidence = 0.22f;
    [Range(0.0f, 0.5f)] public float minClosedShapeLead = 0.05f;
    [Tooltip("Minimum score for Circle to be accepted as best closed shape (lower = easier).")]
    [Range(0.10f, 0.80f)] public float minCircleProbability = 0.24f;
    [Tooltip("Closed-loop resampling count for shape scoring. Higher = finer recognition.")]
    [Range(24, 256)] public int closedShapeSampleCount = 96;

    [Header("Closed Shape Rejection")]
    [Range(1.0f, 2.5f)] public float maxPathToHullPerimeterRatio = 1.55f;
    public bool rejectSelfIntersectingClosedShapes = true;

    [Header("Probability Debug")]
    public bool alwaysShowProbabilities = true;
    public bool logDetectedShape = true;
    public bool logShapeScores = true;

    [Header("Live Scores (runtime)")]
    public string lastDetectedClosedShape = "None";
    [Range(0f, 100f)] public float lastRectangleProbability = 0f;
    [Range(0f, 100f)] public float lastTriangleProbability = 0f;
    [Range(0f, 100f)] public float lastCircleProbability = 0f;

    public event Action<List<Vector3>> OnShapeCommitted;
    public event Action<List<Vector3>, DetectedShapeKind, string> OnShapeCommittedDetailed;

    public DetectedShapeKind LastCommittedShape { get; private set; } = DetectedShapeKind.Free;
    public string LastCommittedShapeName { get; private set; } = "None";

    /// <summary>Vrai si le dernier <see cref="EndDraw"/> a recalé le relâchement sur un sommet de mur voisin (fusion possible côté <see cref="WallBuildController"/>).</summary>
    public bool LastCommitSnappedToWallCorner { get; private set; }

    public IReadOnlyList<Vector3> CurrentPoints => _points;

    private readonly List<Vector3> _points = new List<Vector3>();
    private bool _isDrawing;
    private LineRenderer _lr;

    Coroutine _menuBeginDrawCoroutine;
    bool _menuDeferredBeginDrawActive;

    private static Material s_SharedPreviewMaterial;
    private static Material s_SharedGridMaterial;

    private Transform _gridVisualRoot;
    private readonly List<LineRenderer> _gridLines = new List<LineRenderer>();
    private bool _gridVisualStateDirty = true;
    private bool _lastShowGridInGame;
    private bool _gridHasActiveLines;
    private Vector3 _lastGridVisualCamPos;
    private float _lastGridVisualCamHeight;

    HierarchicalGridManager _cachedHierarchicalGrid;
    float _nextHierarchicalGridRescanTime;
    bool _didHierarchicalGridSearch;

    struct ShapeCandidate
    {
        public string name;
        public float score;
        public List<Vector3> points;
    }

    struct RectFit
    {
        public Vector2 center;
        public Vector2 axisX;
        public Vector2 axisY;
        public float minX;
        public float maxX;
        public float minY;
        public float maxY;

        public float width => maxX - minX;
        public float height => maxY - minY;
    }

    struct CornerSample
    {
        public int index;
        public float turn;
        public float sharpness;
        public Vector2 point;
    }

    void Reset()
    {
        cam = Camera.main;
    }

    void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        ApplyLineRendererSetup();
        if (hierarchicalGrid == null)
            hierarchicalGrid = FindFirstObjectByType<HierarchicalGridManager>();
        if (wallBuild == null)
            wallBuild = FindFirstObjectByType<WallBuildController>();
        _cachedHierarchicalGrid = hierarchicalGrid;
        SyncHierarchicalGridMeshSuppression();
        UpdateGridVisuals();
    }

    void OnEnable()
    {
        _cachedHierarchicalGrid = null;
        _didHierarchicalGridSearch = false;
        _nextHierarchicalGridRescanTime = 0f;
        _gridVisualStateDirty = true;
        _lastShowGridInGame = showGridInGame;
    }

    void OnValidate()
    {
        pointSpacing = Mathf.Max(0.01f, pointSpacing);
        snapCloseDistance = Mathf.Max(0.01f, snapCloseDistance);
        lineWidth = Mathf.Max(0.001f, lineWidth);
        tolerance = Mathf.Clamp(tolerance, 0.01f, 0.5f);
        straightLineToleranceMultiplier = Mathf.Clamp(straightLineToleranceMultiplier, 0.005f, 0.2f);
        openStrokeChordDeviationGridMul = Mathf.Clamp(openStrokeChordDeviationGridMul, 0.65f, 1.15f);
        openArcResolution = Mathf.Clamp(openArcResolution, 24, 196);
        openArcFitTolerance = Mathf.Clamp(openArcFitTolerance, 0.02f, 0.55f);
        openArcMinSagittaGridMul = Mathf.Clamp(openArcMinSagittaGridMul, 0.08f, 0.75f);
        openArcMinSweepDeg = Mathf.Clamp(openArcMinSweepDeg, 10f, 220f);
        openArcMaxSweepDeg = Mathf.Clamp(openArcMaxSweepDeg, 60f, 350f);
        openArcPreferOverLineMul = Mathf.Clamp(openArcPreferOverLineMul, 1f, 2f);
        if (openArcMaxSweepDeg < openArcMinSweepDeg + 1f)
            openArcMaxSweepDeg = openArcMinSweepDeg + 1f;
        circleResolution = Mathf.Clamp(circleResolution, 16, 128);
        circleStrictnessMultiplier = Mathf.Clamp(circleStrictnessMultiplier, 0.01f, 0.5f);
        circleDetectionBoost = Mathf.Clamp(circleDetectionBoost, 1.0f, 1.5f);
        rectPointsPerEdge = Mathf.Clamp(rectPointsPerEdge, 2, 30);
        squareRatioTolerance = Mathf.Clamp(squareRatioTolerance, 0f, 0.4f);
        squareClassificationScoreMul = Mathf.Clamp(squareClassificationScoreMul, 0.88f, 1f);
        minRectangleProbability = Mathf.Clamp(minRectangleProbability, 0.20f, 0.80f);
        rectangleCornerBoost = Mathf.Clamp(rectangleCornerBoost, 0.20f, 0.98f);
        rectangleRoundPenalty = Mathf.Clamp01(rectangleRoundPenalty);
        triangleToleranceMultiplier = Mathf.Clamp(triangleToleranceMultiplier, 0.5f, 8.0f);
        roundedTriangleMaxCurvePoints = Mathf.Clamp(roundedTriangleMaxCurvePoints, 4, 32);
        roundedTriangleMaxApexAngle = Mathf.Clamp(roundedTriangleMaxApexAngle, 40f, 170f);
        minTriangleProbability = Mathf.Clamp(minTriangleProbability, 0.10f, 0.80f);
        triangleMinScoreWhenStrokeIsCircular = Mathf.Clamp(triangleMinScoreWhenStrokeIsCircular, 0.55f, 1f);
        minClosedShapeConfidence = Mathf.Clamp01(minClosedShapeConfidence);
        minClosedShapeLead = Mathf.Clamp(minClosedShapeLead, 0f, 0.5f);
        minCircleProbability = Mathf.Clamp(minCircleProbability, 0.10f, 0.80f);
        closedShapeSampleCount = Mathf.Clamp(closedShapeSampleCount, 24, 256);
        maxPathToHullPerimeterRatio = Mathf.Clamp(maxPathToHullPerimeterRatio, 1f, 2.5f);
        gridHalfExtent = Mathf.Clamp(gridHalfExtent, 4, 200);
        gridMaxLinesPerAxis = Mathf.Clamp(gridMaxLinesPerAxis, 24, 220);
        gridLineWidth = Mathf.Clamp(gridLineWidth, 0.0005f, 0.20f);
        gridInnerAlpha = Mathf.Clamp(gridInnerAlpha, 0.02f, 1f);
        gridOuterAlpha = Mathf.Clamp(gridOuterAlpha, 0.01f, 0.8f);
        gridVisualYOffset = Mathf.Clamp(gridVisualYOffset, -0.05f, 0.2f);

        if (_lr == null)
            _lr = GetComponent<LineRenderer>();

        if (_lr != null)
            ApplyLineRendererSetup();

        SyncHierarchicalGridMeshSuppression();
        _gridVisualStateDirty = true;

        // Avoid creating/updating runtime visuals during OnValidate.
        // Unity warns when Transform/AddComponent messages are triggered here,
        // and it can spam hundreds of operations.
    }

    void Update()
    {
        if (cam == null)
            return;

        SyncHierarchicalGridMeshSuppression();

        if (showGridInGame != _lastShowGridInGame)
        {
            _gridVisualStateDirty = true;
            _lastShowGridInGame = showGridInGame;
        }

        UpdateGridVisuals();

        if (Input.GetMouseButtonDown(0) && !_menuDeferredBeginDrawActive)
            BeginDraw();

        if (_isDrawing && Input.GetMouseButton(0))
            ContinueDraw();

        if (_isDrawing && Input.GetMouseButtonUp(0))
            EndDraw();
    }

    /// <summary>
    /// Après le menu du pivot maison (« Ajouter un mur ») : attend la fin du clic UI puis démarre un tracé au sol.
    /// </summary>
    public void BeginWallStrokeAfterMenuChoice()
    {
        if (!isActiveAndEnabled)
            return;

        if (_menuBeginDrawCoroutine != null)
        {
            StopCoroutine(_menuBeginDrawCoroutine);
            _menuBeginDrawCoroutine = null;
        }

        _menuBeginDrawCoroutine = StartCoroutine(CoBeginWallStrokeAfterMenuChoice());
    }

    IEnumerator CoBeginWallStrokeAfterMenuChoice()
    {
        _menuDeferredBeginDrawActive = true;
        try
        {
            while (Input.GetMouseButton(0) || Input.GetMouseButton(1))
                yield return null;
            yield return null;

            int guard = 0;
            while (UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject() &&
                   guard++ < 600)
                yield return null;

            BeginDraw();
        }
        finally
        {
            _menuDeferredBeginDrawActive = false;
            _menuBeginDrawCoroutine = null;
        }
    }

    void OnDisable()
    {
        if (_menuBeginDrawCoroutine != null)
        {
            StopCoroutine(_menuBeginDrawCoroutine);
            _menuBeginDrawCoroutine = null;
        }

        _menuDeferredBeginDrawActive = false;

        for (int i = 0; i < _gridLines.Count; i++)
        {
            if (_gridLines[i] != null)
                _gridLines[i].enabled = false;
        }

        _gridHasActiveLines = false;

        HierarchicalGridManager h = ResolveHierarchicalGrid();
        if (h != null)
            h.suppressMeshRendering = false;
    }

    void OnDrawGizmos()
    {
        if (!showGridGizmos || showGridInGame)
            return;

        Camera gridCam = cam != null ? cam : Camera.main;
        if (gridCam == null)
            return;

        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return;

        IReadOnlyList<HierarchicalGridNode> leaves = mgr.LeafNodes;
        if (leaves == null || leaves.Count == 0)
            return;

        float baseY = (flattenYToZero ? 0f : mgr.settings.gridPlaneY) + gridVisualYOffset;
        Vector3 camPos = gridCam.transform.position;
        float ext = Mathf.Max(0.25f, mgr.settings.minCellSize) * Mathf.Clamp(gridHalfExtent, 4, 200);
        float minX = camPos.x - ext;
        float maxX = camPos.x + ext;
        float minZ = camPos.z - ext;
        float maxZ = camPos.z + ext;

        Gizmos.color = new Color(0.55f, 0.55f, 0.55f, Mathf.Clamp01(gridOuterAlpha + 0.28f));

        int lineBudget = Mathf.Min(gridMaxLinesPerAxis * 8, 640);
        int drawn = 0;

        for (int i = 0; i < leaves.Count && drawn < lineBudget; i++)
        {
            HierarchicalGridNode leaf = leaves[i];
            if (leaf == null)
                continue;

            Vector2 mn = leaf.Min;
            Vector2 mx = leaf.Max;
            if (mx.x < minX || mn.x > maxX || mx.y < minZ || mn.y > maxZ)
                continue;

            float x0 = mn.x, x1 = mx.x, z0 = mn.y, z1 = mx.y;
            Vector3 a0 = new Vector3(x0, baseY, z0);
            Vector3 a1 = new Vector3(x1, baseY, z0);
            Vector3 a2 = new Vector3(x1, baseY, z1);
            Vector3 a3 = new Vector3(x0, baseY, z1);
            Gizmos.DrawLine(a0, a1);
            Gizmos.DrawLine(a1, a2);
            Gizmos.DrawLine(a2, a3);
            Gizmos.DrawLine(a3, a0);
            drawn += 4;
        }
    }

    void SyncHierarchicalGridMeshSuppression()
    {
        HierarchicalGridManager h = ResolveHierarchicalGrid();
        if (h == null)
            return;

        bool wantSuppress = showGridInGame;
        if (h.suppressMeshRendering != wantSuppress)
        {
            h.suppressMeshRendering = wantSuppress;
            _gridVisualStateDirty = true;
        }
    }

    void ApplyLineRendererSetup()
    {
        if (_lr == null)
            return;

        _lr.useWorldSpace = true;
        _lr.positionCount = 0;
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;
        _lr.textureMode = LineTextureMode.Stretch;
        _lr.loop = false;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows = false;

        if (_lr.sharedMaterial == null)
            _lr.sharedMaterial = GetOrCreateSharedPreviewMaterial();
    }

    static Material GetOrCreateSharedPreviewMaterial()
    {
        if (s_SharedPreviewMaterial != null)
            return s_SharedPreviewMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return null;

        s_SharedPreviewMaterial = new Material(shader);
        s_SharedPreviewMaterial.name = "WallDrawInput_Preview_Shared";
        return s_SharedPreviewMaterial;
    }

    static Material GetOrCreateSharedGridMaterial()
    {
        if (s_SharedGridMaterial != null)
            return s_SharedGridMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return null;

        s_SharedGridMaterial = new Material(shader);
        s_SharedGridMaterial.name = "WallDrawInput_Grid_Shared";
        return s_SharedGridMaterial;
    }

    void EnsureGridVisualObjects()
    {
        if (_gridVisualRoot == null)
        {
            Transform existing = transform.Find("__GridVisual");
            if (existing != null)
                _gridVisualRoot = existing;
            else
            {
                GameObject root = new GameObject("__GridVisual");
                root.transform.SetParent(transform, false);
                _gridVisualRoot = root.transform;
            }
        }

        int wanted = Mathf.Max(512, gridMaxLinesPerAxis * 8 + 128);
        Material mat = GetOrCreateSharedGridMaterial();

        while (_gridLines.Count < wanted)
        {
            GameObject go = new GameObject($"GridLine_{_gridLines.Count:000}");
            go.transform.SetParent(_gridVisualRoot, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.positionCount = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            _gridLines.Add(lr);
        }
    }

    void UpdateGridVisuals()
    {
        if (!showGridInGame)
        {
            if (_gridHasActiveLines)
            {
                for (int i = 0; i < _gridLines.Count; i++)
                {
                    if (_gridLines[i] != null && _gridLines[i].enabled)
                        _gridLines[i].enabled = false;
                }

                _gridHasActiveLines = false;
            }

            return;
        }

        Camera gridCam = cam != null ? cam : Camera.main;
        if (gridCam == null)
            return;

        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return;

        IReadOnlyList<HierarchicalGridNode> leaves = mgr.LeafNodes;
        if (leaves == null || leaves.Count == 0)
            return;

        float baseY = (flattenYToZero ? 0f : mgr.settings.gridPlaneY) + gridVisualYOffset;
        Vector3 camPos = gridCam.transform.position;
        float camHeight = Mathf.Abs(camPos.y - baseY);

        if (!_gridVisualStateDirty)
        {
            Vector2 deltaXZ = new Vector2(camPos.x - _lastGridVisualCamPos.x, camPos.z - _lastGridVisualCamPos.z);
            float heightDelta = Mathf.Abs(camHeight - _lastGridVisualCamHeight);
            if (deltaXZ.sqrMagnitude < 0.0004f && heightDelta < 0.02f)
                return;
        }

        EnsureGridVisualObjects();
        if (_gridLines.Count == 0)
            return;

        float ext = Mathf.Max(0.25f, mgr.settings.minCellSize) * Mathf.Clamp(gridHalfExtent, 4, 200);
        float cx = camPos.x;
        float cz = camPos.z;
        float minX = cx - ext;
        float maxX = cx + ext;
        float minZ = cz - ext;
        float maxZ = cz + ext;

        float width = Mathf.Clamp(gridLineWidth, 0.0005f, 0.20f);
        var col = new Color(0.55f, 0.55f, 0.55f, Mathf.Clamp01(Mathf.Max(gridInnerAlpha, 0.35f)));

        int lineCursor = 0;
        int maxLines = Mathf.Min(_gridLines.Count, gridMaxLinesPerAxis * 8);

        for (int i = 0; i < leaves.Count && lineCursor < maxLines - 4; i++)
        {
            HierarchicalGridNode leaf = leaves[i];
            if (leaf == null)
                continue;

            Vector2 mn = leaf.Min;
            Vector2 mx = leaf.Max;
            if (mx.x < minX || mn.x > maxX || mx.y < minZ || mn.y > maxZ)
                continue;

            float x0 = mn.x;
            float x1 = mx.x;
            float z0 = mn.y;
            float z1 = mx.y;

            EmitGridLine(ref lineCursor, new Vector3(x0, baseY, z0), new Vector3(x1, baseY, z0), width, col);
            EmitGridLine(ref lineCursor, new Vector3(x1, baseY, z0), new Vector3(x1, baseY, z1), width, col);
            EmitGridLine(ref lineCursor, new Vector3(x1, baseY, z1), new Vector3(x0, baseY, z1), width, col);
            EmitGridLine(ref lineCursor, new Vector3(x0, baseY, z1), new Vector3(x0, baseY, z0), width, col);
        }

        for (int i = lineCursor; i < _gridLines.Count; i++)
        {
            if (_gridLines[i] != null)
                _gridLines[i].enabled = false;
        }

        _gridHasActiveLines = lineCursor > 0;

        _lastGridVisualCamPos = camPos;
        _lastGridVisualCamHeight = camHeight;
        _gridVisualStateDirty = false;
    }

    void EmitGridLine(ref int lineCursor, Vector3 a, Vector3 b, float w, Color color)
    {
        if (lineCursor >= _gridLines.Count)
            return;

        LineRenderer lr = _gridLines[lineCursor++];
        if (lr == null)
            return;

        lr.enabled = true;
        lr.loop = false;
        lr.positionCount = 2;
        lr.startWidth = w;
        lr.endWidth = w;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    void BeginDraw()
    {
        _points.Clear();
        ResetLiveScores();
        LastCommittedShape = DetectedShapeKind.Free;
        LastCommittedShapeName = "None";
        LastCommitSnappedToWallCorner = false;

        if (_lr != null)
            _lr.positionCount = 0;

        _isDrawing = true;

        if (TryGetMouseWorldPoint(out Vector3 p))
        {
            p = PostProcessPoint(p);
            _points.Add(p);
            RefreshLine();
        }
    }

    void ContinueDraw()
    {
        if (_points.Count == 0)
            return;

        if (!TryGetMouseWorldPoint(out Vector3 p))
            return;

        p = PostProcessPoint(p);

        if (wallBuild != null && wallBuild.snapDrawToExistingWallCorners &&
            !snapDrawToWallCornersOnlyOnCommit)
            wallBuild.TrySnapWorldPointToExistingWallCorners(ref p);

        float spacing = GetActiveCapturePointSpacing();
        float dist = Vector3.Distance(_points[_points.Count - 1], p);
        if (dist >= spacing)
        {
            _points.Add(p);
            RefreshLine();
        }
    }

    void EndDraw()
    {
        _isDrawing = false;
        LastCommitSnappedToWallCorner = false;

        if (_points.Count < 2)
            return;

        // Dernier échantillon = position au relâchement (sinon le tracé s’arrête au dernier pas fixe).
        if (_points.Count >= 1 && TryGetMouseWorldPoint(out Vector3 releaseP))
        {
            releaseP = PostProcessPointForCommitLattice(releaseP);
            if (wallBuild != null && wallBuild.snapDrawToExistingWallCorners)
            {
                if (wallBuild.TrySnapWorldPointToExistingWallCorners(ref releaseP))
                    LastCommitSnappedToWallCorner = true;
            }

            float spacing = GetActiveCapturePointSpacing();
            float d = Vector3.Distance(_points[_points.Count - 1], releaseP);
            if (LastCommitSnappedToWallCorner)
                _points[_points.Count - 1] = releaseP;
            else if (d <= spacing * 0.75f)
                _points[_points.Count - 1] = releaseP;
            else if (d > 0.0005f)
                _points.Add(releaseP);
        }

        string committedShapeName = "Free";
        bool didGridRectangle = false;

        bool closed = false;

        if (_points.Count >= 3)
        {
            float closeDist = Vector3.Distance(_points[_points.Count - 1], _points[0]);
            if (closeDist <= snapCloseDistance)
            {
                _points[_points.Count - 1] = _points[0];
                closed = true;
            }
        }

        // Manhattan staircase on diagonals: collapse to chord when nearly collinear so straight-line fit works.
        if (!closed && _points.Count >= 3)
            TryCollapseOpenStrokeToChordIfNearlyCollinear(_points);

        if (enableGridSnap && snapToHierarchicalVisualGrid && closed &&
            TryBuildGridRectangleFromPoints(_points, out List<Vector3> gridFitted, out string gridShapeName))
        {
            _points.Clear();
            _points.AddRange(gridFitted);
            RefreshLine();
            committedShapeName = gridShapeName;
            didGridRectangle = true;

            if (logDetectedShape)
                Debug.Log($"GridShape ✅ : {gridShapeName}");
        }
        else if (enableAutoShapes)
        {
            bool canTryClosedShapes = !closed ? false : (!requireClosedLoop || closed);
            bool canTryOpenShapes = !closed && (autoStraightLine || autoOpenArc);
            bool shouldTryAutoFit = canTryClosedShapes || canTryOpenShapes;

            if (closed && useGridShapeDetectionOnlyWhenGridSnap && !enableGridSnap)
                shouldTryAutoFit = false;

            if (shouldTryAutoFit)
            {
                if (TryAutoFitShape(_points, closed, out List<Vector3> fitted, out string shapeName))
                {
                    _points.Clear();
                    _points.AddRange(fitted);
                    RefreshLine();
                    committedShapeName = shapeName;

                    if (logDetectedShape)
                        Debug.Log($"AutoShape ✅ : {shapeName}");
                }
                else
                {
                    committedShapeName = "Free";
                }
            }
        }

        if (!didGridRectangle && enableGridSnap && snapToHierarchicalVisualGrid)
            ProjectPathToHierarchicalInPlace(_points, closed);

        if (wallBuild != null && wallBuild.snapDrawToExistingWallCorners)
            wallBuild.SnapPathVerticesToExistingWallCornersInPlace(_points);

        RefreshLine();

        LastCommittedShape = ShapeNameToKind(committedShapeName);
        LastCommittedShapeName = committedShapeName;

        List<Vector3> committedPoints = new List<Vector3>(_points);
        OnShapeCommittedDetailed?.Invoke(committedPoints, LastCommittedShape, LastCommittedShapeName);
        OnShapeCommitted?.Invoke(committedPoints);
    }

    bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        return TryGetWorldPointFromScreen(Input.mousePosition, out worldPoint);
    }

    /// <summary>
    /// Point monde sous le centre de l’écran (ou autre viewport 0–1) : même logique que le dessin à la souris.
    /// Utilisé par l’UI « Wall draw » pour placer des formes préréglées.
    /// </summary>
    public bool TryGetWorldPointFromViewport(float viewportX01, float viewportY01, out Vector3 worldPoint)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
        {
            worldPoint = default;
            return false;
        }

        Vector3 sp = cam.ViewportToScreenPoint(new Vector3(
            Mathf.Clamp01(viewportX01),
            Mathf.Clamp01(viewportY01),
            0f));
        return TryGetWorldPointFromScreen(sp, out worldPoint);
    }

    bool TryGetWorldPointFromScreen(Vector3 screenPosition, out Vector3 worldPoint)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
        {
            worldPoint = default;
            return false;
        }

        Ray ray = cam.ScreenPointToRay(screenPosition);

        if (groundCollider != null && groundCollider.Raycast(ray, out RaycastHit hit, 10000f))
        {
            worldPoint = hit.point;
            return true;
        }

        if (Physics.Raycast(ray, out RaycastHit hit2, 10000f))
        {
            worldPoint = hit2.point;
            return true;
        }

        worldPoint = default;
        return false;
    }

    Vector3 PostProcessPoint(Vector3 p)
    {
        if (flattenYToZero)
            p.y = 0f;

        if (!enableGridSnap || !snapToHierarchicalVisualGrid)
            return p;

        if (_isDrawing && snapOnlyOnCommitWhileDrawing)
            return p;

        return SnapWorldToUniformMainLattice(p);
    }

    /// <summary>Grille + Y : utilisé au relâchement pour coller le dernier point au même espace que le commit final.</summary>
    Vector3 PostProcessPointForCommitLattice(Vector3 p)
    {
        if (flattenYToZero)
            p.y = 0f;

        if (!enableGridSnap || !snapToHierarchicalVisualGrid)
            return p;

        return SnapWorldToUniformMainLattice(p);
    }

    float GetActiveCapturePointSpacing()
    {
        return Mathf.Max(0.01f, pointSpacing);
    }

    HierarchicalGridManager ResolveHierarchicalGrid()
    {
        if (hierarchicalGrid != null)
            return hierarchicalGrid;

        if (_cachedHierarchicalGrid == null && !_didHierarchicalGridSearch)
        {
            _cachedHierarchicalGrid = FindFirstObjectByType<HierarchicalGridManager>();
            _didHierarchicalGridSearch = true;
        }

        if (_cachedHierarchicalGrid == null && Time.unscaledTime >= _nextHierarchicalGridRescanTime)
        {
            _nextHierarchicalGridRescanTime = Time.unscaledTime + 1.5f;
            _cachedHierarchicalGrid = FindFirstObjectByType<HierarchicalGridManager>();
        }

        return _cachedHierarchicalGrid;
    }

    /// <summary>
    /// Pour la fusion de lots : pas et origine de la grille hiérarchique (feuilles), si disponibles.
    /// </summary>
    public bool TryGetHierarchicalCellStepAndOrigin(out float cellStep, out Vector2 originXZ)
    {
        cellStep = 1f;
        originXZ = Vector2.zero;
        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return false;
        // WallOrthoMergeUtility fonctionne sur une grille discrète *uniforme* (un seul step + une seule origine).
        // Dans une grille hiérarchique, les leaf cells peuvent être plus grosses que minCellSize (LOD / budget leaf),
        // ce qui ferait retomber le merge sur l'ancien lattice uniforme.
        // On ne fournit donc step/origin qu'en cas de grille leaf-uniforme.
        float minCell = Mathf.Max(0.01f, mgr.settings.minCellSize);
        float eps = Mathf.Max(0.0001f, minCell * 0.01f);

        IReadOnlyList<HierarchicalGridNode> leaves = mgr.LeafNodes;
        if (leaves == null || leaves.Count == 0)
            return false;

        for (int i = 0; i < leaves.Count; i++)
        {
            HierarchicalGridNode n = leaves[i];
            if (n == null)
                continue;
            if (Mathf.Abs(n.size - minCell) > eps)
                return false;
        }

        cellStep = minCell;
        originXZ = mgr.settings.gridWorldCenterXZ;
        return true;
    }

    /// <summary>
    /// Même référentiel que la grille grise : centre de la cellule feuille sous le point (XZ).
    /// Utilisable pour le dessin, les handles, et <see cref="SnapCommittedPathToMainGridInPlace"/>.
    /// </summary>
    public Vector3 SnapWorldToHierarchicalLeafCenter(Vector3 world)
    {
        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return world;

        float yOut = flattenYToZero ? 0f : world.y;
        float gy = mgr.settings.gridPlaneY;
        Vector3 test = new Vector3(world.x, gy, world.z);

        if (mgr.TryGetCellAtWorld(test, out HierarchicalGridNode cell))
        {
            Vector3 c = mgr.GetCellCenterWorld(cell);
            return new Vector3(c.x, yOut, c.z);
        }

        IReadOnlyList<HierarchicalGridNode> leaves = mgr.LeafNodes;
        if (leaves == null || leaves.Count == 0)
            return world;

        Vector2 p2 = new Vector2(world.x, world.z);
        float best = float.MaxValue;
        HierarchicalGridNode bestNode = null;
        for (int i = 0; i < leaves.Count; i++)
        {
            HierarchicalGridNode n = leaves[i];
            if (n == null)
                continue;
            float d = (n.center - p2).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestNode = n;
            }
        }

        if (bestNode != null)
        {
            Vector3 c = mgr.GetCellCenterWorld(bestNode);
            return new Vector3(c.x, yOut, c.z);
        }

        return world;
    }

    bool TryResolveLeafForWorldSnap(HierarchicalGridManager mgr, Vector3 world, out HierarchicalGridNode leaf)
    {
        leaf = null;
        if (mgr == null || mgr.settings == null)
            return false;

        float gy = mgr.settings.gridPlaneY;
        Vector3 test = new Vector3(world.x, gy, world.z);

        if (mgr.TryGetCellAtWorld(test, out leaf) && leaf != null)
            return true;

        IReadOnlyList<HierarchicalGridNode> leaves = mgr.LeafNodes;
        if (leaves == null || leaves.Count == 0)
            return false;

        Vector2 p2 = new Vector2(world.x, world.z);
        float best = float.MaxValue;
        for (int i = 0; i < leaves.Count; i++)
        {
            HierarchicalGridNode n = leaves[i];
            if (n == null)
                continue;
            float d = (n.center - p2).sqrMagnitude;
            if (d < best)
            {
                best = d;
                leaf = n;
            }
        }

        return leaf != null;
    }

    /// <summary>
    /// Grille 9 points (3×3) alignée sur le carré feuille sous le point — même géométrie que le contour affiché.
    /// </summary>
    public Vector3 SnapWorldToVisibleLeafNinePointLattice(Vector3 world)
    {
        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return world;

        if (!TryResolveLeafForWorldSnap(mgr, world, out HierarchicalGridNode leaf) || leaf == null)
            return world;

        float half = leaf.size * 0.5f;
        if (half < 1e-8f)
            return world;

        float yOut = flattenYToZero ? 0f : world.y;
        Vector2 mn = leaf.Min;
        float lx = world.x - mn.x;
        float lz = world.z - mn.y;
        int ix = Mathf.Clamp(Mathf.RoundToInt(lx / half), 0, 2);
        int iz = Mathf.Clamp(Mathf.RoundToInt(lz / half), 0, 2);
        float qx = mn.x + ix * half;
        float qz = mn.y + iz * half;
        return new Vector3(qx, yOut, qz);
    }

    /// <summary>
    /// Coin de cellule feuille le plus proche (XZ).
    /// </summary>
    Vector3 SnapWorldToNearestLeafCorner(Vector3 world)
    {
        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return world;

        if (!TryResolveLeafForWorldSnap(mgr, world, out HierarchicalGridNode cell) || cell == null)
            return world;

        Vector2 mn = cell.Min;
        Vector2 mx = cell.Max;
        Vector2 p = new Vector2(world.x, world.z);
        Vector2 c0 = new Vector2(mn.x, mn.y);
        Vector2 c1 = new Vector2(mx.x, mn.y);
        Vector2 c2 = new Vector2(mx.x, mx.y);
        Vector2 c3 = new Vector2(mn.x, mx.y);

        Vector2 bestC = c0;
        float bestD = float.MaxValue;
        TryCorner(p, c0, ref bestC, ref bestD);
        TryCorner(p, c1, ref bestC, ref bestD);
        TryCorner(p, c2, ref bestC, ref bestD);
        TryCorner(p, c3, ref bestC, ref bestD);

        float yOut = flattenYToZero ? 0f : world.y;
        return new Vector3(bestC.x, yOut, bestC.y);
    }

    static void TryCorner(Vector2 p, Vector2 cand, ref Vector2 bestC, ref float bestD)
    {
        float d = (cand - p).sqrMagnitude;
        if (d < bestD)
        {
            bestD = d;
            bestC = cand;
        }
    }

    /// <summary>
    /// Snap pour édition (handles) : 9 points par feuille visible (aligné sur le quadtree).
    /// </summary>
    public Vector3 SnapWorldPointForEditing(Vector3 world)
    {
        if (!enableGridSnap || !snapToHierarchicalVisualGrid)
            return world;

        return SnapWorldToUniformMainLattice(world);
    }

    /// <summary>
    /// Pas et origine « carte » (<see cref="HierarchicalGridSettings.minCellSize"/>, <see cref="HierarchicalGridSettings.gridWorldCenterXZ"/>).
    /// Faux si aucun <see cref="HierarchicalGridManager"/> (plus de fallback).
    /// </summary>
    public bool TryGetMainGridLatticeStepXZ(out float step, out Vector2 worldOriginXZ)
    {
        step = 0f;
        worldOriginXZ = default;
        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return false;

        step = Mathf.Max(0.01f, mgr.settings.minCellSize);
        worldOriginXZ = mgr.settings.gridWorldCenterXZ;
        return true;
    }

    /// <summary>
    /// Snap sur la grille 9 points de la feuille sous le point (même repère que la grille affichée).
    /// </summary>
    public Vector3 SnapWorldToUniformMainLattice(Vector3 world)
    {
        if (!enableGridSnap || !snapToHierarchicalVisualGrid)
            return world;

        return SnapWorldToVisibleLeafNinePointLattice(world);
    }

    void ProjectPathToHierarchicalInPlace(List<Vector3> points, bool closed)
    {
        if (points == null || points.Count == 0)
            return;

        int last = closed && points.Count > 1 &&
                   Vector3.Distance(points[0], points[points.Count - 1]) < snapCloseDistance * 1.25f
            ? points.Count - 1
            : points.Count;

        for (int i = 0; i < last; i++)
            points[i] = SnapWorldToUniformMainLattice(points[i]);

        if (closed && last == points.Count - 1 && points.Count > 1)
            points[points.Count - 1] = points[0];
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

    bool TryBuildGridRectangleFromPoints(List<Vector3> points, out List<Vector3> gridFitted, out string gridShapeName)
    {
        gridFitted = null;
        gridShapeName = "";

        if (points == null || points.Count < 4)
            return false;

        HierarchicalGridManager mgr = ResolveHierarchicalGrid();
        if (mgr == null || mgr.settings == null)
            return false;

        if (Vector3.Distance(points[0], points[points.Count - 1]) > snapCloseDistance * 1.5f)
            return false;

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0 && i == points.Count - 1 && Vector3.Distance(points[0], points[i]) < 0.0001f)
                continue;

            Vector3 p = points[i];
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z);
            maxZ = Mathf.Max(maxZ, p.z);
        }

        float w = maxX - minX;
        float h = maxZ - minZ;
        if (w < 0.02f || h < 0.02f)
            return false;

        float minCell = Mathf.Max(0.01f, mgr.settings.minCellSize);
        float tol = Mathf.Max(tolerance, minCell * 0.2f);

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0 && i == points.Count - 1 && Vector3.Distance(points[0], points[i]) < 0.0001f)
                continue;

            Vector2 p = new Vector2(points[i].x, points[i].z);
            float d = DistancePointToAxisAlignedRectPerimeter(p, minX, maxX, minZ, maxZ);
            if (d > tol)
                return false;
        }

        float y = points[0].y;
        if (alignAxisAlignedClosedShapesToMainGridCells)
        {
            Vector3 tl = new Vector3(minX, y, maxZ);
            Vector3 tr = new Vector3(maxX, y, maxZ);
            Vector3 br = new Vector3(maxX, y, minZ);
            Vector3 bl = new Vector3(minX, y, minZ);

            tl = SnapWorldToUniformMainLattice(tl);
            tr = SnapWorldToUniformMainLattice(tr);
            br = SnapWorldToUniformMainLattice(br);
            bl = SnapWorldToUniformMainLattice(bl);

            minX = Mathf.Min(tl.x, tr.x, br.x, bl.x);
            maxX = Mathf.Max(tl.x, tr.x, br.x, bl.x);
            minZ = Mathf.Min(tl.z, tr.z, br.z, bl.z);
            maxZ = Mathf.Max(tl.z, tr.z, br.z, bl.z);

            w = maxX - minX;
            h = maxZ - minZ;
        }

        if (w < 0.02f || h < 0.02f)
            return false;

        bool forceSquare = Mathf.Abs(w - h) / Mathf.Max(w, h) <= squareRatioTolerance;

        RectFit fit = new RectFit
        {
            center = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f),
            axisX = Vector2.right,
            axisY = new Vector2(0f, 1f),
            minX = -(maxX - minX) * 0.5f,
            maxX = (maxX - minX) * 0.5f,
            minY = -(maxZ - minZ) * 0.5f,
            maxY = (maxZ - minZ) * 0.5f
        };

        gridFitted = MakeRectanglePoints(fit, rectPointsPerEdge, y, forceSquare);
        gridShapeName = forceSquare ? "Square" : "Rectangle";
        return gridFitted != null && gridFitted.Count >= 4;
    }

    /// <summary>
    /// Grid snap rounds X and Z independently, so a diagonal becomes an axis-aligned staircase.
    /// If every sample stays within tolerance of the chord from first to last point, collapse to that segment
    /// so straight-line detection yields a true diagonal wall (two endpoints).
    /// </summary>
    bool TryCollapseOpenStrokeToChordIfNearlyCollinear(List<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return false;

        Vector2 a = new Vector2(points[0].x, points[0].z);
        Vector2 b = new Vector2(points[points.Count - 1].x, points[points.Count - 1].z);
        if (Vector2.Distance(a, b) < 0.02f)
            return false;

        float maxD = 0f;
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 p = new Vector2(points[i].x, points[i].z);
            float d = DistancePointSegment(p, a, b);
            if (d > maxD)
                maxD = d;
        }

        float spacing = GetActiveCapturePointSpacing();
        float tol = Mathf.Max(spacing * openStrokeChordDeviationGridMul, 0.06f);

        if (maxD > tol)
            return false;

        Vector3 first = points[0];
        Vector3 last = points[points.Count - 1];
        points.Clear();
        points.Add(first);
        points.Add(last);
        return true;
    }

    /// <summary>Aligne les sommets sur la grille hiérarchique (centres ou coins selon les options).</summary>
    public void SnapCommittedPathToMainGridInPlace(List<Vector3> points, bool closed)
    {
        if (points == null || points.Count == 0 || !enableGridSnap || !snapToHierarchicalVisualGrid)
            return;

        ProjectPathToHierarchicalInPlace(points, closed);
    }

    void RefreshLine()
    {
        if (_lr == null)
            return;

        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;
        _lr.positionCount = _points.Count;

        for (int i = 0; i < _points.Count; i++)
            _lr.SetPosition(i, _points[i]);
    }

    void ResetLiveScores()
    {
        lastDetectedClosedShape = "None";
        lastRectangleProbability = 0f;
        lastTriangleProbability = 0f;
        lastCircleProbability = 0f;
    }

    void SetLiveScores(float rect, float tri, float circ, string detected)
    {
        lastRectangleProbability = Mathf.Clamp01(rect) * 100f;
        lastTriangleProbability = Mathf.Clamp01(tri) * 100f;
        lastCircleProbability = Mathf.Clamp01(circ) * 100f;
        lastDetectedClosedShape = string.IsNullOrEmpty(detected) ? "None" : detected;
    }

    DetectedShapeKind ShapeNameToKind(string shapeName)
    {
        switch (shapeName)
        {
            case "Straight Line":
                return DetectedShapeKind.StraightLine;
            case "Rectangle":
                return DetectedShapeKind.Rectangle;
            case "Square":
                return DetectedShapeKind.Square;
            case "Circle":
                return DetectedShapeKind.Circle;
            case "Triangle":
                return DetectedShapeKind.Triangle;
            case "Arc":
                return DetectedShapeKind.OpenArc;
            default:
                return DetectedShapeKind.Free;
        }
    }

    bool TryAutoFitShape(List<Vector3> rawPoints, bool closed, out List<Vector3> fittedPoints, out string shapeName)
    {
        fittedPoints = null;
        shapeName = "";

        if (!autoStraightLine && !autoOpenArc && !autoCircle && !autoRectangle && !autoTriangle)
            return false;

        float y = rawPoints[0].y;

        List<Vector2> pts2 = ToXZ(rawPoints);
        float simplifyBase = Mathf.Max(0.01f, pointSpacing);

        pts2 = SimplifyBySpacing(pts2, simplifyBase * 0.85f);

        if (pts2.Count < 2)
        {
            ResetLiveScores();
            return false;
        }

        if (!closed)
        {
            ResetLiveScores();
            // Do not use `flag && Try...()` — short-circuit leaves `out` variables unassigned (CS0165).
            Vector2 lineStart = default;
            Vector2 lineEnd = default;
            float lineErr = float.MaxValue;
            bool hasLine = false;
            if (autoStraightLine)
            {
                hasLine = TryFitStraightLine(
                    pts2,
                    tolerance * straightLineToleranceMultiplier,
                    out lineStart,
                    out lineEnd,
                    out lineErr);
            }

            Vector2 arcCenter = default;
            float arcRadius = 0f;
            float arcStartAngle = 0f;
            float arcEndAngle = 0f;
            bool arcCounterClockwise = true;
            float arcErr = float.MaxValue;
            bool hasArc = false;
            if (autoOpenArc)
            {
                hasArc = TryFitOpenArc(
                    pts2,
                    out arcCenter,
                    out arcRadius,
                    out arcStartAngle,
                    out arcEndAngle,
                    out arcCounterClockwise,
                    out arcErr);
            }

            // Prefer the shape with lower normalized fitting error (arc slightly favored).
            if (hasArc && (!hasLine || arcErr <= lineErr * openArcPreferOverLineMul))
            {
                shapeName = "Arc";
                fittedPoints = MakeOpenArcPoints(
                    arcCenter,
                    arcRadius,
                    arcStartAngle,
                    arcEndAngle,
                    arcCounterClockwise,
                    openArcResolution,
                    y);
                return true;
            }

            if (hasLine)
            {
                shapeName = "Straight Line";
                fittedPoints = MakeStraightLinePoints(lineStart, lineEnd, y);
                return true;
            }

            return false;
        }

        if (Vector2.Distance(pts2[0], pts2[pts2.Count - 1]) > 0.0001f)
            pts2.Add(pts2[0]);

        if (pts2.Count < 6)
        {
            SetLiveScores(0f, 0f, 0f, "Free");
            return false;
        }

        List<Vector2> rawClosed = new List<Vector2>(pts2);
        List<Vector2> rawOpen = GetOpenLoop(rawClosed);

        if (rejectSelfIntersectingClosedShapes && HasSelfIntersection(rawClosed))
        {
            SetLiveScores(0f, 0f, 0f, "Free");
            if (logShapeScores)
                Debug.Log("Shape scores → Rectangle: 0% | Triangle: 0% | Circle: 0% (auto-intersection)");
            return false;
        }

        List<Vector2> sample = ResampleClosedEvenly(rawClosed, closedShapeSampleCount);
        List<CornerSample> corners = DetectCorners(sample);

        ShapeCandidate rectangle = default;
        ShapeCandidate triangle = default;
        ShapeCandidate circle = default;

        if (autoRectangle)
            rectangle = BuildRectangleCandidate(rawClosed, sample, corners, y);

        if (autoTriangle)
            triangle = BuildTriangleCandidate(rawClosed, sample, corners, y);

        if (autoCircle)
            circle = BuildCircleCandidate(rawClosed, sample, corners, y);

        SetLiveScores(rectangle.score, triangle.score, circle.score, "Free");

        if (logShapeScores)
            Debug.Log($"Shape scores → Rectangle: {rectangle.score * 100f:0.#}% | Triangle: {triangle.score * 100f:0.#}% | Circle: {circle.score * 100f:0.#}%");

        ShapeCandidate best = rectangle;
        ShapeCandidate second = triangle;

        if (triangle.score > best.score)
        {
            second = best;
            best = triangle;
        }
        else
        {
            second = triangle;
        }

        if (circle.score > best.score)
        {
            second = best;
            best = circle;
        }
        else if (circle.score > second.score)
        {
            second = circle;
        }

        if (best.points == null || best.score <= 0f)
        {
            lastDetectedClosedShape = "Free";
            return false;
        }

        float perShapeMin = GetPerShapeMinimum(best.name);
        float globalMin = Mathf.Max(minClosedShapeConfidence, perShapeMin);
        bool accepted = best.score >= globalMin && (best.score - second.score) >= minClosedShapeLead;

        if (!accepted)
        {
            lastDetectedClosedShape = "Free";
            return false;
        }

        fittedPoints = best.points;
        shapeName = best.name;
        lastDetectedClosedShape = best.name;
        return true;
    }

    float GetPerShapeMinimum(string shapeName)
    {
        switch (shapeName)
        {
            case "Rectangle":
            case "Square":
                return minRectangleProbability;
            case "Triangle":
                return minTriangleProbability;
            case "Circle":
                return minCircleProbability;
            default:
                return minClosedShapeConfidence;
        }
    }

    ShapeCandidate BuildCircleCandidate(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, float y)
    {
        ShapeCandidate c = default;
        c.name = "Circle";

        if (!EvaluateCircleFit(ptsClosed, sample, corners, out Vector2 center, out float radius, out float score))
            return c;

        c.score = score;
        c.points = MakeCirclePoints(center, radius, circleResolution, y);
        return c;
    }

    ShapeCandidate BuildRectangleCandidate(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, float y)
    {
        ShapeCandidate c = default;
        c.name = "Rectangle";

        if (!EvaluateRectangleFit(ptsClosed, sample, corners, out RectFit fit, out float score))
            return c;

        bool forceSquare =
            Mathf.Abs(fit.width - fit.height) /
            Mathf.Max(0.0001f, Mathf.Max(fit.width, fit.height)) <= squareRatioTolerance;

        ForceAxisAlignedRectFit(ref fit, forceSquare);

        c.name = forceSquare ? "Square" : "Rectangle";
        c.score = forceSquare ? score * squareClassificationScoreMul : score;
        c.points = MakeRectanglePoints(fit, rectPointsPerEdge, y, forceSquare);
        return c;
    }

    /// <summary>
    /// Remplace le rectangle orienté (suivant le tracé) par l’AAB XZ équivalente : axes monde, pas de rotation.
    /// </summary>
    static void ForceAxisAlignedRectFit(ref RectFit fit, bool asSquare)
    {
        float minx = fit.minX;
        float maxx = fit.maxX;
        float miny = fit.minY;
        float maxy = fit.maxY;

        if (asSquare)
        {
            float side = Mathf.Max(fit.width, fit.height);
            float half = side * 0.5f;
            minx = -half;
            maxx = half;
            miny = -half;
            maxy = half;
        }

        Vector2 c = fit.center;
        Vector2 c0 = c + fit.axisX * minx + fit.axisY * miny;
        Vector2 c1 = c + fit.axisX * maxx + fit.axisY * miny;
        Vector2 c2 = c + fit.axisX * maxx + fit.axisY * maxy;
        Vector2 c3 = c + fit.axisX * minx + fit.axisY * maxy;

        float minXw = Mathf.Min(c0.x, c1.x, c2.x, c3.x);
        float maxXw = Mathf.Max(c0.x, c1.x, c2.x, c3.x);
        float minZw = Mathf.Min(c0.y, c1.y, c2.y, c3.y);
        float maxZw = Mathf.Max(c0.y, c1.y, c2.y, c3.y);

        Vector2 centerW = new Vector2((minXw + maxXw) * 0.5f, (minZw + maxZw) * 0.5f);
        float bboxW = maxXw - minXw;
        float bboxZ = maxZw - minZw;

        fit.center = centerW;
        fit.axisX = Vector2.right;
        fit.axisY = new Vector2(0f, 1f);

        if (asSquare)
        {
            float sideAligned = Mathf.Max(bboxW, bboxZ);
            float halfA = sideAligned * 0.5f;
            fit.minX = -halfA;
            fit.maxX = halfA;
            fit.minY = -halfA;
            fit.maxY = halfA;
        }
        else
        {
            float halfW = bboxW * 0.5f;
            float halfH = bboxZ * 0.5f;
            fit.minX = -halfW;
            fit.maxX = halfW;
            fit.minY = -halfH;
            fit.maxY = halfH;
        }
    }

    ShapeCandidate BuildTriangleCandidate(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, float y)
    {
        ShapeCandidate c = default;
        c.name = "Triangle";

        if (!EvaluateTriangleFit(ptsClosed, sample, corners, out List<Vector2> triPts, out float score))
            return c;

        ForceTriangleOneEdgeAlongWorldX(triPts);

        c.score = score;
        c.points = ToWorldPath(triPts, y);
        return c;
    }

    /// <summary>
    /// Tourne le triangle dans XZ pour qu’une arête soit parallèle à l’axe X (plus petite boîte englobante axe-alignée parmi les 3 arêtes).
    /// </summary>
    static void ForceTriangleOneEdgeAlongWorldX(List<Vector2> tri)
    {
        if (tri == null || tri.Count != 3)
            return;

        Vector2 c = (tri[0] + tri[1] + tri[2]) / 3f;
        float bestArea = float.MaxValue;
        float bestAng = 0f;

        for (int e = 0; e < 3; e++)
        {
            Vector2 edge = tri[(e + 1) % 3] - tri[e];
            if (edge.sqrMagnitude < 1e-12f)
                continue;

            float ang = Mathf.Atan2(edge.y, edge.x);
            float ca = Mathf.Cos(-ang);
            float sa = Mathf.Sin(-ang);
            float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = tri[i] - c;
                float x = p.x * ca - p.y * sa;
                float z = p.x * sa + p.y * ca;
                minx = Mathf.Min(minx, x);
                maxx = Mathf.Max(maxx, x);
                minz = Mathf.Min(minz, z);
                maxz = Mathf.Max(maxz, z);
            }

            float area = (maxx - minx) * (maxz - minz);
            if (area < bestArea)
            {
                bestArea = area;
                bestAng = ang;
            }
        }

        float co = Mathf.Cos(-bestAng);
        float si = Mathf.Sin(-bestAng);
        for (int i = 0; i < 3; i++)
        {
            Vector2 p = tri[i] - c;
            tri[i] = new Vector2(p.x * co - p.y * si, p.x * si + p.y * co) + c;
        }
    }

    List<Vector2> ToXZ(List<Vector3> p3)
    {
        var list = new List<Vector2>(p3.Count);
        for (int i = 0; i < p3.Count; i++)
            list.Add(new Vector2(p3[i].x, p3[i].z));
        return list;
    }

    List<Vector3> ToWorldPath(List<Vector2> pts2, float y)
    {
        var res = new List<Vector3>(pts2.Count);
        for (int i = 0; i < pts2.Count; i++)
            res.Add(new Vector3(pts2[i].x, y, pts2[i].y));
        return res;
    }

    List<Vector2> SimplifyBySpacing(List<Vector2> pts, float spacing)
    {
        var res = new List<Vector2>();
        if (pts == null || pts.Count == 0)
            return res;

        res.Add(pts[0]);
        Vector2 last = pts[0];

        for (int i = 1; i < pts.Count; i++)
        {
            if (Vector2.Distance(last, pts[i]) >= spacing)
            {
                res.Add(pts[i]);
                last = pts[i];
            }
        }

        if (res.Count >= 2 && Vector2.Distance(res[0], res[res.Count - 1]) < spacing)
            res[res.Count - 1] = res[0];

        return res;
    }

    bool TryFitStraightLine(List<Vector2> pts, float tol, out Vector2 start, out Vector2 end, out float normalizedError)
    {
        start = Vector2.zero;
        end = Vector2.zero;
        normalizedError = float.MaxValue;

        if (pts == null || pts.Count < 2)
            return false;

        float best = -1f;
        Vector2 aBest = pts[0];
        Vector2 bBest = pts[pts.Count - 1];

        for (int i = 0; i < pts.Count; i++)
        {
            for (int j = i + 1; j < pts.Count; j++)
            {
                float d = (pts[j] - pts[i]).sqrMagnitude;
                if (d > best)
                {
                    best = d;
                    aBest = pts[i];
                    bBest = pts[j];
                }
            }
        }

        float len = Vector2.Distance(aBest, bBest);
        if (len < 0.25f)
            return false;

        float err = 0f;
        for (int i = 0; i < pts.Count; i++)
            err += DistancePointSegment(pts[i], aBest, bBest);

        float normErr = (err / pts.Count) / len;
        if (normErr > tol)
            return false;
        normalizedError = normErr;

        float d0 = Vector2.Distance(pts[0], aBest) + Vector2.Distance(pts[pts.Count - 1], bBest);
        float d1 = Vector2.Distance(pts[0], bBest) + Vector2.Distance(pts[pts.Count - 1], aBest);

        if (d0 <= d1)
        {
            start = aBest;
            end = bBest;
        }
        else
        {
            start = bBest;
            end = aBest;
        }

        return true;
    }

    bool TryFitOpenArc(
        List<Vector2> pts,
        out Vector2 center,
        out float radius,
        out float startAngle,
        out float endAngle,
        out bool counterClockwise,
        out float normalizedError)
    {
        center = Vector2.zero;
        radius = 0f;
        startAngle = 0f;
        endAngle = 0f;
        counterClockwise = true;
        normalizedError = float.MaxValue;

        if (pts == null || pts.Count < 3)
            return false;

        Vector2 a = pts[0];
        Vector2 b = pts[pts.Count - 1];
        if (Vector2.Distance(a, b) < 0.08f)
            return false;

        Vector2 mid = pts.Count == 3 ? pts[1] : GetPathMidpoint(pts);
        float sagitta = DistancePointSegment(mid, a, b);
        float minSagitta = Mathf.Max(pointSpacing * openArcMinSagittaGridMul, 0.05f);
        if (sagitta < minSagitta)
            return false;

        if (!TryCircleFromThreePoints(a, mid, b, out center, out radius))
            return false;

        if (radius < 0.08f || float.IsNaN(radius) || float.IsInfinity(radius))
            return false;

        startAngle = Mathf.Atan2(a.y - center.y, a.x - center.x);
        endAngle = Mathf.Atan2(b.y - center.y, b.x - center.x);
        float midAngle = Mathf.Atan2(mid.y - center.y, mid.x - center.x);

        bool midOnCcw = IsAngleOnCcwArc(startAngle, endAngle, midAngle);
        counterClockwise = midOnCcw;
        float sweep = counterClockwise
            ? PositiveDeltaAngle(startAngle, endAngle)
            : PositiveDeltaAngle(endAngle, startAngle);
        float sweepDeg = sweep * Mathf.Rad2Deg;

        if (sweepDeg < openArcMinSweepDeg || sweepDeg > openArcMaxSweepDeg)
            return false;

        float tolAbs = Mathf.Max(pointSpacing * openArcFitTolerance, 0.04f);

        float sum = 0f;
        float max = 0f;
        int outside = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i];
            float ang = Mathf.Atan2(p.y - center.y, p.x - center.x);
            if (!IsAngleOnDirectedArc(startAngle, endAngle, counterClockwise, ang))
                outside++;

            float d = Mathf.Abs(Vector2.Distance(p, center) - radius);
            sum += d;
            if (d > max) max = d;
        }

        float avg = sum / Mathf.Max(1, pts.Count);
        if (avg > tolAbs || max > tolAbs * 2.5f)
            return false;
        if (outside > Mathf.Max(2, Mathf.RoundToInt(pts.Count * 0.28f)))
            return false;

        normalizedError = avg / Mathf.Max(0.0001f, radius);
        return true;
    }

    List<Vector3> MakeStraightLinePoints(Vector2 start, Vector2 end, float y)
    {
        return new List<Vector3>
        {
            new Vector3(start.x, y, start.y),
            new Vector3(end.x, y, end.y)
        };
    }

    List<Vector3> MakeOpenArcPoints(
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        bool counterClockwise,
        int maxResolution,
        float y)
    {
        float sweep = counterClockwise
            ? PositiveDeltaAngle(startAngle, endAngle)
            : PositiveDeltaAngle(endAngle, startAngle);
        float arcLen = radius * sweep;
        int target = Mathf.Clamp(Mathf.RoundToInt(arcLen / Mathf.Max(0.08f, pointSpacing * 0.6f)), 10, Mathf.Max(10, maxResolution));
        List<Vector3> pts = new List<Vector3>(target + 1);

        for (int i = 0; i <= target; i++)
        {
            float t = target <= 0 ? 0f : i / (float)target;
            float ang = counterClockwise
                ? startAngle + sweep * t
                : startAngle - sweep * t;
            float x = center.x + Mathf.Cos(ang) * radius;
            float z = center.y + Mathf.Sin(ang) * radius;
            pts.Add(new Vector3(x, y, z));
        }

        return pts;
    }

    Vector2 GetPathMidpoint(List<Vector2> pts)
    {
        if (pts == null || pts.Count == 0)
            return Vector2.zero;
        if (pts.Count == 1)
            return pts[0];

        float total = 0f;
        for (int i = 1; i < pts.Count; i++)
            total += Vector2.Distance(pts[i - 1], pts[i]);
        if (total < 0.0001f)
            return pts[pts.Count / 2];

        float half = total * 0.5f;
        float acc = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            float seg = Vector2.Distance(pts[i - 1], pts[i]);
            if (acc + seg >= half)
            {
                float t = Mathf.InverseLerp(acc, acc + seg, half);
                return Vector2.Lerp(pts[i - 1], pts[i], t);
            }
            acc += seg;
        }
        return pts[pts.Count / 2];
    }

    static bool TryCircleFromThreePoints(Vector2 a, Vector2 b, Vector2 c, out Vector2 center, out float radius)
    {
        center = Vector2.zero;
        radius = 0f;

        float d = 2f * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
        if (Mathf.Abs(d) < 0.0001f)
            return false;

        float a2 = a.sqrMagnitude;
        float b2 = b.sqrMagnitude;
        float c2 = c.sqrMagnitude;

        float ux = (a2 * (b.y - c.y) + b2 * (c.y - a.y) + c2 * (a.y - b.y)) / d;
        float uy = (a2 * (c.x - b.x) + b2 * (a.x - c.x) + c2 * (b.x - a.x)) / d;
        center = new Vector2(ux, uy);
        radius = Vector2.Distance(center, a);
        return !float.IsNaN(radius) && !float.IsInfinity(radius) && radius > 0.0001f;
    }

    static float PositiveDeltaAngle(float from, float to)
    {
        float d = to - from;
        while (d < 0f) d += Mathf.PI * 2f;
        while (d >= Mathf.PI * 2f) d -= Mathf.PI * 2f;
        return d;
    }

    static bool IsAngleOnCcwArc(float from, float to, float angle)
    {
        float total = PositiveDeltaAngle(from, to);
        float part = PositiveDeltaAngle(from, angle);
        return part <= total + 0.0001f;
    }

    static bool IsAngleOnDirectedArc(float from, float to, bool ccw, float angle)
    {
        return ccw ? IsAngleOnCcwArc(from, to, angle) : IsAngleOnCcwArc(to, from, angle);
    }

    bool EvaluateCircleFit(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, out Vector2 center, out float radius, out float score)
    {
        center = Vector2.zero;
        radius = 0f;
        score = 0f;

        int n = sample.Count;
        if (n < 12)
            return false;

        for (int i = 0; i < n; i++)
            center += sample[i];
        center /= n;

        float sumRadius = 0f;
        float minDist = float.PositiveInfinity;
        float maxDist = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            float d = Vector2.Distance(center, sample[i]);
            sumRadius += d;
            minDist = Mathf.Min(minDist, d);
            maxDist = Mathf.Max(maxDist, d);
        }

        radius = sumRadius / n;
        if (radius < 0.12f)
            return false;

        float radialErr = 0f;
        for (int i = 0; i < n; i++)
            radialErr += Mathf.Abs(Vector2.Distance(center, sample[i]) - radius);
        radialErr = (radialErr / n) / Mathf.Max(0.0001f, radius);

        ComputeAabb(sample, out float minX, out float maxX, out float minY, out float maxY);
        float width = maxX - minX;
        float height = maxY - minY;
        if (width < 0.001f || height < 0.001f)
            return false;

        float aspect = Mathf.Min(width, height) / Mathf.Max(width, height);
        float strict = Mathf.Max(0.015f, circleStrictnessMultiplier);
        float radialScore = Mathf.Clamp01(1f - radialErr / strict);
        float cornerPenalty = Mathf.Clamp01((corners.Count - 2) / 5f);
        // Faceted / grid circles create many corner hits; if radial fit is already good, do not crush the circle score.
        float cornerRelax = Mathf.InverseLerp(strict * 1.2f, strict * 0.32f, radialErr);
        cornerPenalty *= Mathf.Lerp(1f, 0.30f, cornerRelax);
        // Grid-snapped circles often have a slightly non-square AABB; be a bit forgiving.
        float aspectScore = Mathf.InverseLerp(0.45f, 0.98f, aspect);
        float rangeScore = Mathf.Clamp01(1f - ((maxDist - minDist) / Mathf.Max(0.0001f, radius)) / 0.55f);

        score = (radialScore * 0.58f + aspectScore * 0.22f + rangeScore * 0.20f) * circleDetectionBoost;
        score *= Mathf.Lerp(1f, 1f - rectangleRoundPenalty * 0.60f, cornerPenalty);
        score = Mathf.Clamp01(score);
        return true;
    }

    List<Vector3> MakeCirclePoints(Vector2 center, float radius, int resolution, float y)
    {
        var res = new List<Vector3>(resolution + 1);
        for (int i = 0; i < resolution; i++)
        {
            float t = (i / (float)resolution) * Mathf.PI * 2f;
            float x = center.x + Mathf.Cos(t) * radius;
            float z = center.y + Mathf.Sin(t) * radius;
            res.Add(new Vector3(x, y, z));
        }

        res.Add(res[0]);
        EnsureCounterClockwiseXZ(res);
        return res;
    }

    /// <summary>Cercle fermé pour spawn UI (résolution = <see cref="circleResolution"/>).</summary>
    public List<Vector3> BuildUiPresetClosedCircle(Vector3 centerWorld, float radiusMeters)
    {
        radiusMeters = Mathf.Max(0.05f, radiusMeters);
        return MakeCirclePoints(new Vector2(centerWorld.x, centerWorld.z), radiusMeters, circleResolution, centerWorld.y);
    }

    /// <summary>Carré fermé axes monde, côté = <paramref name="sideLengthMeters"/>.</summary>
    public List<Vector3> BuildUiPresetClosedSquare(Vector3 centerWorld, float sideLengthMeters)
    {
        float half = Mathf.Max(0.05f, sideLengthMeters * 0.5f);
        var fit = new RectFit
        {
            center = new Vector2(centerWorld.x, centerWorld.z),
            axisX = Vector2.right,
            axisY = new Vector2(0f, 1f),
            minX = -half,
            maxX = half,
            minY = -half,
            maxY = half,
        };
        return MakeRectanglePoints(fit, rectPointsPerEdge, centerWorld.y, true);
    }

    /// <summary>Triangle équilatéral fermé (côté = <paramref name="sideLengthMeters"/>), centre au centre du triangle.</summary>
    public List<Vector3> BuildUiPresetClosedTriangle(Vector3 centerWorld, float sideLengthMeters)
    {
        sideLengthMeters = Mathf.Max(0.05f, sideLengthMeters);
        float R = sideLengthMeters / Mathf.Sqrt(3f);
        var pts = new List<Vector3>(4);
        for (int i = 0; i < 3; i++)
        {
            float ang = i * (2f * Mathf.PI / 3f);
            float x = centerWorld.x + Mathf.Cos(ang) * R;
            float z = centerWorld.z + Mathf.Sin(ang) * R;
            pts.Add(new Vector3(x, centerWorld.y, z));
        }

        pts.Add(pts[0]);
        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    bool EvaluateRectangleFit(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, out RectFit fit, out float score)
    {
        fit = default;
        score = 0f;

        if (sample == null || sample.Count < 10)
            return false;

        if (!ComputeBestRectFit(sample, out fit))
            return false;

        float maxDim = Mathf.Max(fit.width, fit.height);
        if (maxDim < 0.25f || fit.width < 0.15f || fit.height < 0.15f)
            return false;

        float borderErr = 0f;
        float cornerSupport = 0f;
        int nearBorderCount = 0;
        float borderThreshold = Mathf.Max(0.03f, maxDim * 0.09f);

        for (int i = 0; i < sample.Count; i++)
        {
            Vector2 v = sample[i] - fit.center;
            float x = Vector2.Dot(v, fit.axisX);
            float yy = Vector2.Dot(v, fit.axisY);

            float dx = Mathf.Min(Mathf.Abs(x - fit.minX), Mathf.Abs(x - fit.maxX));
            float dy = Mathf.Min(Mathf.Abs(yy - fit.minY), Mathf.Abs(yy - fit.maxY));
            float d = Mathf.Min(dx, dy);
            borderErr += d;

            if (d <= borderThreshold)
                nearBorderCount++;
        }

        Vector2 c0 = fit.center + fit.axisX * fit.minX + fit.axisY * fit.minY;
        Vector2 c1 = fit.center + fit.axisX * fit.maxX + fit.axisY * fit.minY;
        Vector2 c2 = fit.center + fit.axisX * fit.maxX + fit.axisY * fit.maxY;
        Vector2 c3 = fit.center + fit.axisX * fit.minX + fit.axisY * fit.maxY;
        Vector2[] rectCorners = { c0, c1, c2, c3 };

        float cornerThreshold = Mathf.Max(0.04f, maxDim * 0.14f);
        for (int k = 0; k < rectCorners.Length; k++)
        {
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < corners.Count; i++)
                bestDist = Mathf.Min(bestDist, Vector2.Distance(rectCorners[k], corners[i].point));

            float support = corners.Count == 0 ? 0f : Mathf.Clamp01(1f - bestDist / cornerThreshold);
            cornerSupport += support;
        }
        cornerSupport /= 4f;

        float borderScore = Mathf.Clamp01(1f - ((borderErr / sample.Count) / Mathf.Max(0.0001f, maxDim)) / Mathf.Max(0.02f, tolerance * 0.95f));
        float edgeCoverage = nearBorderCount / (float)sample.Count;
        float angleScore = EvaluateRectangleCornerAngles(corners, rectCorners);
        float circleLikePenalty = ComputeCircleLikeness(sample);

        score = borderScore * 0.48f;
        score += edgeCoverage * 0.22f;
        score += cornerSupport * rectangleCornerBoost * 0.18f;
        score += angleScore * 0.12f;
        score *= Mathf.Lerp(1f, 1f - rectangleRoundPenalty, circleLikePenalty);

        if (corners.Count < 3)
            score *= 0.65f;
        else if (corners.Count > 6)
            score *= 0.82f;

        score = Mathf.Clamp01(score);
        return true;
    }

    float EvaluateRectangleCornerAngles(List<CornerSample> corners, Vector2[] rectCorners)
    {
        if (corners == null || corners.Count == 0)
            return 0f;

        float sum = 0f;
        int count = 0;
        float maxRectDim = 0f;
        for (int i = 0; i < rectCorners.Length; i++)
            for (int j = i + 1; j < rectCorners.Length; j++)
                maxRectDim = Mathf.Max(maxRectDim, Vector2.Distance(rectCorners[i], rectCorners[j]));

        float threshold = Mathf.Max(0.04f, maxRectDim * 0.18f);
        for (int k = 0; k < rectCorners.Length; k++)
        {
            CornerSample best = default;
            float bestDist = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < corners.Count; i++)
            {
                float d = Vector2.Distance(rectCorners[k], corners[i].point);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = corners[i];
                    found = true;
                }
            }

            if (!found || bestDist > threshold)
                continue;

            float angleScore = Mathf.Clamp01(1f - Mathf.Abs(best.turn - 90f) / 45f);
            sum += angleScore;
            count++;
        }

        return count == 0 ? 0f : sum / count;
    }

    List<Vector3> MakeRectanglePoints(RectFit fit, int pointsPerEdge, float y, bool forceSquare)
    {
        float minx = fit.minX;
        float maxx = fit.maxX;
        float miny = fit.minY;
        float maxy = fit.maxY;

        if (forceSquare)
        {
            float size = Mathf.Max(fit.width, fit.height);
            float half = size * 0.5f;
            minx = -half;
            maxx = half;
            miny = -half;
            maxy = half;
        }

        Vector2 c = fit.center;
        Vector2 c0 = c + fit.axisX * minx + fit.axisY * miny;
        Vector2 c1 = c + fit.axisX * maxx + fit.axisY * miny;
        Vector2 c2 = c + fit.axisX * maxx + fit.axisY * maxy;
        Vector2 c3 = c + fit.axisX * minx + fit.axisY * maxy;

        var res = new List<Vector3>(pointsPerEdge * 4 + 1);
        AddEdge(res, c0, c1, pointsPerEdge, y);
        AddEdge(res, c1, c2, pointsPerEdge, y);
        AddEdge(res, c2, c3, pointsPerEdge, y);
        AddEdge(res, c3, c0, pointsPerEdge, y);

        if (res.Count > 0)
            res.Add(res[0]);

        EnsureCounterClockwiseXZ(res);
        return res;
    }

    void AddEdge(List<Vector3> list, Vector2 a, Vector2 b, int steps, float y)
    {
        steps = Mathf.Max(2, steps);

        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            Vector2 p = Vector2.Lerp(a, b, t);
            list.Add(new Vector3(p.x, y, p.y));
        }
    }

    bool EvaluateTriangleFit(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, out List<Vector2> fittedTriangle, out float score)
    {
        fittedTriangle = null;
        score = 0f;

        if (sample == null || sample.Count < 9)
            return false;

        List<CornerSample> dominant = GetDominantCorners(corners, 6, sample.Count / 10);
        if (dominant.Count < 3)
        {
            dominant = BuildFallbackCornersFromHull(sample);
        }

        if (dominant.Count < 3)
            return false;

        float bestScore = 0f;
        List<Vector2> bestTriangle = null;

        for (int i = 0; i < dominant.Count - 2; i++)
        {
            for (int j = i + 1; j < dominant.Count - 1; j++)
            {
                for (int k = j + 1; k < dominant.Count; k++)
                {
                    Vector2 a = dominant[i].point;
                    Vector2 b = dominant[j].point;
                    Vector2 c = dominant[k].point;

                    float doubleArea = Mathf.Abs(Cross(b - a, c - a));
                    if (doubleArea < 0.05f)
                        continue;

                    float perimeter = Vector2.Distance(a, b) + Vector2.Distance(b, c) + Vector2.Distance(c, a);
                    if (perimeter < 0.60f)
                        continue;

                    float angleA = AngleDeg(b - a, c - a);
                    float angleB = AngleDeg(a - b, c - b);
                    float angleC = AngleDeg(a - c, b - c);
                    float maxAngle = Mathf.Max(angleA, Mathf.Max(angleB, angleC));
                    float minAngle = Mathf.Min(angleA, Mathf.Min(angleB, angleC));

                    if (maxAngle > roundedTriangleMaxApexAngle)
                        continue;
                    if (minAngle < 14f)
                        continue;

                    float edgeErr = 0f;
                    int insideCount = 0;
                    for (int p = 0; p < sample.Count; p++)
                    {
                        Vector2 pt = sample[p];
                        float d1 = DistancePointSegment(pt, a, b);
                        float d2 = DistancePointSegment(pt, b, c);
                        float d3 = DistancePointSegment(pt, c, a);
                        float edgeDist = Mathf.Min(d1, Mathf.Min(d2, d3));
                        edgeErr += edgeDist;
                        if (PointInTriangleInclusive(pt, a, b, c))
                            insideCount++;
                    }

                    float outsideRatio = 1f - insideCount / (float)sample.Count;
                    float edgeNorm = (edgeErr / sample.Count) / Mathf.Max(0.0001f, perimeter / 3f);
                    float edgeScore = Mathf.Clamp01(1f - edgeNorm / Mathf.Max(0.03f, tolerance * triangleToleranceMultiplier));
                    float insideScore = Mathf.Clamp01(1f - outsideRatio / 0.45f);
                    float cornerSharpness = 0f;
                    cornerSharpness += FindNearestCornerSharpness(dominant[i], a);
                    cornerSharpness += FindNearestCornerSharpness(dominant[j], b);
                    cornerSharpness += FindNearestCornerSharpness(dominant[k], c);
                    cornerSharpness /= 3f;

                    float circlePenalty = ComputeCircleLikeness(sample);
                    float triScore = edgeScore * 0.50f + insideScore * 0.30f + cornerSharpness * 0.20f;
                    triScore *= Mathf.Lerp(1f, triangleMinScoreWhenStrokeIsCircular, circlePenalty);
                    triScore = Mathf.Clamp01(triScore);

                    if (triScore > bestScore)
                    {
                        bestScore = triScore;
                        bestTriangle = new List<Vector2> { a, b, c, a };
                    }
                }
            }
        }

        if (bestTriangle == null)
            return false;

        score = bestScore;
        fittedTriangle = bestTriangle;
        EnsureCounterClockwiseXZ(fittedTriangle);
        return true;
    }

    float FindNearestCornerSharpness(CornerSample sample, Vector2 point)
    {
        return Mathf.Clamp01(sample.sharpness / 120f);
    }

    bool PointInTriangleInclusive(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float o1 = Orientation(a, b, p);
        float o2 = Orientation(b, c, p);
        float o3 = Orientation(c, a, p);

        bool hasNeg = (o1 < -0.0001f) || (o2 < -0.0001f) || (o3 < -0.0001f);
        bool hasPos = (o1 > 0.0001f) || (o2 > 0.0001f) || (o3 > 0.0001f);

        return !(hasNeg && hasPos);
    }

    float AngleDeg(Vector2 a, Vector2 b)
    {
        if (a.sqrMagnitude < 0.000001f || b.sqrMagnitude < 0.000001f)
            return 180f;

        float dot = Mathf.Clamp(Vector2.Dot(a.normalized, b.normalized), -1f, 1f);
        return Mathf.Acos(dot) * Mathf.Rad2Deg;
    }

    float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;

        if (len2 < 0.000001f)
            return Vector2.Distance(p, a);

        float t = Vector2.Dot(p - a, ab) / len2;
        t = Mathf.Clamp01(t);
        return Vector2.Distance(p, a + ab * t);
    }

    bool ComputeBestRectFit(List<Vector2> pts, out RectFit bestFit)
    {
        bestFit = default;
        if (pts == null || pts.Count < 4)
            return false;

        List<Vector2> hull = ComputeConvexHull(new List<Vector2>(pts));
        if (hull.Count < 4)
            return false;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < pts.Count; i++)
            centroid += pts[i];
        centroid /= pts.Count;

        float bestArea = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % hull.Count];
            Vector2 axisX = (b - a).normalized;
            if (axisX.sqrMagnitude < 0.0001f)
                continue;

            Vector2 axisY = new Vector2(-axisX.y, axisX.x);
            float minx = float.PositiveInfinity;
            float maxx = float.NegativeInfinity;
            float miny = float.PositiveInfinity;
            float maxy = float.NegativeInfinity;

            for (int p = 0; p < pts.Count; p++)
            {
                Vector2 v = pts[p] - centroid;
                float x = Vector2.Dot(v, axisX);
                float yy = Vector2.Dot(v, axisY);
                minx = Mathf.Min(minx, x);
                maxx = Mathf.Max(maxx, x);
                miny = Mathf.Min(miny, yy);
                maxy = Mathf.Max(maxy, yy);
            }

            float area = (maxx - minx) * (maxy - miny);
            if (area < bestArea)
            {
                bestArea = area;
                bestFit.center = centroid;
                bestFit.axisX = axisX;
                bestFit.axisY = axisY;
                bestFit.minX = minx;
                bestFit.maxX = maxx;
                bestFit.minY = miny;
                bestFit.maxY = maxy;
                found = true;
            }
        }

        return found;
    }

    List<Vector2> GetOpenLoop(List<Vector2> closed)
    {
        List<Vector2> res = new List<Vector2>(closed);
        if (res.Count > 1 && Vector2.Distance(res[0], res[res.Count - 1]) < 0.0001f)
            res.RemoveAt(res.Count - 1);
        return res;
    }

    List<Vector2> ResampleClosedEvenly(List<Vector2> closed, int count)
    {
        List<Vector2> open = GetOpenLoop(closed);
        List<Vector2> res = new List<Vector2>();
        if (open.Count < 3)
            return res;

        float[] cumulative = new float[open.Count + 1];
        cumulative[0] = 0f;
        for (int i = 1; i < open.Count; i++)
            cumulative[i] = cumulative[i - 1] + Vector2.Distance(open[i - 1], open[i]);
        cumulative[open.Count] = cumulative[open.Count - 1] + Vector2.Distance(open[open.Count - 1], open[0]);

        float total = cumulative[open.Count];
        if (total < 0.0001f)
            return new List<Vector2>(open);

        for (int k = 0; k < count; k++)
        {
            float t = (k / (float)count) * total;
            int seg = 1;
            while (seg < cumulative.Length && cumulative[seg] < t)
                seg++;
            seg = Mathf.Clamp(seg, 1, cumulative.Length - 1);

            int aIndex = seg - 1;
            int bIndex = seg % open.Count;
            float segT = Mathf.InverseLerp(cumulative[seg - 1], cumulative[seg], t);
            res.Add(Vector2.Lerp(open[aIndex], open[bIndex], segT));
        }

        return res;
    }

    List<CornerSample> DetectCorners(List<Vector2> sample)
    {
        List<CornerSample> corners = new List<CornerSample>();
        if (sample == null || sample.Count < 6)
            return corners;

        int n = sample.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = sample[(i - 2 + n) % n];
            Vector2 cur = sample[i];
            Vector2 next = sample[(i + 2) % n];
            Vector2 v1 = (prev - cur).normalized;
            Vector2 v2 = (next - cur).normalized;
            if (v1.sqrMagnitude < 0.0001f || v2.sqrMagnitude < 0.0001f)
                continue;

            float angle = AngleDeg(v1, v2);
            float sharpness = Mathf.Clamp(180f - angle, 0f, 180f);
            if (sharpness < 18f)
                continue;

            bool isPeak = true;
            for (int offset = -2; offset <= 2; offset++)
            {
                if (offset == 0)
                    continue;

                int j = (i + offset + n) % n;
                Vector2 prev2 = sample[(j - 2 + n) % n];
                Vector2 cur2 = sample[j];
                Vector2 next2 = sample[(j + 2) % n];
                Vector2 a = (prev2 - cur2).normalized;
                Vector2 b = (next2 - cur2).normalized;
                float otherSharpness = Mathf.Clamp(180f - AngleDeg(a, b), 0f, 180f);
                if (otherSharpness > sharpness)
                {
                    isPeak = false;
                    break;
                }
            }

            if (!isPeak)
                continue;

            corners.Add(new CornerSample
            {
                index = i,
                turn = angle,
                sharpness = sharpness,
                point = cur
            });
        }

        corners.Sort((a, b) => b.sharpness.CompareTo(a.sharpness));
        return corners;
    }

    List<CornerSample> GetDominantCorners(List<CornerSample> corners, int maxCount, int minIndexGap)
    {
        List<CornerSample> result = new List<CornerSample>();
        if (corners == null)
            return result;

        for (int i = 0; i < corners.Count; i++)
        {
            bool tooClose = false;
            for (int k = 0; k < result.Count; k++)
            {
                if (Mathf.Abs(corners[i].index - result[k].index) < minIndexGap)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            result.Add(corners[i]);
            if (result.Count >= maxCount)
                break;
        }

        return result;
    }

    List<CornerSample> BuildFallbackCornersFromHull(List<Vector2> sample)
    {
        List<CornerSample> result = new List<CornerSample>();
        List<Vector2> hull = ComputeConvexHull(new List<Vector2>(sample));
        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 prev = hull[(i - 1 + hull.Count) % hull.Count];
            Vector2 cur = hull[i];
            Vector2 next = hull[(i + 1) % hull.Count];
            result.Add(new CornerSample
            {
                index = i,
                point = cur,
                turn = AngleDeg(prev - cur, next - cur),
                sharpness = Mathf.Clamp(180f - AngleDeg(prev - cur, next - cur), 0f, 180f)
            });
        }
        result.Sort((a, b) => b.sharpness.CompareTo(a.sharpness));
        return result;
    }

    float ComputeCircleLikeness(List<Vector2> sample)
    {
        if (sample == null || sample.Count < 8)
            return 0f;

        Vector2 center = Vector2.zero;
        for (int i = 0; i < sample.Count; i++)
            center += sample[i];
        center /= sample.Count;

        float avg = 0f;
        for (int i = 0; i < sample.Count; i++)
            avg += Vector2.Distance(center, sample[i]);
        avg /= sample.Count;
        if (avg < 0.0001f)
            return 0f;

        float err = 0f;
        for (int i = 0; i < sample.Count; i++)
            err += Mathf.Abs(Vector2.Distance(center, sample[i]) - avg);
        float radial = (err / sample.Count) / avg;
        return Mathf.Clamp01(1f - radial / 0.40f);
    }

    void ComputeAabb(List<Vector2> pts, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;

        for (int i = 0; i < pts.Count; i++)
        {
            minX = Mathf.Min(minX, pts[i].x);
            maxX = Mathf.Max(maxX, pts[i].x);
            minY = Mathf.Min(minY, pts[i].y);
            maxY = Mathf.Max(maxY, pts[i].y);
        }
    }

    List<Vector2> ComputeConvexHull(List<Vector2> pts)
    {
        if (pts.Count <= 3)
            return new List<Vector2>(pts);

        pts.Sort((p1, p2) =>
        {
            int cmp = p1.x.CompareTo(p2.x);
            return cmp == 0 ? p1.y.CompareTo(p2.y) : cmp;
        });

        List<Vector2> lower = new List<Vector2>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 &&
                   Cross(lower[lower.Count - 1] - lower[lower.Count - 2], p - lower[lower.Count - 1]) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(p);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            Vector2 p = pts[i];
            while (upper.Count >= 2 &&
                   Cross(upper[upper.Count - 1] - upper[upper.Count - 2], p - upper[upper.Count - 1]) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    bool HasSelfIntersection(List<Vector2> poly)
    {
        if (poly == null || poly.Count < 5)
            return false;

        for (int i = 0; i < poly.Count - 1; i++)
        {
            Vector2 a1 = poly[i];
            Vector2 a2 = poly[i + 1];

            for (int j = i + 1; j < poly.Count - 1; j++)
            {
                if (Mathf.Abs(i - j) <= 1)
                    continue;

                if (i == 0 && j == poly.Count - 2)
                    continue;

                Vector2 b1 = poly[j];
                Vector2 b2 = poly[j + 1];

                if (SegmentsIntersect(a1, a2, b1, b2))
                    return true;
            }
        }

        return false;
    }

    bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float o1 = Orientation(p1, p2, q1);
        float o2 = Orientation(p1, p2, q2);
        float o3 = Orientation(q1, q2, p1);
        float o4 = Orientation(q1, q2, p2);

        return (o1 * o2 < 0f) && (o3 * o4 < 0f);
    }

    float Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    void EnsureCounterClockwiseXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4)
            return;

        int count = pts.Count;
        bool closed = Vector3.Distance(pts[0], pts[count - 1]) < 0.0001f;
        int effective = closed ? count - 1 : count;

        float area = 0f;
        for (int i = 0; i < effective; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % effective];
            area += (a.x * b.z - b.x * a.z);
        }

        if (area < 0f)
        {
            if (closed)
            {
                pts.RemoveAt(pts.Count - 1);
                pts.Reverse();
                pts.Add(pts[0]);
            }
            else
            {
                pts.Reverse();
            }
        }
    }

    void EnsureCounterClockwiseXZ(List<Vector2> pts)
    {
        if (pts == null || pts.Count < 4)
            return;

        int count = pts.Count;
        bool closed = Vector2.Distance(pts[0], pts[count - 1]) < 0.0001f;
        int effective = closed ? count - 1 : count;

        float area = 0f;
        for (int i = 0; i < effective; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[(i + 1) % effective];
            area += (a.x * b.y - b.x * a.y);
        }

        if (area < 0f)
        {
            if (closed)
            {
                pts.RemoveAt(pts.Count - 1);
                pts.Reverse();
                pts.Add(pts[0]);
            }
            else
            {
                pts.Reverse();
            }
        }
    }
}
