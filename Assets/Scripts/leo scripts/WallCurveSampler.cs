using System.Collections.Generic;
using UnityEngine;

public static class WallCurveSampler
{
    // Chaikin smoothing (super simple, look TinyGlade-like)
    public static List<Vector3> Chaikin(List<Vector3> pts, int iterations, bool closed)
    {
        if (pts == null || pts.Count < 2) return pts == null ? new List<Vector3>() : new List<Vector3>(pts);

        var work = new List<Vector3>(pts);

        // si fermé: on travaille sans dupliquer le dernier point
        if (closed && Vector3.Distance(work[0], work[^1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        for (int it = 0; it < iterations; it++)
        {
            var res = new List<Vector3>(work.Count * 2);
            int n = work.Count;

            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = work[i];
                Vector3 b = work[i + 1];
                Vector3 q = Vector3.Lerp(a, b, 0.25f);
                Vector3 r = Vector3.Lerp(a, b, 0.75f);
                res.Add(q);
                res.Add(r);
            }

            if (closed)
            {
                Vector3 a = work[n - 1];
                Vector3 b = work[0];
                Vector3 q = Vector3.Lerp(a, b, 0.25f);
                Vector3 r = Vector3.Lerp(a, b, 0.75f);
                res.Add(q);
                res.Add(r);
            }
            else
            {
                // préserver les extrémités
                res.Insert(0, work[0]);
                res.Add(work[^1]);
            }

            work = res;
        }

        if (closed)
            work.Add(work[0]);

        return work;
    }

    // Resample à une résolution fixe (pour que le LineRenderer soit régulier)
    public static List<Vector3> ResampleByCount(List<Vector3> pts, int count)
    {
        if (pts == null || pts.Count == 0) return new List<Vector3>();
        if (count <= 2) return new List<Vector3> { pts[0], pts[^1] };
        if (pts.Count <= count) return new List<Vector3>(pts);

        var dist = new float[pts.Count];
        dist[0] = 0f;
        for (int i = 1; i < pts.Count; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);

        float total = dist[^1];
        if (total < 1e-6f) return new List<Vector3>(pts);

        var res = new List<Vector3>(count);
        for (int k = 0; k < count; k++)
        {
            float t = (k / (float)(count - 1)) * total;
            int i = 1;
            while (i < dist.Length && dist[i] < t) i++;
            i = Mathf.Clamp(i, 1, dist.Length - 1);
            float segT = Mathf.InverseLerp(dist[i - 1], dist[i], t);
            res.Add(Vector3.Lerp(pts[i - 1], pts[i], segT));
        }
        return res;
    }
}
