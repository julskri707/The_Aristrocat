using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Helpers for batched GPU instancing of identical stone meshes (same Mesh + Material).
/// The current <see cref="WallCladdingGenerator"/> builds unique meshes per stone; migrating to GPU
/// instancing requires grouping stones into shared meshes first — see README in this folder.
/// </summary>
public static class GpuStoneInstancingUtility
{
    public const int MaxInstancesPerDrawCall = 1023;

    /// <summary>
    /// Draws instances from <paramref name="matrices"/>[<paramref name="startIndex"/> .. <paramref name="startIndex"/> + <paramref name="count"/>).
    /// Reuses <paramref name="chunkScratch"/> (resize if needed) to satisfy Unity’s API (contiguous array prefix).
    /// </summary>
    public static void DrawMeshInstancedRange(
        Mesh mesh,
        int submeshIndex,
        Material material,
        Matrix4x4[] matrices,
        int startIndex,
        int count,
        ref Matrix4x4[] chunkScratch,
        MaterialPropertyBlock propertyBlock = null,
        ShadowCastingMode castShadows = ShadowCastingMode.On,
        bool receiveShadows = true,
        int layer = 0,
        Camera camera = null,
        LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes)
    {
        if (mesh == null || material == null || matrices == null || count <= 0)
            return;

        if (startIndex < 0 || startIndex + count > matrices.Length)
            return;

        int remaining = count;
        int src = startIndex;
        while (remaining > 0)
        {
            int n = Mathf.Min(MaxInstancesPerDrawCall, remaining);
            if (chunkScratch == null || chunkScratch.Length < n)
                chunkScratch = new Matrix4x4[MaxInstancesPerDrawCall];

            System.Array.Copy(matrices, src, chunkScratch, 0, n);
            Graphics.DrawMeshInstanced(
                mesh,
                submeshIndex,
                material,
                chunkScratch,
                n,
                propertyBlock,
                castShadows,
                receiveShadows,
                layer,
                camera,
                lightProbeUsage);

            src += n;
            remaining -= n;
        }
    }

    /// <summary>
    /// Draws all <paramref name="totalCount"/> entries at the start of <paramref name="matrices"/>.
    /// </summary>
    public static void DrawMeshInstancedAll(
        Mesh mesh,
        int submeshIndex,
        Material material,
        Matrix4x4[] matrices,
        int totalCount,
        ref Matrix4x4[] chunkScratch,
        MaterialPropertyBlock propertyBlock = null,
        ShadowCastingMode castShadows = ShadowCastingMode.On,
        bool receiveShadows = true,
        int layer = 0,
        Camera camera = null,
        LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes)
    {
        if (matrices == null || totalCount <= 0)
            return;

        totalCount = Mathf.Min(totalCount, matrices.Length);
        DrawMeshInstancedRange(
            mesh,
            submeshIndex,
            material,
            matrices,
            0,
            totalCount,
            ref chunkScratch,
            propertyBlock,
            castShadows,
            receiveShadows,
            layer,
            camera,
            lightProbeUsage);
    }
}
