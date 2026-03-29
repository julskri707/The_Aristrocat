using System;
using System.Collections.Generic;
using UnityEngine;

public enum WallCladdingMode
{
    StonePacked = 0,
    BrickPacked = 1,
}

[Serializable]
public sealed class WallCladdingGeneralSettings
{
    [Min(0f)] public float sideInset = 0.01f;
    [Min(-0.20f)] public float depthOffset = 0f;
    [Min(0f)] public float randomSeedOffset = 0f;
}

[Serializable]
public sealed class StoneCladdingSettings
{
    [Header("Rows")]
    [Min(0.08f)] public float targetRowHeight = 0.22f;
    [Range(0f, 0.30f)] public float rowHeightJitter = 0.08f;
    [Min(0f)] public float horizontalSpacing = 0.008f;
    [Min(0f)] public float verticalSpacing = 0.008f;
    [Range(0f, 0.6f)] public float staggerFraction = 0.30f;

    [Header("Stone Size")]
    [Min(0.05f)] public float minStoneWidth = 0.12f;
    [Min(0.08f)] public float maxStoneWidth = 0.80f;
    [Min(0.05f)] public float minStoneHeight = 0.10f;
    [Min(0.08f)] public float maxStoneHeight = 0.36f;

    [Header("Desired Width Ratio")]
    [Min(0.5f)] public float minWidthVsHeight = 1.10f;
    [Min(0.5f)] public float maxWidthVsHeight = 2.20f;
    [Min(0.5f)] public float nearCornerMaxWidthVsHeight = 1.45f;

    [Header("Depth")]
    [Min(0f)] public float embedDepth = 0.04f;
    [Min(0f)] public float surfaceProtrusion = 0.035f;
    [Min(0.01f)] public float minStoneDepth = 0.09f;
    [Min(0.01f)] public float maxStoneDepth = 0.13f;

    [Header("Adaptive Scaling")]
    [Range(0f, 0.25f)] public float widthJitter = 0.08f;
    [Range(0f, 0.18f)] public float heightJitter = 0.04f;
    [Range(0f, 0.18f)] public float depthJitter = 0.03f;
    [Range(0f, 0.18f)] public float scaleJitter = 0.02f;
    [Min(0.5f)] public float minWidthScale = 0.75f;
    [Min(0.5f)] public float maxWidthScale = 1.35f;
    [Min(0.5f)] public float minHeightScale = 0.88f;
    [Min(0.5f)] public float maxHeightScale = 1.08f;
    [Min(0.5f)] public float minDepthScale = 0.92f;
    [Min(0.5f)] public float maxDepthScale = 1.08f;
    [Min(1f)] public float maxScaleAspectRatio = 1.30f;

    [Header("Placement")]
    [Min(0f)] public float positionJitter = 0.002f;
    [Range(0f, 8f)] public float randomYaw = 1f;
    [Range(0f, 8f)] public float randomPitch = 1f;
    [Range(0f, 12f)] public float randomRoll = 4f;

    [Header("Packing")]
    [Range(0f, 1f)] public float smallStoneFillChance = 0.18f;
    public bool preferSmallModulesNearCorners = true;
    [Min(0.05f)] public float cornerSmallModuleZone = 0.18f;
    [Min(0.02f)] public float minRowUsableWidth = 0.12f;
    [Min(0f)] public float endGapTolerance = 0.04f;
    [Min(0f)] public float rejectSliverGapBelow = 0.06f;

    [Header("Shape")]
    [Range(0f, 0.12f)] public float facePlaneJitter = 0.015f;
    [Min(0.05f)] public float uvMetersPerUnit = 0.50f;

    [Header("Per Stone Visual Variation")]
    public bool enablePerStoneColorVariation = true;
    [Range(0f, 0.10f)] public float hueJitter = 0.015f;
    [Range(0f, 0.35f)] public float saturationJitter = 0.08f;
    [Range(0f, 0.35f)] public float valueJitter = 0.12f;
    [Min(0f)] public float uvOffsetJitter = 0.35f;
    public Color baseTint = Color.white;

    [Header("End Quoins")]
    public EndQuoinSettings endQuoins = new EndQuoinSettings();
}


[Serializable]
public sealed class EndQuoinSettings
{
    public bool enabled = true;
    [Min(0.12f)] public float reserveWidth = 0.34f;
    [Min(0.12f)] public float targetHeight = 0.32f;
    [Range(0f, 0.25f)] public float rowHeightJitter = 0.08f;
    [Min(0.12f)] public float minLength = 0.24f;
    [Min(0.12f)] public float maxLength = 0.48f;
    [Range(0f, 0.25f)] public float lengthJitter = 0.08f;
    [Min(0f)] public float extraOutsideDepth = 0.04f;
    [Range(0.4f, 1f)] public float alternateShortScale = 0.74f;
    [Range(1f, 2f)] public float alternateLongScale = 1.18f;
    [Min(0f)] public float edgeInset = 0f;
    [Min(0f)] public float verticalSpacing = 0.01f;

    [Tooltip("90° rectangle corners: extra multiplier on through-wall depth so the L-quoin reads on both wall faces.")]
    [Min(1f)] public float cornerLDepthMul = 1.14f;
}

[Serializable]
public sealed class BrickCladdingSettings
{
    [Min(0.05f)] public float brickWidth = 0.32f;
}

[CreateAssetMenu(menuName = "TinyGlade/Walls/Cladding/Wall Cladding Profile", fileName = "WallCladdingProfile")]
public sealed class WallCladdingProfile : ScriptableObject
{
    [Header("Identity")]
    public string profileId = "wall_cladding_profile";
    public string displayName = "Wall Cladding Profile";
    public Sprite icon;
    public WallCladdingMode mode = WallCladdingMode.StonePacked;

    [Header("Materials")]
    public Material fallbackWallMaterial;
    public Material stoneMaterial;

    [Header("General")]
    public WallCladdingGeneralSettings general = new WallCladdingGeneralSettings();

    [Header("Stone")]
    public StoneCladdingSettings stone = new StoneCladdingSettings();
    public List<WallStoneModuleDefinition> stoneLargeModules = new List<WallStoneModuleDefinition>();
    public List<WallStoneModuleDefinition> stoneMediumModules = new List<WallStoneModuleDefinition>();
    public List<WallStoneModuleDefinition> stoneSmallModules = new List<WallStoneModuleDefinition>();

    [Header("Brick (later)")]
    public BrickCladdingSettings brick = new BrickCladdingSettings();

    public bool UsesStoneMode => mode == WallCladdingMode.StonePacked;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(profileId)) profileId = name;
        if (string.IsNullOrWhiteSpace(displayName)) displayName = name;

        general ??= new WallCladdingGeneralSettings();
        stone ??= new StoneCladdingSettings();
        stone.endQuoins ??= new EndQuoinSettings();
        brick ??= new BrickCladdingSettings();

        general.sideInset = Mathf.Max(0f, general.sideInset);
        stone.targetRowHeight = Mathf.Max(0.08f, stone.targetRowHeight);
        stone.rowHeightJitter = Mathf.Clamp(stone.rowHeightJitter, 0f, 0.30f);
        stone.horizontalSpacing = Mathf.Max(0f, stone.horizontalSpacing);
        stone.verticalSpacing = Mathf.Max(0f, stone.verticalSpacing);
        stone.staggerFraction = Mathf.Clamp(stone.staggerFraction, 0f, 0.6f);

        stone.minStoneWidth = Mathf.Max(0.05f, stone.minStoneWidth);
        stone.maxStoneWidth = Mathf.Max(stone.minStoneWidth, stone.maxStoneWidth);
        stone.minStoneHeight = Mathf.Max(0.05f, stone.minStoneHeight);
        stone.maxStoneHeight = Mathf.Max(stone.minStoneHeight, stone.maxStoneHeight);

        stone.minWidthVsHeight = Mathf.Max(0.5f, stone.minWidthVsHeight);
        stone.maxWidthVsHeight = Mathf.Max(stone.minWidthVsHeight, stone.maxWidthVsHeight);
        stone.nearCornerMaxWidthVsHeight = Mathf.Clamp(stone.nearCornerMaxWidthVsHeight, 0.5f, stone.maxWidthVsHeight);

        stone.embedDepth = Mathf.Max(0f, stone.embedDepth);
        stone.surfaceProtrusion = Mathf.Max(0f, stone.surfaceProtrusion);
        stone.minStoneDepth = Mathf.Max(0.01f, stone.minStoneDepth);
        stone.maxStoneDepth = Mathf.Max(stone.minStoneDepth, stone.maxStoneDepth);

        stone.widthJitter = Mathf.Clamp(stone.widthJitter, 0f, 0.25f);
        stone.heightJitter = Mathf.Clamp(stone.heightJitter, 0f, 0.18f);
        stone.depthJitter = Mathf.Clamp(stone.depthJitter, 0f, 0.18f);
        stone.scaleJitter = Mathf.Clamp(stone.scaleJitter, 0f, 0.18f);
        stone.minWidthScale = Mathf.Max(0.5f, stone.minWidthScale);
        stone.maxWidthScale = Mathf.Max(stone.minWidthScale, stone.maxWidthScale);
        stone.minHeightScale = Mathf.Max(0.5f, stone.minHeightScale);
        stone.maxHeightScale = Mathf.Max(stone.minHeightScale, stone.maxHeightScale);
        stone.minDepthScale = Mathf.Max(0.5f, stone.minDepthScale);
        stone.maxDepthScale = Mathf.Max(stone.minDepthScale, stone.maxDepthScale);
        stone.maxScaleAspectRatio = Mathf.Max(1f, stone.maxScaleAspectRatio);

        stone.positionJitter = Mathf.Max(0f, stone.positionJitter);
        stone.randomYaw = Mathf.Max(0f, stone.randomYaw);
        stone.randomPitch = Mathf.Max(0f, stone.randomPitch);
        stone.randomRoll = Mathf.Max(0f, stone.randomRoll);

        stone.smallStoneFillChance = Mathf.Clamp01(stone.smallStoneFillChance);
        stone.cornerSmallModuleZone = Mathf.Max(0.05f, stone.cornerSmallModuleZone);
        stone.minRowUsableWidth = Mathf.Max(0.02f, stone.minRowUsableWidth);
        stone.endGapTolerance = Mathf.Max(0f, stone.endGapTolerance);
        stone.rejectSliverGapBelow = Mathf.Max(0f, stone.rejectSliverGapBelow);
        stone.facePlaneJitter = Mathf.Clamp(stone.facePlaneJitter, 0f, 0.12f);
        stone.uvMetersPerUnit = Mathf.Max(0.05f, stone.uvMetersPerUnit);

        stone.endQuoins.enabled = stone.endQuoins.enabled;
        stone.endQuoins.reserveWidth = Mathf.Max(0.12f, stone.endQuoins.reserveWidth);
        stone.endQuoins.targetHeight = Mathf.Max(0.12f, stone.endQuoins.targetHeight);
        stone.endQuoins.rowHeightJitter = Mathf.Clamp(stone.endQuoins.rowHeightJitter, 0f, 0.25f);
        stone.endQuoins.minLength = Mathf.Max(0.12f, stone.endQuoins.minLength);
        stone.endQuoins.maxLength = Mathf.Max(stone.endQuoins.minLength, stone.endQuoins.maxLength);
        stone.endQuoins.lengthJitter = Mathf.Clamp(stone.endQuoins.lengthJitter, 0f, 0.25f);
        stone.endQuoins.extraOutsideDepth = Mathf.Max(0f, stone.endQuoins.extraOutsideDepth);
        stone.endQuoins.alternateShortScale = Mathf.Clamp(stone.endQuoins.alternateShortScale, 0.4f, 1f);
        stone.endQuoins.alternateLongScale = Mathf.Clamp(stone.endQuoins.alternateLongScale, 1f, 2f);
        stone.endQuoins.edgeInset = Mathf.Max(0f, stone.endQuoins.edgeInset);
        stone.endQuoins.verticalSpacing = Mathf.Max(0f, stone.endQuoins.verticalSpacing);
        stone.endQuoins.cornerLDepthMul = Mathf.Max(1f, stone.endQuoins.cornerLDepthMul);

        stone.hueJitter = Mathf.Clamp(stone.hueJitter, 0f, 0.10f);
        stone.saturationJitter = Mathf.Clamp(stone.saturationJitter, 0f, 0.35f);
        stone.valueJitter = Mathf.Clamp(stone.valueJitter, 0f, 0.35f);
        stone.uvOffsetJitter = Mathf.Max(0f, stone.uvOffsetJitter);

        stoneLargeModules ??= new List<WallStoneModuleDefinition>();
        stoneMediumModules ??= new List<WallStoneModuleDefinition>();
        stoneSmallModules ??= new List<WallStoneModuleDefinition>();
    }
}
