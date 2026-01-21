using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RoofMeshGenerator : MonoBehaviour
{
    [Header("Roof")]
    public float roofHeight = 2.0f;      // hauteur du toit
    public float roofYOffset = 2.5f;     // base du toit (souvent = wallHeight)
    public float uvScale = 2.0f;

    [Header("Underside (optional)")]
    public bool buildUndersideCap = true;
    public float undersideOffset = 0.02f; // descend un poil pour éviter z-fighting

    Mesh mesh;

    void Awake()
    {
        mesh = new Mesh { name = "RoofMesh" };
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    public void Generate(List<Vector3> pointsWorld)
    {
        if (pointsWorld == null || pointsWorld.Count < 4) return;

        // Convert to local + plan XZ
        List<Vector3> pts = new List<Vector3>(pointsWorld.Count);
        for (int i = 0; i < pointsWorld.Count; i++)
        {
            Vector3 p = transform.InverseTransformPoint(pointsWorld[i]);
            p.y = 0f;
            pts.Add(p);
        }

        bool closed = (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 0.0001f;
        int count = closed ? pts.Count - 1 : pts.Count;
        if (count < 3) return;

        List<Vector3> poly = new List<Vector3>(count);
        for (int i = 0; i < count; i++) poly.Add(pts[i]);

        // Triangulation requiert un polygone non auto-croisé (chez toi c'est déjà filtré)
        // Orientation stable
        if (SignedAreaXZ(poly) < 0f) poly.Reverse();

        List<int> tri = Triangulate(poly);
        if (tri == null || tri.Count < 3) return;

        // Centre du toit (moyenne) + hauteur
        Vector3 center = ComputeCentroid(poly);
        center.y = roofYOffset + roofHeight;

        // Vertices : contour à roofYOffset + 1 centre
        List<Vector3> verts = new List<Vector3>(poly.Count + 1);
        List<Vector2> uvs = new List<Vector2>(poly.Count + 1);
        List<int> tris = new List<int>();

        for (int i = 0; i < poly.Count; i++)
        {
            Vector3 v = poly[i];
            v.y = roofYOffset;
            verts.Add(v);
            uvs.Add(new Vector2(v.x / uvScale, v.z / uvScale));
        }

        int centerIndex = verts.Count;
        verts.Add(center);
        uvs.Add(new Vector2(center.x / uvScale, center.z / uvScale));

        // ✅ Roof sides ONLY (plus stable, pas de "plancher" qui glitch)
        for (int i = 0; i < poly.Count; i++)
        {
            int next = (i + 1) % poly.Count;

            // Sens choisi pour être "face vers l'extérieur"
            tris.Add(i);
            tris.Add(centerIndex);
            tris.Add(next);
        }

        // ✅ Optionnel : cap dessous (ferme l’intérieur, sans z-fighting)
        if (buildUndersideCap)
        {
            int baseOffset = verts.Count;

            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 v = poly[i];
                v.y = roofYOffset - undersideOffset;
                verts.Add(v);
                uvs.Add(new Vector2(v.x / uvScale, v.z / uvScale));
            }

            // Cap dessous = triangulation inversée
            for (int i = 0; i < tri.Count; i += 3)
            {
                int a = tri[i] + baseOffset;
                int b = tri[i + 1] + baseOffset;
                int c = tri[i + 2] + baseOffset;

                // inversé pour que la face soit visible depuis le dessous
                tris.Add(a);
                tris.Add(c);
                tris.Add(b);
            }
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // ---------- Helpers ----------
    static Vector3 ComputeCentroid(List<Vector3> poly)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < poly.Count; i++) sum += poly[i];
        return sum / poly.Count;
    }

    static float SignedAreaXZ(List<Vector3> p)
    {
        float area = 0f;
        for (int i = 0; i < p.Count; i++)
        {
            Vector3 a = p[i];
            Vector3 b = p[(i + 1) % p.Count];
            area += (a.x * b.z - b.x * a.z);
        }
        return area * 0.5f;
    }

    // Ear clipping triangulation
    static List<int> Triangulate(List<Vector3> poly)
    {
        int n = poly.Count;
        if (n < 3) return null;

        List<int> indices = new List<int>(n);
        for (int i = 0; i < n; i++) indices.Add(i);

        List<int> result = new List<int>();
        int guard = 0;

        while (indices.Count > 3 && guard < 5000)
        {
            guard++;
            bool earFound = false;

            for (int i = 0; i < indices.Count; i++)
            {
                int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                int i1 = indices[i];
                int i2 = indices[(i + 1) % indices.Count];

                Vector2 a = new Vector2(poly[i0].x, poly[i0].z);
                Vector2 b = new Vector2(poly[i1].x, poly[i1].z);
                Vector2 c = new Vector2(poly[i2].x, poly[i2].z);

                if (!IsConvex(a, b, c)) continue;

                bool anyInside = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int pIndex = indices[j];
                    if (pIndex == i0 || pIndex == i1 || pIndex == i2) continue;

                    Vector2 p = new Vector2(poly[pIndex].x, poly[pIndex].z);
                    if (PointInTriangle(p, a, b, c))
                    {
                        anyInside = true;
                        break;
                    }
                }

                if (anyInside) continue;

                result.Add(i0);
                result.Add(i1);
                result.Add(i2);

                indices.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound) break;
        }

        if (indices.Count == 3)
        {
            result.Add(indices[0]);
            result.Add(indices[1]);
            result.Add(indices[2]);
        }

        return result;
    }

    static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
    {
        float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        return cross > 0f;
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(hasNeg && hasPos);
    }

    static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
