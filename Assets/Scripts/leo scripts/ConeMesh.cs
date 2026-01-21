using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ConeMesh : MonoBehaviour
{
    [Header("Cone")]
    public float radius = 1f;
    public float height = 2f;
    [Range(6, 64)] public int sides = 20;
    public bool capBottom = true;

    void Awake()
    {
        Build();
    }

    public void Build()
    {
        Mesh mesh = new Mesh();
        mesh.name = "ConeMesh";

        int ringCount = sides;
        int vertCount = ringCount + 1 + (capBottom ? 1 : 0); 
        // ring + tip + (bottom center)

        Vector3[] v = new Vector3[vertCount];
        Vector2[] uv = new Vector2[vertCount];

        //  Base ring at y=0 (pivot au sol/base)
        for (int i = 0; i < ringCount; i++)
        {
            float t = (i / (float)ringCount) * Mathf.PI * 2f;
            float x = Mathf.Cos(t) * radius;
            float z = Mathf.Sin(t) * radius;
            v[i] = new Vector3(x, 0f, z);
            uv[i] = new Vector2(i / (float)ringCount, 0f);
        }

        int tipIndex = ringCount;
        v[tipIndex] = new Vector3(0f, height, 0f);
        uv[tipIndex] = new Vector2(0.5f, 1f);

        int bottomCenterIndex = -1;
        if (capBottom)
        {
            bottomCenterIndex = ringCount + 1;
            v[bottomCenterIndex] = Vector3.zero;
            uv[bottomCenterIndex] = new Vector2(0.5f, 0.5f);
        }

        // Triangles
        int sideTriCount = ringCount * 3;
        int capTriCount = capBottom ? ringCount * 3 : 0;
        int[] tris = new int[sideTriCount + capTriCount];

        int ti = 0;

        //  Sides
        for (int i = 0; i < ringCount; i++)
        {
            int next = (i + 1) % ringCount;
            tris[ti++] = i;
            tris[ti++] = tipIndex;
            tris[ti++] = next;
        }

        //  Bottom cap
        if (capBottom)
        {
            for (int i = 0; i < ringCount; i++)
            {
                int next = (i + 1) % ringCount;
                tris[ti++] = bottomCenterIndex;
                tris[ti++] = next;
                tris[ti++] = i;
            }
        }

        mesh.vertices = v;
        mesh.triangles = tris;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
