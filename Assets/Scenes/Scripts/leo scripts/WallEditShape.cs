using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WallEditShape : MonoBehaviour, IControlPointProvider, IControlPointPathProvider
{
    public enum ShapeKind
    {
        Free,
        Rectangle,
        Ellipse
    }

    [Header("References")]
    public WallObject wall;

    [Header("Detected Shape")]
    public ShapeKind shapeKind = ShapeKind.Free;

    [Header("Bounds Shape Data")]
    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;
    public float shapeY = 0f;

    [Header("Ellipse")]
    public int ellipseWallResolution = 64;

    [Header("Free Shape")]
    public List<Vector3> freeControlPoints = new List<Vector3>();

    [Header("Free Shape Settings")]
    public float freeHandleSpacing = 2.5f;
    public int minFreeHandles = 4;
    public int maxFreeHandles = 12;
    public int freeWallResolution = 64;

    [Header("Closed Free Shapes")]
    [Tooltip("Plus petit = plus de points gardés pour les formes libres fermées")]
    [Range(0.2f, 1.0f)] public float closedFreeHandleSpacingMultiplier = 0.5f;

    [Tooltip("Minimum de points de contrôle pour un gribouillis fermé")]
    [Range(6, 48)] public int closedFreeMinHandles = 10;

    [Tooltip("Maximum de points de contrôle pour un gribouillis fermé")]
    [Range(8, 64)] public int closedFreeMaxHandles = 24;

    [Tooltip("Résolution finale minimum pour un gribouillis fermé")]
    [Range(32, 256)] public int closedFreeWallResolution = 128;

    [Tooltip("Nombre d'itérations de Chaikin pour les formes libres fermées")]
    [Range(1, 5)] public int closedFreeSmoothIterations = 3;

    [Header("Open Free Shapes")]
    [Tooltip("Si une forme ouverte est presque une ligne droite, on garde ses coins nets")]
    [Range(1.0f, 1.5f)] public float mostlyStraightArcRatioThreshold = 1.06f;

    [Range(0f, 30f)] public float mostlyStraightAverageTurnThreshold = 8f;

    [Tooltip("Nombre d'itérations de Chaikin pour les formes libres ouvertes courbes")]
    [Range(1, 5)] public int openFreeSmoothIterations = 2;

    private bool _closedLoop = true;

    void Awake()
    {
        if (wall == null)
            wall = GetComponent<WallObject>();
    }

    public void InitFromPath(List<Vector3> points)
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (points == null || points.Count < 2)
            return;

        List<Vector3> src = new List<Vector3>(points);

        _closedLoop = IsClosed(src);

        if (_closedLoop && src.Count > 1 && Vector3.Distance(src[0], src[src.Count - 1]) < 0.001f)
            src.RemoveAt(src.Count - 1);

        if (TrySetupEllipse(src))
        {
            shapeKind = ShapeKind.Ellipse;
            ApplyToWall();
            return;
        }

        if (TrySetupRectangle(src))
        {
            shapeKind = ShapeKind.Rectangle;
            ApplyToWall();
            return;
        }

        SetupFree(src);
        shapeKind = ShapeKind.Free;
        ApplyToWall();
    }

    public int ControlPointCount
    {
        get
        {
            switch (shapeKind)
            {
                case ShapeKind.Ellipse:
                    return 4;
                case ShapeKind.Rectangle:
                    return 8;
                default:
                    return freeControlPoints.Count;
            }
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                return GetEllipseControlPoint(index);

            case ShapeKind.Rectangle:
                return GetRectangleControlPoint(index);

            default:
                if (index < 0 || index >= freeControlPoints.Count)
                    return Vector3.zero;
                return freeControlPoints[index];
        }
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                SetEllipseControlPoint(index, worldPos);
                break;

            case ShapeKind.Rectangle:
                SetRectangleControlPoint(index, worldPos);
                break;

            default:
                if (index < 0 || index >= freeControlPoints.Count)
                    return;

                freeControlPoints[index] = new Vector3(worldPos.x, shapeY, worldPos.z);
                break;
        }

        ApplyToWall();
    }

    public bool IsControlPointEditable(int index)
    {
        return index >= 0 && index < ControlPointCount;
    }

    public List<Vector3> GetPreviewPathWorld()
    {
        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                return BuildEllipsePath(Mathf.Max(48, ellipseWallResolution));

            case ShapeKind.Rectangle:
                return BuildRectanglePath();

            default:
                return BuildFreePreviewPath();
        }
    }

    bool TrySetupEllipse(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points.Count < 10) return false;

        List<Vector2> hull = ComputeConvexHullXZ(points);
        if (hull.Count < 5)
            return false;

        shapeY = points[0].y;
        ComputeBounds(points);

        float rx = (maxX - minX) * 0.5f;
        float rz = (maxZ - minZ) * 0.5f;
        Vector3 center = GetBoundsCenter();

        if (rx < 0.1f || rz < 0.1f)
            return false;

        float error = 0f;
        int count = 0;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float nx = (p.x - center.x) / rx;
            float nz = (p.z - center.z) / rz;
            float v = nx * nx + nz * nz;
            error += Mathf.Abs(v - 1f);
            count++;
        }

        float avgError = error / Mathf.Max(1, count);
        return avgError <= 0.20f;
    }

    Vector3 GetEllipseControlPoint(int index)
    {
        Vector3 c = GetBoundsCenter();

        switch (index)
        {
            case 0: return new Vector3(maxX, shapeY, c.z);
            case 1: return new Vector3(c.x, shapeY, maxZ);
            case 2: return new Vector3(minX, shapeY, c.z);
            case 3: return new Vector3(c.x, shapeY, minZ);
            default: return c;
        }
    }

    void SetEllipseControlPoint(int index, Vector3 worldPos)
    {
        switch (index)
        {
            case 0: maxX = Mathf.Max(worldPos.x, minX + 0.1f); break;
            case 1: maxZ = Mathf.Max(worldPos.z, minZ + 0.1f); break;
            case 2: minX = Mathf.Min(worldPos.x, maxX - 0.1f); break;
            case 3: minZ = Mathf.Min(worldPos.z, maxZ - 0.1f); break;
        }
    }

    List<Vector3> BuildEllipsePath(int resolution)
    {
        List<Vector3> pts = new List<Vector3>();
        resolution = Mathf.Max(16, resolution);

        Vector3 c = GetBoundsCenter();
        float rx = Mathf.Max(0.1f, (maxX - minX) * 0.5f);
        float rz = Mathf.Max(0.1f, (maxZ - minZ) * 0.5f);

        for (int i = 0; i < resolution; i++)
        {
            float t = (i / (float)resolution) * Mathf.PI * 2f;
            float x = Mathf.Cos(t) * rx;
            float z = Mathf.Sin(t) * rz;
            pts.Add(new Vector3(c.x + x, shapeY, c.z + z));
        }

        pts.Add(pts[0]);
        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    bool TrySetupRectangle(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points.Count < 4) return false;

        List<Vector2> hull = ComputeConvexHullXZ(points);
        if (hull.Count != 4)
            return false;

        shapeY = points[0].y;
        ComputeBounds(points);

        float width = maxX - minX;
        float depth = maxZ - minZ;

        if (width < 0.1f || depth < 0.1f)
            return false;

        float borderTolerance = Mathf.Max(width, depth) * 0.10f;
        int nearBorder = 0;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            bool onLeft = Mathf.Abs(p.x - minX) <= borderTolerance;
            bool onRight = Mathf.Abs(p.x - maxX) <= borderTolerance;
            bool onBottom = Mathf.Abs(p.z - minZ) <= borderTolerance;
            bool onTop = Mathf.Abs(p.z - maxZ) <= borderTolerance;

            if (onLeft || onRight || onBottom || onTop)
                nearBorder++;
        }

        float ratio = nearBorder / (float)Mathf.Max(1, points.Count);
        return ratio >= 0.85f;
    }

    Vector3 GetRectangleControlPoint(int index)
    {
        Vector3 topLeft = new Vector3(minX, shapeY, maxZ);
        Vector3 topRight = new Vector3(maxX, shapeY, maxZ);
        Vector3 bottomRight = new Vector3(maxX, shapeY, minZ);
        Vector3 bottomLeft = new Vector3(minX, shapeY, minZ);

        switch (index)
        {
            case 0: return topLeft;
            case 1: return (topLeft + topRight) * 0.5f;
            case 2: return topRight;
            case 3: return (topRight + bottomRight) * 0.5f;
            case 4: return bottomRight;
            case 5: return (bottomRight + bottomLeft) * 0.5f;
            case 6: return bottomLeft;
            case 7: return (bottomLeft + topLeft) * 0.5f;
            default: return GetBoundsCenter();
        }
    }

    void SetRectangleControlPoint(int index, Vector3 worldPos)
    {
        switch (index)
        {
            case 0:
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 2:
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 4:
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 6:
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 1:
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 3:
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                break;

            case 5:
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 7:
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                break;
        }
    }

    List<Vector3> BuildRectanglePath()
    {
        Vector3 topLeft = new Vector3(minX, shapeY, maxZ);
        Vector3 bottomLeft = new Vector3(minX, shapeY, minZ);
        Vector3 bottomRight = new Vector3(maxX, shapeY, minZ);
        Vector3 topRight = new Vector3(maxX, shapeY, maxZ);

        List<Vector3> pts = new List<Vector3>
        {
            topLeft,
            bottomLeft,
            bottomRight,
            topRight,
            topLeft
        };

        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    void SetupFree(List<Vector3> points)
    {
        freeControlPoints.Clear();

        float localSpacing = freeHandleSpacing;
        int localMin = minFreeHandles;
        int localMax = maxFreeHandles;

        if (_closedLoop)
        {
            localSpacing *= closedFreeHandleSpacingMultiplier;
            localMin = Mathf.Max(localMin, closedFreeMinHandles);
            localMax = Mathf.Max(localMax, closedFreeMaxHandles);
        }

        float perimeter = ComputePerimeter(points, _closedLoop);
        int wantedHandles = Mathf.RoundToInt(perimeter / Mathf.Max(0.1f, localSpacing));
        wantedHandles = Mathf.Clamp(wantedHandles, localMin, localMax);

        if (points.Count <= wantedHandles)
        {
            freeControlPoints.AddRange(points);
        }
        else
        {
            for (int i = 0; i < wantedHandles; i++)
            {
                float t = _closedLoop
                    ? i / (float)wantedHandles
                    : i / (float)(wantedHandles - 1);

                int idx = Mathf.RoundToInt(t * (points.Count - 1));
                idx = Mathf.Clamp(idx, 0, points.Count - 1);
                freeControlPoints.Add(points[idx]);
            }
        }

        if (_closedLoop && freeControlPoints.Count > 1)
        {
            if (Vector3.Distance(freeControlPoints[0], freeControlPoints[freeControlPoints.Count - 1]) < 0.0001f)
                freeControlPoints.RemoveAt(freeControlPoints.Count - 1);
        }

        if (freeControlPoints.Count > 0)
            shapeY = freeControlPoints[0].y;
    }

    List<Vector3> BuildFreePreviewPath()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return null;

        if (_closedLoop)
        {
            List<Vector3> smoothClosed = Chaikin(freeControlPoints, closedFreeSmoothIterations, true);
            int targetCount = Mathf.Max(closedFreeWallResolution, freeControlPoints.Count * 10);
            List<Vector3> denseClosed = ResampleClosedByCount(smoothClosed, targetCount);

            if (denseClosed.Count > 0 && Vector3.Distance(denseClosed[0], denseClosed[denseClosed.Count - 1]) > 0.001f)
                denseClosed.Add(denseClosed[0]);

            return denseClosed;
        }

        if (IsMostlyStraightOpen(freeControlPoints))
        {
            return new List<Vector3>(freeControlPoints);
        }

        List<Vector3> smoothOpen = Chaikin(freeControlPoints, openFreeSmoothIterations, false);
        int openTargetCount = Mathf.Max(freeWallResolution, freeControlPoints.Count * 8);
        return ResampleOpenByCount(smoothOpen, openTargetCount);
    }

    static List<Vector3> Chaikin(List<Vector3> pts, int iterations, bool closed)
    {
        if (pts == null || pts.Count < 2)
            return pts == null ? new List<Vector3>() : new List<Vector3>(pts);

        List<Vector3> work = new List<Vector3>(pts);

        if (closed && work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        for (int it = 0; it < iterations; it++)
        {
            List<Vector3> res = new List<Vector3>(work.Count * 2);
            int n = work.Count;

            if (closed)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = work[i];
                    Vector3 b = work[(i + 1) % n];
                    res.Add(Vector3.Lerp(a, b, 0.25f));
                    res.Add(Vector3.Lerp(a, b, 0.75f));
                }
            }
            else
            {
                res.Add(work[0]);

                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 a = work[i];
                    Vector3 b = work[i + 1];
                    res.Add(Vector3.Lerp(a, b, 0.25f));
                    res.Add(Vector3.Lerp(a, b, 0.75f));
                }

                res.Add(work[n - 1]);
            }

            work = res;
        }

        return work;
    }

    static List<Vector3> ResampleOpenByCount(List<Vector3> pts, int count)
    {
        if (pts == null || pts.Count == 0)
            return new List<Vector3>();

        if (pts.Count == 1)
            return new List<Vector3>(pts);

        count = Mathf.Max(2, count);

        float[] dist = new float[pts.Count];
        dist[0] = 0f;

        for (int i = 1; i < pts.Count; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);

        float total = dist[pts.Count - 1];
        if (total < 1e-6f)
            return new List<Vector3>(pts);

        List<Vector3> res = new List<Vector3>(count);

        for (int k = 0; k < count; k++)
        {
            float t = (k / (float)(count - 1)) * total;
            int i = 1;

            while (i < dist.Length && dist[i] < t)
                i++;

            i = Mathf.Clamp(i, 1, dist.Length - 1);
            float segT = Mathf.InverseLerp(dist[i - 1], dist[i], t);
            res.Add(Vector3.Lerp(pts[i - 1], pts[i], segT));
        }

        return res;
    }

    static List<Vector3> ResampleClosedByCount(List<Vector3> pts, int count)
    {
        if (pts == null || pts.Count == 0)
            return new List<Vector3>();

        if (pts.Count == 1)
            return new List<Vector3>(pts);

        count = Mathf.Max(3, count);

        List<Vector3> work = new List<Vector3>(pts);
        if (Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        int n = work.Count;
        if (n < 2)
            return new List<Vector3>(work);

        float[] dist = new float[n + 1];
        dist[0] = 0f;

        for (int i = 1; i < n; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(work[i - 1], work[i]);

        dist[n] = dist[n - 1] + Vector3.Distance(work[n - 1], work[0]);

        float total = dist[n];
        if (total < 1e-6f)
            return new List<Vector3>(work);

        List<Vector3> res = new List<Vector3>(count);

        for (int k = 0; k < count; k++)
        {
            float t = (k / (float)count) * total;
            int seg = 1;

            while (seg < dist.Length && dist[seg] < t)
                seg++;

            seg = Mathf.Clamp(seg, 1, dist.Length - 1);

            int aIndex = seg - 1;
            int bIndex = seg % n;

            float segT = Mathf.InverseLerp(dist[seg - 1], dist[seg], t);
            res.Add(Vector3.Lerp(work[aIndex], work[bIndex], segT));
        }

        return res;
    }

    bool IsMostlyStraightOpen(List<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return true;

        float pathLen = 0f;
        for (int i = 0; i < points.Count - 1; i++)
            pathLen += Vector3.Distance(points[i], points[i + 1]);

        float chord = Vector3.Distance(points[0], points[points.Count - 1]);
        if (chord < 0.0001f)
            return false;

        float arcRatio = pathLen / chord;

        float turnSum = 0f;
        int turnCount = 0;

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 a = (points[i] - points[i - 1]).normalized;
            Vector3 b = (points[i + 1] - points[i]).normalized;

            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f)
                continue;

            turnSum += Vector3.Angle(a, b);
            turnCount++;
        }

        float avgTurn = turnCount > 0 ? turnSum / turnCount : 0f;

        return arcRatio <= mostlyStraightArcRatioThreshold && avgTurn <= mostlyStraightAverageTurnThreshold;
    }

    public void ApplyToWall()
    {
        if (wall == null)
            return;

        List<Vector3> path = null;

        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                path = BuildEllipsePath(ellipseWallResolution);
                break;

            case ShapeKind.Rectangle:
                path = BuildRectanglePath();
                break;

            default:
                path = BuildFreePreviewPath();
                break;
        }

        if (path != null && path.Count >= 2)
        {
            wall.closedLoop = _closedLoop;
            wall.SetPath(path);
        }
    }

    void ComputeBounds(List<Vector3> points)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        minZ = float.MaxValue;
        maxZ = float.MinValue;

        for (int i = 0; i < points.Count; i++)
        {
            minX = Mathf.Min(minX, points[i].x);
            maxX = Mathf.Max(maxX, points[i].x);
            minZ = Mathf.Min(minZ, points[i].z);
            maxZ = Mathf.Max(maxZ, points[i].z);
        }
    }

    Vector3 GetBoundsCenter()
    {
        return new Vector3((minX + maxX) * 0.5f, shapeY, (minZ + maxZ) * 0.5f);
    }

    bool IsClosed(List<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return false;

        return Vector3.Distance(points[0], points[points.Count - 1]) < 0.2f;
    }

    float ComputePerimeter(List<Vector3> points, bool closed)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float len = 0f;

        for (int i = 0; i < points.Count - 1; i++)
            len += Vector3.Distance(points[i], points[i + 1]);

        if (closed)
            len += Vector3.Distance(points[points.Count - 1], points[0]);

        return len;
    }

    List<Vector2> ComputeConvexHullXZ(List<Vector3> pts3)
    {
        List<Vector2> pts = new List<Vector2>(pts3.Count);
        for (int i = 0; i < pts3.Count; i++)
            pts.Add(new Vector2(pts3[i].x, pts3[i].z));

        if (pts.Count <= 3)
            return new List<Vector2>(pts);

        pts.Sort(delegate (Vector2 p1, Vector2 p2)
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

    float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    void EnsureCounterClockwiseXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4)
            return;

        int count = pts.Count;
        bool duplicatedClose = Vector3.Distance(pts[0], pts[count - 1]) < 0.0001f;
        int effectiveCount = duplicatedClose ? count - 1 : count;

        float area = 0f;
        for (int i = 0; i < effectiveCount; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % effectiveCount];
            area += (a.x * b.z - b.x * a.z);
        }

        if (area < 0f)
        {
            if (duplicatedClose)
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