using System;
using System.Collections.Generic;
using UnityEngine;

public enum WallCladdingMode
{
    StoneRandom = 0,
    BrickSymmetric = 1,
}

public enum WallModulePlacement
{
    Side = 0,
    Top = 1,
    Cap = 2,
    Corner = 3,
}

[Serializable]
public sealed class WallCladdingGeneralSettings
{
    [Min(0.001f)] public float sideInset = 0.01f;
    [Min(0.001f)] public float topInset = 0.01f;
    [Min(0.001f)] public float capInset = 0.01f;

    [Min(0f)] public float depthOffset = 0f;
    [Min(0f)] public float collisionPadding = 0.01f;

    [Min(0.05f)] public float cornerBlendDistance = 0.35f;
    [Min(0f)] public float randomSeedOffset = 0f;
}

[Serializable]
public sealed class StoneCladdingSettings
{
    [Header("Course")]
    [Min(0.08f)] public float targetRowHeight = 0.38f;
    [Range(0f, 0.6f)] public float rowHeightJitter = 0.18f;
    [Min(0f)] public float horizontalSpacing = 0.035f;
    [Min(0f)] public float verticalSpacing = 0.03f;

    [Header("Mortar / Embed")]
    [Min(0.001f)] public float embedDepth = 0.08f;
    [Min(0f)] public float surfaceProtrusion = 0.035f;
    [Min(0.02f)] public float minStoneDepth = 0.08f;
    [Min(0.02f)] public float maxStoneDepth = 0.16f;

    [Header("Variation")]
    [Range(0f, 1f)] public float smallStoneFillChance = 0.45f;
    [Range(0f, 1f)] public float widthJitter = 0.18f;
    [Range(0f, 1f)] public float heightJitter = 0.12f;
    [Range(0f, 1f)] public float depthJitter = 0.12f;
    [Range(0f, 0.25f)] public float positionJitter = 0.025f;
    [Range(0f, 0.25f)] public float scaleJitter = 0.08f;

    [Header("Rotation")]
    [Range(0f, 15f)] public float randomYaw = 2f;
    [Range(0f, 15f)] public float randomPitch = 1.5f;
    [Range(0f, 25f)] public float randomRoll = 8f;

    [Header("Corners")]
    public bool preferSmallModulesNearCorners = true;
    [Min(0.05f)] public float cornerSmallModuleZone = 0.45f;

    [Header("Top")]
    public bool generateTopCourse = false;
    [Range(0f, 0.25f)] public float topCourseLift = 0.02f;
}

[Serializable]
public sealed class BrickCladdingSettings
{
    [Min(0.05f)] public float brickWidth = 0.32f;
    [Min(0.05f)] public float brickHeight = 0.16f;
    [Min(0.001f)] public float mortarGapX = 0.01f;
    [Min(0.001f)] public float mortarGapY = 0.01f;

    public bool useHalfOffsetEveryOtherRow = true;
    [Range(0f, 0.5f)] public float rowOffsetFraction = 0.5f;

    [Range(0f, 0.1f)] public float sizeJitter = 0.015f;
    [Range(0f, 3f)] public float randomYaw = 0.5f;
    [Range(0f, 0.5f)] public float positionJitter = 0.005f;

    public bool allowEdgeCutBricks = true;
    [Min(0.05f)] public float minimumCutWidth = 0.08f;
}

[CreateAssetMenu(menuName = "TinyGlade/Walls/Cladding/Wall Cladding Profile", fileName = "WallCladdingProfile")]
public sealed class WallCladdingProfile : ScriptableObject
{
    [Header("Identity")]
    public string profileId = "wall_profile";
    public string displayName = "Wall Profile";
    public Sprite icon;
    public WallCladdingMode mode = WallCladdingMode.StoneRandom;

    [Header("Base Materials")]
    [Tooltip("Material du coeur du mur / mortier.")]
    public Material fallbackWallMaterial;
    public Material topMaterial;
    public Material capMaterial;

    [Header("General")]
    public WallCladdingGeneralSettings general = new WallCladdingGeneralSettings();

    [Header("Stone")]
    public StoneCladdingSettings stone = new StoneCladdingSettings();
    public List<WallStoneModuleDefinition> stoneLargeModules = new List<WallStoneModuleDefinition>();
    public List<WallStoneModuleDefinition> stoneMediumModules = new List<WallStoneModuleDefinition>();
    public List<WallStoneModuleDefinition> stoneSmallModules = new List<WallStoneModuleDefinition>();

    [Header("Brick")]
    public BrickCladdingSettings brick = new BrickCladdingSettings();
    public List<WallBrickModuleDefinition> brickFullModules = new List<WallBrickModuleDefinition>();
    public List<WallBrickModuleDefinition> brickHalfModules = new List<WallBrickModuleDefinition>();
    public List<WallBrickModuleDefinition> brickCornerModules = new List<WallBrickModuleDefinition>();

    public bool UsesStoneMode => mode == WallCladdingMode.StoneRandom;
    public bool UsesBrickMode => mode == WallCladdingMode.BrickSymmetric;

    private void OnValidate()
    {
        profileId = string.IsNullOrWhiteSpace(profileId) ? "wall_profile" : profileId.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();

        general ??= new WallCladdingGeneralSettings();
        stone ??= new StoneCladdingSettings();
        brick ??= new BrickCladdingSettings();

        general.sideInset = Mathf.Max(0.001f, general.sideInset);
        general.topInset = Mathf.Max(0.001f, general.topInset);
        general.capInset = Mathf.Max(0.001f, general.capInset);
        general.depthOffset = Mathf.Max(0f, general.depthOffset);
        general.collisionPadding = Mathf.Max(0f, general.collisionPadding);
        general.cornerBlendDistance = Mathf.Max(0.05f, general.cornerBlendDistance);
        general.randomSeedOffset = Mathf.Max(0f, general.randomSeedOffset);

        stone.targetRowHeight = Mathf.Max(0.08f, stone.targetRowHeight);
        stone.rowHeightJitter = Mathf.Clamp01(stone.rowHeightJitter);
        stone.horizontalSpacing = Mathf.Max(0f, stone.horizontalSpacing);
        stone.verticalSpacing = Mathf.Max(0f, stone.verticalSpacing);
        stone.embedDepth = Mathf.Max(0.001f, stone.embedDepth);
        stone.surfaceProtrusion = Mathf.Max(0f, stone.surfaceProtrusion);
        stone.minStoneDepth = Mathf.Max(0.02f, stone.minStoneDepth);
        stone.maxStoneDepth = Mathf.Max(stone.minStoneDepth, stone.maxStoneDepth);
        stone.smallStoneFillChance = Mathf.Clamp01(stone.smallStoneFillChance);
        stone.widthJitter = Mathf.Clamp01(stone.widthJitter);
        stone.heightJitter = Mathf.Clamp01(stone.heightJitter);
        stone.depthJitter = Mathf.Clamp01(stone.depthJitter);
        stone.positionJitter = Mathf.Clamp(stone.positionJitter, 0f, 0.25f);
        stone.scaleJitter = Mathf.Clamp(stone.scaleJitter, 0f, 0.25f);
        stone.cornerSmallModuleZone = Mathf.Max(0.05f, stone.cornerSmallModuleZone);
        stone.topCourseLift = Mathf.Clamp(stone.topCourseLift, 0f, 0.25f);

        brick.brickWidth = Mathf.Max(0.05f, brick.brickWidth);
        brick.brickHeight = Mathf.Max(0.05f, brick.brickHeight);
        brick.mortarGapX = Mathf.Max(0f, brick.mortarGapX);
        brick.mortarGapY = Mathf.Max(0f, brick.mortarGapY);
        brick.rowOffsetFraction = Mathf.Clamp(brick.rowOffsetFraction, 0f, 0.5f);
        brick.sizeJitter = Mathf.Clamp(brick.sizeJitter, 0f, 0.25f);
        brick.positionJitter = Mathf.Clamp(brick.positionJitter, 0f, 0.25f);
        brick.minimumCutWidth = Mathf.Max(0.02f, brick.minimumCutWidth);

        stoneLargeModules ??= new List<WallStoneModuleDefinition>();
        stoneMediumModules ??= new List<WallStoneModuleDefinition>();
        stoneSmallModules ??= new List<WallStoneModuleDefinition>();
        brickFullModules ??= new List<WallBrickModuleDefinition>();
        brickHalfModules ??= new List<WallBrickModuleDefinition>();
        brickCornerModules ??= new List<WallBrickModuleDefinition>();
    }
}
