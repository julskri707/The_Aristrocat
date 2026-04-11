using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public sealed partial class WallCladdingGenerator
{
    /// <summary>
    /// Piste C: CPU-side merge of all stone renderers under <paramref name="root"/> into one <see cref="Mesh"/>
    /// and one <see cref="MeshRenderer"/> (submesh per distinct <see cref="Material"/>).
    /// </summary>
    void TryCombineGeneratedStonesForRoot(Transform root)
    {
        if (!combineGeneratedStonesPerSide || root == null)
            return;

        Profiler.BeginSample("WallCladding.CombineMeshes");
        try
        {
        MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>(true);
        if (mfs == null || mfs.Length == 0)
            return;

        var groups = new Dictionary<Material, List<CombineInstance>>(32);
        Matrix4x4 w2l = root.worldToLocalMatrix;

        foreach (MeshFilter mf in mfs)
        {
            if (mf == null || mf.sharedMesh == null)
                continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null)
                continue;

            Mesh mesh = mf.sharedMesh;
            Material[] smats = mr.sharedMaterials;
            Matrix4x4 mtx = w2l * mf.transform.localToWorldMatrix;
            int subCount = mesh.subMeshCount;

            for (int si = 0; si < subCount; si++)
            {
                Material mat = (smats != null && si < smats.Length) ? smats[si] : mr.sharedMaterial;
                if (mat == null)
                    continue;

                var ci = new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = si,
                    transform = mtx
                };

                if (!groups.TryGetValue(mat, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>(64);
                    groups[mat] = list;
                }

                list.Add(ci);
            }
        }

        if (groups.Count == 0)
            return;

        var materialList = new List<Material>(groups.Keys);
        materialList.Sort(CompareMaterialsByName);

        var tempMeshes = new List<Mesh>(materialList.Count);
        try
        {
            var finalCombines = new List<CombineInstance>(materialList.Count);
            var orderedMaterials = new List<Material>(materialList.Count);
            foreach (Material mat in materialList)
            {
                List<CombineInstance> cis = groups[mat];
                if (cis == null || cis.Count == 0)
                    continue;

                var part = new Mesh { name = $"MergedPart_{mat.name}" };
                // Default meshes use UInt16 indices (max 65535 verts). Large walls exceed that per material batch.
                part.indexFormat = IndexFormat.UInt32;
                part.CombineMeshes(cis.ToArray(), mergeSubMeshes: true, useMatrices: true, hasLightmapData: false);
                tempMeshes.Add(part);
                orderedMaterials.Add(mat);

                finalCombines.Add(new CombineInstance
                {
                    mesh = part,
                    subMeshIndex = 0,
                    transform = Matrix4x4.identity
                });
            }

            if (finalCombines.Count == 0)
                return;

            var combined = new Mesh { name = "MergedWallCladding" };
            combined.indexFormat = IndexFormat.UInt32;
            combined.CombineMeshes(finalCombines.ToArray(), mergeSubMeshes: false, useMatrices: true, hasLightmapData: false);
            combined.RecalculateBounds();
            combined.RecalculateNormals();
            combined.RecalculateTangents();
            combined.OptimizeIndexBuffers();

            for (int i = root.childCount - 1; i >= 0; i--)
                DestroyObjectSafe(root.GetChild(i).gameObject);

            GameObject mergedGo = new GameObject("MergedWallCladding");
            mergedGo.transform.SetParent(root, false);
            mergedGo.transform.localPosition = Vector3.zero;
            mergedGo.transform.localRotation = Quaternion.identity;
            mergedGo.transform.localScale = Vector3.one;

            MeshFilter mfOut = mergedGo.AddComponent<MeshFilter>();
            MeshRenderer mrOut = mergedGo.AddComponent<MeshRenderer>();
            mfOut.sharedMesh = combined;

            // Per-stone tint is baked as vertex colors; stock URP Lit ignores COLOR — swap to TinyGlade/WallStoneVertexTintLit.
            Shader vertexTintShader = Resources.Load<Shader>("Shaders/WallStoneVertexTintLit");
            if (vertexTintShader == null)
                vertexTintShader = Shader.Find("TinyGlade/WallStoneVertexTintLit");

            if (vertexTintShader == null && ShouldLogCladdingDebug())
                Debug.LogWarning(
                    "[WallCladdingGenerator] Shader 'TinyGlade/WallStoneVertexTintLit' not found (expected Assets/Resources/Shaders/WallStoneVertexTintLit.shader). Merged wall skips per-stone vertex tint.",
                    root);

            Material[] mergedMats = new Material[orderedMaterials.Count];
            for (int i = 0; i < orderedMaterials.Count; i++)
            {
                Material src = orderedMaterials[i];
                mergedMats[i] = src != null ? new Material(src) : null;
                if (mergedMats[i] != null && vertexTintShader != null)
                    mergedMats[i].shader = vertexTintShader;
            }

            mrOut.sharedMaterials = mergedMats;

            if (ShouldLogCladdingDebug())
                Debug.Log($"[WallCladdingGenerator] Combined {mfs.Length} stone renderers under '{root.name}' → 1 mesh, {orderedMaterials.Count} submesh(es).", this);

            if (uploadGeneratedStoneMeshesToGpu && Application.isPlaying)
                combined.UploadMeshData(true);
        }
        finally
        {
            for (int i = 0; i < tempMeshes.Count; i++)
                DestroyObjectSafe(tempMeshes[i]);
        }
        }
        finally
        {
            Profiler.EndSample();
        }
    }

    static int CompareMaterialsByName(Material a, Material b)
    {
        string na = a != null ? a.name : "";
        string nb = b != null ? b.name : "";
        return string.CompareOrdinal(na, nb);
    }

    static void DestroyObjectSafe(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(obj);
        else
            Object.DestroyImmediate(obj);
    }
}
