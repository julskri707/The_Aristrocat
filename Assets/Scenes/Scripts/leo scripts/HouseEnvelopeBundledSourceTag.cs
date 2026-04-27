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

        var colliders = sourceWall.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        MeshRenderer baseMr = sourceWall.GetComponent<MeshRenderer>();
        if (baseMr != null)
            baseMr.enabled = false;

        WallCladdingGenerator gen = sourceWall.GetComponent<WallCladdingGenerator>();
        if (gen != null)
        {
            gen.SetExteriorCladdingMinHeightFromWallBaseMeters(Mathf.Max(0f, commonShellMaxHeightMeters));
            gen.enabled = true;
        }

        HouseParquetFloor parquet = sourceWall.GetComponent<HouseParquetFloor>();
        if (parquet != null)
            parquet.SetFloorRendererEnabled(true);
    }
}
