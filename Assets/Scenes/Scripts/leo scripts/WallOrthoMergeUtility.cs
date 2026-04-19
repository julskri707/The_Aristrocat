using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Union de rectangles alignés axes (monde XZ) : contour fermé orthogonal (L, U, grand rectangle, etc.).
/// </summary>
public static class WallOrthoMergeUtility
{
    const float WorldEdgeEps = 1e-4f;

    public struct RectXZ
    {
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
    }

    /// <summary>
    /// Construit un contour fermé (premier point = dernier) couvrant l’union des rectangles.
    /// Si l’union remplit exactement le rectangle englobant → 5 points (rectangle).
    /// Sinon → polygone orthogonal (ex. L).
    /// </summary>
    public static bool TryBuildMergedClosedPath(
        List<RectXZ> rects,
        float cellStep,
        Vector2 gridOriginXZ,
        float y,
        out List<Vector3> path,
        out bool isFilledRectangle)
    {
        path = null;
        isFilledRectangle = false;

        if (rects == null || rects.Count == 0 || cellStep <= 1e-6f)
            return false;

        float step = cellStep;
        float ox = gridOriginXZ.x;
        float oz = gridOriginXZ.y;

        float gminX = float.PositiveInfinity, gmaxX = float.NegativeInfinity;
        float gminZ = float.PositiveInfinity, gmaxZ = float.NegativeInfinity;
        for (int r = 0; r < rects.Count; r++)
        {
            var b = rects[r];
            if (b.maxX - b.minX <= 1e-6f || b.maxZ - b.minZ <= 1e-6f)
                continue;
            gminX = Mathf.Min(gminX, b.minX);
            gmaxX = Mathf.Max(gmaxX, b.maxX);
            gminZ = Mathf.Min(gminZ, b.minZ);
            gmaxZ = Mathf.Max(gmaxZ, b.maxZ);
        }

        if (float.IsInfinity(gminX))
            return false;

        int ix0 = Mathf.FloorToInt((gminX - ox) / step);
        int ix1 = Mathf.CeilToInt((gmaxX - ox) / step);
        int iz0 = Mathf.FloorToInt((gminZ - oz) / step);
        int iz1 = Mathf.CeilToInt((gmaxZ - oz) / step);

        int nx = Mathf.Clamp(ix1 - ix0, 1, 2048);
        int nz = Mathf.Clamp(iz1 - iz0, 1, 2048);

        bool CellOverlapsAnyRect(int ci, int cj)
        {
            float cx0 = ox + (ix0 + ci) * step;
            float cz0 = oz + (iz0 + cj) * step;
            float cx1 = cx0 + step;
            float cz1 = cz0 + step;

            for (int r = 0; r < rects.Count; r++)
            {
                var b = rects[r];
                if (cx1 <= b.minX + 1e-5f || cx0 >= b.maxX - 1e-5f || cz1 <= b.minZ + 1e-5f || cz0 >= b.maxZ - 1e-5f)
                    continue;
                return true;
            }

            return false;
        }

        bool CellOccupied(int ci, int cj)
        {
            return CellOverlapsAnyRect(ci, cj);
        }

        int unionCount = 0;
        var occ = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                if (CellOccupied(i, j))
                {
                    occ[i, j] = true;
                    unionCount++;
                }
            }
        }

        if (unionCount == 0)
            return false;

        int si = -1, sj = -1;
        for (int i = 0; i < nx && si < 0; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                if (!occ[i, j])
                    continue;
                si = i;
                sj = j;
                break;
            }
        }

        int floodCount = FloodFillCount4(occ, nx, nz, si, sj);
        if (floodCount != unionCount)
        {
            // Union en plusieurs îlots (lots éloignés) : pas de bord unique sur la grille — englober par l'AABB.
            isFilledRectangle = true;
            path = BuildRectLoop(gminX, gmaxX, gminZ, gmaxZ, y);
            return path != null && path.Count >= 4;
        }

        int bboxCount = nx * nz;
        if (unionCount == bboxCount)
        {
            isFilledRectangle = true;
            float bx0 = ox + ix0 * step;
            float bx1 = ox + (ix0 + nx) * step;
            float bz0 = oz + iz0 * step;
            float bz1 = oz + (iz0 + nz) * step;
            path = BuildRectLoop(bx0, bx1, bz0, bz1, y);
            return true;
        }

        isFilledRectangle = false;
        if (!TryBuildBoundaryFromGrid(occ, nx, nz, ix0, iz0, step, ox, oz, y, out path))
            return false;

        return path != null && path.Count >= 4;
    }

    static List<Vector3> BuildRectLoop(float minX, float maxX, float minZ, float maxZ, float y)
    {
        return new List<Vector3>(5)
        {
            new Vector3(minX, y, maxZ),
            new Vector3(minX, y, minZ),
            new Vector3(maxX, y, minZ),
            new Vector3(maxX, y, maxZ),
            new Vector3(minX, y, maxZ)
        };
    }

    /// <summary>
    /// Secours lorsque la construction du polygone d’union échoue : rectangle monde englobant les empreintes.
    /// </summary>
    public static List<Vector3> BuildAxisAlignedRectLoopWorld(float minX, float maxX, float minZ, float maxZ, float y)
    {
        if (!(maxX > minX && maxZ > minZ))
            return null;
        return BuildRectLoop(minX, maxX, minZ, maxZ, y);
    }

    static int FloodFillCount4(bool[,] occ, int nx, int nz, int si, int sj)
    {
        if (si < 0 || sj < 0 || si >= nx || sj >= nz || !occ[si, sj])
            return 0;

        var q = new Queue<(int x, int z)>();
        var vis = new bool[nx, nz];
        q.Enqueue((si, sj));
        vis[si, sj] = true;
        int count = 0;
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        while (q.Count > 0)
        {
            (int x, int z) = q.Dequeue();
            if (!occ[x, z])
                continue;
            count++;

            for (int k = 0; k < 4; k++)
            {
                int nx2 = x + dx[k];
                int nz2 = z + dz[k];
                if (nx2 < 0 || nz2 < 0 || nx2 >= nx || nz2 >= nz)
                    continue;
                if (vis[nx2, nz2] || !occ[nx2, nz2])
                    continue;
                vis[nx2, nz2] = true;
                q.Enqueue((nx2, nz2));
            }
        }

        return count;
    }

    /// <summary>
    /// Arêtes unitaires entre cellule occupée et vide, puis boucle ordonnée (sommets aux coins).
    /// </summary>
    static bool TryBuildBoundaryFromGrid(bool[,] occ, int nx, int nz, int ix0, int iz0, float step, float ox, float oz, float y, out List<Vector3> path)
    {
        path = null;

        var adj = new Dictionary<(int cx, int cz), List<(int cx, int cz)>>();

        void AddUndirected(int x1, int z1, int x2, int z2)
        {
            var a = (x1, z1);
            var b = (x2, z2);
            if (!adj.TryGetValue(a, out var la))
            {
                la = new List<(int cx, int cz)>(2);
                adj[a] = la;
            }

            if (!la.Contains(b))
                la.Add(b);

            if (!adj.TryGetValue(b, out var lb))
            {
                lb = new List<(int cx, int cz)>(2);
                adj[b] = lb;
            }

            if (!lb.Contains(a))
                lb.Add(a);
        }

        for (int cx = 0; cx <= nx; cx++)
        {
            for (int cz = 0; cz < nz; cz++)
            {
                bool left = cx > 0 && occ[cx - 1, cz];
                bool right = cx < nx && occ[cx, cz];
                if (left != right)
                    AddUndirected(cx, cz, cx, cz + 1);
            }
        }

        for (int cz = 0; cz <= nz; cz++)
        {
            for (int cx = 0; cx < nx; cx++)
            {
                bool bottom = cz > 0 && occ[cx, cz - 1];
                bool top = cz < nz && occ[cx, cz];
                if (bottom != top)
                    AddUndirected(cx, cz, cx + 1, cz);
            }
        }

        if (adj.Count == 0)
            return false;

        (int cx, int cz) start = (int.MaxValue, int.MaxValue);
        foreach (var kv in adj.Keys)
        {
            if (kv.cx < start.cx || (kv.cx == start.cx && kv.cz < start.cz))
                start = kv;
        }

        if (!adj.TryGetValue(start, out var firstNeighbors) || firstNeighbors.Count == 0)
            return false;

        var verts = new List<Vector3>(64);

        void PushW(int gx, int gz)
        {
            float wx = ox + (ix0 + gx) * step;
            float wz = oz + (iz0 + gz) * step;
            verts.Add(new Vector3(wx, y, wz));
        }

        PushW(start.cx, start.cz);
        (int cx, int cz) prev = start;
        (int cx, int cz) curr = firstNeighbors[0];

        int guard = 0;
        int maxGuard = adj.Count * 4 + 64;

        while (guard++ < maxGuard)
        {
            if (curr.cx == start.cx && curr.cz == start.cz && verts.Count > 1)
                break;

            PushW(curr.cx, curr.cz);

            if (!adj.TryGetValue(curr, out var nb) || nb.Count != 2)
                return false;

            (int cx, int cz) next = nb[0].cx == prev.cx && nb[0].cz == prev.cz ? nb[1] : nb[0];
            prev = curr;
            curr = next;
        }

        if (verts.Count < 3)
            return false;

        DedupeConsecutive(verts);
        if (verts.Count >= 2 && (verts[0] - verts[verts.Count - 1]).sqrMagnitude < 1e-8f)
            verts.RemoveAt(verts.Count - 1);
        verts.Add(verts[0]);
        path = verts;
        return true;
    }

    /// <summary>
    /// Vrai si l’union des rectangles monde forme au moins deux composantes connexes (4-voisins) sur la grille compressée
    /// (ex. deux carrés qui ne se touchent qu’en diagonal) — dans ce cas un seul mur enveloppe ne peut pas suivre deux contours séparés.
    /// </summary>
    public static bool IsRectUnionDisconnectedFourWay(List<RectXZ> rects)
    {
        if (rects == null || rects.Count == 0)
            return false;

        var xs = new List<float>(rects.Count * 2);
        var zs = new List<float>(rects.Count * 2);

        for (int r = 0; r < rects.Count; r++)
        {
            RectXZ b = rects[r];
            if (b.maxX - b.minX <= 1e-6f || b.maxZ - b.minZ <= 1e-6f)
                continue;
            xs.Add(b.minX);
            xs.Add(b.maxX);
            zs.Add(b.minZ);
            zs.Add(b.maxZ);
        }

        if (xs.Count < 2 || zs.Count < 2)
            return false;

        xs.Sort();
        zs.Sort();
        float[] xV = MergeCloseSortedValues(xs, WorldEdgeEps).ToArray();
        float[] zV = MergeCloseSortedValues(zs, WorldEdgeEps).ToArray();

        if (xV.Length < 2 || zV.Length < 2)
            return false;

        int nx = xV.Length - 1;
        int nz = zV.Length - 1;
        if (nx <= 0 || nz <= 0)
            return false;

        var occ = new bool[nx, nz];
        int unionCount = 0;

        for (int i = 0; i < nx; i++)
        {
            float cx0 = xV[i];
            float cx1 = xV[i + 1];
            if (cx1 - cx0 <= 1e-8f)
                continue;

            for (int j = 0; j < nz; j++)
            {
                float cz0 = zV[j];
                float cz1 = zV[j + 1];
                if (cz1 - cz0 <= 1e-8f)
                    continue;

                if (!CellOverlapsAnyRectWorld(cx0, cx1, cz0, cz1, rects))
                    continue;

                occ[i, j] = true;
                unionCount++;
            }
        }

        if (unionCount == 0)
            return false;

        int si = -1, sj = -1;
        for (int i = 0; i < nx && si < 0; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                if (!occ[i, j])
                    continue;
                si = i;
                sj = j;
                break;
            }
        }

        return FloodFillCount4(occ, nx, nz, si, sj) != unionCount;
    }

    /// <summary>
    /// Contour exact de l’union des rectangles (monde), sans remplir le rectangle englobant :
    /// deux branches en L restent un L. Grille compressée sur les arêtes des lots.
    /// </summary>
    public static bool TryBuildMergedClosedPathFromRectUnionWorld(
        List<RectXZ> rects,
        float y,
        out List<Vector3> path,
        out bool isFilledRectangle)
    {
        path = null;
        isFilledRectangle = false;

        if (rects == null || rects.Count == 0)
            return false;

        var xs = new List<float>(rects.Count * 2);
        var zs = new List<float>(rects.Count * 2);

        for (int r = 0; r < rects.Count; r++)
        {
            var b = rects[r];
            if (b.maxX - b.minX <= 1e-6f || b.maxZ - b.minZ <= 1e-6f)
                continue;
            xs.Add(b.minX);
            xs.Add(b.maxX);
            zs.Add(b.minZ);
            zs.Add(b.maxZ);
        }

        if (xs.Count < 2 || zs.Count < 2)
            return false;

        xs.Sort();
        zs.Sort();
        float[] xV = MergeCloseSortedValues(xs, WorldEdgeEps).ToArray();
        float[] zV = MergeCloseSortedValues(zs, WorldEdgeEps).ToArray();

        if (xV.Length < 2 || zV.Length < 2)
            return false;

        int nx = xV.Length - 1;
        int nz = zV.Length - 1;
        if (nx <= 0 || nz <= 0)
            return false;

        var occ = new bool[nx, nz];
        int unionCount = 0;

        for (int i = 0; i < nx; i++)
        {
            float cx0 = xV[i];
            float cx1 = xV[i + 1];
            if (cx1 - cx0 <= 1e-8f)
                continue;

            for (int j = 0; j < nz; j++)
            {
                float cz0 = zV[j];
                float cz1 = zV[j + 1];
                if (cz1 - cz0 <= 1e-8f)
                    continue;

                if (!CellOverlapsAnyRectWorld(cx0, cx1, cz0, cz1, rects))
                    continue;

                occ[i, j] = true;
                unionCount++;
            }
        }

        if (unionCount == 0)
            return false;

        int si = -1, sj = -1;
        for (int i = 0; i < nx && si < 0; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                if (!occ[i, j])
                    continue;
                si = i;
                sj = j;
                break;
            }
        }

        if (FloodFillCount4(occ, nx, nz, si, sj) != unionCount)
        {
            // Même logique que TryBuildMergedClosedPath : îlots disjoints → rectangle englobant des empreintes.
            isFilledRectangle = true;
            path = BuildRectLoop(xV[0], xV[xV.Length - 1], zV[0], zV[zV.Length - 1], y);
            return true;
        }

        if (unionCount == nx * nz)
        {
            isFilledRectangle = true;
            path = BuildRectLoop(xV[0], xV[xV.Length - 1], zV[0], zV[zV.Length - 1], y);
            return true;
        }

        isFilledRectangle = false;
        return TryBuildBoundaryFromGridAtVertices(occ, nx, nz, xV, zV, y, out path);
    }

    static List<float> MergeCloseSortedValues(List<float> sorted, float eps)
    {
        var r = new List<float>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            float v = sorted[i];
            if (r.Count == 0 || Mathf.Abs(v - r[r.Count - 1]) > eps)
                r.Add(v);
        }

        return r;
    }

    static bool CellOverlapsAnyRectWorld(float cx0, float cx1, float cz0, float cz1, List<RectXZ> rects)
    {
        for (int r = 0; r < rects.Count; r++)
        {
            var b = rects[r];
            if (b.maxX - b.minX <= 1e-6f || b.maxZ - b.minZ <= 1e-6f)
                continue;
            if (cx1 <= b.minX + WorldEdgeEps || cx0 >= b.maxX - WorldEdgeEps ||
                cz1 <= b.minZ + WorldEdgeEps || cz0 >= b.maxZ - WorldEdgeEps)
                continue;
            return true;
        }

        return false;
    }

    /// <summary>Même logique que <see cref="TryBuildBoundaryFromGrid"/> mais sommets aux coordonnées monde réelles.</summary>
    static bool TryBuildBoundaryFromGridAtVertices(
        bool[,] occ,
        int nx,
        int nz,
        float[] xV,
        float[] zV,
        float y,
        out List<Vector3> path)
    {
        path = null;

        var adj = new Dictionary<(int cx, int cz), List<(int cx, int cz)>>();

        void AddUndirected(int x1, int z1, int x2, int z2)
        {
            var a = (x1, z1);
            var b = (x2, z2);
            if (!adj.TryGetValue(a, out var la))
            {
                la = new List<(int cx, int cz)>(2);
                adj[a] = la;
            }

            if (!la.Contains(b))
                la.Add(b);

            if (!adj.TryGetValue(b, out var lb))
            {
                lb = new List<(int cx, int cz)>(2);
                adj[b] = lb;
            }

            if (!lb.Contains(a))
                lb.Add(a);
        }

        for (int cx = 0; cx <= nx; cx++)
        {
            for (int cz = 0; cz < nz; cz++)
            {
                bool left = cx > 0 && occ[cx - 1, cz];
                bool right = cx < nx && occ[cx, cz];
                if (left != right)
                    AddUndirected(cx, cz, cx, cz + 1);
            }
        }

        for (int cz = 0; cz <= nz; cz++)
        {
            for (int cx = 0; cx < nx; cx++)
            {
                bool bottom = cz > 0 && occ[cx, cz - 1];
                bool top = cz < nz && occ[cx, cz];
                if (bottom != top)
                    AddUndirected(cx, cz, cx + 1, cz);
            }
        }

        if (adj.Count == 0)
            return false;

        (int cx, int cz) start = (int.MaxValue, int.MaxValue);
        foreach (var kv in adj.Keys)
        {
            if (kv.cx < start.cx || (kv.cx == start.cx && kv.cz < start.cz))
                start = kv;
        }

        if (!adj.TryGetValue(start, out var firstNeighbors) || firstNeighbors.Count == 0)
            return false;

        var verts = new List<Vector3>(64);

        void PushW(int gx, int gz)
        {
            verts.Add(new Vector3(xV[gx], y, zV[gz]));
        }

        PushW(start.cx, start.cz);
        (int cx, int cz) prev = start;
        (int cx, int cz) curr = firstNeighbors[0];

        int guard = 0;
        int maxGuard = adj.Count * 4 + 64;

        while (guard++ < maxGuard)
        {
            if (curr.cx == start.cx && curr.cz == start.cz && verts.Count > 1)
                break;

            PushW(curr.cx, curr.cz);

            if (!adj.TryGetValue(curr, out var nb) || nb.Count != 2)
                return false;

            (int cx, int cz) next = nb[0].cx == prev.cx && nb[0].cz == prev.cz ? nb[1] : nb[0];
            prev = curr;
            curr = next;
        }

        if (verts.Count < 3)
            return false;

        DedupeConsecutive(verts);
        if (verts.Count >= 2 && (verts[0] - verts[verts.Count - 1]).sqrMagnitude < 1e-8f)
            verts.RemoveAt(verts.Count - 1);
        verts.Add(verts[0]);
        path = verts;
        return true;
    }

    static void DedupeConsecutive(List<Vector3> verts)
    {
        const float eps = 1e-4f;
        float epsSqr = eps * eps;
        for (int i = verts.Count - 1; i >= 1; i--)
        {
            if ((verts[i] - verts[i - 1]).sqrMagnitude <= epsSqr)
                verts.RemoveAt(i);
        }
    }

    /// <summary>
    /// Décompose une boucle fermée dont les arêtes sont alignées X/Z en rectangles d’union disjoints (maille entre coordonnées des sommets).
    /// Utilisé pour fusionner un lot déjà en L (ou autre orthogonal) avec de nouveaux rectangles sans passer par l’AABB.
    /// </summary>
    public static bool TryDecomposeOrthogonalClosedLoopToRects(List<Vector3> pathWorld, out List<RectXZ> rects)
    {
        rects = null;
        if (pathWorld == null || pathWorld.Count < 3)
            return false;

        var work = new List<Vector3>(pathWorld.Count);
        for (int i = 0; i < pathWorld.Count; i++)
            work.Add(pathWorld[i]);

        if (work.Count >= 2 && (work[0] - work[work.Count - 1]).sqrMagnitude < 1e-8f)
            work.RemoveAt(work.Count - 1);

        DedupeConsecutive(work);
        if (work.Count < 3)
            return false;

        if (!IsClosedOrthoLoopXZ(work, WorldEdgeEps))
            return false;

        var poly = new List<Vector2>(work.Count);
        for (int i = 0; i < work.Count; i++)
            poly.Add(new Vector2(work[i].x, work[i].z));

        var xs = new List<float>(work.Count * 2);
        var zs = new List<float>(work.Count * 2);
        for (int i = 0; i < work.Count; i++)
        {
            xs.Add(work[i].x);
            zs.Add(work[i].z);
        }

        xs.Sort();
        zs.Sort();
        float[] xV = MergeCloseSortedValues(xs, WorldEdgeEps).ToArray();
        float[] zV = MergeCloseSortedValues(zs, WorldEdgeEps).ToArray();

        if (xV.Length < 2 || zV.Length < 2)
            return false;

        rects = new List<RectXZ>();
        for (int i = 0; i < xV.Length - 1; i++)
        {
            float x0 = xV[i];
            float x1 = xV[i + 1];
            if (x1 - x0 <= 1e-7f)
                continue;

            for (int j = 0; j < zV.Length - 1; j++)
            {
                float z0 = zV[j];
                float z1 = zV[j + 1];
                if (z1 - z0 <= 1e-7f)
                    continue;

                float cx = (x0 + x1) * 0.5f;
                float cz = (z0 + z1) * 0.5f;
                if (!PointInPolygonEvenOddXZ(poly, new Vector2(cx, cz)))
                    continue;

                rects.Add(new RectXZ { minX = x0, maxX = x1, minZ = z0, maxZ = z1 });
            }
        }

        return rects.Count > 0;
    }

    static bool IsClosedOrthoLoopXZ(List<Vector3> verts, float eps)
    {
        int n = verts.Count;
        if (n < 3)
            return false;

        for (int i = 0; i < n; i++)
        {
            Vector3 a = verts[i];
            Vector3 b = verts[(i + 1) % n];
            float dx = Mathf.Abs(b.x - a.x);
            float dz = Mathf.Abs(b.z - a.z);
            bool axisX = dz <= eps && dx > eps;
            bool axisZ = dx <= eps && dz > eps;
            if (!axisX && !axisZ)
                return false;
        }

        return true;
    }

    /// <summary>Polygone simple XZ ; pair/impair (rayon horizontal vers +X).</summary>
    static bool PointInPolygonEvenOddXZ(List<Vector2> poly, Vector2 p)
    {
        int n = poly.Count;
        if (n < 3)
            return false;

        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];
            if ((pi.y > p.y) == (pj.y > p.y))
                continue;

            float xInt = (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 1e-30f) + pi.x;
            if (p.x < xInt)
                inside = !inside;
        }

        return inside;
    }

    // -------------------------------------------------------------------------
    // Fusion lots : conserver les murs intérieurs (arête partagée par 2 rectangles)
    // dans un contour fermé orthogonal — pic aller-retour sur une arête du périmètre.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Si une cloison tombe au milieu d’un bord du rectangle (même valeur que le milieu coin–coin),
    /// décale légèrement pour ne pas coïncider avec le milieu de mur injecté ensuite par
    /// <see cref="WallEditShape.EnforceOrthogonalRingSingleMidpointPerEdge"/>.
    /// </summary>
    static void NudgePartitionCoordsAwayFromRectangleMidAxis(
        List<float> coords,
        float minB,
        float maxB,
        float tol)
    {
        if (coords == null || coords.Count == 0)
            return;

        float span = maxB - minB;
        if (span <= tol * 4f)
            return;

        float mid = (minB + maxB) * 0.5f;
        float margin = Mathf.Max(tol * 2.5f, 0.004f);
        float nudge = Mathf.Max(0.006f, span * 1e-6f);

        for (int i = 0; i < coords.Count; i++)
        {
            float v = coords[i];
            if (Mathf.Abs(v - mid) > margin)
                continue;

            float vPlus = mid + nudge;
            float vMinus = mid - nudge;
            if (vPlus <= maxB - margin && vPlus >= minB + margin)
                coords[i] = vPlus;
            else if (vMinus >= minB + margin && vMinus <= maxB - margin)
                coords[i] = vMinus;
        }
    }

    struct AxisEdgeCount
    {
        public bool vertical;
        public float fixedCoord;
        public float spanMin;
        public float spanMax;

        public bool ApproxEquals(AxisEdgeCount o, float eps)
        {
            if (vertical != o.vertical)
                return false;
            if (Mathf.Abs(fixedCoord - o.fixedCoord) > eps)
                return false;
            return Mathf.Abs(spanMin - o.spanMin) <= eps && Mathf.Abs(spanMax - o.spanMax) <= eps;
        }
    }

    static void AddRectBoundaryEdgesForMergeCount(List<AxisEdgeCount> edges, List<int> counts, RectXZ r, float eps)
    {
        if (r.maxX - r.minX <= eps || r.maxZ - r.minZ <= eps)
            return;

        void TryAdd(AxisEdgeCount e)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].ApproxEquals(e, eps))
                {
                    counts[i]++;
                    return;
                }
            }

            edges.Add(e);
            counts.Add(1);
        }

        TryAdd(new AxisEdgeCount
        {
            vertical = true,
            fixedCoord = r.minX,
            spanMin = r.minZ,
            spanMax = r.maxZ
        });
        TryAdd(new AxisEdgeCount
        {
            vertical = true,
            fixedCoord = r.maxX,
            spanMin = r.minZ,
            spanMax = r.maxZ
        });
        TryAdd(new AxisEdgeCount
        {
            vertical = false,
            fixedCoord = r.minZ,
            spanMin = r.minX,
            spanMax = r.maxX
        });
        TryAdd(new AxisEdgeCount
        {
            vertical = false,
            fixedCoord = r.maxZ,
            spanMin = r.minX,
            spanMax = r.maxX
        });
    }

    static bool VerticalZRangesTouchOrOverlap(float aLo, float aHi, float bLo, float bHi, float eps)
    {
        return !(aHi < bLo - eps || bHi < aLo - eps);
    }

    static bool HorizontalXRangesTouchOrOverlap(float aLo, float aHi, float bLo, float bHi, float eps)
    {
        return !(aHi < bLo - eps || bHi < aLo - eps);
    }

    static void MergeVerticalEdgeSpans(List<float> xs, List<float> zLo, List<float> zHi, float eps)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < xs.Count && !changed; i++)
            {
                for (int j = i + 1; j < xs.Count && !changed; j++)
                {
                    if (Mathf.Abs(xs[i] - xs[j]) > eps)
                        continue;
                    if (!VerticalZRangesTouchOrOverlap(zLo[i], zHi[i], zLo[j], zHi[j], eps))
                        continue;
                    float nLo = Mathf.Min(zLo[i], zLo[j]);
                    float nHi = Mathf.Max(zHi[i], zHi[j]);
                    xs[i] = (xs[i] + xs[j]) * 0.5f;
                    zLo[i] = nLo;
                    zHi[i] = nHi;
                    xs.RemoveAt(j);
                    zLo.RemoveAt(j);
                    zHi.RemoveAt(j);
                    changed = true;
                }
            }
        }
    }

    static void MergeHorizontalEdgeSpans(List<float> zs, List<float> xLo, List<float> xHi, float eps)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < zs.Count && !changed; i++)
            {
                for (int j = i + 1; j < zs.Count && !changed; j++)
                {
                    if (Mathf.Abs(zs[i] - zs[j]) > eps)
                        continue;
                    if (!HorizontalXRangesTouchOrOverlap(xLo[i], xHi[i], xLo[j], xHi[j], eps))
                        continue;
                    float nLo = Mathf.Min(xLo[i], xLo[j]);
                    float nHi = Mathf.Max(xHi[i], xHi[j]);
                    zs[i] = (zs[i] + zs[j]) * 0.5f;
                    xLo[i] = nLo;
                    xHi[i] = nHi;
                    zs.RemoveAt(j);
                    xLo.RemoveAt(j);
                    xHi.RemoveAt(j);
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// Lorsque l’union de rectangles est un rectangle plein, les arêtes partagées par exactement
    /// deux rectangles (comptées sur le bord de chaque lot) sont des murs intérieurs — elles
    /// disparaissent du contour extérieur ; on les réinjecte comme aller-retour le long du périmètre.
    /// </summary>
    public static bool TryExpandFilledRectanglePathWithInternalPartitionSpikes(
        List<Vector3> mergedClosedRectPath,
        List<RectXZ> unionRects,
        float y,
        out List<Vector3> closedPathOut)
    {
        closedPathOut = null;
        if (mergedClosedRectPath == null || mergedClosedRectPath.Count < 4 || unionRects == null || unionRects.Count < 2)
            return false;

        float eps = Mathf.Max(WorldEdgeEps * 10f, 1e-4f);
        var edgeList = new List<AxisEdgeCount>(unionRects.Count * 4);
        var edgeCount = new List<int>(unionRects.Count * 4);
        for (int i = 0; i < unionRects.Count; i++)
            AddRectBoundaryEdgesForMergeCount(edgeList, edgeCount, unionRects[i], eps);

        var verticalX = new List<float>(8);
        var verticalZLo = new List<float>(8);
        var verticalZHi = new List<float>(8);
        var horizontalZ = new List<float>(8);
        var horizontalXLo = new List<float>(8);
        var horizontalXHi = new List<float>(8);

        for (int i = 0; i < edgeList.Count; i++)
        {
            if (edgeCount[i] != 2)
                continue;
            var e = edgeList[i];
            if (e.vertical)
            {
                verticalX.Add(e.fixedCoord);
                verticalZLo.Add(e.spanMin);
                verticalZHi.Add(e.spanMax);
            }
            else
            {
                horizontalZ.Add(e.fixedCoord);
                horizontalXLo.Add(e.spanMin);
                horizontalXHi.Add(e.spanMax);
            }
        }

        if (verticalX.Count == 0 && horizontalZ.Count == 0)
            return false;

        MergeVerticalEdgeSpans(verticalX, verticalZLo, verticalZHi, eps);
        MergeHorizontalEdgeSpans(horizontalZ, horizontalXLo, horizontalXHi, eps);

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        int nPts = mergedClosedRectPath.Count;
        int limit = nPts;
        if (nPts >= 2 &&
            Vector3.SqrMagnitude(mergedClosedRectPath[0] - mergedClosedRectPath[nPts - 1]) < 1e-6f)
            limit = nPts - 1;

        for (int i = 0; i < limit; i++)
        {
            Vector3 p = mergedClosedRectPath[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        if (float.IsInfinity(minX) || maxX - minX <= eps || maxZ - minZ <= eps)
            return false;

        float tol = Mathf.Max(eps, (maxX - minX + maxZ - minZ) * 1e-5f);

        var fullVertical = new List<float>();
        for (int i = 0; i < verticalX.Count; i++)
        {
            float x = verticalX[i];
            if (x <= minX + tol || x >= maxX - tol)
                continue;
            if (verticalZLo[i] > minZ + tol || verticalZHi[i] < maxZ - tol)
                continue;
            fullVertical.Add(x);
        }

        var fullHorizontal = new List<float>();
        for (int i = 0; i < horizontalZ.Count; i++)
        {
            float z = horizontalZ[i];
            if (z <= minZ + tol || z >= maxZ - tol)
                continue;
            if (horizontalXLo[i] > minX + tol || horizontalXHi[i] < maxX - tol)
                continue;
            fullHorizontal.Add(z);
        }

        if (fullVertical.Count == 0 && fullHorizontal.Count == 0)
            return false;

        // Grille 2×2 (ou plus) : murs horizontaux et verticaux — la chaîne de pics simple ne suffit pas.
        if (fullVertical.Count > 0 && fullHorizontal.Count > 0)
            return false;

        fullVertical.Sort();
        fullHorizontal.Sort();

        // Évite que le pic de cloison tombe exactement au milieu du haut / bas (ou gauche / droite) : sinon
        // EnforceOrthogonalRingSingleMidpointPerEdge ajoute un « milieu coin–coin » au même XZ → doublon et
        // décalage ligne de contrôle / maillage. Léger décalage invisible, sans toucher à l’édition courante.
        NudgePartitionCoordsAwayFromRectangleMidAxis(fullVertical, minX, maxX, tol);
        NudgePartitionCoordsAwayFromRectangleMidAxis(fullHorizontal, minZ, maxZ, tol);

        var open = new List<Vector3>(16 + fullVertical.Count * 3 + fullHorizontal.Count * 3);

        if (fullVertical.Count > 0)
        {
            open.Add(new Vector3(minX, y, maxZ));
            open.Add(new Vector3(minX, y, minZ));
            for (int i = 0; i < fullVertical.Count; i++)
            {
                float xi = fullVertical[i];
                open.Add(new Vector3(xi, y, minZ));
                open.Add(new Vector3(xi, y, maxZ));
                open.Add(new Vector3(xi, y, minZ));
            }

            open.Add(new Vector3(maxX, y, minZ));
            open.Add(new Vector3(maxX, y, maxZ));
        }
        else
        {
            open.Add(new Vector3(minX, y, maxZ));
            for (int i = fullHorizontal.Count - 1; i >= 0; i--)
            {
                float zi = fullHorizontal[i];
                open.Add(new Vector3(minX, y, zi));
                open.Add(new Vector3(maxX, y, zi));
                open.Add(new Vector3(minX, y, zi));
            }

            open.Add(new Vector3(minX, y, minZ));
            open.Add(new Vector3(maxX, y, minZ));
            open.Add(new Vector3(maxX, y, maxZ));
        }

        closedPathOut = new List<Vector3>(open.Count + 1);
        closedPathOut.AddRange(open);
        closedPathOut.Add(open[0]);
        return true;
    }
}
