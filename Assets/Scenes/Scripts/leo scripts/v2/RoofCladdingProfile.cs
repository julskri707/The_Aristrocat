using UnityEngine;

/// <summary>
/// Profil de habillage pour le shell du toit : tuiles / bardeaux sur la submesh 0 du maillage <see cref="HouseRoofSystem"/>.
/// Peut réutiliser les matériaux d’un <see cref="WallCladdingProfile"/> comme pour les murs en pierre.
/// </summary>
[CreateAssetMenu(menuName = "TinyGlade/Roof/Cladding/Roof Cladding Profile", fileName = "RoofCladdingProfile")]
public sealed class RoofCladdingProfile : ScriptableObject
{
    [Header("Identity")]
    public string profileId = "roof_cladding_profile";
    public string displayName = "Roof Cladding";

    [Header("Materials")]
    [Tooltip("Prioritaire sur Tile Material : assigné tel quel au MeshRenderer du cladding (couleur / textures = celles du matériau, sans surcouche dans le code).")]
    public Material claddingMaterial;
    [Tooltip("Si Cladding Material est vide : ce Material est utilisé pour le cladding (même règle : assignation directe, pas de clone).")]
    public Material tileMaterial;

    [Tooltip("Réutilise stoneMaterial / fallback du mur (comme le rendu pierre).")]
    public WallCladdingProfile wallProfileForMaterials;

    [Header("Couleur tuiles (variation vertex si activée)")]
    [Tooltip("Utilisé seulement si Tile Material n’est pas assigné (mode fallback). Avec un Tile Material, la couleur du matériau / texture prime ; ne pas s’attendre à ce que ce champ teinte les tuiles.")]
    public Color baseTileColor = new Color(0.65f, 0.28f, 0.16f, 1f);

    [Header("Tuiles — grille par triangle")]
    [Min(0.08f)] public float tileWidthMeters = 0.42f;
    [Min(0.06f)] public float tileHeightMeters = 0.28f;
    [Min(0f)] public float tileOverlapMeters = 0.035f;

    [Tooltip("Saillie hors surface du shell (normale sortante).")]
    [Min(0.002f)] public float tileThicknessMeters = 0.022f;

    [Tooltip("Profondeur d'ancrage dans la surface cladée (2 cm par défaut), comme les pierres du mur.")]
    [Min(0f)] public float tileEmbedDepthMeters = 0.02f;

    [Tooltip("Évite z-fight avec le shell d’origine.")]
    [Min(0f)] public float normalSurfaceOffsetMeters = 0.003f;

    [Range(0f, 1f)] public float rowStaggerFraction = 0.5f;

    [Tooltip("Si désactivé : seule la face visible (quad ~2 tris), moins coûteux. Si activé : boîte fine (12 tris) pour bords visibles en coupe.")]
    public bool solidTileMesh = false;

    [Header("Variation")]
    [Range(0f, 0.12f)] public float uniformScaleJitter = 0.04f;
    [Range(0f, 0.2f)] public float hueJitter = 0.028f;
    [Range(0f, 0.2f)] public float saturationJitter = 0.065f;
    [Range(0f, 0.2f)] public float valueJitter = 0.10f;
    public bool enablePerTileVertexColor = true;

    [Header("Limites")]
    [Min(16)] public int maxGeneratedTiles = 4000;

    public Material ResolveTileMaterial()
    {
        if (claddingMaterial != null)
            return claddingMaterial;
        if (tileMaterial != null)
            return tileMaterial;
        if (wallProfileForMaterials == null)
            return null;
        if (wallProfileForMaterials.stoneMaterial != null)
            return wallProfileForMaterials.stoneMaterial;
        return wallProfileForMaterials.fallbackWallMaterial;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(profileId)) profileId = name;
        if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        tileWidthMeters = Mathf.Max(0.08f, tileWidthMeters);
        tileHeightMeters = Mathf.Max(0.06f, tileHeightMeters);
        tileOverlapMeters = Mathf.Max(0f, tileOverlapMeters);
        tileThicknessMeters = Mathf.Max(0.002f, tileThicknessMeters);
        tileEmbedDepthMeters = Mathf.Max(0f, tileEmbedDepthMeters);
        maxGeneratedTiles = Mathf.Max(16, maxGeneratedTiles);
    }
}
