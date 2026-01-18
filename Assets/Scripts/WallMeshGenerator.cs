using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WallMeshGenerator : MonoBehaviour
{
    [Header("Wall Shape")]
    public float wallHeight = 2.5f;
    public float wallThickness = 0.4f;

    [Header("Corner Settings")]
    public float maxMiterScale = 4f; // limite les pics aux angles très serrés

    [Header("UV Tiling")]
    public float uvScale = 1.0f;

    Mesh mesh;

    void Awake()
    {
        mesh = new Mesh { name = "WallMesh" };
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    public void Generate(List<Vector3> pointsWorld)
    {
        if (pointsWorld == null || pointsWorld.Count < 4) return;

        // Convert to local space (évite les décalages)
        List<Vector3> pts = new List<Vector3>(pointsWorld.Count);
        for (int i = 0; i < pointsWorld.Count; i++)
        {
            Vector3 p = transform.InverseTransformPoint(pointsWorld[i]);
            p.y = 0f; // on force sur le plan XZ pour éviter des twists
            pts.Add(p);
        }

        // Assure fermé (dernier = premier)
        if ((pts[0] - pts[pts.Count - 1]).sqrMagnitude > 0.0001f)
            pts.Add(pts[0]);

        // Enlève dernier point pour travailler en boucle
        pts.RemoveAt(pts.Count - 1);

        if (pts.Count < 3) return;

        // Fix orientation (clockwise/counter) pour stabiliser "extérieur/intérieur"
        if (SignedAreaXZ(pts) < 0f)
            pts.Reverse();

        int n = pts.Count;

        // Pré-calc offset (miter) par vertex
        Vector3[] outBase = new Vector3[n];
        Vector3[] inBase  = new Vector3[n];
        Vector3[] outTop  = new Vector3[n];
        Vector3[] inTop   = new Vector3[n];

        float half = wallThickness * 0.5f;

        for (int i = 0; i < n; i++)
        {
            Vector3 pPrev = pts[(i - 1 + n) % n];
            Vector3 pCur  = pts[i];
            Vector3 pNext = pts[(i + 1) % n];

            Vector3 d0 = (pCur - pPrev); d0.y = 0f;
            Vector3 d1 = (pNext - pCur); d1.y = 0f;

            if (d0.sqrMagnitude < 0.00001f) d0 = (pCur - pts[(i - 2 + n) % n]);
            if (d1.sqrMagnitude < 0.00001f) d1 = (pts[(i + 2) % n] - pCur);

            d0.Normalize();
            d1.Normalize();

            // Normales "à droite" sur XZ
            Vector3 n0 = new Vector3(d0.z, 0f, -d0.x);
            Vector3 n1 = new Vector3(d1.z, 0f, -d1.x);

            // Miter = somme des normales
            Vector3 m = (n0 + n1);
            if (m.sqrMagnitude < 0.00001f)
            {
                // Angle ~ 180°, on prend une normale simple
                m = n1;
            }
            m.Normalize();

            // scale pour garder l'épaisseur correcte au coin
            float denom = Vector3.Dot(m, n1);
            float scale = (Mathf.Abs(denom) < 0.0001f) ? maxMiterScale : (1f / denom);

            // clamp anti pics
            scale = Mathf.Clamp(scale, -maxMiterScale, maxMiterScale);

            Vector3 offset = m * (half * scale);

            // Convention : "out" = +offset, "in" = -offset (stable grâce à l'orientation)
            outBase[i] = pCur + offset;
            inBase[i]  = pCur - offset;

            outTop[i] = outBase[i] + Vector3.up * wallHeight;
            inTop[i]  = inBase[i]  + Vector3.up * wallHeight;
        }

        // Build mesh
        List<Vector3> verts = new List<Vector3>(n * 4);
        List<int> tris = new List<int>(n * 24);
        List<Vector2> uvs = new List<Vector2>(n * 4);

        // Vert layout per point: 0 outBase, 1 outTop, 2 inBase, 3 inTop
        float u = 0f;
        for (int i = 0; i < n; i++)
        {
            verts.Add(outBase[i]);
            verts.Add(outTop[i]);
            verts.Add(inBase[i]);
            verts.Add(inTop[i]);

            // UV simple: U = longueur cumulée approx
            int iNext = (i + 1) % n;
            float seg = Vector3.Distance(pts[i], pts[iNext]);
            float u0 = u / uvScale;
            float u1 = (u + seg) / uvScale;

            // On met u0 sur ce point, et ça interpolera vers u1 sur le segment
            uvs.Add(new Vector2(u0, 0));
            uvs.Add(new Vector2(u0, 1));
            uvs.Add(new Vector2(u0, 0));
            uvs.Add(new Vector2(u0, 1));

            u += seg;
        }

        for (int i = 0; i < n; i++)
        {
            int iNext = (i + 1) % n;

            int v0 = i * 4;
            int v1 = iNext * 4;

            // OUTSIDE quad: outBase/outTop
            AddQuad(tris, v0 + 0, v0 + 1, v1 + 1, v1 + 0);

            // INSIDE quad: inBase/inTop (sens inversé)
            AddQuad(tris, v1 + 2, v1 + 3, v0 + 3, v0 + 2);

            // TOP quad: outTop -> inTop
            AddQuad(tris, v0 + 1, v0 + 3, v1 + 3, v1 + 1);

            // BOTTOM quad: inBase -> outBase
            AddQuad(tris, v0 + 2, v0 + 0, v1 + 0, v1 + 2);
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    static void AddQuad(List<int> t, int a, int b, int c, int d)
    {
        // (a,b,c) (a,c,d)
        t.Add(a); t.Add(b); t.Add(c);
        t.Add(a); t.Add(c); t.Add(d);
    }

    static float SignedAreaXZ(List<Vector3> p)
    {
        // aire signée en XZ (positive = clockwise selon convention ici)
        float area = 0f;
        for (int i = 0; i < p.Count; i++)
        {
            Vector3 a = p[i];
            Vector3 b = p[(i + 1) % p.Count];
            area += (a.x * b.z - b.x * a.z);
        }
        return area * 0.5f;
    }
}
