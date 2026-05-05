using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class WallControlPointProvider : MonoBehaviour, IControlPointProvider, IControlPointWallShapeBinding
{
    public bool ControlPointsBelongToWallShape => true;

    [Header("Target")]
    public WallObject wall;

    [Header("Edit Handles")]
    public int maxEditHandles = 12;
    public bool closedLoop = true;

    [Header("Smoothing")]
    public int smoothIterations = 3;
    public int densePointCount = 96;

    private List<Vector3> _edit = new();

    void Awake()
    {
        if (wall == null) wall = GetComponent<WallObject>();
        PullFromWall();
    }

    // ---------- IControlPointProvider ----------
    public int ControlPointCount => _edit.Count;

    public Vector3 GetControlPointWorld(int index)
    {
        if (index < 0 || index >= _edit.Count) return Vector3.zero;
        return _edit[index];
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        if (index < 0 || index >= _edit.Count) return;

        worldPos.y = _edit[index].y;
        _edit[index] = worldPos;

        // rebuild un chemin dense (courbe)
        var dense = BuildDensePathFromEdit();

        // applique au mur
        if (wall != null)
            wall.SetPath(dense);

        // ⚠️ pas de wall.Rebuild() (car ta classe ne l’a pas)
        // on tente les méthodes existantes chez toi via reflection
        TryInvokeWallRebuild(wall);
    }

    public bool IsControlPointEditable(int index) => index >= 0 && index < _edit.Count;

    // ---------- Build ----------
    public void PullFromWall()
    {
        if (wall == null) { _edit.Clear(); return; }

        var raw = TryGetWallPointsReflection(wall);
        if (raw == null || raw.Count < 2)
        {
            _edit.Clear();
            return;
        }

        bool closed = closedLoop && raw.Count > 2 && Vector3.Distance(raw[0], raw[^1]) < 0.001f;
        if (closed) raw.RemoveAt(raw.Count - 1);

        _edit = BuildEditHandles(raw, maxEditHandles);

        if (closedLoop && _edit.Count > 2 && Vector3.Distance(_edit[0], _edit[^1]) > 0.001f)
            _edit.Add(_edit[0]);
    }

    List<Vector3> BuildEditHandles(List<Vector3> rawOpen, int maxHandles)
    {
        int want = Mathf.Clamp(maxHandles, 4, 32);
        if (rawOpen.Count <= want) return new List<Vector3>(rawOpen);

        var res = new List<Vector3>(want);
        for (int i = 0; i < want; i++)
        {
            float t = (i / (float)(want - 1));
            int idx = Mathf.RoundToInt(t * (rawOpen.Count - 1));
            res.Add(rawOpen[Mathf.Clamp(idx, 0, rawOpen.Count - 1)]);
        }
        return res;
    }

    List<Vector3> BuildDensePathFromEdit()
    {
        if (_edit == null || _edit.Count < 2) return new List<Vector3>(_edit);

        bool closed = closedLoop && Vector3.Distance(_edit[0], _edit[^1]) < 0.001f;

        var smooth = Chaikin(_edit, smoothIterations, closed);
        var dense = ResampleByCount(smooth, Mathf.Clamp(densePointCount, 32, 256));

        if (closed && Vector3.Distance(dense[0], dense[^1]) > 0.001f)
            dense.Add(dense[0]);

        return dense;
    }

    // ---------- Smoothing helpers ----------
    static List<Vector3> Chaikin(List<Vector3> pts, int iterations, bool closed)
    {
        if (pts == null || pts.Count < 2) return pts == null ? new List<Vector3>() : new List<Vector3>(pts);

        var work = new List<Vector3>(pts);
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
                res.Add(Vector3.Lerp(a, b, 0.25f));
                res.Add(Vector3.Lerp(a, b, 0.75f));
            }

            if (closed)
            {
                Vector3 a = work[n - 1];
                Vector3 b = work[0];
                res.Add(Vector3.Lerp(a, b, 0.25f));
                res.Add(Vector3.Lerp(a, b, 0.75f));
            }
            else
            {
                res.Insert(0, work[0]);
                res.Add(work[^1]);
            }

            work = res;
        }

        if (closed) work.Add(work[0]);
        return work;
    }

    static List<Vector3> ResampleByCount(List<Vector3> pts, int count)
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

    // ---------- Reflection (compat) ----------
    static List<Vector3> TryGetWallPointsReflection(WallObject w)
    {
        if (w == null) return new List<Vector3>();

        var t = w.GetType();

        string[] fieldNames = { "points", "Points", "path", "Path", "controlPoints", "ControlPoints", "worldPoints" };
        foreach (var n in fieldNames)
        {
            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && typeof(List<Vector3>).IsAssignableFrom(f.FieldType))
                return new List<Vector3>((List<Vector3>)f.GetValue(w));
        }

        string[] propNames = { "Points", "points", "Path", "path" };
        foreach (var n in propNames)
        {
            var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && typeof(List<Vector3>).IsAssignableFrom(p.PropertyType))
                return new List<Vector3>((List<Vector3>)p.GetValue(w));
        }

        return new List<Vector3>();
    }

    static void TryInvokeWallRebuild(WallObject w)
    {
        if (w == null) return;
        var t = w.GetType();

        // tente plusieurs noms possibles sans casser ton code
        string[] methods = { "Rebuild", "RebuildMesh", "RequestRebuild", "Build", "BuildMesh", "Regenerate" };
        foreach (var name in methods)
        {
            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null && m.GetParameters().Length == 0)
            {
                m.Invoke(w, null);
                return;
            }
        }
    }
}
