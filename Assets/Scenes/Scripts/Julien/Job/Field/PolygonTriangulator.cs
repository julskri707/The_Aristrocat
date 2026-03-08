// PolygonTriangulator.cs
// Unity 2022+
// Public static triangulation helper for simple polygon meshes (convex/concave).
// - Ear clipping in 2D
// - No external libs, no MonoBehaviour
// Notes:
// - Expects a simple (non self-intersecting) polygon with vertices in any winding.
// - Removes ears until triangulated or fails (returns empty array on failure).

using System;
using System.Collections.Generic;
using UnityEngine;

public static class PolygonTriangulator
{
    /// <summary>
    /// Triangulates a simple polygon (convex or concave) using ear clipping.
    /// Returns triangle indices into the input polygon array.
    /// If triangulation fails (degenerate/self-intersecting), returns an empty array.
    /// </summary>
    public static int[] Triangulate(Vector2[] polygon)
    {
        if (polygon == null || polygon.Length < 3)
            return Array.Empty<int>();

        int n = polygon.Length;
        var indices = new List<int>(n);
        for (int i = 0; i < n; i++) indices.Add(i);

        // Ensure CCW winding (ear clipping expects consistent winding)
        if (SignedArea(polygon) < 0f)
            indices.Reverse();

        var triangles = new List<int>((n - 2) * 3);

        int guard = 0;
        int maxGuard = n * n * 4;

        while (indices.Count > 3 && guard < maxGuard)
        {
            guard++;

            bool earFound = false;

            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];

                Vector2 a = polygon[prev];
                Vector2 b = polygon[curr];
                Vector2 c = polygon[next];

                if (IsNearlyCollinear(a, b, c))
                    continue;

                if (!IsConvexCCW(a, b, c))
                    continue;

                bool containsAny = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int pIdx = indices[j];
                    if (pIdx == prev || pIdx == curr || pIdx == next)
                        continue;

                    if (PointInTriangle(polygon[pIdx], a, b, c))
                    {
                        containsAny = true;
                        break;
                    }
                }

                if (containsAny)
                    continue;

                // Ear found
                triangles.Add(prev);
                triangles.Add(curr);
                triangles.Add(next);

                indices.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound)
                return Array.Empty<int>();
        }

        if (indices.Count == 3)
        {
            triangles.Add(indices[0]);
            triangles.Add(indices[1]);
            triangles.Add(indices[2]);
        }

        return triangles.ToArray();
    }

    private static float SignedArea(Vector2[] poly)
    {
        double sum = 0.0;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            double xi = poly[i].x;
            double yi = poly[i].y;
            double xj = poly[j].x;
            double yj = poly[j].y;
            sum += (xj * yi) - (xi * yj);
        }
        return (float)(sum * 0.5);
    }

    private static bool IsConvexCCW(Vector2 a, Vector2 b, Vector2 c)
    {
        float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        return cross > 0f;
    }

    private static bool IsNearlyCollinear(Vector2 a, Vector2 b, Vector2 c)
    {
        float area2 = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        return area2 <= 1e-6f;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);

        bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
        bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);

        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
