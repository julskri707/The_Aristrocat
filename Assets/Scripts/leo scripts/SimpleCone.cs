using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class SimpleCone : MonoBehaviour
{
    [Range(0.01f, 0.9f)]
    public float topScale = 0.05f; // 0.05 = pointu

    void Awake()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = Instantiate(mf.sharedMesh);
        mf.sharedMesh = mesh;

        Vector3[] v = mesh.vertices;

        // On ne touche PAS à la hauteur ici, seulement le haut (x/z)
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i].y > 0f) // vertices du haut
            {
                v[i].x *= topScale;
                v[i].z *= topScale;
            }
        }

        mesh.vertices = v;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
