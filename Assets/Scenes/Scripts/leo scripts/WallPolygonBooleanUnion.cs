using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

/// <summary>
/// Union booléenne 2D (plan XZ) des empreintes fermées via Clipper2 — cercles, ovales, triangles, polygones quelconques,
/// sans dépendre du découpage en rectangles ni du carré englobant du cercle.
/// </summary>
public static class WallPolygonBooleanUnion
{
    const int DefaultPrecisionDecimalPlaces = 6;

    /// <summary>
    /// Union (FillRule.NonZero) de plusieurs boucles fermées monde XZ ; retourne le plus grand contour extérieur (aire positive).
    /// </summary>
    public static bool TryUnionClosedRingsWorldXZ(
        IReadOnlyList<List<Vector3>> rings,
        float worldY,
        out List<Vector3> mergedClosedPath,
        out bool isAxisFilledRectangleGuess)
    {
        mergedClosedPath = null;
        isAxisFilledRectangleGuess = false;

        if (rings == null || rings.Count == 0)
            return false;

        var subject = new PathsD();
        for (int r = 0; r < rings.Count; r++)
        {
            List<Vector3> ring = rings[r];
            if (!TryBuildPathDFromWorldRing(ring, out PathD pd) || pd.Count < 3)
                continue;
            subject.Add(pd);
        }

        if (subject.Count == 0)
            return false;

        // PathsD : Union(subject, fillRule) n’a pas de paramètre precision — utiliser BooleanOp pour la précision décimale.
        PathsD solution = Clipper.BooleanOp(ClipType.Union, subject, null, FillRule.NonZero, DefaultPrecisionDecimalPlaces);
        if (solution == null || solution.Count == 0)
            return false;

        int bestIdx = 0;
        double bestArea = -1.0;
        for (int i = 0; i < solution.Count; i++)
        {
            double a = System.Math.Abs(Clipper.Area(solution[i]));
            if (a > bestArea)
            {
                bestArea = a;
                bestIdx = i;
            }
        }

        PathD outer = solution[bestIdx];
        if (outer == null || outer.Count < 3)
            return false;

        mergedClosedPath = PathDToClosedWorldXZ(outer, worldY);
        isAxisFilledRectangleGuess = GuessAxisAlignedFilledRectangle(mergedClosedPath, 0.02f);
        return mergedClosedPath != null && mergedClosedPath.Count >= 4;
    }

    static bool TryBuildPathDFromWorldRing(List<Vector3> ring, out PathD pd)
    {
        pd = new PathD();
        if (ring == null || ring.Count < 3)
            return false;

        int n = ring.Count;
        if (n >= 2 && Vector3.Distance(ring[0], ring[n - 1]) < 0.0005f)
            n--;

        if (n < 3)
            return false;

        for (int i = 0; i < n; i++)
            pd.Add(new PointD(ring[i].x, ring[i].z));

        return true;
    }

    static List<Vector3> PathDToClosedWorldXZ(PathD path, float y)
    {
        var list = new List<Vector3>(path.Count + 1);
        for (int i = 0; i < path.Count; i++)
            list.Add(new Vector3((float)path[i].x, y, (float)path[i].y));

        if (list.Count >= 2 && (list[0] - list[list.Count - 1]).sqrMagnitude > 1e-8f)
            list.Add(list[0]);

        return list;
    }

    static bool GuessAxisAlignedFilledRectangle(List<Vector3> path, float tol)
    {
        if (path == null)
            return false;
        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;

        if (n != 4)
            return false;

        for (int i = 0; i < n; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[(i + 1) % n];
            bool horiz = Mathf.Abs(a.z - b.z) <= tol;
            bool vert = Mathf.Abs(a.x - b.x) <= tol;
            if (!horiz && !vert)
                return false;
        }

        return true;
    }
}
