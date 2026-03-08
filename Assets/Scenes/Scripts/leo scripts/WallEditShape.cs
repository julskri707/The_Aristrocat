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

    private bool _closedLoop = true;

    void Awake()
    {
        if (wall == null)
            wall = GetComponent<WallObject>();
    }

    // =====================================================
    // INIT
    // =====================================================
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

    // =====================================================
    // PROVIDER
    // =====================================================
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

    // =====================================================
    // PREVIEW PATH
    // =====================================================
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

    // =====================================================
    // ELLIPSE
    // =====================================================
    bool TrySetupEllipse(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points.Count < 10) return false;

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
        return avgError <= 0.25f;
    }

    Vector3 GetEllipseControlPoint(int index)
    {
        Vector3 c = GetBoundsCenter();

        switch (index)
        {
            case 0: return new Vector3(maxX, shapeY, c.z); // right
            case 1: return new Vector3(c.x, shapeY, maxZ); // top
            case 2: return new Vector3(minX, shapeY, c.z); // left
            case 3: return new Vector3(c.x, shapeY, minZ); // bottom
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

        // ferme la boucle
        pts.Add(pts[0]);

        // IMPORTANT: forcer CCW propre pour éviter faces invisibles
        EnsureCounterClockwiseXZ(pts);

        return pts;
    }

    // =====================================================
    // RECTANGLE
    // =====================================================
    bool TrySetupRectangle(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points.Count < 4) return false;

        shapeY = points[0].y;
        ComputeBounds(points);

        float width = maxX - minX;
        float depth = maxZ - minZ;

        if (width < 0.1f || depth < 0.1f)
            return false;

        float tolerance = Mathf.Max(width, depth) * 0.15f;
        int nearBorder = 0;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            bool onLeft   = Mathf.Abs(p.x - minX) <= tolerance;
            bool onRight  = Mathf.Abs(p.x - maxX) <= tolerance;
            bool onBottom = Mathf.Abs(p.z - minZ) <= tolerance;
            bool onTop    = Mathf.Abs(p.z - maxZ) <= tolerance;

            if (onLeft || onRight || onBottom || onTop)
                nearBorder++;
        }

        float ratio = nearBorder / (float)Mathf.Max(1, points.Count);
        return ratio >= 0.7f;
    }

    Vector3 GetRectangleControlPoint(int index)
    {
        Vector3 topLeft     = new Vector3(minX, shapeY, maxZ);
        Vector3 topRight    = new Vector3(maxX, shapeY, maxZ);
        Vector3 bottomRight = new Vector3(maxX, shapeY, minZ);
        Vector3 bottomLeft  = new Vector3(minX, shapeY, minZ);

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
            case 0: // top-left
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 2: // top-right
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 4: // bottom-right
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 6: // bottom-left
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 1: // top
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 3: // right
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                break;

            case 5: // bottom
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 7: // left
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                break;
        }
    }

    List<Vector3> BuildRectanglePath()
    {
        // IMPORTANT:
        // On génère dans un ordre CCW en XZ
        // pour éviter qu’un côté soit culled / invisible.
        Vector3 topLeft     = new Vector3(minX, shapeY, maxZ);
        Vector3 bottomLeft  = new Vector3(minX, shapeY, minZ);
        Vector3 bottomRight = new Vector3(maxX, shapeY, minZ);
        Vector3 topRight    = new Vector3(maxX, shapeY, maxZ);

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

    // =====================================================
    // FREE SHAPE
    // =====================================================
    void SetupFree(List<Vector3> points)
    {
        freeControlPoints.Clear();

        float perimeter = ComputePerimeter(points, _closedLoop);
        int wantedHandles = Mathf.RoundToInt(perimeter / Mathf.Max(0.1f, freeHandleSpacing));
        wantedHandles = Mathf.Clamp(wantedHandles, minFreeHandles, maxFreeHandles);

        if (points.Count <= wantedHandles)
        {
            freeControlPoints.AddRange(points);
        }
        else
        {
            for (int i = 0; i < wantedHandles; i++)
            {
                float t = i / (float)(wantedHandles - 1);
                int idx = Mathf.RoundToInt(t * (points.Count - 1));
                idx = Mathf.Clamp(idx, 0, points.Count - 1);
                freeControlPoints.Add(points[idx]);
            }
        }

        if (freeControlPoints.Count > 0)
            shapeY = freeControlPoints[0].y;
    }

    List<Vector3> BuildFreePreviewPath()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return null;

        List<Vector3> path = new List<Vector3>();

        if (freeControlPoints.Count == 2)
        {
            path.AddRange(freeControlPoints);
            return path;
        }

        int resolution = Mathf.Max(freeWallResolution, freeControlPoints.Count * 4);

        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            path.Add(GetPointOnCatmullRom(t));
        }

        if (_closedLoop && path.Count > 0)
            path.Add(path[0]);

        return path;
    }

    Vector3 GetPointOnCatmullRom(float t)
    {
        int count = freeControlPoints.Count;
        if (count == 0) return Vector3.zero;
        if (count == 1) return freeControlPoints[0];

        float scaledT = t * (count - 1);
        int i = Mathf.FloorToInt(scaledT);
        float localT = scaledT - i;

        int p0 = Mathf.Clamp(i - 1, 0, count - 1);
        int p1 = Mathf.Clamp(i,     0, count - 1);
        int p2 = Mathf.Clamp(i + 1, 0, count - 1);
        int p3 = Mathf.Clamp(i + 2, 0, count - 1);

        Vector3 P0 = freeControlPoints[p0];
        Vector3 P1 = freeControlPoints[p1];
        Vector3 P2 = freeControlPoints[p2];
        Vector3 P3 = freeControlPoints[p3];

        return 0.5f * (
            (2f * P1) +
            (-P0 + P2) * localT +
            (2f * P0 - 5f * P1 + 4f * P2 - P3) * localT * localT +
            (-P0 + 3f * P1 - 3f * P2 + P3) * localT * localT * localT
        );
    }

    // =====================================================
    // APPLY
    // =====================================================
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

    // =====================================================
    // UTILS
    // =====================================================
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

    void EnsureCounterClockwiseXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4)
            return;

        // si fermé avec point dupliqué à la fin, on ignore le dernier pour l'aire
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

        // area > 0 => CCW ; area < 0 => CW
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