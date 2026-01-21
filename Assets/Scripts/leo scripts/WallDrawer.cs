using System.Collections.Generic;
using UnityEngine;

public class WallDrawer : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public LineRenderer linePrefab;

    [Header("Draw Settings")]
    public float pointSpacing = 0.3f;

    [Header("Auto Close")]
    public bool autoClose = true;
    public float closeDistance = 1.0f;

    [Header("Closed Shape")]
    public GameObject closedShapePrefab;

    private LineRenderer currentLine;
    private List<Vector3> points = new List<Vector3>();

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
            StartLine();

        if (Input.GetMouseButton(0) && currentLine != null)
            UpdateLine();

        if (Input.GetMouseButtonUp(0))
            EndLine();
    }

    void StartLine()
    {
        points.Clear();

        currentLine = Instantiate(linePrefab);
        currentLine.positionCount = 0;
    }

    void UpdateLine()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit))
            return;

        Vector3 point = hit.point;

        if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], point) > pointSpacing)
        {
            points.Add(point);
            currentLine.positionCount = points.Count;
            currentLine.SetPositions(points.ToArray());
        }
    }

    void EndLine()
    {
        bool closed = false;

        if (autoClose && points.Count >= 3)
        {
            float d = Vector3.Distance(points[points.Count - 1], points[0]);
            if (d <= closeDistance)
            {
                points.Add(points[0]);
                currentLine.positionCount = points.Count;
                currentLine.SetPositions(points.ToArray());
                closed = true;
            }
        }

        if (closed)
        {
            var cleaned = CleanPoints(points, pointSpacing);

            if (!IsSelfIntersectingXZ(cleaned))
                CreateClosedShape(cleaned);
            else
                Debug.LogWarning("Shape self-intersecting -> skipped");
        }

        currentLine = null;
    }

    void CreateClosedShape(List<Vector3> shapePoints)
    {
        GameObject go = closedShapePrefab != null
            ? Instantiate(closedShapePrefab)
            : new GameObject("ClosedShape");

        ClosedShape shape = go.GetComponent<ClosedShape>();
        if (shape == null)
            shape = go.AddComponent<ClosedShape>();

        shape.points = new List<Vector3>(shapePoints);
    }

    // -------- CLEAN + CHECK --------

    List<Vector3> CleanPoints(List<Vector3> input, float minDist)
    {
        if (input == null || input.Count < 3) return input;

        List<Vector3> result = new List<Vector3>();
        Vector3 last = input[0];
        result.Add(last);

        for (int i = 1; i < input.Count; i++)
        {
            Vector3 p = input[i];
            if (Vector3.Distance(last, p) >= minDist * 0.75f)
            {
                result.Add(p);
                last = p;
            }
        }

        for (int i = result.Count - 2; i > 0; i--)
        {
            Vector3 a = result[i - 1];
            Vector3 b = result[i];
            Vector3 c = result[i + 1];

            Vector3 ab = b - a; ab.y = 0;
            Vector3 bc = c - b; bc.y = 0;

            if (ab.sqrMagnitude < 0.0001f || bc.sqrMagnitude < 0.0001f)
                continue;

            ab.Normalize();
            bc.Normalize();

            if (Vector3.Dot(ab, bc) > 0.995f)
                result.RemoveAt(i);
        }

        if (result.Count >= 3 && Vector3.Distance(result[0], result[^1]) < minDist * 2f)
            result[^1] = result[0];

        return result;
    }

    bool IsSelfIntersectingXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4) return false;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a1 = new Vector2(pts[i].x, pts[i].z);
            Vector2 a2 = new Vector2(pts[i + 1].x, pts[i + 1].z);

            for (int j = i + 2; j < pts.Count - 1; j++)
            {
                if (i == 0 && j == pts.Count - 2) continue;

                Vector2 b1 = new Vector2(pts[j].x, pts[j].z);
                Vector2 b2 = new Vector2(pts[j + 1].x, pts[j + 1].z);

                if (SegmentsIntersect(a1, a2, b1, b2))
                    return true;
            }
        }
        return false;
    }

    bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float o1 = Orient(p1, p2, q1);
        float o2 = Orient(p1, p2, q2);
        float o3 = Orient(q1, q2, p1);
        float o4 = Orient(q1, q2, p2);

        return (o1 * o2 < 0) && (o3 * o4 < 0);
    }

    float Orient(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) -
               (b.y - a.y) * (c.x - a.x);
    }
}
