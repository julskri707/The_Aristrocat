using System;
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
        Triangle
    }

    [Header("References")]
    public Camera cam;
    public Collider groundCollider;

    [Header("Drawing")]
    [Min(0.01f)] public float pointSpacing = 0.35f;
    [Min(0.01f)] public float snapCloseDistance = 1.0f;
    public bool flattenYToZero = true;

    [Header("Line Preview")]
    [Min(0.001f)] public float lineWidth = 0.12f;

    [Header("Grid")]
    public bool enableGridSnap = true;
    [Min(0.05f)] public float gridSize = 1.0f;
    public bool showGridInGame = false;
    public bool showGridGizmos = true;
    [Range(4, 200)] public int gridHalfExtent = 25;
    [Range(3, 8)] public int gridHierarchyLevels = 5;
    [Range(4f, 512f)] public float gridRootCellMultiplier = 64f;
    [Range(0.6f, 8f)] public float gridZoomRevealFactor = 2.4f;
    [Range(24, 220)] public int gridMaxLinesPerAxis = 120;
    [Range(0.0005f, 0.20f)] public float gridLineWidth = 0.02f;
    [Range(0.02f, 1.0f)] public float gridInnerAlpha = 0.38f;
    [Range(0.01f, 0.8f)] public float gridOuterAlpha = 0.06f;
    [Range(-0.05f, 0.2f)] public float gridVisualYOffset = 0.01f;
    public Vector3 gridOrigin = Vector3.zero;

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

    [Header("Circle")]
    [Range(16, 128)] public int circleResolution = 48;
    [Range(0.01f, 0.5f)] public float circleStrictnessMultiplier = 0.18f;
    [Range(1.0f, 1.5f)] public float circleDetectionBoost = 1.10f;

    [Header("Rectangle")]
    [Range(2, 30)] public int rectPointsPerEdge = 10;
    [Range(0.0f, 0.4f)] public float squareRatioTolerance = 0.12f;
    [Range(0.20f, 0.80f)] public float minRectangleProbability = 0.40f;
    [Range(0.20f, 0.98f)] public float rectangleCornerBoost = 0.70f;
    [Range(0.0f, 1.0f)] public float rectangleRoundPenalty = 0.45f;

    [Header("Triangle")]
    [Range(0.5f, 8.0f)] public float triangleToleranceMultiplier = 4.4f;
    [Range(4, 32)] public int roundedTriangleMaxCurvePoints = 12;
    [Range(40f, 170f)] public float roundedTriangleMaxApexAngle = 142f;
    [Range(0.10f, 0.80f)] public float minTriangleProbability = 0.18f;

    [Header("Shape Decision")]
    [Range(0.0f, 1.0f)] public float minClosedShapeConfidence = 0.22f;
    [Range(0.0f, 0.5f)] public float minClosedShapeLead = 0.05f;
    [Range(0.10f, 0.80f)] public float minCircleProbability = 0.30f;

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

    public IReadOnlyList<Vector3> CurrentPoints => _points;

    private readonly List<Vector3> _points = new List<Vector3>();
    private bool _isDrawing;
    private LineRenderer _lr;
    private Transform _gridVisualRoot;
    private readonly List<LineRenderer> _gridLines = new List<LineRenderer>();

    private static Material s_SharedPreviewMaterial;
    private static Material s_SharedGridMaterial;
    private bool _legacyGridForcedOff;

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
        EvaluateLegacyGridCompatibility();
        EnsureGridVisualObjects();
        UpdateGridVisuals();
    }

    void OnValidate()
    {
        pointSpacing = Mathf.Max(0.01f, pointSpacing);
        snapCloseDistance = Mathf.Max(0.01f, snapCloseDistance);
        lineWidth = Mathf.Max(0.001f, lineWidth);
        gridSize = Mathf.Max(0.05f, gridSize);
        gridHalfExtent = Mathf.Clamp(gridHalfExtent, 4, 200);
        gridHierarchyLevels = Mathf.Clamp(gridHierarchyLevels, 3, 8);
        gridRootCellMultiplier = Mathf.Clamp(gridRootCellMultiplier, 4f, 512f);
        gridZoomRevealFactor = Mathf.Clamp(gridZoomRevealFactor, 0.6f, 8f);
        gridMaxLinesPerAxis = Mathf.Clamp(gridMaxLinesPerAxis, 24, 220);
        gridLineWidth = Mathf.Clamp(gridLineWidth, 0.0005f, 0.20f);
        gridInnerAlpha = Mathf.Clamp(gridInnerAlpha, 0.02f, 1f);
        gridOuterAlpha = Mathf.Clamp(gridOuterAlpha, 0.01f, 0.8f);
        gridVisualYOffset = Mathf.Clamp(gridVisualYOffset, -0.05f, 0.2f);
        tolerance = Mathf.Clamp(tolerance, 0.01f, 0.5f);
        straightLineToleranceMultiplier = Mathf.Clamp(straightLineToleranceMultiplier, 0.005f, 0.2f);
        circleResolution = Mathf.Clamp(circleResolution, 16, 128);
        circleStrictnessMultiplier = Mathf.Clamp(circleStrictnessMultiplier, 0.01f, 0.5f);
        circleDetectionBoost = Mathf.Clamp(circleDetectionBoost, 1.0f, 1.5f);
        rectPointsPerEdge = Mathf.Clamp(rectPointsPerEdge, 2, 30);
        squareRatioTolerance = Mathf.Clamp(squareRatioTolerance, 0f, 0.4f);
        minRectangleProbability = Mathf.Clamp(minRectangleProbability, 0.20f, 0.80f);
        rectangleCornerBoost = Mathf.Clamp(rectangleCornerBoost, 0.20f, 0.98f);
        rectangleRoundPenalty = Mathf.Clamp01(rectangleRoundPenalty);
        triangleToleranceMultiplier = Mathf.Clamp(triangleToleranceMultiplier, 0.5f, 8.0f);
        roundedTriangleMaxCurvePoints = Mathf.Clamp(roundedTriangleMaxCurvePoints, 4, 32);
        roundedTriangleMaxApexAngle = Mathf.Clamp(roundedTriangleMaxApexAngle, 40f, 170f);
        minTriangleProbability = Mathf.Clamp(minTriangleProbability, 0.10f, 0.80f);
        minClosedShapeConfidence = Mathf.Clamp01(minClosedShapeConfidence);
        minClosedShapeLead = Mathf.Clamp(minClosedShapeLead, 0f, 0.5f);
        minCircleProbability = Mathf.Clamp(minCircleProbability, 0.10f, 0.80f);
        maxPathToHullPerimeterRatio = Mathf.Clamp(maxPathToHullPerimeterRatio, 1f, 2.5f);

        if (_lr == null)
            _lr = GetComponent<LineRenderer>();

        if (_lr != null)
            ApplyLineRendererSetup();

        EvaluateLegacyGridCompatibility();

        // Avoid creating/updating runtime grid visuals during OnValidate.
        // Unity warns when Transform/AddComponent messages are triggered here,
        // and it can spam hundreds of operations.
    }

    void Update()
    {
        if (cam == null)
            return;

        EvaluateLegacyGridCompatibility();
        UpdateGridVisuals();

        if (Input.GetMouseButtonDown(0))
            BeginDraw();

        if (_isDrawing && Input.GetMouseButton(0))
            ContinueDraw();

        if (_isDrawing && Input.GetMouseButtonUp(0))
            EndDraw();
    }

    void OnDisable()
    {
        for (int i = 0; i < _gridLines.Count; i++)
        {
            if (_gridLines[i] != null)
                _gridLines[i].enabled = false;
        }
    }

    void EvaluateLegacyGridCompatibility()
    {
        bool shouldForceOff = FindFirstObjectByType<HierarchicalGridManager>() != null;
        if (shouldForceOff == _legacyGridForcedOff)
            return;

        _legacyGridForcedOff = shouldForceOff;

        if (_legacyGridForcedOff)
        {
            for (int i = 0; i < _gridLines.Count; i++)
            {
                if (_gridLines[i] != null)
                    _gridLines[i].enabled = false;
            }
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

        int wanted = CountHierarchicalGridLineCount();
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
        EnsureGridVisualObjects();

        if (_legacyGridForcedOff || !showGridInGame || !enableGridSnap || _gridLines.Count == 0)
        {
            for (int i = 0; i < _gridLines.Count; i++)
            {
                if (_gridLines[i] != null && _gridLines[i].enabled)
                    _gridLines[i].enabled = false;
            }
            return;
        }

        Camera gridCam = cam != null ? cam : Camera.main;
        if (gridCam == null)
            return;

        float fineStep = Mathf.Max(0.05f, gridSize);
        int levels = Mathf.Clamp(gridHierarchyLevels, 3, 8);
        float rootStep = ComputeRootGridStep(fineStep);
        float coverage = rootStep * Mathf.Clamp(gridHalfExtent, 4, 200);
        float baseY = (flattenYToZero ? 0f : gridOrigin.y) + gridVisualYOffset;
        float cx = gridCam.transform.position.x;
        float cz = gridCam.transform.position.z;
        float camHeight = Mathf.Abs(gridCam.transform.position.y - baseY);
        float widthBase = Mathf.Clamp(gridLineWidth, 0.0005f, 0.20f);
        float minX = cx - coverage;
        float maxX = cx + coverage;
        float minZ = cz - coverage;
        float maxZ = cz + coverage;
        int lineCursor = 0;

        for (int level = 0; level < levels; level++)
        {
            float levelStep = ComputeLevelStep(rootStep, level);
            float visibility = ComputeLevelVisibility(camHeight, levelStep, level);
            if (visibility <= 0.001f)
                continue;

            float t = levels <= 1 ? 0f : level / (float)(levels - 1);
            float alpha = Mathf.Lerp(gridInnerAlpha, gridOuterAlpha, t) * visibility;
            float gray = Mathf.Lerp(0.62f, 0.83f, t);
            float width = widthBase * Mathf.Lerp(1.9f, 0.6f, t);
            Color color = new Color(gray, gray, gray, alpha);

            int xStartIndex = Mathf.FloorToInt((minX - gridOrigin.x) / levelStep);
            int xEndIndex = Mathf.CeilToInt((maxX - gridOrigin.x) / levelStep);
            int zStartIndex = Mathf.FloorToInt((minZ - gridOrigin.z) / levelStep);
            int zEndIndex = Mathf.CeilToInt((maxZ - gridOrigin.z) / levelStep);

            int xSampleStep = ComputeIndexSampleStep(xStartIndex, xEndIndex);
            int zSampleStep = ComputeIndexSampleStep(zStartIndex, zEndIndex);

            int firstX = AlignIndexToStride(xStartIndex, xSampleStep);
            int firstZ = AlignIndexToStride(zStartIndex, zSampleStep);

            for (int i = firstX; i <= xEndIndex; i += xSampleStep)
            {
                float x = gridOrigin.x + i * levelStep;
                EmitGridLine(ref lineCursor, new Vector3(x, baseY, minZ), new Vector3(x, baseY, maxZ), width, color);
            }

            for (int i = firstZ; i <= zEndIndex; i += zSampleStep)
            {
                float z = gridOrigin.z + i * levelStep;
                EmitGridLine(ref lineCursor, new Vector3(minX, baseY, z), new Vector3(maxX, baseY, z), width, color);
            }
        }

        for (int i = lineCursor; i < _gridLines.Count; i++)
        {
            if (_gridLines[i] != null)
                _gridLines[i].enabled = false;
        }
    }

    int CountHierarchicalGridLineCount()
    {
        float fineStep = Mathf.Max(0.05f, gridSize);
        int levels = Mathf.Clamp(gridHierarchyLevels, 3, 8);
        float rootStep = ComputeRootGridStep(fineStep);
        float coverage = rootStep * Mathf.Clamp(gridHalfExtent, 4, 200);
        int total = 0;

        for (int level = 0; level < levels; level++)
        {
            float levelStep = ComputeLevelStep(rootStep, level);

            int xStart = Mathf.FloorToInt((-coverage) / levelStep);
            int xEnd = Mathf.CeilToInt((coverage) / levelStep);
            int zStart = xStart;
            int zEnd = xEnd;

            int xSampleStep = ComputeIndexSampleStep(xStart, xEnd);
            int zSampleStep = ComputeIndexSampleStep(zStart, zEnd);

            int xFirst = AlignIndexToStride(xStart, xSampleStep);
            int zFirst = AlignIndexToStride(zStart, zSampleStep);

            int xCount = Mathf.Max(0, ((xEnd - xFirst) / xSampleStep) + 1);
            int zCount = Mathf.Max(0, ((zEnd - zFirst) / zSampleStep) + 1);
            total += xCount + zCount;
        }

        return Mathf.Max(1, total);
    }

    float ComputeRootGridStep(float fineStep)
    {
        return fineStep * Mathf.Max(4f, gridRootCellMultiplier);
    }

    float ComputeLevelStep(float rootStep, int level)
    {
        if (level <= 0)
            return rootStep;

        if (level == 1)
            return rootStep * 0.5f;

        float divisor = 2f * Mathf.Pow(4f, level - 1);
        return rootStep / Mathf.Max(1f, divisor);
    }

    float ComputeLevelVisibility(float cameraHeight, float levelStep, int level)
    {
        if (level == 0)
            return 1f;

        float reveal = Mathf.Max(0.6f, gridZoomRevealFactor);
        float appearStart = levelStep * reveal * 2.4f;
        float appearEnd = levelStep * reveal * 0.85f;
        if (appearStart <= appearEnd + 0.0001f)
            return cameraHeight <= appearEnd ? 1f : 0f;

        float t = Mathf.InverseLerp(appearStart, appearEnd, cameraHeight);
        return 1f - Mathf.Clamp01(t);
    }

    int ComputeIndexSampleStep(int startIndex, int endIndex)
    {
        int count = Mathf.Max(1, endIndex - startIndex + 1);
        int maxLines = Mathf.Clamp(gridMaxLinesPerAxis, 24, 220);
        if (count <= maxLines)
            return 1;

        return Mathf.Max(1, Mathf.CeilToInt(count / (float)maxLines));
    }

    static int AlignIndexToStride(int value, int stride)
    {
        int remainder = value % stride;
        if (remainder == 0)
            return value;

        if (value >= 0)
            return value + (stride - remainder);

        return value - remainder;
    }

    void EmitGridLine(ref int lineCursor, Vector3 a, Vector3 b, float width, Color color)
    {
        if (lineCursor >= _gridLines.Count)
            return;

        LineRenderer lr = _gridLines[lineCursor++];
        if (lr == null)
            return;

        lr.enabled = true;
        lr.loop = false;
        lr.positionCount = 2;
        lr.startWidth = width;
        lr.endWidth = width;
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

        float dist = Vector3.Distance(_points[_points.Count - 1], p);
        if (dist >= pointSpacing)
        {
            _points.Add(p);
            RefreshLine();
        }
    }

    void EndDraw()
    {
        _isDrawing = false;

        if (_points.Count < 2)
            return;

        string committedShapeName = "Free";

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

        if (enableGridSnap && closed && TryBuildGridRectangleFromPoints(_points, out List<Vector3> gridFitted, out string gridShapeName))
        {
            _points.Clear();
            _points.AddRange(gridFitted);
            RefreshLine();
            committedShapeName = gridShapeName;

            if (logDetectedShape)
                Debug.Log($"GridShape ✅ : {gridShapeName}");
        }
        else if (enableAutoShapes)
        {
            if (enableGridSnap && closed && useGridShapeDetectionOnlyWhenGridSnap)
            {
                committedShapeName = "Free";
            }
            else
            {
            bool canTryClosedShapes = (!requireClosedLoop || closed);
            bool canTryLine = autoStraightLine && !closed;

            if (canTryClosedShapes || canTryLine)
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
                    committedShapeName = closed ? "Free" : "Free";
                }
            }
            }
        }

        RefreshLine();

        LastCommittedShape = ShapeNameToKind(committedShapeName);
        LastCommittedShapeName = committedShapeName;

        List<Vector3> committedPoints = new List<Vector3>(_points);
        OnShapeCommittedDetailed?.Invoke(committedPoints, LastCommittedShape, LastCommittedShapeName);
        OnShapeCommitted?.Invoke(committedPoints);
    }

    bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

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

        if (enableGridSnap)
            p = SnapPointToGrid(p);

        return p;
    }

    Vector3 SnapPointToGrid(Vector3 p)
    {
        float step = Mathf.Max(0.05f, gridSize);
        float x = SnapAxis(p.x, gridOrigin.x, step);
        float z = SnapAxis(p.z, gridOrigin.z, step);
        return new Vector3(x, p.y, z);
    }

    static float SnapAxis(float value, float origin, float step)
    {
        return origin + Mathf.Round((value - origin) / step) * step;
    }

    bool TryBuildGridRectangleFromPoints(List<Vector3> points, out List<Vector3> fitted, out string shapeName)
    {
        fitted = null;
        shapeName = "Free";

        if (points == null || points.Count < 4)
            return false;

        List<Vector3> clean = BuildClosedUniquePath(points);
        if (clean.Count < 4)
            return false;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < clean.Count; i++)
        {
            Vector3 p = clean[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float width = maxX - minX;
        float depth = maxZ - minZ;
        float step = Mathf.Max(0.05f, gridSize);
        if (width < step || depth < step)
            return false;

        int nearLeft = 0;
        int nearRight = 0;
        int nearBottom = 0;
        int nearTop = 0;
        float borderTolerance = Mathf.Max(step * 0.40f, pointSpacing * 0.45f);
        float avgDistance = 0f;

        for (int i = 0; i < clean.Count; i++)
        {
            Vector3 p = clean[i];
            float dLeft = Mathf.Abs(p.x - minX);
            float dRight = Mathf.Abs(p.x - maxX);
            float dBottom = Mathf.Abs(p.z - minZ);
            float dTop = Mathf.Abs(p.z - maxZ);
            float d = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
            avgDistance += d;

            if (dLeft <= borderTolerance) nearLeft++;
            if (dRight <= borderTolerance) nearRight++;
            if (dBottom <= borderTolerance) nearBottom++;
            if (dTop <= borderTolerance) nearTop++;
        }

        avgDistance /= clean.Count;
        if (avgDistance > borderTolerance)
            return false;

        if (nearLeft == 0 || nearRight == 0 || nearBottom == 0 || nearTop == 0)
            return false;

        float y = points[0].y;
        fitted = new List<Vector3>(5)
        {
            new Vector3(minX, y, maxZ),
            new Vector3(minX, y, minZ),
            new Vector3(maxX, y, minZ),
            new Vector3(maxX, y, maxZ),
            new Vector3(minX, y, maxZ)
        };

        EnsureCounterClockwiseXZ(fitted);
        float ratio = Mathf.Abs(width - depth) / Mathf.Max(0.0001f, Mathf.Max(width, depth));
        shapeName = ratio <= Mathf.Max(squareRatioTolerance, step / Mathf.Max(0.0001f, width + depth)) ? "Square" : "Rectangle";
        return true;
    }

    static List<Vector3> BuildClosedUniquePath(List<Vector3> source)
    {
        var result = new List<Vector3>();
        if (source == null || source.Count == 0)
            return result;

        const float eps = 0.0001f;
        float epsSqr = eps * eps;

        for (int i = 0; i < source.Count; i++)
        {
            Vector3 p = source[i];
            if (result.Count == 0 || (p - result[result.Count - 1]).sqrMagnitude > epsSqr)
                result.Add(p);
        }

        if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude <= epsSqr)
            result.RemoveAt(result.Count - 1);

        return result;
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
            default:
                return DetectedShapeKind.Free;
        }
    }

    bool TryAutoFitShape(List<Vector3> rawPoints, bool closed, out List<Vector3> fittedPoints, out string shapeName)
    {
        fittedPoints = null;
        shapeName = "";

        if (!autoStraightLine && !autoCircle && !autoRectangle && !autoTriangle)
            return false;

        float y = rawPoints[0].y;

        List<Vector2> pts2 = ToXZ(rawPoints);
        pts2 = SimplifyBySpacing(pts2, pointSpacing * 0.85f);

        if (pts2.Count < 2)
        {
            ResetLiveScores();
            return false;
        }

        if (!closed)
        {
            ResetLiveScores();

            if (autoStraightLine &&
                TryFitStraightLine(
                    pts2,
                    tolerance * straightLineToleranceMultiplier,
                    out Vector2 lineStart,
                    out Vector2 lineEnd))
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

        List<Vector2> sample = ResampleClosedEvenly(rawClosed, 48);
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

        c.name = forceSquare ? "Square" : "Rectangle";
        c.score = score;
        c.points = MakeRectanglePoints(fit, rectPointsPerEdge, y, forceSquare);
        return c;
    }

    ShapeCandidate BuildTriangleCandidate(List<Vector2> ptsClosed, List<Vector2> sample, List<CornerSample> corners, float y)
    {
        ShapeCandidate c = default;
        c.name = "Triangle";

        if (!EvaluateTriangleFit(ptsClosed, sample, corners, out List<Vector2> triPts, out float score))
            return c;

        c.score = score;
        c.points = ToWorldPath(triPts, y);
        return c;
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

    bool TryFitStraightLine(List<Vector2> pts, float tol, out Vector2 start, out Vector2 end)
    {
        start = Vector2.zero;
        end = Vector2.zero;

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

    List<Vector3> MakeStraightLinePoints(Vector2 start, Vector2 end, float y)
    {
        return new List<Vector3>
        {
            new Vector3(start.x, y, start.y),
            new Vector3(end.x, y, end.y)
        };
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
        if (radius < 0.15f)
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
        float cornerPenalty = Mathf.Clamp01((corners.Count - 2) / 5f);
        float radialScore = Mathf.Clamp01(1f - radialErr / Mathf.Max(0.02f, circleStrictnessMultiplier));
        float aspectScore = Mathf.InverseLerp(0.55f, 0.98f, aspect);
        float rangeScore = Mathf.Clamp01(1f - ((maxDist - minDist) / Mathf.Max(0.0001f, radius)) / 0.55f);

        score = (radialScore * 0.55f + aspectScore * 0.25f + rangeScore * 0.20f) * circleDetectionBoost;
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
                    triScore *= Mathf.Lerp(1f, 0.80f, circlePenalty);
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
        return Mathf.Clamp01(1f - radial / 0.35f);
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

    void OnDrawGizmos()
    {
        if (!showGridGizmos || FindFirstObjectByType<HierarchicalGridManager>() != null)
            return;

        float fineStep = Mathf.Max(0.05f, gridSize);
        int levels = Mathf.Clamp(gridHierarchyLevels, 3, 8);
        float rootStep = ComputeRootGridStep(fineStep);
        float coverage = rootStep * Mathf.Clamp(gridHalfExtent, 4, 200);

        Vector3 center = transform.position;
        if (cam != null)
            center = cam.transform.position;

        float baseY = flattenYToZero ? 0f : gridOrigin.y;
        float minX = center.x - coverage;
        float maxX = center.x + coverage;
        float minZ = center.z - coverage;
        float maxZ = center.z + coverage;
        float camHeight = Mathf.Abs(center.y - baseY);

        for (int level = 0; level < levels; level++)
        {
            float levelStep = ComputeLevelStep(rootStep, level);
            float visibility = ComputeLevelVisibility(camHeight, levelStep, level);
            if (visibility <= 0.001f)
                continue;

            float t = levels <= 1 ? 0f : level / (float)(levels - 1);
            float alpha = Mathf.Lerp(gridInnerAlpha, gridOuterAlpha, t) * visibility;
            float gray = Mathf.Lerp(0.62f, 0.83f, t);
            Gizmos.color = new Color(gray, gray, gray, alpha);

            int xStartIndex = Mathf.FloorToInt((minX - gridOrigin.x) / levelStep);
            int xEndIndex = Mathf.CeilToInt((maxX - gridOrigin.x) / levelStep);
            int zStartIndex = Mathf.FloorToInt((minZ - gridOrigin.z) / levelStep);
            int zEndIndex = Mathf.CeilToInt((maxZ - gridOrigin.z) / levelStep);

            int xSampleStep = ComputeIndexSampleStep(xStartIndex, xEndIndex);
            int zSampleStep = ComputeIndexSampleStep(zStartIndex, zEndIndex);

            int firstX = AlignIndexToStride(xStartIndex, xSampleStep);
            int firstZ = AlignIndexToStride(zStartIndex, zSampleStep);

            for (int i = firstX; i <= xEndIndex; i += xSampleStep)
            {
                float x = gridOrigin.x + i * levelStep;
                Gizmos.DrawLine(new Vector3(x, baseY, minZ), new Vector3(x, baseY, maxZ));
            }

            for (int i = firstZ; i <= zEndIndex; i += zSampleStep)
            {
                float z = gridOrigin.z + i * levelStep;
                Gizmos.DrawLine(new Vector3(minX, baseY, z), new Vector3(maxX, baseY, z));
            }
        }
    }
}
