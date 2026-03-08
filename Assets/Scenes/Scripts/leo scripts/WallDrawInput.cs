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
    [Range(0.005f, 0.2f)] public float straightLineToleranceMultiplier = 0.45f;

    [Header("Circle")]
    [Range(16, 128)] public int circleResolution = 48;
    [Range(0.01f, 0.25f)] public float circleStrictnessMultiplier = 0.35f;

    [Header("Rectangle")]
    [Range(2, 30)] public int rectPointsPerEdge = 10;
    [Range(0.0f, 0.4f)] public float squareRatioTolerance = 0.12f;

    [Header("Rounded Triangle")]
    [Tooltip("Plus grand = triangle plus facile à reconnaître")]
    public float triangleToleranceMultiplier = 2.3f;

    [Range(6, 48)] public int roundedTriangleResolution = 16;
    [Range(0.02f, 0.45f)] public float roundedTriangleBulge = 0.18f;
    [Range(40f, 150f)] public float roundedTriangleMaxApexAngle = 120f;

    [Header("Debug")]
    public bool logDetectedShape = true;

    public event Action<List<Vector3>> OnShapeCommitted;

    public IReadOnlyList<Vector3> CurrentPoints
    {
        get { return _points; }
    }

    private readonly List<Vector3> _points = new List<Vector3>();
    private bool _isDrawing;
    private LineRenderer _lr;

    private struct RectFit
    {
        public Vector2 center;
        public Vector2 axisX;
        public Vector2 axisY;
        public float minX;
        public float maxX;
        public float minY;
        public float maxY;

        public float width
        {
            get { return maxX - minX; }
        }

        public float height
        {
            get { return maxY - minY; }
        }
    }

    private struct RoundedTriFit
    {
        public Vector2 apex;
        public Vector2 shoulderA;
        public Vector2 shoulderB;
        public Vector2 control;
    }

    void Reset()
    {
        cam = Camera.main;
    }

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

        Vector3 p;
        if (TryGetMouseWorldPoint(out p))
        {
            p = PostProcessPoint(p);
            _points.Add(p);
            RefreshLine();
        }
    }

    void ContinueDraw()
    {
        Vector3 p;
        if (!TryGetMouseWorldPoint(out p))
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
                List<Vector3> fitted;
                string shapeName;
                if (TryAutoFitShape(_points, closed, out fitted, out shapeName))
                {
                    _points.Clear();
                    _points.AddRange(fitted);
                    RefreshLine();

                    if (logDetectedShape)
                        Debug.Log("AutoShape ✅ : " + shapeName);
                }
            }
        }

        RefreshLine();
        OnShapeCommitted?.Invoke(new List<Vector3>(_points));
    }

    bool TryGetMouseWorldPoint(out Vector3 worldPoint)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;
        if (groundCollider != null && groundCollider.Raycast(ray, out hit, 10000f))
        {
            worldPoint = hit.point;
            return true;
        }

        if (Physics.Raycast(ray, out hit, 10000f))
        {
            worldPoint = hit.point;
            return true;
        }

        worldPoint = default(Vector3);
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
            Vector2 lineStart;
            Vector2 lineEnd;
            if (autoStraightLine && TryFitStraightLine(pts2, tolerance * straightLineToleranceMultiplier, out lineStart, out lineEnd))
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

        RectFit rectFit;
        if (autoRectangle && TryFitRectangle(pts2, tolerance, out rectFit))
        {
            bool forceSquare =
                Mathf.Abs(rectFit.width - rectFit.height) /
                Mathf.Max(0.0001f, Mathf.Max(rectFit.width, rectFit.height)) <= squareRatioTolerance;

            shapeName = forceSquare ? "Square" : "Rectangle";
            fittedPoints = MakeRectanglePoints(rectFit, rectPointsPerEdge, y, forceSquare);
            return true;
        }

        RoundedTriFit triFit;
        if (autoTriangle && TryFitRoundedTriangle(pts2, out triFit))
        {
            shapeName = "Rounded Triangle";
            fittedPoints = MakeRoundedTrianglePoints(triFit, y);
            return true;
        }

        Vector2 cc;
        float rr;
        float circleTol = tolerance * circleStrictnessMultiplier;
        if (autoCircle && TryFitCircle(pts2, circleTol, out cc, out rr))
        {
            shapeName = "Circle";
            fittedPoints = MakeCirclePoints(cc, rr, circleResolution, y);
            return true;
        }

        return false;
    }

    List<Vector2> ToXZ(List<Vector3> p3)
    {
        List<Vector2> list = new List<Vector2>(p3.Count);
        for (int i = 0; i < p3.Count; i++)
            list.Add(new Vector2(p3[i].x, p3[i].z));
        return list;
    }

    List<Vector2> SimplifyBySpacing(List<Vector2> pts, float spacing)
    {
        List<Vector2> res = new List<Vector2>();
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

    bool TryFitStraightLine(List<Vector2> pts, float tol, out Vector2 start, out Vector2 end)
    {
        start = Vector2.zero;
        end = Vector2.zero;

        if (pts == null || pts.Count < 2) return false;

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
        if (len < 0.25f) return false;

        float err = 0f;
        for (int i = 0; i < pts.Count; i++)
            err += DistancePointSegment(pts[i], aBest, bBest);

        float normErr = (err / pts.Count) / len;
        if (normErr > tol) return false;

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

    bool TryFitCircle(List<Vector2> ptsClosed, float tol, out Vector2 center, out float radius)
    {
        center = Vector2.zero;
        radius = 0f;

        int n = ptsClosed.Count - 1;
        if (n < 8) return false;

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
        if (err > tol) return false;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < n; i++)
        {
            minX = Mathf.Min(minX, ptsClosed[i].x);
            maxX = Mathf.Max(maxX, ptsClosed[i].x);
            minY = Mathf.Min(minY, ptsClosed[i].y);
            maxY = Mathf.Max(maxY, ptsClosed[i].y);
        }

        float w = maxX - minX;
        float h = maxY - minY;
        if (w < 0.001f || h < 0.001f) return false;

        float ratio = Mathf.Min(w, h) / Mathf.Max(w, h);
        if (ratio < 0.82f) return false;

        return true;
    }

    List<Vector3> MakeCirclePoints(Vector2 center, float radius, int resolution, float y)
    {
        List<Vector3> res = new List<Vector3>(resolution + 1);
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

    bool TryFitRectangle(List<Vector2> ptsClosed, float tol, out RectFit fit)
    {
        fit = default(RectFit);

        int n = ptsClosed.Count - 1;
        if (n < 8) return false;

        List<Vector2> pts = new List<Vector2>(n);
        for (int i = 0; i < n; i++) pts.Add(ptsClosed[i]);

        List<Vector2> hull = ComputeConvexHull(pts);
        if (hull.Count < 4) return false;

        float bestArea = float.PositiveInfinity;
        RectFit bestFit = default(RectFit);
        bool found = false;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++) centroid += ptsClosed[i];
        centroid /= n;

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % hull.Count];
            Vector2 axisX = (b - a).normalized;
            if (axisX.sqrMagnitude < 0.0001f) continue;

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
            if (w < 0.0001f || h < 0.0001f) continue;

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

        if (!found) return false;

        float wBest = bestFit.width;
        float hBest = bestFit.height;
        if (wBest < 0.3f || hBest < 0.3f) return false;

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
        if (norm > tol) return false;

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

        Vector2 C = fit.center;

        Vector2 c0 = C + fit.axisX * minx + fit.axisY * miny;
        Vector2 c1 = C + fit.axisX * maxx + fit.axisY * miny;
        Vector2 c2 = C + fit.axisX * maxx + fit.axisY * maxy;
        Vector2 c3 = C + fit.axisX * minx + fit.axisY * maxy;

        List<Vector3> res = new List<Vector3>(pointsPerEdge * 4 + 1);
        AddEdge(res, c0, c1, pointsPerEdge, y);
        AddEdge(res, c1, c2, pointsPerEdge, y);
        AddEdge(res, c2, c3, pointsPerEdge, y);
        AddEdge(res, c3, c0, pointsPerEdge, y);

        if (res.Count > 0) res.Add(res[0]);
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

    bool TryFitRoundedTriangle(List<Vector2> ptsClosed, out RoundedTriFit fit)
    {
        fit = default(RoundedTriFit);

        int n = ptsClosed.Count - 1;
        if (n < 5) return false;

        List<Vector2> raw = new List<Vector2>(n);
        for (int i = 0; i < n; i++) raw.Add(ptsClosed[i]);

        List<Vector2> hull = ComputeConvexHull(raw);
        if (hull.Count != 3)
            return false;

        int apexIndex = FindSharpestHullCorner(hull);
        if (apexIndex < 0)
            return false;

        float apexAngle = GetHullCornerAngleDeg(hull, apexIndex);
        if (apexAngle > roundedTriangleMaxApexAngle)
            return false;

        Vector2 apex = hull[apexIndex];
        Vector2 shoulderA = hull[(apexIndex - 1 + hull.Count) % hull.Count];
        Vector2 shoulderB = hull[(apexIndex + 1) % hull.Count];

        float lenA = Vector2.Distance(apex, shoulderA);
        float lenB = Vector2.Distance(apex, shoulderB);
        float baseLen = Vector2.Distance(shoulderA, shoulderB);

        if (lenA < 0.25f || lenB < 0.25f || baseLen < 0.25f)
            return false;

        Vector2 mid = (shoulderA + shoulderB) * 0.5f;
        Vector2 baseDir = (shoulderB - shoulderA).normalized;
        Vector2 normal = new Vector2(-baseDir.y, baseDir.x);

        Vector2 toApex = (apex - mid).normalized;
        if (Vector2.Dot(normal, toApex) > 0f)
            normal = -normal;

        Vector2 control = mid + normal * (baseLen * roundedTriangleBulge);

        fit.apex = apex;
        fit.shoulderA = shoulderA;
        fit.shoulderB = shoulderB;
        fit.control = control;
        return true;
    }

    List<Vector3> MakeRoundedTrianglePoints(RoundedTriFit fit, float y)
    {
        List<Vector3> result = new List<Vector3>();

        result.Add(new Vector3(fit.shoulderA.x, y, fit.shoulderA.y));
        result.Add(new Vector3(fit.apex.x, y, fit.apex.y));
        result.Add(new Vector3(fit.shoulderB.x, y, fit.shoulderB.y));

        int res = Mathf.Max(6, roundedTriangleResolution);
        for (int i = 1; i <= res; i++)
        {
            float t = i / (float)res;
            Vector2 p = EvaluateQuadraticBezier(fit.shoulderB, fit.control, fit.shoulderA, t);
            result.Add(new Vector3(p.x, y, p.y));
        }

        if (result.Count > 0 && result[result.Count - 1] != result[0])
            result.Add(result[0]);

        EnsureCounterClockwiseXZ(result);
        return result;
    }

    Vector2 EvaluateQuadraticBezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return (u * u * a) + (2f * u * t * b) + (t * t * c);
    }

    int FindSharpestHullCorner(List<Vector2> hull)
    {
        if (hull == null || hull.Count < 3)
            return -1;

        int bestIndex = -1;
        float smallestAngle = float.PositiveInfinity;

        for (int i = 0; i < hull.Count; i++)
        {
            float angle = GetHullCornerAngleDeg(hull, i);
            if (angle < smallestAngle)
            {
                smallestAngle = angle;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    float GetHullCornerAngleDeg(List<Vector2> hull, int i)
    {
        Vector2 prev = hull[(i - 1 + hull.Count) % hull.Count];
        Vector2 curr = hull[i];
        Vector2 next = hull[(i + 1) % hull.Count];

        Vector2 a = (prev - curr).normalized;
        Vector2 b = (next - curr).normalized;

        float dot = Mathf.Clamp(Vector2.Dot(a, b), -1f, 1f);
        return Mathf.Acos(dot) * Mathf.Rad2Deg;
    }

    List<Vector2> ComputeConvexHull(List<Vector2> pts)
    {
        if (pts.Count <= 3)
            return new List<Vector2>(pts);

        pts.Sort(
            delegate (Vector2 p1, Vector2 p2)
            {
                int cmp = p1.x.CompareTo(p2.x);
                return cmp == 0 ? p1.y.CompareTo(p2.y) : cmp;
            });

        List<Vector2> lower = new List<Vector2>();
        foreach (Vector2 p in pts)
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
}