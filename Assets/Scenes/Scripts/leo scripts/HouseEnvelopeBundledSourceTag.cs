using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sur un lot source rattaché à une enveloppe maison : référence l’enveloppe pour recalculer le mur extérieur
/// quand on édite ce lot indépendamment.
/// </summary>
[DisallowMultipleComponent]
public sealed class HouseEnvelopeBundledSourceTag : MonoBehaviour
{
    public WallObject envelopeWall;

    public static WallObject GetEnvelopeIfBundled(WallObject sourceWall)
    {
        if (sourceWall == null)
            return null;
        HouseEnvelopeBundledSourceTag tag = sourceWall.GetComponent<HouseEnvelopeBundledSourceTag>();
        return tag != null ? tag.envelopeWall : null;
    }

    /// <summary>
    /// Enveloppe associée à ce lot source : tag si présent, sinon résolution via
    /// <see cref="HouseExteriorEnvelopeSources"/> et réparation optionnelle du tag.
    /// </summary>
    public static WallObject ResolveEnvelopeForSourceLot(WallObject sourceWall, bool repairMissingTag = true)
    {
        if (sourceWall == null)
            return null;

        HouseEnvelopeBundledSourceTag tag = sourceWall.GetComponent<HouseEnvelopeBundledSourceTag>();
        if (tag != null && tag.envelopeWall != null)
            return tag.envelopeWall;

        if (HouseExteriorEnvelopeSources.TryFindEnvelopeWallForSourceLot(sourceWall, out WallObject env) && env != null)
        {
            if (repairMissingTag)
            {
                if (tag == null)
                    tag = sourceWall.gameObject.AddComponent<HouseEnvelopeBundledSourceTag>();
                tag.envelopeWall = env;
            }

            return env;
        }

        return null;
    }
}

/// <summary>
/// Masque le rendu des murs sources (pierres + sol) pendant que l’enveloppe affiche le contour fusionné.
/// </summary>
public static class HouseEnvelopeBundledSourceVisuals
{
    const string UpperBandBackingName = "__BundledUpperBandBacking";
    const float UpperBandStoneLiftMeters = 0.05f;
    const float UpperBandBackingInsetMeters = 0f;
    const float UpperBandBackingExtraDropMeters = 0.18f;
    const float AdjacencyGapMeters = 0.08f;

    public static void SetBundledSourceVisualsHidden(WallObject sourceWall, bool hide)
    {
        if (sourceWall == null)
            return;

        WallCladdingGenerator cg = sourceWall.GetComponent<WallCladdingGenerator>();
        if (cg != null)
            cg.ClearExteriorCladdingMinHeightFromWallBaseMeters();

        var rends = sourceWall.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].enabled = !hide;
        }

        var colliders = sourceWall.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = !hide;
        }

        WallCladdingGenerator cladding = sourceWall.GetComponent<WallCladdingGenerator>();
        if (cladding != null)
            cladding.enabled = !hide;

        HouseParquetFloor parquet = sourceWall.GetComponent<HouseParquetFloor>();
        if (parquet != null && hide)
            parquet.ClearFloor();

        SetUpperBandBackingVisible(sourceWall, false);
    }

    /// <summary>
    /// Le toit et les mailles pignon sous-toit restent sur les lots sources pour undo / split, mais ne doivent pas
    /// réapparaître quand on réactive les pierres en « bande haute » — sinon double faîtage à l’ancienne empreinte.
    /// </summary>
    public static void SuppressBundledSourceRoofAndGableRenderers(WallObject sourceWall)
    {
        if (sourceWall == null)
            return;

        void DisableUnderChildNamed(string childName)
        {
            Transform child = sourceWall.transform.Find(childName);
            if (child == null)
                return;
            var renderers = child.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }
        }

        DisableUnderChildNamed(HouseRoofSystem.RoofChildName);
        DisableUnderChildNamed(HouseGableWallSystem.GableRootChildName);
    }

    /// <summary>
    /// Lot source plus haut que le <b>seuil</b> commun (en m depuis la base du prisme) : n’active que le habillage
    /// extérieur au-dessus de ce seuil (l’enveloppe couvre le bas sur tout le pourtour) ; prisme de base + colliders
    /// du source restent masqués côté interaction.
    /// </summary>
    public static void ApplyTallerSourceUpperBandExteriorCladdingOnly(WallObject sourceWall, float commonShellMaxHeightMeters)
    {
        if (sourceWall == null)
            return;

        // Le lot peut sortir d'un état "hidden" (tous renderers désactivés).
        // Réactiver d'abord les renderers pour garantir que la bande haute pierre redevienne visible.
        var rends = sourceWall.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].enabled = true;
        }

        var colliders = sourceWall.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        // Le mur de base complet ne doit pas être visible en mode "upper band" :
        // sinon le mortier plein traverse les intersections entre lots.
        // Le rendu doit venir du cladding filtré par hauteur.
        MeshRenderer baseMr = sourceWall.GetComponent<MeshRenderer>();
        if (baseMr != null)
            baseMr.enabled = false;

        WallCladdingGenerator gen = sourceWall.GetComponent<WallCladdingGenerator>();
        // Réglage courant : pierres +5 cm.
        float upperBandStart = Mathf.Max(0f, commonShellMaxHeightMeters + UpperBandStoneLiftMeters);
        // Extension de mortier vers le bas (sans cailloux) pour supprimer le jour entre étages.
        float upperBandBackingStart = Mathf.Max(0f, commonShellMaxHeightMeters - UpperBandBackingExtraDropMeters);
        if (gen != null)
        {
            gen.SetExteriorCladdingMinHeightFromWallBaseMeters(upperBandStart);
            gen.enabled = true;
        }

        HouseParquetFloor parquet = sourceWall.GetComponent<HouseParquetFloor>();
        if (parquet != null)
            parquet.SetFloorRendererEnabled(true);

        // Connectivité "par étage" :
        // si ce lot haut est connecté à un autre lot haut (au-dessus du même niveau commun),
        // on supprime le backing interne pour éviter un mur d'intersection résiduel.
        bool connectedToAnotherUpperBandLot =
            HasAdjacentUpperBandSourceLotInEnvelope(sourceWall, commonShellMaxHeightMeters, AdjacencyGapMeters);

        // Sinon (lot haut isolé), garder un backing pour éviter les trous visuels.
        if (!connectedToAnotherUpperBandLot)
            EnsureUpperBandBackingMesh(sourceWall, upperBandBackingStart);
        else
            SetUpperBandBackingVisible(sourceWall, false);

        SuppressBundledSourceRoofAndGableRenderers(sourceWall);
    }

    static bool HasAdjacentUpperBandSourceLotInEnvelope(WallObject sourceWall, float commonShellMaxHeightMeters, float maxGap)
    {
        if (sourceWall == null)
            return false;
        if (sourceWall.height <= commonShellMaxHeightMeters + 0.01f)
            return false;

        WallObject envelope = HouseEnvelopeBundledSourceTag.ResolveEnvelopeForSourceLot(sourceWall, repairMissingTag: false);
        if (envelope == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelope.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || meta.SourceLotObjects == null || meta.SourceLotObjects.Count == 0)
            return false;

        if (!TryGetPathAabbXZ(sourceWall, out float aMinX, out float aMaxX, out float aMinZ, out float aMaxZ))
            return false;

        IReadOnlyList<GameObject> src = meta.SourceLotObjects;
        for (int i = 0; i < src.Count; i++)
        {
            GameObject go = src[i];
            if (go == null)
                continue;
            WallObject other = go.GetComponent<WallObject>();
            if (other == null || other == sourceWall)
                continue;
            if (other.height <= commonShellMaxHeightMeters + 0.01f)
                continue;

            if (!TryGetPathAabbXZ(other, out float bMinX, out float bMaxX, out float bMinZ, out float bMaxZ))
                continue;

            if (AreAabbsAdjacentOrOverlapping(aMinX, aMaxX, aMinZ, aMaxZ, bMinX, bMaxX, bMinZ, bMaxZ, maxGap))
                return true;
        }

        return false;
    }

    static bool TryGetPathAabbXZ(WallObject wall, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = minZ = float.PositiveInfinity;
        maxX = maxZ = float.NegativeInfinity;
        if (wall == null)
            return false;

        WallEditShape ed = wall.GetComponent<WallEditShape>();
        List<Vector3> path = ed != null ? ed.GetPreviewPathWorld() : null;
        if (path == null || path.Count < 2)
            return false;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        return maxX > minX && maxZ > minZ;
    }

    static bool AreAabbsAdjacentOrOverlapping(
        float aMinX, float aMaxX, float aMinZ, float aMaxZ,
        float bMinX, float bMaxX, float bMinZ, float bMaxZ,
        float gap)
    {
        float overlapX = Mathf.Min(aMaxX, bMaxX) - Mathf.Max(aMinX, bMinX);
        float overlapZ = Mathf.Min(aMaxZ, bMaxZ) - Mathf.Max(aMinZ, bMinZ);
        bool overlapArea = overlapX > 0f && overlapZ > 0f;
        if (overlapArea)
            return true;

        float zSpan = Mathf.Min(aMaxZ, bMaxZ) - Mathf.Max(aMinZ, bMinZ);
        if (zSpan > 0f)
        {
            if (Mathf.Abs(aMaxX - bMinX) <= gap || Mathf.Abs(bMaxX - aMinX) <= gap)
                return true;
        }

        float xSpan = Mathf.Min(aMaxX, bMaxX) - Mathf.Max(aMinX, bMinX);
        if (xSpan > 0f)
        {
            if (Mathf.Abs(aMaxZ - bMinZ) <= gap || Mathf.Abs(bMaxZ - aMinZ) <= gap)
                return true;
        }

        return false;
    }

    static void EnsureUpperBandBackingMesh(WallObject sourceWall, float minFromBaseMeters)
    {
        if (sourceWall == null)
            return;

        MeshFilter srcMf = sourceWall.GetComponent<MeshFilter>();
        MeshRenderer srcMr = sourceWall.GetComponent<MeshRenderer>();
        if (srcMf == null || srcMf.sharedMesh == null || srcMr == null || srcMr.sharedMaterial == null)
        {
            SetUpperBandBackingVisible(sourceWall, false);
            return;
        }

        float wallBaseY = sourceWall.transform.position.y;
        if (sourceWall.Points != null && sourceWall.Points.Count > 0)
            wallBaseY = sourceWall.Points[0].y;

        float clipY = wallBaseY + Mathf.Max(0f, minFromBaseMeters);
        if (clipY >= wallBaseY + sourceWall.height - 0.001f)
        {
            SetUpperBandBackingVisible(sourceWall, false);
            return;
        }

        Transform child = sourceWall.transform.Find(UpperBandBackingName);
        GameObject go;
        MeshFilter mf;
        MeshRenderer mr;
        if (child == null)
        {
            go = new GameObject(UpperBandBackingName);
            go.transform.SetParent(sourceWall.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = sourceWall.gameObject.layer;
            mf = go.AddComponent<MeshFilter>();
            mr = go.AddComponent<MeshRenderer>();
        }
        else
        {
            go = child.gameObject;
            mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
            mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
        }

        Mesh src = srcMf.sharedMesh;
        Vector3[] verts = src != null ? src.vertices : null;
        if (verts == null || verts.Length == 0)
        {
            SetUpperBandBackingVisible(sourceWall, false);
            return;
        }

        Vector3[] srcNormals = src.normals;
        bool hasNormals = srcNormals != null && srcNormals.Length == verts.Length;

        var clippedVerts = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            if (v.y < clipY)
                v.y = clipY;
            if (hasNormals)
                v -= srcNormals[i] * UpperBandBackingInsetMeters;
            clippedVerts[i] = v;
        }

        int[] srcTris = src.triangles;
        var keptTris = new List<int>(srcTris != null ? srcTris.Length : 0);
        if (srcTris != null)
        {
            const float yEps = 0.0005f;
            for (int t = 0; t + 2 < srcTris.Length; t += 3)
            {
                int i0 = srcTris[t];
                int i1 = srcTris[t + 1];
                int i2 = srcTris[t + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
                    continue;

                float y0 = verts[i0].y;
                float y1 = verts[i1].y;
                float y2 = verts[i2].y;

                // On retire seulement les triangles entièrement sous la coupe.
                // Les triangles qui croisent la coupe sont conservés et recoupés via clippedVerts.
                if (y0 < clipY - yEps && y1 < clipY - yEps && y2 < clipY - yEps)
                    continue;

                keptTris.Add(i0);
                keptTris.Add(i1);
                keptTris.Add(i2);
            }
        }

        if (keptTris.Count < 3)
        {
            SetUpperBandBackingVisible(sourceWall, false);
            return;
        }

        Mesh outMesh = mf.sharedMesh;
        if (outMesh == null)
        {
            outMesh = new Mesh();
            outMesh.name = "BundledUpperBandBackingMesh";
            mf.sharedMesh = outMesh;
        }
        else
            outMesh.Clear();

        var outVerts = new List<Vector3>(clippedVerts);
        var outTris = new List<int>(keptTris);
        var outUvs = new List<Vector2>(src.uv != null ? src.uv : new Vector2[clippedVerts.Length]);

        // Bouchon bas découpé comme le top réel : on copie les triangles horizontaux du top
        // puis on les redescend à clipY, pour conserver la même forme (bordures incluses).
        float topY = float.NegativeInfinity;
        for (int i = 0; i < verts.Length; i++)
            if (verts[i].y > topY) topY = verts[i].y;

        if (topY > float.NegativeInfinity)
        {
            const float topYEps = 0.01f;
            const float topNormalYEps = 0.75f;
            var remap = new Dictionary<int, int>();

            if (srcTris != null)
            {
                for (int t = 0; t + 2 < srcTris.Length; t += 3)
                {
                    int i0 = srcTris[t];
                    int i1 = srcTris[t + 1];
                    int i2 = srcTris[t + 2];
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
                        continue;

                    Vector3 v0 = verts[i0];
                    Vector3 v1 = verts[i1];
                    Vector3 v2 = verts[i2];
                    if (Mathf.Abs(v0.y - topY) > topYEps ||
                        Mathf.Abs(v1.y - topY) > topYEps ||
                        Mathf.Abs(v2.y - topY) > topYEps)
                        continue;

                    Vector3 nTri = Vector3.Cross(v1 - v0, v2 - v0);
                    if (nTri.sqrMagnitude < 1e-10f)
                        continue;
                    nTri.Normalize();
                    if (Mathf.Abs(nTri.y) < topNormalYEps)
                        continue;

                    int MapIdx(int srcIdx)
                    {
                        if (remap.TryGetValue(srcIdx, out int mapped))
                            return mapped;
                        Vector3 sv = verts[srcIdx];
                        sv.y = clipY;
                        int newIdx = outVerts.Count;
                        outVerts.Add(sv);
                        Vector2 uv = (src.uv != null && srcIdx < src.uv.Length) ? src.uv[srcIdx] : Vector2.zero;
                        outUvs.Add(uv);
                        remap[srcIdx] = newIdx;
                        return newIdx;
                    }

                    int c0 = MapIdx(i0);
                    int c1 = MapIdx(i1);
                    int c2 = MapIdx(i2);

                    // Double-face pour éviter les problèmes de culling.
                    outTris.Add(c0); outTris.Add(c1); outTris.Add(c2);
                    outTris.Add(c2); outTris.Add(c1); outTris.Add(c0);
                }
            }
        }

        outMesh.vertices = outVerts.ToArray();
        outMesh.triangles = outTris.ToArray();
        outMesh.uv = outUvs.ToArray();
        outMesh.RecalculateNormals();
        outMesh.RecalculateBounds();

        mr.sharedMaterial = srcMr.sharedMaterial;
        mr.shadowCastingMode = srcMr.shadowCastingMode;
        mr.receiveShadows = srcMr.receiveShadows;
        mr.enabled = true;
        go.SetActive(true);
    }

    static void SetUpperBandBackingVisible(WallObject sourceWall, bool visible)
    {
        if (sourceWall == null)
            return;

        Transform child = sourceWall.transform.Find(UpperBandBackingName);
        if (child == null)
            return;

        child.gameObject.SetActive(visible);
    }
}
