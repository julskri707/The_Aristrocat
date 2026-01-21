using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class FieldArea : MonoBehaviour
{
    [Header("Ground Projection")]
    public string groundTag = "Ground";
    public float raycastUp = 50f;
    public float raycastDown = 200f;

    [Header("Point Handling")]
    public bool autoSortPointsByAngle = true;

    [Header("Visual - Fill")]
    [Range(0f, 1f)] public float fillAlpha = 0.18f;
    public float yOffset = 0.03f;
    public Color fillNormal = Color.black;
    public Color fillSelected = Color.yellow;

    [Header("Visual - Outline")]
    public float lineWidth = 0.08f;
    public Color outlineNormal = Color.black;
    public Color outlineSelected = Color.yellow;

    [Header("Colliders")]
    [Tooltip("Clickable polygon collider. If triangulation fails, it may be empty.")]
    public bool addMeshCollider = true;

    [Tooltip("ALWAYS add a box trigger for reliable clicking (recommended).")]
    public bool alwaysAddSelectionBox = true;

    [Tooltip("Extra height for the selection box (so clicking still works on uneven ground).")]
    public float selectionBoxHeight = 2f;

    [Tooltip("Extra padding around the polygon for selection box.")]
    public float selectionBoxPadding = 0.2f;

    private readonly List<Vector3> _localPoints = new();

    private Mesh _mesh;
    private MeshFilter _mf;
    private MeshRenderer _mr;
    private MeshCollider _mc;
    private LineRenderer _lr;
    private BoxCollider _selectionBox;

    private bool _selected;

    private void Awake()
    {
        EnsureComponents();
        ApplySelected(false);
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplySelected(_selected);
    }

    public void SetPolygonWorldPoints(List<Vector3> worldPoints)
    {
        _localPoints.Clear();
        if (worldPoints == null || worldPoints.Count < 3) return;

        for (int i = 0; i < worldPoints.Count; i++)
            _localPoints.Add(transform.InverseTransformPoint(worldPoints[i]));

        CleanupPointsInPlace(_localPoints);

        if (autoSortPointsByAngle)
            SortByAngleAroundCenter(_localPoints);

        ConformToGroundAndRebuild();
    }

    public void ConformToGroundAndRebuild()
    {
        if (_localPoints.Count < 3) return;

        for (int i = 0; i < _localPoints.Count; i++)
        {
            Vector3 wp = transform.TransformPoint(_localPoints[i]);
            Vector3 origin = new Vector3(wp.x, wp.y + raycastUp, wp.z);

            if (Physics.Raycast(origin, Vector3.down, out var hit, raycastUp + raycastDown, ~0, QueryTriggerInteraction.Ignore))
            {
                if (HasTagInParents(hit.collider.transform, groundTag))
                {
                    Vector3 newWorld = new Vector3(wp.x, hit.point.y, wp.z);
                    _localPoints[i] = transform.InverseTransformPoint(newWorld);
                }
            }
        }

        bool meshOk = RebuildMeshFill();   // may fail on self-intersecting polygons
        RebuildOutline();

        // ✅ Always keep selection working
        RebuildSelectionBox();

        // If mesh triangulation failed, clear mesh collider so it doesn't do weird things
        if (!meshOk)
        {
            if (_mf != null) _mf.sharedMesh = null;
            if (_mc != null) _mc.sharedMesh = null;
        }

        ApplySelected(_selected);
    }

    // =========================
    // ✅ NEW: Area calculation
    // =========================

    /// <summary>
    /// Area in WORLD units^2 on the XZ plane, based on the polygon corners.
    /// This is what you want to scale income by.
    /// </summary>
    public float GetAreaWorldXZ()
    {
        if (_localPoints.Count < 3) return 0f;

        float sum = 0f;
        for (int i = 0; i < _localPoints.Count; i++)
        {
            int j = (i + 1) % _localPoints.Count;

            Vector3 wi = transform.TransformPoint(_localPoints[i]);
            Vector3 wj = transform.TransformPoint(_localPoints[j]);

            // Shoelace formula on XZ
            sum += (wi.x * wj.z) - (wj.x * wi.z);
        }

        return Mathf.Abs(sum) * 0.5f;
    }
    // ✅ Used by RuntimeBuildAndAssignController for precise selection
    public bool ContainsWorldPoint(Vector3 worldPoint)
    {
        if (_localPoints == null || _localPoints.Count < 3)
            return false;

        // Convert click point to LOCAL space
        Vector3 lp = transform.InverseTransformPoint(worldPoint);
        Vector2 p = new Vector2(lp.x, lp.z);

        bool inside = false;

        for (int i = 0, j = _localPoints.Count - 1; i < _localPoints.Count; j = i++)
        {
            Vector2 a = new Vector2(_localPoints[i].x, _localPoints[i].z);
            Vector2 b = new Vector2(_localPoints[j].x, _localPoints[j].z);

            bool intersect =
                ((a.y > p.y) != (b.y > p.y)) &&
                (p.x < (b.x - a.x) * (p.y - a.y) / Mathf.Max(0.000001f, (b.y - a.y)) + a.x);

            if (intersect)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Area in LOCAL units^2 on the XZ plane. Usually you want world.
    /// </summary>
    public float GetAreaLocalXZ()
    {
        if (_localPoints.Count < 3) return 0f;

        float sum = 0f;
        for (int i = 0; i < _localPoints.Count; i++)
        {
            int j = (i + 1) % _localPoints.Count;

            Vector3 a = _localPoints[i];
            Vector3 b = _localPoints[j];

            sum += (a.x * b.z) - (b.x * a.z);
        }

        return Mathf.Abs(sum) * 0.5f;
    }

    // ---------- Components ----------
    private void EnsureComponents()
    {
        _mf = GetComponent<MeshFilter>();
        if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();

        _mr = GetComponent<MeshRenderer>();
        if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();

        if (_mesh == null) _mesh = new Mesh { name = "FieldAreaMesh" };

        if (_mr.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            _mr.sharedMaterial = new Material(s);
        }

        if (addMeshCollider)
        {
            _mc = GetComponent<MeshCollider>();
            if (_mc == null) _mc = gameObject.AddComponent<MeshCollider>();
            _mc.convex = false;
            _mc.isTrigger = false; // mesh collider not needed for movement; selection box handles clicking
        }

        if (alwaysAddSelectionBox)
        {
            _selectionBox = GetComponent<BoxCollider>();
            if (_selectionBox == null) _selectionBox = gameObject.AddComponent<BoxCollider>();
            _selectionBox.isTrigger = true; // ✅ never blocks farmer movement
        }

        _lr = GetComponent<LineRenderer>();
        if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();

        _lr.useWorldSpace = true;
        _lr.loop = true;
        _lr.alignment = LineAlignment.View;
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;

        if (_lr.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _lr.sharedMaterial = new Material(s);
        }
    }

    private void ApplySelected(bool selected)
    {
        var baseFill = selected ? fillSelected : fillNormal;
        var fill = new Color(baseFill.r, baseFill.g, baseFill.b, fillAlpha);

        if (_mr != null && _mr.sharedMaterial != null)
            _mr.sharedMaterial.color = fill;

        var outC = selected ? outlineSelected : outlineNormal;
        if (_lr != null)
        {
            _lr.startColor = outC;
            _lr.endColor = outC;
            _lr.startWidth = lineWidth;
            _lr.endWidth = lineWidth;
        }
    }

    // ---------- Outline ----------
    private void RebuildOutline()
    {
        if (_lr == null || _localPoints.Count < 3) return;

        _lr.positionCount = _localPoints.Count;
        for (int i = 0; i < _localPoints.Count; i++)
        {
            Vector3 wp = transform.TransformPoint(_localPoints[i]);
            _lr.SetPosition(i, new Vector3(wp.x, wp.y + yOffset, wp.z));
        }
    }

    // ---------- Fill Mesh ----------
    private bool RebuildMeshFill()
    {
        if (_localPoints.Count < 3) return false;

        var poly2 = new List<Vector2>(_localPoints.Count);
        for (int i = 0; i < _localPoints.Count; i++)
            poly2.Add(new Vector2(_localPoints[i].x, _localPoints[i].z));

        if (SignedArea(poly2) < 0f)
        {
            _localPoints.Reverse();
            poly2.Reverse();
        }

        var indices = EarClipTriangulate(poly2);
        if (indices.Count < 3) return false;

        var verts = new Vector3[_localPoints.Count];
        for (int i = 0; i < _localPoints.Count; i++)
        {
            var p = _localPoints[i];
            verts[i] = new Vector3(p.x, p.y + yOffset, p.z);
        }

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.triangles = indices.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        _mf.sharedMesh = _mesh;

        if (_mc != null)
        {
            _mc.sharedMesh = null;
            _mc.sharedMesh = _mesh;
        }

        return true;
    }

    // ---------- Selection Box (ALWAYS works for clicking) ----------
    private void RebuildSelectionBox()
    {
        if (_selectionBox == null || _localPoints.Count < 3) return;

        // Bounds in LOCAL space from polygon points
        var b = new Bounds(_localPoints[0], Vector3.zero);
        for (int i = 1; i < _localPoints.Count; i++)
            b.Encapsulate(_localPoints[i]);

        // pad
        b.Expand(new Vector3(selectionBoxPadding * 2f, 0f, selectionBoxPadding * 2f));

        // Put the box centered horizontally, and tall vertically for easy clicking on terrain
        Vector3 center = b.center;
        center.y = 0f; // keep around field object origin
        _selectionBox.center = new Vector3(center.x, 0f, center.z);

        _selectionBox.size = new Vector3(
            Mathf.Max(0.1f, b.size.x),
            Mathf.Max(0.1f, selectionBoxHeight),
            Mathf.Max(0.1f, b.size.z)
        );
    }

    // ---------- Helpers ----------
    private static bool HasTagInParents(Transform t, string tag)
    {
        if (string.IsNullOrEmpty(tag)) return true;
        while (t != null)
        {
            if (t.CompareTag(tag)) return true;
            t = t.parent;
        }
        return false;
    }

    private static void CleanupPointsInPlace(List<Vector3> pts)
    {
        const float minDist = 0.05f;
        for (int i = pts.Count - 1; i > 0; i--)
            if (Vector3.Distance(pts[i], pts[i - 1]) < minDist)
                pts.RemoveAt(i);

        if (pts.Count >= 3 && Vector3.Distance(pts[0], pts[^1]) < minDist)
            pts.RemoveAt(pts.Count - 1);
    }

    private static void SortByAngleAroundCenter(List<Vector3> pts)
    {
        Vector3 c = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) c += pts[i];
        c /= pts.Count;

        pts.Sort((a, b) =>
        {
            float aa = Mathf.Atan2(a.z - c.z, a.x - c.x);
            float bb = Mathf.Atan2(b.z - c.z, b.x - c.x);
            return aa.CompareTo(bb);
        });
    }

    private static float SignedArea(List<Vector2> p)
    {
        float a = 0f;
        for (int i = 0; i < p.Count; i++)
        {
            int j = (i + 1) % p.Count;
            a += (p[i].x * p[j].y) - (p[j].x * p[i].y);
        }
        return a * 0.5f;
    }

    private static List<int> EarClipTriangulate(List<Vector2> poly)
    {
        var result = new List<int>();
        int n = poly.Count;
        if (n < 3) return result;

        var V = new List<int>(n);
        for (int i = 0; i < n; i++) V.Add(i);

        int guard = 0;
        while (V.Count > 3 && guard < 5000)
        {
            guard++;
            bool earFound = false;

            for (int i = 0; i < V.Count; i++)
            {
                int i0 = V[(i - 1 + V.Count) % V.Count];
                int i1 = V[i];
                int i2 = V[(i + 1) % V.Count];

                Vector2 a = poly[i0];
                Vector2 b = poly[i1];
                Vector2 c = poly[i2];

                if (!IsConvex(a, b, c)) continue;

                bool containsAny = false;
                for (int k = 0; k < V.Count; k++)
                {
                    int vi = V[k];
                    if (vi == i0 || vi == i1 || vi == i2) continue;
                    if (PointInTriangle(poly[vi], a, b, c)) { containsAny = true; break; }
                }
                if (containsAny) continue;

                result.Add(i0); result.Add(i1); result.Add(i2);
                V.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound) break;
        }

        if (V.Count == 3)
        {
            result.Add(V[0]); result.Add(V[1]); result.Add(V[2]);
        }

        return result;
    }

    private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - b) > 0f;
    private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var v0 = c - a;
        var v1 = b - a;
        var v2 = p - a;

        float dot00 = Vector2.Dot(v0, v0);
        float dot01 = Vector2.Dot(v0, v1);
        float dot02 = Vector2.Dot(v0, v2);
        float dot11 = Vector2.Dot(v1, v1);
        float dot12 = Vector2.Dot(v1, v2);

        float denom = dot00 * dot11 - dot01 * dot01;
        if (Mathf.Abs(denom) < 1e-8f) return false;

        float inv = 1f / denom;
        float u = (dot11 * dot02 - dot01 * dot12) * inv;
        float v = (dot00 * dot12 - dot01 * dot02) * inv;

        return (u >= 0) && (v >= 0) && (u + v <= 1);
    }
}
