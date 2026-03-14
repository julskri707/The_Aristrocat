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

    [Header("Rectangle Shape Data")]
    public Vector2 rectangleOriginXZ;
    public Vector2 rectangleAxisX = Vector2.right;
    public Vector2 rectangleAxisY = Vector2.up;
    public float rectangleMinX = -0.5f;
    public float rectangleMaxX = 0.5f;
    public float rectangleMinY = -0.5f;
    public float rectangleMaxY = 0.5f;

    [Header("Ellipse")]
    public int ellipseWallResolution = 64;

    [Header("Free Shape")]
    public List<Vector3> freeControlPoints = new List<Vector3>();

    [Header("Free Shape Settings")]
    public float freeHandleSpacing = 2.5f;
    public int minFreeHandles = 4;
    public int maxFreeHandles = 12;
    public int freeWallResolution = 64;

    [Header("Preserve Drawn Freeform")]
    public bool preserveInitialFreeDrawnPath = true;
    [Range(0.02f, 1.0f)] public float rawFreeMinPointSpacing = 0.08f;

    [Header("Closed Free Shapes")]
    [Range(0.2f, 1.0f)] public float closedFreeHandleSpacingMultiplier = 0.5f;
    [Range(6, 48)] public int closedFreeMinHandles = 10;
    [Range(8, 64)] public int closedFreeMaxHandles = 24;
    [Range(32, 256)] public int closedFreeWallResolution = 128;
    [Range(0, 4)] public int closedFreeSmoothIterations = 0;

    [Header("Closed Free Shape Safety")]
    [Tooltip("Distance minimale entre deux points consécutifs pour éviter les micro-segments")]
    [Range(0.02f, 1.0f)] public float minClosedSegmentLength = 0.18f;

    [Tooltip("Si une boucle fermée brute est invalide, on retombe sur un contour sûr")]
    public bool useSafeClosedFallback = true;

    [Header("Open Free Shapes")]
    [Range(1.0f, 1.5f)] public float mostlyStraightArcRatioThreshold = 1.06f;
    [Range(0f, 30f)] public float mostlyStraightAverageTurnThreshold = 8f;
    [Range(0, 4)] public int openFreeSmoothIterations = 1;

    private bool _closedLoop = true;
    private readonly List<Vector3> _freeRawPath = new List<Vector3>();
    private bool _freePathWasEdited = false;

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

        _freeRawPath.Clear();
        _freePathWasEdited = false;

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
        CacheInitialFreeRawPath(src);
        shapeKind = ShapeKind.Free;
        ApplyToWall();
    }

    public void InitFromDetectedPath(List<Vector3> points, WallDrawInput.DetectedShapeKind detectedKind)
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (points == null || points.Count < 2)
            return;

        List<Vector3> src = new List<Vector3>(points);

        _closedLoop = IsClosed(src);

        if (_closedLoop && src.Count > 1 && Vector3.Distance(src[0], src[src.Count - 1]) < 0.001f)
            src.RemoveAt(src.Count - 1);

        _freeRawPath.Clear();
        _freePathWasEdited = false;

        bool wantsRectangle =
            detectedKind == WallDrawInput.DetectedShapeKind.Rectangle ||
            detectedKind == WallDrawInput.DetectedShapeKind.Square;

        if (wantsRectangle)
        {
            if (TrySetupRectangleForcedFromDetected(src))
            {
                shapeKind = ShapeKind.Rectangle;
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
            CacheInitialFreeRawPath(src);
            shapeKind = ShapeKind.Free;
            ApplyToWall();
            return;
        }

        // Important:
        // - on n'autorise plus de 2e passe rectangle ici
        // - mais on réautorise la 2e passe ellipse pour retrouver
        //   les vrais cercles / ovales avec 4 handles.
        // Donc:
        //   cercle détecté -> ellipse
        //   ovale dessiné mais classé Free -> ellipse si le fit passe
        //   triangle / free non-elliptique -> restent Free
        if (_closedLoop && TrySetupEllipse(src))
        {
            shapeKind = ShapeKind.Ellipse;
            ApplyToWall();
            return;
        }

        SetupFree(src);
        CacheInitialFreeRawPath(src);
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
                    return freeControlPoints != null ? freeControlPoints.Count : 0;
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
                if (freeControlPoints == null || index < 0 || index >= freeControlPoints.Count)
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
                if (freeControlPoints == null || index < 0 || index >= freeControlPoints.Count)
                    return;

                freeControlPoints[index] = new Vector3(worldPos.x, shapeY, worldPos.z);
                _freePathWasEdited = true;
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



    public bool InsertFreeControlPointAtWorld(Vector3 worldPos)
    {
        if (shapeKind != ShapeKind.Free)
            return false;

        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return false;

        Vector3 insertPos = new Vector3(worldPos.x, shapeY, worldPos.z);

        int bestInsertIndex = FindBestInsertIndexForFreePoint(insertPos);
        if (bestInsertIndex < 0)
            return false;

        freeControlPoints.Insert(bestInsertIndex, insertPos);
        _freePathWasEdited = true;

        ApplyToWall();
        return true;
    }

    int FindBestInsertIndexForFreePoint(Vector3 worldPos)
    {
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return -1;

        float bestDist = float.MaxValue;
        int bestInsertIndex = -1;

        if (_closedLoop)
        {
            int count = freeControlPoints.Count;

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                float dist = DistancePointToSegmentXZ(worldPos, freeControlPoints[i], freeControlPoints[next]);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestInsertIndex = i + 1;
                }
            }

            return bestInsertIndex;
        }

        for (int i = 0; i < freeControlPoints.Count - 1; i++)
        {
            float dist = DistancePointToSegmentXZ(worldPos, freeControlPoints[i], freeControlPoints[i + 1]);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestInsertIndex = i + 1;
            }
        }

        return bestInsertIndex;
    }

    static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 pp = new Vector2(p.x, p.z);
        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);

        Vector2 ab = bb - aa;
        float len2 = ab.sqrMagnitude;

        if (len2 < 0.000001f)
            return Vector2.Distance(pp, aa);

        float t = Vector2.Dot(pp - aa, ab) / len2;
        t = Mathf.Clamp01(t);

        Vector2 proj = aa + ab * t;
        return Vector2.Distance(pp, proj);
    }

    bool TrySetupEllipse(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points == null || points.Count < 10) return false;

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
            case 0:
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                break;

            case 1:
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 2:
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                break;

            case 3:
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;
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
        if (points == null || points.Count < 4) return false;

        List<Vector3> work = new List<Vector3>(points);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 4)
            return false;

        List<Vector2> hull = ComputeConvexHullXZ(work);
        if (hull == null || hull.Count < 4 || hull.Count > 10)
            return false;

        if (!TryBuildOrientedRectangleFromPath(
                work,
                out Vector2 origin,
                out Vector2 axisX,
                out Vector2 axisY,
                out float localMinX,
                out float localMaxX,
                out float localMinY,
                out float localMaxY))
            return false;

        shapeY = work[0].y;
        rectangleOriginXZ = origin;
        rectangleAxisX = axisX;
        rectangleAxisY = axisY;
        rectangleMinX = localMinX;
        rectangleMaxX = localMaxX;
        rectangleMinY = localMinY;
        rectangleMaxY = localMaxY;

        ComputeBounds(BuildRectanglePath());
        return true;
    }

    bool TrySetupRectangleForcedFromDetected(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points == null || points.Count < 4) return false;

        List<Vector3> work = new List<Vector3>(points);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 4)
            return false;

        if (!TryBuildOrientedRectangleFromPathRelaxed(
                work,
                out Vector2 origin,
                out Vector2 axisX,
                out Vector2 axisY,
                out float localMinX,
                out float localMaxX,
                out float localMinY,
                out float localMaxY))
            return false;

        shapeY = work[0].y;
        rectangleOriginXZ = origin;
        rectangleAxisX = axisX;
        rectangleAxisY = axisY;
        rectangleMinX = localMinX;
        rectangleMaxX = localMaxX;
        rectangleMinY = localMinY;
        rectangleMaxY = localMaxY;

        ComputeBounds(BuildRectanglePath());
        return true;
    }

    bool TryBuildOrientedRectangleFromPath(
        List<Vector3> work,
        out Vector2 origin,
        out Vector2 axisX,
        out Vector2 axisY,
        out float localMinX,
        out float localMaxX,
        out float localMinY,
        out float localMaxY)
    {
        return TryBuildOrientedRectangleFromPathInternal(
            work,
            0.68f,
            out origin,
            out axisX,
            out axisY,
            out localMinX,
            out localMaxX,
            out localMinY,
            out localMaxY);
    }

    bool TryBuildOrientedRectangleFromPathRelaxed(
        List<Vector3> work,
        out Vector2 origin,
        out Vector2 axisX,
        out Vector2 axisY,
        out float localMinX,
        out float localMaxX,
        out float localMinY,
        out float localMaxY)
    {
        return TryBuildOrientedRectangleFromPathInternal(
            work,
            0.20f,
            out origin,
            out axisX,
            out axisY,
            out localMinX,
            out localMaxX,
            out localMinY,
            out localMaxY);
    }

    bool TryBuildOrientedRectangleFromPathInternal(
        List<Vector3> work,
        float minScore,
        out Vector2 origin,
        out Vector2 axisX,
        out Vector2 axisY,
        out float localMinX,
        out float localMaxX,
        out float localMinY,
        out float localMaxY)
    {
        origin = Vector2.zero;
        axisX = Vector2.right;
        axisY = Vector2.up;
        localMinX = localMaxX = localMinY = localMaxY = 0f;

        if (work == null || work.Count < 4)
            return false;

        List<Vector2> hull = ComputeConvexHullXZ(work);
        if (hull == null || hull.Count < 4)
            return false;

        Vector2 hullCenter = Vector2.zero;
        for (int i = 0; i < hull.Count; i++)
            hullCenter += hull[i];
        hullCenter /= hull.Count;

        float bestScore = -1f;
        bool found = false;

        Vector2 bestOrigin = Vector2.zero;
        Vector2 bestAxisX = Vector2.right;
        Vector2 bestAxisY = Vector2.up;
        float bestMinX = 0f;
        float bestMaxX = 0f;
        float bestMinY = 0f;
        float bestMaxY = 0f;

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % hull.Count];
            Vector2 edge = b - a;

            if (edge.sqrMagnitude < 0.0001f)
                continue;

            Vector2 candidateAxisX = edge.normalized;
            Vector2 candidateAxisY = new Vector2(-candidateAxisX.y, candidateAxisX.x);

            List<Vector2> localPts = new List<Vector2>(work.Count);

            float minPX = float.PositiveInfinity;
            float maxPX = float.NegativeInfinity;
            float minPY = float.PositiveInfinity;
            float maxPY = float.NegativeInfinity;

            for (int p = 0; p < work.Count; p++)
            {
                Vector2 wp = new Vector2(work[p].x, work[p].z);
                Vector2 v = wp - hullCenter;

                float px = Vector2.Dot(v, candidateAxisX);
                float py = Vector2.Dot(v, candidateAxisY);

                localPts.Add(new Vector2(px, py));

                if (px < minPX) minPX = px;
                if (px > maxPX) maxPX = px;
                if (py < minPY) minPY = py;
                if (py > maxPY) maxPY = py;
            }

            float width = maxPX - minPX;
            float height = maxPY - minPY;

            if (width < 0.1f || height < 0.1f)
                continue;

            float edgeTol = Mathf.Max(width, height) * 0.10f;
            edgeTol = Mathf.Max(edgeTol, 0.08f);

            float cornerTol = edgeTol * 1.75f;

            int nearEdgeCount = 0;
            bool hitLeft = false;
            bool hitRight = false;
            bool hitBottom = false;
            bool hitTop = false;

            for (int p = 0; p < localPts.Count; p++)
            {
                Vector2 lp = localPts[p];

                float dLeft = Mathf.Abs(lp.x - minPX);
                float dRight = Mathf.Abs(lp.x - maxPX);
                float dBottom = Mathf.Abs(lp.y - minPY);
                float dTop = Mathf.Abs(lp.y - maxPY);

                float minEdge = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
                if (minEdge <= edgeTol)
                    nearEdgeCount++;

                if (dLeft <= edgeTol) hitLeft = true;
                if (dRight <= edgeTol) hitRight = true;
                if (dBottom <= edgeTol) hitBottom = true;
                if (dTop <= edgeTol) hitTop = true;
            }

            float edgeRatio = nearEdgeCount / (float)Mathf.Max(1, localPts.Count);

            int edgesHit = 0;
            if (hitLeft) edgesHit++;
            if (hitRight) edgesHit++;
            if (hitBottom) edgesHit++;
            if (hitTop) edgesHit++;

            float edgeCoverage = edgesHit / 4f;

            int cornersHit = 0;
            if (HasPointNearLocalCorner(localPts, minPX, maxPY, cornerTol)) cornersHit++;
            if (HasPointNearLocalCorner(localPts, maxPX, maxPY, cornerTol)) cornersHit++;
            if (HasPointNearLocalCorner(localPts, maxPX, minPY, cornerTol)) cornersHit++;
            if (HasPointNearLocalCorner(localPts, minPX, minPY, cornerTol)) cornersHit++;

            float cornerCoverage = cornersHit / 4f;

            float boxArea = width * height;
            float polyArea = ComputeAbsoluteSignedAreaXZ(work);
            float fillRatio = 0f;
            if (boxArea > 0.0001f)
                fillRatio = Mathf.Clamp01(polyArea / boxArea);

            float score =
                edgeRatio * 0.45f +
                edgeCoverage * 0.20f +
                cornerCoverage * 0.20f +
                fillRatio * 0.15f;

            if (edgesHit < 4)
                score -= 0.20f;

            if (cornerCoverage < 0.50f)
                score -= 0.15f;

            if (edgeRatio < 0.70f)
                score -= 0.15f;

            if (score > bestScore)
            {
                float midX = (minPX + maxPX) * 0.5f;
                float midY = (minPY + maxPY) * 0.5f;

                bestScore = score;
                bestAxisX = candidateAxisX;
                bestAxisY = candidateAxisY;
                bestOrigin = hullCenter + candidateAxisX * midX + candidateAxisY * midY;

                bestMinX = minPX - midX;
                bestMaxX = maxPX - midX;
                bestMinY = minPY - midY;
                bestMaxY = maxPY - midY;

                found = true;
            }
        }

        if (!found)
            return false;

        if (bestScore < minScore)
            return false;

        origin = bestOrigin;
        axisX = bestAxisX;
        axisY = bestAxisY;
        localMinX = bestMinX;
        localMaxX = bestMaxX;
        localMinY = bestMinY;
        localMaxY = bestMaxY;

        return true;
    }

    bool HasPointNearLocalCorner(List<Vector2> localPts, float x, float y, float tolerance)
    {
        float sqrTol = tolerance * tolerance;
        Vector2 corner = new Vector2(x, y);

        for (int i = 0; i < localPts.Count; i++)
        {
            if ((localPts[i] - corner).sqrMagnitude <= sqrTol)
                return true;
        }

        return false;
    }

    Vector3 GetRectangleControlPoint(int index)
    {
        Vector3 topLeft = RectangleLocalToWorld(rectangleMinX, rectangleMaxY);
        Vector3 topRight = RectangleLocalToWorld(rectangleMaxX, rectangleMaxY);
        Vector3 bottomRight = RectangleLocalToWorld(rectangleMaxX, rectangleMinY);
        Vector3 bottomLeft = RectangleLocalToWorld(rectangleMinX, rectangleMinY);

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
            default: return GetRectangleCenterWorld();
        }
    }

    void SetRectangleControlPoint(int index, Vector3 worldPos)
    {
        Vector2 local = RectangleWorldToLocal(worldPos);

        switch (index)
        {
            case 0:
                rectangleMinX = Mathf.Min(local.x, rectangleMaxX - 0.1f);
                rectangleMaxY = Mathf.Max(local.y, rectangleMinY + 0.1f);
                break;

            case 2:
                rectangleMaxX = Mathf.Max(local.x, rectangleMinX + 0.1f);
                rectangleMaxY = Mathf.Max(local.y, rectangleMinY + 0.1f);
                break;

            case 4:
                rectangleMaxX = Mathf.Max(local.x, rectangleMinX + 0.1f);
                rectangleMinY = Mathf.Min(local.y, rectangleMaxY - 0.1f);
                break;

            case 6:
                rectangleMinX = Mathf.Min(local.x, rectangleMaxX - 0.1f);
                rectangleMinY = Mathf.Min(local.y, rectangleMaxY - 0.1f);
                break;

            case 1:
                rectangleMaxY = Mathf.Max(local.y, rectangleMinY + 0.1f);
                break;

            case 3:
                rectangleMaxX = Mathf.Max(local.x, rectangleMinX + 0.1f);
                break;

            case 5:
                rectangleMinY = Mathf.Min(local.y, rectangleMaxY - 0.1f);
                break;

            case 7:
                rectangleMinX = Mathf.Min(local.x, rectangleMaxX - 0.1f);
                break;
        }

        ComputeBounds(BuildRectanglePath());
    }

    List<Vector3> BuildRectanglePath()
    {
        Vector3 topLeft = RectangleLocalToWorld(rectangleMinX, rectangleMaxY);
        Vector3 bottomLeft = RectangleLocalToWorld(rectangleMinX, rectangleMinY);
        Vector3 bottomRight = RectangleLocalToWorld(rectangleMaxX, rectangleMinY);
        Vector3 topRight = RectangleLocalToWorld(rectangleMaxX, rectangleMaxY);

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

    Vector3 RectangleLocalToWorld(float x, float y)
    {
        Vector2 p = rectangleOriginXZ + rectangleAxisX * x + rectangleAxisY * y;
        return new Vector3(p.x, shapeY, p.y);
    }

    Vector2 RectangleWorldToLocal(Vector3 worldPos)
    {
        Vector2 v = new Vector2(worldPos.x, worldPos.z) - rectangleOriginXZ;
        return new Vector2(Vector2.Dot(v, rectangleAxisX), Vector2.Dot(v, rectangleAxisY));
    }

    Vector3 GetRectangleCenterWorld()
    {
        float cx = (rectangleMinX + rectangleMaxX) * 0.5f;
        float cy = (rectangleMinY + rectangleMaxY) * 0.5f;
        return RectangleLocalToWorld(cx, cy);
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

    void CacheInitialFreeRawPath(List<Vector3> points)
    {
        _freeRawPath.Clear();
        _freePathWasEdited = false;

        if (points == null || points.Count < 2)
            return;

        for (int i = 0; i < points.Count; i++)
            _freeRawPath.Add(new Vector3(points[i].x, shapeY, points[i].z));
    }

    List<Vector3> BuildFreePreviewPath()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return null;

        if (preserveInitialFreeDrawnPath && !_freePathWasEdited)
        {
            List<Vector3> rawPreserved = BuildPreservedRawFreePath();
            if (rawPreserved != null && rawPreserved.Count >= 2)
                return rawPreserved;
        }

        return BuildHandleDrivenFreePath();
    }

    List<Vector3> BuildPreservedRawFreePath()
    {
        if (_freeRawPath == null || _freeRawPath.Count < 2)
            return null;

        if (_closedLoop)
        {
            List<Vector3> raw = new List<Vector3>(_freeRawPath);
            if (raw.Count > 1 && Vector3.Distance(raw[0], raw[raw.Count - 1]) < 0.001f)
                raw.RemoveAt(raw.Count - 1);

            raw = RemoveTooShortSegmentsClosed(raw, Mathf.Min(minClosedSegmentLength, Mathf.Max(0.02f, rawFreeMinPointSpacing)));

            List<Vector3> validated = ValidateClosedCandidate(raw);
            if (validated != null)
                return validated;

            if (useSafeClosedFallback)
                return BuildHandleDrivenFreePath();

            return null;
        }

        return SimplifyOpenByMinSpacing(_freeRawPath, rawFreeMinPointSpacing);
    }

    List<Vector3> BuildHandleDrivenFreePath()
    {
        if (_closedLoop)
            return BuildHandleDrivenClosedFreePath();

        if (IsMostlyStraightOpen(freeControlPoints))
            return new List<Vector3>(freeControlPoints);

        List<Vector3> smoothOpen = Chaikin(freeControlPoints, openFreeSmoothIterations, false);
        int openTargetCount = Mathf.Max(freeWallResolution, smoothOpen.Count * 6);
        return ResampleOpenByCount(smoothOpen, openTargetCount);
    }

    List<Vector3> BuildHandleDrivenClosedFreePath()
    {
        List<Vector3> work = new List<Vector3>(freeControlPoints);
        work = RemoveTooShortSegmentsClosed(work, minClosedSegmentLength);

        if (work.Count < 3)
            return BuildSafeClosedFallbackFromControls();

        if (closedFreeSmoothIterations > 0)
            work = Chaikin(work, closedFreeSmoothIterations, true);

        work = RemoveTooShortSegmentsClosed(work, minClosedSegmentLength);
        if (work.Count < 3)
            return BuildSafeClosedFallbackFromControls();

        int targetCount = Mathf.Max(work.Count, Mathf.Max(closedFreeWallResolution, work.Count * 3));
        List<Vector3> denseClosed = ResampleClosedByCount(work, targetCount);
        denseClosed = RemoveTooShortSegmentsClosed(denseClosed, minClosedSegmentLength);

        List<Vector3> validated = ValidateClosedCandidate(denseClosed);
        if (validated != null)
            return validated;

        if (useSafeClosedFallback)
            return BuildSafeClosedFallbackFromControls();

        return null;
    }

    List<Vector3> BuildSafeClosedFallbackFromControls()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return null;

        List<Vector3> raw = new List<Vector3>(freeControlPoints);
        raw = RemoveTooShortSegmentsClosed(raw, minClosedSegmentLength);

        List<Vector3> validated = ValidateClosedCandidate(raw);
        if (validated != null)
            return validated;

        return BuildConvexHullClosedFallbackFromControls();
    }

    List<Vector3> BuildConvexHullClosedFallbackFromControls()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return null;

        List<Vector2> hull = ComputeConvexHullXZ(freeControlPoints);
        if (hull == null || hull.Count < 3)
            return null;

        List<Vector3> pts = new List<Vector3>(hull.Count + 1);
        for (int i = 0; i < hull.Count; i++)
            pts.Add(new Vector3(hull[i].x, shapeY, hull[i].y));

        pts.Add(pts[0]);
        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    List<Vector3> ValidateClosedCandidate(List<Vector3> candidate)
    {
        if (candidate == null || candidate.Count < 3)
            return null;

        List<Vector3> work = new List<Vector3>(candidate);

        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        work = RemoveTooShortSegmentsClosed(work, minClosedSegmentLength);
        if (work.Count < 3)
            return null;

        if (ComputeAbsoluteSignedAreaXZ(work) < 0.0025f)
            return null;

        if (HasSelfIntersectionXZ(work))
            return null;

        work.Add(work[0]);
        EnsureCounterClockwiseXZ(work);
        return work;
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

    static List<Vector3> RemoveTooShortSegmentsClosed(List<Vector3> pts, float minLen)
    {
        List<Vector3> work = new List<Vector3>();
        if (pts == null || pts.Count == 0)
            return work;

        work.AddRange(pts);

        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 3)
            return work;

        bool changed = true;
        int guard = 0;

        while (changed && work.Count >= 3 && guard < 64)
        {
            changed = false;
            guard++;

            for (int i = work.Count - 1; i >= 0; i--)
            {
                int next = (i + 1) % work.Count;
                if (Vector3.Distance(work[i], work[next]) < minLen)
                {
                    work.RemoveAt(next);
                    changed = true;
                    if (work.Count < 3)
                        break;
                }
            }
        }

        return work;
    }

    static List<Vector3> SimplifyOpenByMinSpacing(List<Vector3> pts, float minSpacing)
    {
        List<Vector3> res = new List<Vector3>();
        if (pts == null || pts.Count == 0)
            return res;

        minSpacing = Mathf.Max(0.001f, minSpacing);

        res.Add(pts[0]);
        Vector3 last = pts[0];

        for (int i = 1; i < pts.Count - 1; i++)
        {
            if (Vector3.Distance(last, pts[i]) >= minSpacing)
            {
                res.Add(pts[i]);
                last = pts[i];
            }
        }

        if (pts.Count > 1)
        {
            Vector3 end = pts[pts.Count - 1];
            if (res.Count == 0 || Vector3.Distance(res[res.Count - 1], end) > 0.0001f)
                res.Add(end);
        }

        return res;
    }

    static float ComputeAbsoluteSignedAreaXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 3)
            return 0f;

        int count = pts.Count;
        if (Vector3.Distance(pts[0], pts[count - 1]) < 0.0001f)
            count--;

        if (count < 3)
            return 0f;

        float area = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % count];
            area += (a.x * b.z - b.x * a.z);
        }

        return Mathf.Abs(area) * 0.5f;
    }

    static bool HasSelfIntersectionXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4)
            return false;

        List<Vector3> work = new List<Vector3>(pts);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        int n = work.Count;
        if (n < 4)
            return false;

        for (int i = 0; i < n; i++)
        {
            int nextI = (i + 1) % n;

            for (int j = i + 1; j < n; j++)
            {
                int nextJ = (j + 1) % n;

                if (i == j || nextI == j || nextJ == i)
                    continue;

                if (SegmentsIntersectXZ(work[i], work[nextI], work[j], work[nextJ]))
                    return true;
            }
        }

        return false;
    }

    static bool SegmentsIntersectXZ(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
    {
        float o1 = OrientationXZ(a1, a2, b1);
        float o2 = OrientationXZ(a1, a2, b2);
        float o3 = OrientationXZ(b1, b2, a1);
        float o4 = OrientationXZ(b1, b2, a2);

        return (o1 * o2 < 0f) && (o3 * o4 < 0f);
    }

    static float OrientationXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
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
