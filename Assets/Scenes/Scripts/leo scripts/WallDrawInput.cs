using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class WallDrawInput : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public Collider groundCollider;

    [Header("Drawing")]
    [Min(0.01f)] public float pointSpacing = 0.35f;
    [Min(0.01f)] public float snapCloseDistance = 1.0f;
    public bool flattenYToZero = true;

    [Header("Line Preview")]
    [Min(0.001f)] public float lineWidth = 0.12f;

    [Header("Auto Shapes")]
    public bool enableAutoShapes = true;
    public bool requireClosedLoop = true;

    [Range(0.01f, 0.5f)] public float tolerance = 0.12f;

    public bool autoStraightLine = true;
    public bool autoCircle = true;
    public bool autoRectangle = true;
    public bool autoTriangle = true;

    [Header("Straight Line")]
    [Tooltip("Plus petit = ligne plus stricte")]
    [Range(0.005f, 0.2f)] public float straightLineToleranceMultiplier = 0.45f;

    [Header("Circle")]
    [Range(16, 128)] public int circleResolution = 48;
    [Range(0.01f, 0.5f)] public float circleStrictnessMultiplier = 0.35f;
    [Tooltip("1.15 = environ 15% plus facile à reconnaître")]
    [Range(1.0f, 1.5f)] public float circleDetectionBoost = 1.15f;

    [Header("Rectangle")]
    [Range(2, 30)] public int rectPointsPerEdge = 10;
    [Range(0.0f, 0.4f)] public float squareRatioTolerance = 0.12f;

    [Header("Triangle")]
    [Tooltip("Plus grand = triangle plus tolérant")]
    [Range(0.5f, 8.0f)] public float triangleToleranceMultiplier = 2.6f;

    [Tooltip("Conservé pour compatibilité UI")]
    [Range(4, 32)] public int roundedTriangleMaxCurvePoints = 12;

    [Tooltip("Angle maximal (en degrés) du coin le plus pointu")]
    [Range(40f, 170f)] public float roundedTriangleMaxApexAngle = 120f;

    [Header("Shape Decision")]
    [Tooltip("Score absolu minimum pour accepter une forme fermée. 0.25 = 25%")]
    [Range(0.0f, 1.0f)] public float minClosedShapeConfidence = 0.25f;

    [Tooltip("Écart minimum entre la meilleure forme et la deuxième")]
    [Range(0.0f, 0.5f)] public float minClosedShapeLead = 0.04f;

    [Header("Closed Shape Rejection")]
    [Tooltip("Si le tracé est trop long par rapport à son contour global, on garde la forme libre")]
    [Range(1.0f, 2.5f)] public float maxPathToHullPerimeterRatio = 1.35f;

    [Tooltip("Si le tracé s'auto-croise, on garde la forme libre")]
    public bool rejectSelfIntersectingClosedShapes = true;

    [Header("Debug")]
    public bool logDetectedShape = true;
    public bool logShapeScores = true;

    public event Action<List<Vector3>> OnShapeCommitted;

    public IReadOnlyList<Vector3> CurrentPoints => _points;

    private readonly List<Vector3> _points = new List<Vector3>();
    private bool _isDrawing;
    private LineRenderer _lr;

    private static Material s_SharedPreviewMaterial;

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

    void Reset()
    {
        cam = Camera.main;
    }

    void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        ApplyLineRendererSetup();
    }

    void OnValidate()
    {
        triangleToleranceMultiplier = Mathf.Clamp(triangleToleranceMultiplier, 0.5f, 8.0f);
        roundedTriangleMaxApexAngle = Mathf.Clamp(roundedTriangleMaxApexAngle, 40f, 170f);
        circleStrictnessMultiplier = Mathf.Clamp(circleStrictnessMultiplier, 0.01f, 0.5f);
        circleDetectionBoost = Mathf.Clamp(circleDetectionBoost, 1.0f, 1.5f);
        minClosedShapeConfidence = Mathf.Clamp01(minClosedShapeConfidence);
        minClosedShapeLead = Mathf.Clamp(minClosedShapeLead, 0f, 0.5f);
        maxPathToHullPerimeterRatio = Mathf.Clamp(maxPathToHullPerimeterRatio, 1f, 2.5f);

        if (_lr == null)
            _lr = GetComponent<LineRenderer>();

        if (_lr != null)
            ApplyLineRendererSetup();
    }

    void Update()
    {
        if (cam == null)
            return;

        if (Input.GetMouseButtonDown(0))
            BeginDraw();

        if (_isDrawing && Input.GetMouseButton(0))
            ContinueDraw();

        if (_isDrawing && Input.GetMouseButtonUp(0))
            EndDraw();
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

    void BeginDraw()
    {
        _points.Clear();

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

        if (enableAutoShapes)
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

                    if (logDetectedShape)
                        Debug.Log($"AutoShape ✅ : {shapeName}");
                }
            }
        }

        RefreshLine();
        OnShapeCommitted?.Invoke(new List<Vector3>(_points));
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

        return p;
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

    bool TryAutoFitShape(List<Vector3> rawPoints, bool closed, out List<Vector3> fittedPoints, out string shapeName)
    {
        fittedPoints = null;
        shapeName = "";

        if (!autoStraightLine && !autoCircle && !autoRectangle && !autoTriangle)
            return false;

        float y = rawPoints[0].y;

        List<Vector2> pts2 = ToXZ(rawPoints);
        pts2 = SimplifyBySpacing(pts2, pointSpacing * 0.9f);

        if (pts2.Count < 2)
            return false;

        if (!closed)
        {
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
            return false;

        if (ShouldRejectClosedShapeAsFreeform(pts2))
        {
            if (logShapeScores)
                Debug.Log("Shape scores → Rectangle: 0% | Triangle: 0% | Circle: 0% (rejeté: forme libre)");
            return false;
        }

        ShapeCandidate rectangle = default;
        ShapeCandidate triangle = default;
        ShapeCandidate circle = default;

        if (autoRectangle)
            rectangle = BuildRectangleCandidate(pts2, y);

        if (autoTriangle)
            triangle = BuildTriangleCandidate(pts2, y);

        if (autoCircle)
            circle = BuildCircleCandidate(pts2, y);

        float rectPct = rectangle.score * 100f;
        float triPct = triangle.score * 100f;
        float circPct = circle.score * 100f;

        if (logShapeScores)
            Debug.Log($"Shape scores → Rectangle: {rectPct:0.#}% | Triangle: {triPct:0.#}% | Circle: {circPct:0.#}%");

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

        if (best.points == null)
            return false;

        if (best.score < minClosedShapeConfidence)
            return false;

        if ((best.score - second.score) < minClosedShapeLead)
            return false;

        fittedPoints = best.points;
        shapeName = best.name;
        return true;
    }

    bool ShouldRejectClosedShapeAsFreeform(List<Vector2> ptsClosed)
    {
        if (ptsClosed == null || ptsClosed.Count < 6)
            return true;

        int n = ptsClosed.Count - 1;
        if (n < 5)
            return true;

        List<Vector2> raw = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
            raw.Add(ptsClosed[i]);

        if (rejectSelfIntersectingClosedShapes && HasSelfIntersection(ptsClosed))
            return true;

        List<Vector2> hull = ComputeConvexHull(new List<Vector2>(raw));
        if (hull.Count < 3)
            return true;

        float pathPerimeter = ComputePolylineLength(raw, true);
        float hullPerimeter = ComputePolylineLength(hull, true);

        if (hullPerimeter < 0.0001f)
            return true;

        float pathToHullRatio = pathPerimeter / hullPerimeter;
        if (pathToHullRatio > maxPathToHullPerimeterRatio)
            return true;

        return false;
    }

    ShapeCandidate BuildCircleCandidate(List<Vector2> ptsClosed, float y)
    {
        ShapeCandidate c = default;
        c.name = "Circle";

        if (!EvaluateCircleFit(ptsClosed, out Vector2 center, out float radius, out float score))
            return c;

        c.score = score;
        c.points = MakeCirclePoints(center, radius, circleResolution, y);
        return c;
    }

    ShapeCandidate BuildRectangleCandidate(List<Vector2> ptsClosed, float y)
    {
        ShapeCandidate c = default;
        c.name = "Rectangle";

        if (!EvaluateRectangleFit(ptsClosed, out RectFit fit, out float score))
            return c;

        bool forceSquare =
            Mathf.Abs(fit.width - fit.height) /
            Mathf.Max(0.0001f, Mathf.Max(fit.width, fit.height)) <= squareRatioTolerance;

        c.name = forceSquare ? "Square" : "Rectangle";
        c.score = score;
        c.points = MakeRectanglePoints(fit, rectPointsPerEdge, y, forceSquare);
        return c;
    }

    ShapeCandidate BuildTriangleCandidate(List<Vector2> ptsClosed, float y)
    {
        ShapeCandidate c = default;
        c.name = "Triangle";

        if (!EvaluateTriangleFit(ptsClosed, out List<Vector2> triPts, out float score))
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
        if (pts.Count == 0)
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

    bool EvaluateCircleFit(List<Vector2> ptsClosed, out Vector2 center, out float radius, out float score)
    {
        center = Vector2.zero;
        radius = 0f;
        score = 0f;

        int n = ptsClosed.Count - 1;
        if (n < 6)
            return false;

        for (int i = 0; i < n; i++)
            center += ptsClosed[i];
        center /= n;

        float sum = 0f;
        for (int i = 0; i < n; i++)
            sum += Vector2.Distance(center, ptsClosed[i]);

        radius = sum / n;
        if (radius < 0.15f)
            return false;

        float err = 0f;
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < n; i++)
        {
            float d = Vector2.Distance(center, ptsClosed[i]);
            err += Mathf.Abs(d - radius);

            minX = Mathf.Min(minX, ptsClosed[i].x);
            maxX = Mathf.Max(maxX, ptsClosed[i].x);
            minY = Mathf.Min(minY, ptsClosed[i].y);
            maxY = Mathf.Max(maxY, ptsClosed[i].y);
        }

        float radialErr = (err / n) / Mathf.Max(0.0001f, radius);

        float w = maxX - minX;
        float h = maxY - minY;
        if (w < 0.001f || h < 0.001f)
            return false;

        float ratio = Mathf.Min(w, h) / Mathf.Max(w, h);

        float effectiveTol = Mathf.Max(0.035f, tolerance * Mathf.Max(0.95f, circleStrictnessMultiplier * 4.0f));
        effectiveTol *= circleDetectionBoost;

        float errorScore = Mathf.Clamp01(1f - (radialErr / effectiveTol));
        float ratioScore = Mathf.InverseLerp(0.36f, 0.98f, ratio);

        score = errorScore * 0.72f + ratioScore * 0.28f;
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

    bool EvaluateRectangleFit(List<Vector2> ptsClosed, out RectFit fit, out float score)
    {
        fit = default;
        score = 0f;

        int n = ptsClosed.Count - 1;
        if (n < 8)
            return false;

        var pts = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
            pts.Add(ptsClosed[i]);

        var hull = ComputeConvexHull(new List<Vector2>(pts));
        if (hull.Count < 4)
            return false;

        float bestArea = float.PositiveInfinity;
        RectFit bestFit = default;
        bool found = false;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++)
            centroid += ptsClosed[i];
        centroid /= n;

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

            for (int k = 0; k < hull.Count; k++)
            {
                Vector2 v = hull[k] - centroid;
                float x = Vector2.Dot(v, axisX);
                float yy = Vector2.Dot(v, axisY);
                minx = Mathf.Min(minx, x);
                maxx = Mathf.Max(maxx, x);
                miny = Mathf.Min(miny, yy);
                maxy = Mathf.Max(maxy, yy);
            }

            float w = maxx - minx;
            float h = maxy - miny;
            if (w < 0.0001f || h < 0.0001f)
                continue;

            float area = w * h;
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

        if (!found)
            return false;

        float wBest = bestFit.width;
        float hBest = bestFit.height;
        if (wBest < 0.3f || hBest < 0.3f)
            return false;

        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 v = ptsClosed[i] - bestFit.center;
            float x = Vector2.Dot(v, bestFit.axisX);
            float yy = Vector2.Dot(v, bestFit.axisY);

            float dx = Mathf.Min(Mathf.Abs(x - bestFit.minX), Mathf.Abs(x - bestFit.maxX));
            float dy = Mathf.Min(Mathf.Abs(yy - bestFit.minY), Mathf.Abs(yy - bestFit.maxY));
            total += Mathf.Min(dx, dy);
        }

        float norm = (total / n) / Mathf.Max(wBest, hBest);
        float effectiveTol = Mathf.Max(0.025f, tolerance * 1.10f);

        score = Mathf.Clamp01(1f - (norm / effectiveTol));

        float aspect = Mathf.Min(wBest, hBest) / Mathf.Max(wBest, hBest);
        if (aspect > 0.70f)
            score *= 0.90f;

        if (hull.Count > 5)
            score *= 0.88f;

        fit = bestFit;
        return true;
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

    bool EvaluateTriangleFit(List<Vector2> ptsClosed, out List<Vector2> fittedTriangle, out float score)
    {
        fittedTriangle = null;
        score = 0f;

        int n = ptsClosed.Count - 1;
        if (n < 5)
            return false;

        List<Vector2> raw = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
            raw.Add(ptsClosed[i]);

        List<Vector2> hull = ComputeConvexHull(new List<Vector2>(raw));
        if (hull.Count < 3 || hull.Count > 5)
            return false;

        float bestError = float.PositiveInfinity;
        Vector2 bestA = Vector2.zero;
        Vector2 bestB = Vector2.zero;
        Vector2 bestC = Vector2.zero;
        bool found = false;

        for (int i = 0; i < hull.Count - 2; i++)
        {
            for (int j = i + 1; j < hull.Count - 1; j++)
            {
                for (int k = j + 1; k < hull.Count; k++)
                {
                    Vector2 a = hull[i];
                    Vector2 b = hull[j];
                    Vector2 c = hull[k];

                    float area2 = Mathf.Abs(Cross(b - a, c - a));
                    if (area2 < 0.08f)
                        continue;

                    float perimeter = Vector2.Distance(a, b) + Vector2.Distance(b, c) + Vector2.Distance(c, a);
                    if (perimeter < 0.8f)
                        continue;

                    float angleA = AngleDeg(b - a, c - a);
                    float angleB = AngleDeg(a - b, c - b);
                    float angleC = AngleDeg(a - c, b - c);

                    float minAngle = Mathf.Min(angleA, Mathf.Min(angleB, angleC));
                    float maxAllowedSharpness = Mathf.Min(roundedTriangleMaxApexAngle + 10f, 150f);

                    if (minAngle > maxAllowedSharpness)
                        continue;

                    float errorSum = 0f;
                    int outsideCount = 0;

                    for (int p = 0; p < raw.Count; p++)
                    {
                        Vector2 pt = raw[p];

                        float d1 = DistancePointSegment(pt, a, b);
                        float d2 = DistancePointSegment(pt, b, c);
                        float d3 = DistancePointSegment(pt, c, a);
                        float d = Mathf.Min(d1, Mathf.Min(d2, d3));

                        bool inside = PointInTriangleInclusive(pt, a, b, c);
                        if (!inside)
                            outsideCount++;

                        errorSum += inside ? d : d * 2.4f;
                    }

                    float outsideRatio = outsideCount / (float)raw.Count;
                    if (outsideRatio > 0.18f)
                        continue;

                    float normErr = (errorSum / raw.Count) / Mathf.Max(0.0001f, perimeter / 3f);

                    if (normErr < bestError)
                    {
                        bestError = normErr;
                        bestA = a;
                        bestB = b;
                        bestC = c;
                        found = true;
                    }
                }
            }
        }

        if (!found)
            return false;

        float effectiveTol = Mathf.Max(0.03f, tolerance * triangleToleranceMultiplier * 0.75f);
        score = Mathf.Clamp01(1f - (bestError / effectiveTol));

        fittedTriangle = new List<Vector2>
        {
            bestA,
            bestB,
            bestC,
            bestA
        };

        EnsureCounterClockwiseXZ(fittedTriangle);
        return true;
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

    float ComputePolylineLength(List<Vector2> pts, bool closed)
    {
        if (pts == null || pts.Count < 2)
            return 0f;

        float len = 0f;

        for (int i = 0; i < pts.Count - 1; i++)
            len += Vector2.Distance(pts[i], pts[i + 1]);

        if (closed)
            len += Vector2.Distance(pts[pts.Count - 1], pts[0]);

        return len;
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