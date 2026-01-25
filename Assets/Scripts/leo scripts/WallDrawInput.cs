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

    public bool autoCircle = true;
    public bool autoRectangle = true;
    public bool autoTriangle = true;

    [Range(16, 128)] public int circleResolution = 48;
    [Range(2, 30)] public int rectPointsPerEdge = 10;
    [Range(0.0f, 0.4f)] public float squareRatioTolerance = 0.12f;

    [Header("Triangle (Improved)")]
    public float triangleToleranceMultiplier = 1.6f;
    [Range(0.30f, 0.95f)] public float triangleMinEdgeCoverage = 0.60f;
    [Range(0.05f, 0.45f)] public float triangleMinPerEdgeUsage = 0.12f;

    [Header("Debug")]
    public bool logDetectedShape = true;

    // ✅ IMPORTANT : event pour construire un vrai mur après le dessin
    public event Action<List<Vector3>> OnShapeCommitted;

    public IReadOnlyList<Vector3> CurrentPoints => _points;

    private readonly List<Vector3> _points = new List<Vector3>();
    private bool _isDrawing;
    private LineRenderer _lr;

    void Reset() => cam = Camera.main;

    void Awake()
    {
        _lr = GetComponent<LineRenderer>();
        _lr.useWorldSpace = true;
        _lr.positionCount = 0;
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;

        if (_lr.material == null)
            _lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        _lr.textureMode = LineTextureMode.Stretch;
    }

    void OnValidate()
    {
        triangleToleranceMultiplier = Mathf.Clamp(triangleToleranceMultiplier, 0.3f, 5.0f);
    }

    void Update()
    {
        if (cam == null) return;

        if (Input.GetMouseButtonDown(0)) BeginDraw();
        if (_isDrawing && Input.GetMouseButton(0)) ContinueDraw();
        if (_isDrawing && Input.GetMouseButtonUp(0)) EndDraw();
    }

    void BeginDraw()
    {
        _points.Clear();
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

        if (_points.Count < 3)
            return;

        bool closed = false;
        float closeDist = Vector3.Distance(_points[_points.Count - 1], _points[0]);
        if (closeDist <= snapCloseDistance)
        {
            _points[_points.Count - 1] = _points[0];
            closed = true;
        }

        // Auto shapes
        if (enableAutoShapes)
        {
            if (!requireClosedLoop || closed)
            {
                if (TryAutoFitShape(_points, out List<Vector3> fitted, out string shapeName))
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

        // ✅ ENVOIE LA FORME AU WALL SYSTEM
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
        if (flattenYToZero) p.y = 0f;
        return p;
    }

    void RefreshLine()
    {
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;

        _lr.positionCount = _points.Count;
        for (int i = 0; i < _points.Count; i++)
            _lr.SetPosition(i, _points[i]);
    }

    // =========================
    // AUTO SHAPES (simple)
    // =========================

    bool TryAutoFitShape(List<Vector3> rawPoints, out List<Vector3> fittedPoints, out string shapeName)
    {
        fittedPoints = null;
        shapeName = "";

        if (!autoCircle && !autoRectangle && !autoTriangle) return false;

        List<Vector2> pts2 = ToXZ(rawPoints);
        pts2 = SimplifyBySpacing(pts2, pointSpacing * 0.9f);
        if (pts2.Count < 6) return false;

        if (Vector2.Distance(pts2[0], pts2[pts2.Count - 1]) > 0.0001f)
            pts2.Add(pts2[0]);

        float y = rawPoints[0].y;

        if (autoCircle && TryFitCircle(pts2, tolerance, out Vector2 cc, out float rr))
        {
            shapeName = "Circle";
            fittedPoints = MakeCirclePoints(cc, rr, circleResolution, y);
            return true;
        }

        if (autoRectangle && TryFitRectangle(pts2, tolerance, out RectFit rectFit))
        {
            bool forceSquare =
                Mathf.Abs(rectFit.width - rectFit.height) /
                Mathf.Max(0.0001f, Mathf.Max(rectFit.width, rectFit.height)) <= squareRatioTolerance;

            shapeName = forceSquare ? "Square" : "Rectangle";
            fittedPoints = MakeRectanglePoints(rectFit, rectPointsPerEdge, y, forceSquare);
            return true;
        }

        if (autoTriangle && TryFitTriangleImproved(pts2, tolerance, out Vector2 A, out Vector2 B, out Vector2 C))
        {
            shapeName = "Triangle";
            fittedPoints = MakeTrianglePoints(A, B, C, y);
            return true;
        }

        return false;
    }

    List<Vector2> ToXZ(List<Vector3> p3)
    {
        var list = new List<Vector2>(p3.Count);
        for (int i = 0; i < p3.Count; i++)
            list.Add(new Vector2(p3[i].x, p3[i].z));
        return list;
    }

    List<Vector2> SimplifyBySpacing(List<Vector2> pts, float spacing)
    {
        var res = new List<Vector2>();
        if (pts.Count == 0) return res;

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

    // Circle
    bool TryFitCircle(List<Vector2> ptsClosed, float tol, out Vector2 center, out float radius)
    {
        center = Vector2.zero;
        radius = 0f;

        int n = ptsClosed.Count - 1;
        if (n < 6) return false;

        for (int i = 0; i < n; i++) center += ptsClosed[i];
        center /= n;

        float sum = 0f;
        for (int i = 0; i < n; i++) sum += Vector2.Distance(center, ptsClosed[i]);
        radius = sum / n;
        if (radius < 0.2f) return false;

        float err = 0f;
        for (int i = 0; i < n; i++)
            err += Mathf.Abs(Vector2.Distance(center, ptsClosed[i]) - radius);

        err = (err / n) / radius;
        return err <= tol;
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
        return res;
    }

    // Rectangle
    struct RectFit
    {
        public Vector2 center;
        public Vector2 axisX;
        public Vector2 axisY;
        public float minX, maxX;
        public float minY, maxY;

        public float width => maxX - minX;
        public float height => maxY - minY;
    }

    bool TryFitRectangle(List<Vector2> ptsClosed, float tol, out RectFit fit)
    {
        fit = default;

        int n = ptsClosed.Count - 1;
        if (n < 8) return false;

        Vector2 pMin = ptsClosed[0], pMax = ptsClosed[0];
        float best = 0f;

        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            float d = (ptsClosed[j] - ptsClosed[i]).sqrMagnitude;
            if (d > best)
            {
                best = d;
                pMin = ptsClosed[i];
                pMax = ptsClosed[j];
            }
        }

        Vector2 axisX = (pMax - pMin).normalized;
        if (axisX.sqrMagnitude < 0.0001f) return false;
        Vector2 axisY = new Vector2(-axisX.y, axisX.x);

        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++) c += ptsClosed[i];
        c /= n;

        float minx = float.PositiveInfinity, maxx = float.NegativeInfinity;
        float miny = float.PositiveInfinity, maxy = float.NegativeInfinity;

        for (int i = 0; i < n; i++)
        {
            Vector2 v = ptsClosed[i] - c;
            float x = Vector2.Dot(v, axisX);
            float y = Vector2.Dot(v, axisY);
            minx = Mathf.Min(minx, x); maxx = Mathf.Max(maxx, x);
            miny = Mathf.Min(miny, y); maxy = Mathf.Max(maxy, y);
        }

        float w = maxx - minx;
        float h = maxy - miny;
        if (w < 0.3f || h < 0.3f) return false;

        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 v = ptsClosed[i] - c;
            float x = Vector2.Dot(v, axisX);
            float y = Vector2.Dot(v, axisY);

            float dx = Mathf.Min(Mathf.Abs(x - minx), Mathf.Abs(x - maxx));
            float dy = Mathf.Min(Mathf.Abs(y - miny), Mathf.Abs(y - maxy));
            total += Mathf.Min(dx, dy);
        }

        float norm = (total / n) / Mathf.Max(w, h);
        if (norm > tol) return false;

        fit.center = c;
        fit.axisX = axisX;
        fit.axisY = axisY;
        fit.minX = minx; fit.maxX = maxx;
        fit.minY = miny; fit.maxY = maxy;
        return true;
    }

    List<Vector3> MakeRectanglePoints(RectFit fit, int pointsPerEdge, float y, bool forceSquare)
    {
        float minx = fit.minX, maxx = fit.maxX, miny = fit.minY, maxy = fit.maxY;

        if (forceSquare)
        {
            float size = Mathf.Max(fit.width, fit.height);
            float half = size * 0.5f;
            minx = -half; maxx = half;
            miny = -half; maxy = half;
        }

        Vector2 C = fit.center;
        Vector2 Corner(float x, float yy) => C + fit.axisX * x + fit.axisY * yy;

        Vector2 c0 = Corner(minx, miny);
        Vector2 c1 = Corner(maxx, miny);
        Vector2 c2 = Corner(maxx, maxy);
        Vector2 c3 = Corner(minx, maxy);

        var res = new List<Vector3>(pointsPerEdge * 4 + 1);
        AddEdge(res, c0, c1, pointsPerEdge, y);
        AddEdge(res, c1, c2, pointsPerEdge, y);
        AddEdge(res, c2, c3, pointsPerEdge, y);
        AddEdge(res, c3, c0, pointsPerEdge, y);

        if (res.Count > 0) res.Add(res[0]);
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

    // Triangle (improved simple)
    bool TryFitTriangleImproved(List<Vector2> ptsClosed, float baseTol, out Vector2 A, out Vector2 B, out Vector2 C)
    {
        A = B = C = Vector2.zero;

        int n = ptsClosed.Count - 1;
        if (n < 7) return false;

        var pts = new List<Vector2>(n);
        for (int i = 0; i < n; i++) pts.Add(ptsClosed[i]);

        var hull = ComputeConvexHull(pts);
        if (hull.Count < 3) return false;

        FindMaxAreaTriangle(hull, out A, out B, out C);

        float ab = Vector2.Distance(A, B);
        float bc = Vector2.Distance(B, C);
        float ca = Vector2.Distance(C, A);
        float scale = Mathf.Max(ab, Mathf.Max(bc, ca));
        if (scale < 0.4f) return false;

        float tol = baseTol * triangleToleranceMultiplier;
        float edgeThreshold = tol * scale;

        int nearCount = 0;
        int useAB = 0, useBC = 0, useCA = 0;

        for (int i = 0; i < n; i++)
        {
            Vector2 p = pts[i];

            float dAB = DistancePointToSegment(p, A, B);
            float dBC = DistancePointToSegment(p, B, C);
            float dCA = DistancePointToSegment(p, C, A);

            float dMin = dAB;
            int edge = 0;
            if (dBC < dMin) { dMin = dBC; edge = 1; }
            if (dCA < dMin) { dMin = dCA; edge = 2; }

            if (dMin <= edgeThreshold)
            {
                nearCount++;
                if (edge == 0) useAB++;
                else if (edge == 1) useBC++;
                else useCA++;
            }
        }

        float coverage = nearCount / (float)n;
        if (coverage < triangleMinEdgeCoverage) return false;

        float fAB = useAB / (float)Mathf.Max(1, nearCount);
        float fBC = useBC / (float)Mathf.Max(1, nearCount);
        float fCA = useCA / (float)Mathf.Max(1, nearCount);

        if (fAB < triangleMinPerEdgeUsage || fBC < triangleMinPerEdgeUsage || fCA < triangleMinPerEdgeUsage)
            return false;

        return true;
    }

    List<Vector3> MakeTrianglePoints(Vector2 a, Vector2 b, Vector2 c, float y)
    {
        var res = new List<Vector3>(4);
        res.Add(new Vector3(a.x, y, a.y));
        res.Add(new Vector3(b.x, y, b.y));
        res.Add(new Vector3(c.x, y, c.y));
        res.Add(new Vector3(a.x, y, a.y));
        return res;
    }

    // hull
    List<Vector2> ComputeConvexHull(List<Vector2> points)
    {
        if (points.Count <= 1) return new List<Vector2>(points);

        points.Sort((p1, p2) =>
        {
            int c = p1.x.CompareTo(p2.x);
            return c != 0 ? c : p1.y.CompareTo(p2.y);
        });

        var lower = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 1] - lower[lower.Count - 2], points[i] - lower[lower.Count - 1]) <= 0f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(points[i]);
        }

        var upper = new List<Vector2>();
        for (int i = points.Count - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 && Cross(upper[upper.Count - 1] - upper[upper.Count - 2], points[i] - upper[upper.Count - 1]) <= 0f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(points[i]);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    void FindMaxAreaTriangle(List<Vector2> hull, out Vector2 A, out Vector2 B, out Vector2 C)
    {
        A = hull[0]; B = hull[0]; C = hull[0];
        float bestArea2 = -1f;
        int n = Mathf.Min(hull.Count, 80);

        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        for (int k = j + 1; k < n; k++)
        {
            float area2 = Mathf.Abs(Cross(hull[j] - hull[i], hull[k] - hull[i]));
            if (area2 > bestArea2)
            {
                bestArea2 = area2;
                A = hull[i]; B = hull[j]; C = hull[k];
            }
        }
    }

    float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude);
        t = Mathf.Clamp01(t);
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }
}
