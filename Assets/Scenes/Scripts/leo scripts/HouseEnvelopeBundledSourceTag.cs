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
}
