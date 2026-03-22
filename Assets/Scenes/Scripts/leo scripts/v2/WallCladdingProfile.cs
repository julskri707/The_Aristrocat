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
    [Min(0.01f)] public float sideInset = 0.01f;
    [Min(0.01f)] public float topInset = 0.01f;
    [Min(0.01f)] public float capInset = 0.01f;

    [Min(0.01f)] public float depthOffset = 0.01f;
    [Min(0.01f)] public float collisionPadding = 0.02f;

    [Min(0.1f)] public float cornerBlendDistance = 0.35f;
    [Min(0f)] public float randomSeedOffset = 0f;
}

[Serializable]
public sealed class StoneCladdingSettings
{
    [Min(0.1f)] public float targetRowHeight = 0.45f;
    [Min(0.1f)] public float horizontalSpacing = 0.02f;
    [Min(0.1f)] public float verticalSpacing = 0.02f;

    [Range(0f, 1f)] public float smallStoneFillChance = 0.65f;
    [Range(0f, 25f)] public float randomYaw = 8f;
    [Range(0f, 25f)] public float randomPitch = 2f;
    [Range(0f, 25f)] public float randomRoll = 4f;

    [Range(0f, 1f)] public float positionJitter = 0.05f;
    [Range(0f, 1f)] public float scaleJitter = 0.15f;

    public bool preferSmallModulesNearCorners = true;
    [Min(0.05f)] public float cornerSmallModuleZone = 0.5f;
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

        if (general == null) general = new WallCladdingGeneralSettings();
        if (stone == null) stone = new StoneCladdingSettings();
        if (brick == null) brick = new BrickCladdingSettings();

        general.sideInset = Mathf.Max(0.001f, general.sideInset);
        general.topInset = Mathf.Max(0.001f, general.topInset);
        general.capInset = Mathf.Max(0.001f, general.capInset);
        general.depthOffset = Mathf.Max(0.001f, general.depthOffset);
        general.collisionPadding = Mathf.Max(0f, general.collisionPadding);
        general.cornerBlendDistance = Mathf.Max(0.05f, general.cornerBlendDistance);

        stone.targetRowHeight = Mathf.Max(0.05f, stone.targetRowHeight);
        stone.horizontalSpacing = Mathf.Max(0f, stone.horizontalSpacing);
        stone.verticalSpacing = Mathf.Max(0f, stone.verticalSpacing);
        stone.cornerSmallModuleZone = Mathf.Max(0.05f, stone.cornerSmallModuleZone);
        stone.smallStoneFillChance = Mathf.Clamp01(stone.smallStoneFillChance);
        stone.positionJitter = Mathf.Clamp01(stone.positionJitter);
        stone.scaleJitter = Mathf.Clamp01(stone.scaleJitter);

        brick.brickWidth = Mathf.Max(0.05f, brick.brickWidth);
        brick.brickHeight = Mathf.Max(0.05f, brick.brickHeight);
        brick.mortarGapX = Mathf.Max(0f, brick.mortarGapX);
        brick.mortarGapY = Mathf.Max(0f, brick.mortarGapY);
        brick.rowOffsetFraction = Mathf.Clamp(brick.rowOffsetFraction, 0f, 0.5f);
        brick.sizeJitter = Mathf.Clamp(brick.sizeJitter, 0f, 0.25f);
        brick.positionJitter = Mathf.Clamp(brick.positionJitter, 0f, 0.25f);
        brick.minimumCutWidth = Mathf.Max(0.02f, brick.minimumCutWidth);

        EnsureLists();
    }

    private void EnsureLists()
    {
        stoneLargeModules ??= new List<WallStoneModuleDefinition>();
        stoneMediumModules ??= new List<WallStoneModuleDefinition>();
        stoneSmallModules ??= new List<WallStoneModuleDefinition>();

        brickFullModules ??= new List<WallBrickModuleDefinition>();
        brickHalfModules ??= new List<WallBrickModuleDefinition>();
        brickCornerModules ??= new List<WallBrickModuleDefinition>();
    }
}
