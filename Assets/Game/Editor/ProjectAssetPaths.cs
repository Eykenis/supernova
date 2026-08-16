public static class ProjectAssetPaths
{
    public static string ToAbsoluteFileSystemPath(string assetPath)
    {
        string projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(projectRoot, assetPath));
    }

    public static class Folders
    {
        public const string AssetsRoot = "Assets";
        public const string Game = AssetsRoot + "/Game";
        public const string Config = Game + "/Config";
        public const string AudioConfig = Config + "/Audio";
        public const string SoundEffects = AssetsRoot + "/SFX";
        public const string InputConfig = Config + "/Input";
        public const string Levels = Config + "/Levels";
        public const string Equipment = Config + "/Equipment";
        public const string Tools = Config + "/Tools";
        public const string Shop = Config + "/Shop";
        public const string VoxelTypes = Config + "/VoxelTypes";
        public const string TerrainVoxelTypes = VoxelTypes + "/Terrain";
        public const string StructuralVoxelTypes = VoxelTypes + "/Structural";
        public const string OreFeatures = Config + "/OreFeatures";
        public const string Biomes = Config + "/Biomes";
        public const string SurfaceBrushes = Config + "/SurfaceBrushes";
        public const string StructureConfigs = Config + "/Structures";
        public const string StructureFeatures = Config + "/StructureFeatures";
        public const string JigsawStructureFeatures =
            StructureFeatures + "/Jigsaw";
        public const string Worlds = Config + "/Worlds";
        public const string UiConfig = Config + "/UI";
        public const string Animations = Game + "/Animations";
        public const string PlayerAnimations = Animations + "/Player";
        public const string SciFiAnimations = Animations + "/Scifi";
        public const string GeneratedPlayerAnimations =
            PlayerAnimations + "/Generated";
        public const string ChoppingAnimations =
            GeneratedPlayerAnimations + "/Chopping";
        public const string ArchivedAnimations = Animations + "/NotUsed";
        public const string Prefabs = Game + "/Prefabs";
        public const string PlayerPrefabs = Prefabs;
        public const string EquipmentPrefabs = Prefabs + "/Equipment";
        public const string ToolPrefabs = Prefabs + "/Tools";
        public const string FlashlightPrefabs = ToolPrefabs + "/Flashlight";
        public const string BombPrefabs = ToolPrefabs + "/Bomb";
        public const string PickaxePrefabs = ToolPrefabs + "/Pickaxe";
        public const string GunPrefabs = ToolPrefabs + "/Guns";
        public const string ProjectilePrefabs = GunPrefabs + "/Projectiles";
        public const string EffectPrefabs = Prefabs + "/Effects";
        public const string ExplosionEffectPrefabs =
            EffectPrefabs + "/Explosions";
        public const string StructurePrefabs = Prefabs + "/Structures";
        public const string SurfaceContentPrefabs = Prefabs + "/SurfaceContent";
        public const string MuzzleFlashPrefabs =
            EffectPrefabs + "/MuzzleFlashes";
        public const string TreasurePrefabs = Prefabs + "/Treasures";
        public const string TreasureFractureVariants =
            TreasurePrefabs + "/FractureVariants";
        public const string ExampleCreatureAssets =
            Prefabs + "/Mobs/ExampleCreature";
        public const string Structures = Game + "/Structures";
        public const string Materials = Game + "/Materials";
        public const string ToolMaterials = Materials + "/Tools";
        public const string LightingMaterials = Materials + "/Lighting";
        public const string VegetationMaterials = Materials + "/Vegetation";
        public const string TerrainMaterials = Materials + "/Terrain";
        public const string SurfaceContentMaterials =
            Materials + "/SurfaceContent";

        public const string PortalExampleMaterials =
            Materials + "/PortalExample";
        public const string EffectMaterials = Materials + "/Effects";
        public const string MuzzleFlashMaterials =
            EffectMaterials + "/MuzzleFlashes";
        public const string Models = Game + "/Models";
        public const string SurfaceContentModels = Models + "/SurfaceContent";
        public const string VegetationModels = Models + "/Vegetation";
        public const string EffectModels = Models + "/Effects";
        public const string MuzzleFlashModels =
            EffectModels + "/MuzzleFlashes";
        public const string Shaders = Game + "/Shaders";
        public const string VegetationShaders = Shaders + "/Vegetation";
        public const string TerrainShaders = Shaders + "/Terrain";

        public const string PortalExampleShaders =
            Shaders + "/PortalExample";
        public const string EffectShaders = Shaders + "/Effects";
        public const string MuzzleFlashShaders =
            EffectShaders + "/MuzzleFlashes";
        public const string Textures = Game + "/Textures";
        public const string ToolTextures = Textures + "/Tools";
        public const string UiTextures = Textures + "/UI";
        public const string EquipmentIconTextures =
            UiTextures + "/EquipmentIcons";
        public const string EffectTextures = Textures + "/Effects";
        public const string MuzzleFlashTextures =
            EffectTextures + "/MuzzleFlashes";
        public const string PhysicsMaterials = Materials + "/Physics";
        public const string ShopMaterials = Materials + "/Shop";
        public const string Scenes = "Assets/Scenes";
        public const string VoxelIntegrityExperimentScenes =
            Scenes + "/Experiments/VoxelIntegrity";
        public const string TestScenes = AssetsRoot + "/Scene";
        public const string PortalExampleScenes = TestScenes;
        public const string PortalExampleModels = Models + "/PortalExample";
        public const string Ui = "Assets/UI";
        public const string UiViews = Ui + "/UI";
        public const string SciFiUi = UiViews + "/SciFi";
        public const string PausePose = Ui + "/PausePose";
        public const string Screenshots = "Assets/Screenshots";
    }

    public static class Config
    {
        public const string RunMovementSound =
            Folders.AudioConfig + "/RunMovementSound.asset";
        public const string HomeCellMovementSound =
            Folders.AudioConfig + "/HomeCellMovementSound.asset";
        public const string MagnetInteractionSound =
            Folders.AudioConfig + "/MagnetInteractionSound.asset";
        public const string CoinDepositSound =
            Folders.AudioConfig + "/CoinDepositSound.asset";
        public const string CaveAmbienceSound =
            Folders.AudioConfig + "/CaveAmbienceSound.asset";
        public const string CashGrowingSound =
            Folders.AudioConfig + "/CashGrowingSound.asset";
        public const string MissionStartSound =
            Folders.AudioConfig + "/MissionStartSound.asset";
        public const string MissionReadySound =
            Folders.AudioConfig + "/MissionReadySound.asset";
        public const string PickaxeMonsterHitSound =
            Folders.AudioConfig + "/PickaxeMonsterHitSound.asset";
        public const string PickaxeMiningHitSound =
            Folders.AudioConfig + "/PickaxeMiningHitSound.asset";
        public const string PickaxeWooshSound =
            Folders.AudioConfig + "/PickaxeWooshSound.asset";
        public const string PickaxeThrownImpactSound =
            Folders.AudioConfig + "/PickaxeThrownImpactSound.asset";
        public const string ToolThrowSound =
            Folders.AudioConfig + "/ToolThrowSound.asset";
        public const string SolidGunShotSound =
            Folders.AudioConfig + "/SolidGunShotSound.asset";
        public const string PortalGunShotSound =
            Folders.AudioConfig + "/PortalGunShotSound.asset";
        public const string CreatureRunSound =
            Folders.AudioConfig + "/CreatureRunSound.asset";
        public const string CreatureAttackSound =
            Folders.AudioConfig + "/CreatureAttackSound.asset";
        public const string CreatureHitPlayerSound =
            Folders.AudioConfig + "/CreatureHitPlayerSound.asset";
        public const string PlayerFallSmallSound =
            Folders.AudioConfig + "/PlayerFallSmallSound.asset";
        public const string PlayerFallBigSound =
            Folders.AudioConfig + "/PlayerFallBigSound.asset";
        public const string BombFuseSound =
            Folders.AudioConfig + "/BombFuseSound.asset";
        public const string BombExplosionSound =
            Folders.AudioConfig + "/BombExplosionSound.asset";
        public const string GameAssetCatalog =
            Folders.Config + "/GameAssetCatalog.asset";
        public const string GameInputActions =
            Folders.InputConfig + "/GameInputActions.inputactions";
        public const string PlayerShaderVariants =
            Folders.Config + "/PlayerShaderVariants.shadervariants";
        public const string FirstLevel = Folders.Levels + "/FirstLevel.asset";
        public const string SecondLevel = Folders.Levels + "/SecondLevel.asset";
        public const string ThirdLevel = Folders.Levels + "/ThirdLevel.asset";
        public const string CombatTestLevel =
            Folders.Levels + "/CombatTestLevel.asset";
        public const string WorldGeneration =
            Folders.Worlds + "/DefaultWorldGeneration.asset";
        public const string JigsawSuperflatWorldGeneration =
            Folders.Worlds + "/JigsawSuperflatWorldGeneration.asset";
        public const string DenseJigsawRegionWorldGeneration =
            Folders.Worlds + "/DenseJigsawRegionWorld.asset";
        public const string MonsterSpawnTable =
            Folders.Config + "/MonsterSpawnTable.asset";
        public const string TreasureSpawnTable =
            Folders.Config + "/TreasureSpawnTable.asset";
        public const string VoxelCatalog =
            Folders.Config + "/MinecraftVoxelTypes.asset";
        public const string OreFeature = Folders.OreFeatures + "/Ore.asset";
        public const string CaveBiomeCatalog =
            Folders.Biomes + "/DefaultCaveBiomes.asset";
        public const string GrassyCaveBiome =
            Folders.Biomes + "/Grassy.asset";
        public const string BaldCaveBiome =
            Folders.Biomes + "/Bald.asset";
        public const string GrassSurfaceBrush =
            Folders.SurfaceBrushes + "/Grass.asset";
        public const string VineSurfaceBrush =
            Folders.SurfaceBrushes + "/Vine.asset";
        public const string StoneVoxel =
            Folders.TerrainVoxelTypes + "/Stone.asset";
        public const string SolidStoneVoxel =
            Folders.TerrainVoxelTypes + "/Solid Stone.asset";
        public const string StructureBrickVoxel =
            Folders.StructuralVoxelTypes + "/StructureBrick.asset";
        public const string FortressBrickVoxel =
            Folders.StructuralVoxelTypes + "/FortressBrick.asset";
        public const string TrialChamberFeature =
            Folders.StructureFeatures + "/TrialChamber.asset";
        public const string AbandonedMineshaftJigsaw =
            Folders.JigsawStructureFeatures + "/AbandonedMineshaft.asset";
        public const string StrongholdJigsaw =
            Folders.JigsawStructureFeatures + "/Stronghold.asset";
        public const string NetherFortressJigsaw =
            Folders.JigsawStructureFeatures + "/NetherFortress.asset";
        public const string AncientCityJigsaw =
            Folders.JigsawStructureFeatures + "/AncientCity.asset";
        public const string CaveVillageJigsaw =
            Folders.JigsawStructureFeatures + "/CaveVillage.asset";
        public const string AncientPrisonJigsaw =
            Folders.JigsawStructureFeatures + "/AncientPrison.asset";
        public const string CactusGrottoJigsaw =
            Folders.JigsawStructureFeatures + "/CactusGrotto.asset";
        public const string SpawnCheckpointHallJigsaw =
            Folders.JigsawStructureFeatures + "/SpawnCheckpointHall.asset";
        public const string PickaxeTool = Folders.Tools + "/PickaxeTool.asset";
        public const string FlashlightTool =
            Folders.Tools + "/FlashlightTool.asset";
        public const string BombTool = Folders.Tools + "/BombTool.asset";
        public const string SolidGunTool =
            Folders.Tools + "/SolidGunTool.asset";
        public const string PortalGunTool =
            Folders.Tools + "/PortalGunTool.asset";
        public const string FlashlightProduct =
            Folders.Shop + "/FlashlightProduct.asset";
        public const string SolidGunProduct =
            Folders.Shop + "/SolidGunProduct.asset";
        public const string PortalGunProduct =
            Folders.Shop + "/PortalGunProduct.asset";
        public const string Jetpack = Folders.Equipment + "/Jetpack.asset";
        public const string JetpackInteraction =
            Folders.Equipment + "/JetpackInteraction.asset";
        public const string UiDesignTokens =
            Folders.UiConfig + "/DefaultUiDesignTokens.asset";
        public const string EquipmentIconCatalog =
            Folders.UiConfig + "/EquipmentIconCatalog.asset";
        public const string EquipmentPortraitSettings =
            Folders.UiConfig + "/EquipmentPortraitSettings.asset";
    }

    public static class Audio
    {
        public const string Run = Folders.SoundEffects + "/run.wav";
        public const string HomeFootstep =
            Folders.SoundEffects + "/home_footstep..mp3";
        public const string Magnet = Folders.SoundEffects + "/magnet.wav";
        public const string Coin1 = Folders.SoundEffects + "/coin1.wav";
        public const string Coin2 = Folders.SoundEffects + "/coin2.wav";
        public const string Ambience =
            Folders.SoundEffects + "/ambience.wav";
        public const string CashGrowing =
            Folders.SoundEffects + "/cash_growing.wav";
        public const string Start = Folders.SoundEffects + "/start.wav";
        public const string Ready = Folders.SoundEffects + "/ready.wav";
        public const string Punch1 = Folders.SoundEffects + "/punch1.wav";
        public const string Punch2 = Folders.SoundEffects + "/punch2.wav";
        public const string Punch3 = Folders.SoundEffects + "/punch3.wav";
        public const string Mine1 = Folders.SoundEffects + "/mine1.wav";
        public const string Mine2 = Folders.SoundEffects + "/mine2.wav";
        public const string Mine3 = Folders.SoundEffects + "/mine3.wav";
        public const string Mine4 = Folders.SoundEffects + "/mine4.wav";
        public const string Mine5 = Folders.SoundEffects + "/mine5.wav";
        public const string Mine6 = Folders.SoundEffects + "/mine6.wav";
        public const string Mine7 = Folders.SoundEffects + "/mine7.wav";
        public const string Mine8 = Folders.SoundEffects + "/mine8.wav";
        public const string Mine9 = Folders.SoundEffects + "/mine9.wav";
        public const string Mine10 = Folders.SoundEffects + "/mine10.wav";
        public const string Woosh = Folders.SoundEffects + "/woosh.wav";
        public const string PickaxeThrown =
            Folders.SoundEffects + "/pickaxe_thrown.wav";
        public const string Throw = Folders.SoundEffects + "/throw.wav";
        public const string Laser = Folders.SoundEffects + "/laser.wav";
        public const string PortalShot =
            Folders.SoundEffects + "/portalshot.wav";
        public const string Hit1 = Folders.SoundEffects + "/hit1.ogg";
        public const string Hit2 = Folders.SoundEffects + "/hit2.ogg";
        public const string Hit3 = Folders.SoundEffects + "/hit3.ogg";
        public const string FallBig = Folders.SoundEffects + "/fallbig.ogg";
        public const string FallSmall =
            Folders.SoundEffects + "/fallsmall.ogg";
        public const string Fuse = Folders.SoundEffects + "/fuse.ogg";
        public const string Explode1 =
            Folders.SoundEffects + "/explode1.ogg";
        public const string Explode2 =
            Folders.SoundEffects + "/explode2.ogg";
        public const string Explode3 =
            Folders.SoundEffects + "/explode3.ogg";
        public const string Explode4 =
            Folders.SoundEffects + "/explode4.ogg";
    }

    public static class Animations
    {
        public const string PlayerController =
            Folders.PlayerAnimations + "/P05Player.controller";
        public const string ToolUpperBodyMask =
            Folders.PlayerAnimations + "/ToolUpperBody.mask";
        public const string CrouchToolArmsMask =
            Folders.PlayerAnimations + "/CrouchToolArms.mask";
        public const string ToolPrimaryActionPlaceholder =
            Folders.PlayerAnimations + "/ToolPrimaryActionPlaceholder.anim";
        public const string FirearmIdle =
            Folders.PlayerAnimations + "/FirearmIdle.anim";
        public const string FirearmMove =
            Folders.PlayerAnimations + "/FirearmMove.anim";
        public const string FireContinuous =
            Folders.PlayerAnimations + "/FireContinuous.anim";
        public const string Mining = Folders.PlayerAnimations + "/mining_aki.anim";
        public const string PickaxeSpin =
            Folders.Animations + "/pickaxe_spin.anim";
        public const string PickaxeThrown =
            Folders.Animations + "/pickaxe_thrown.anim";
        public const string ThrownPickaxeController =
            Folders.Animations + "/ThrownPickaxe.controller";
        public const string Hover = Folders.PlayerAnimations + "/HoverLoop.anim";
        public const string SciFiDoorController =
            Folders.SciFiAnimations + "/Door_Vert_01.controller";
        public const string SciFiDoorOpen =
            Folders.SciFiAnimations + "/open_door.anim";
        public const string CrouchIdle =
            Folders.PlayerAnimations + "/CrouchLoop.anim";
        public const string CrouchMove =
            Folders.PlayerAnimations + "/Crouch_WalkFwd.anim";
        public const string Chopping =
            Folders.PlayerAnimations + "/P05_Chop_Reauthored.anim";
        public const string MiningBackup =
            Folders.ArchivedAnimations + "/mining_aki_before_lowerbody_hands.anim";
        public const string MiningHandFixBackup =
            Folders.ArchivedAnimations + "/mining_aki_before_IdleA_handfix.anim";
        public const string MiningTwistFixBackup =
            Folders.ArchivedAnimations + "/mining_aki_before_forearm_twist_fix.anim";
        public const string MiningFistEyeBackup =
            Folders.ArchivedAnimations + "/mining_aki_before_right_fist_eye_up.anim";
    }

    public static class Prefabs
    {
        public const string Player = Folders.PlayerPrefabs + "/Player.prefab";
        public const string LandingCell =
            Folders.StructurePrefabs + "/Cell.prefab";
        public const string Jetpack =
            Folders.EquipmentPrefabs + "/Jetpack.prefab";
        public const string FlashlightProjectile =
            Folders.FlashlightPrefabs + "/FlashlightProjectile.prefab";
        public const string BombHeld =
            Folders.BombPrefabs + "/BombHeld.prefab";
        public const string BombProjectile =
            Folders.BombPrefabs + "/BombProjectile.prefab";
        public const string BombExplosionEffect =
            Folders.ExplosionEffectPrefabs + "/BombExplosion.prefab";
        public const string ThrownPickaxe =
            Folders.PickaxePrefabs + "/ThrownPickaxe.prefab";
        public const string SolidGun =
            Folders.GunPrefabs + "/SolidGun.prefab";
        public const string PortalGun =
            Folders.GunPrefabs + "/PortalGun.prefab";
        public const string SolidVoxelProjectile =
            Folders.ProjectilePrefabs + "/SolidVoxelProjectile.prefab";
        public const string PortalGunProjectile =
            Folders.ProjectilePrefabs + "/PortalGunProjectile.prefab";
        public const string MuzzleFlash =
            Folders.MuzzleFlashPrefabs + "/MuzzleFlash1.prefab";
        public const string GrassSurfacePlaceholder =
            Folders.SurfaceContentPrefabs + "/GrassPlaceholder.prefab";
        public const string VineSurfacePlaceholder =
            Folders.SurfaceContentPrefabs + "/VinePlaceholder.prefab";
        public const string MainMenu = Folders.UiViews + "/MainMenuCanvas.prefab";
        public const string PausePortrait =
            Folders.PausePose + "/PausePortrait.prefab";
    }

    public static class Ui
    {
        public const string PauseSettings =
            Folders.PausePose + "/PausePortraitSettings.asset";
        public const string PauseController =
            Folders.PausePose + "/PausePortrait.controller";
        public const string PauseBodyMaterial =
            Folders.PausePose + "/PauseSilhouetteBody.mat";
        public const string PauseBackgroundMaterial =
            Folders.PausePose + "/PauseSilhouetteBackground.mat";
        public const string PrimaryFrame = Folders.SciFiUi + "/FramePrimary.png";
        public const string WideFrame = Folders.SciFiUi + "/FrameWide.png";
        public const string SlotFrame = Folders.SciFiUi + "/FrameSlot.png";
        public const string ThinFrame = Folders.SciFiUi + "/FrameThin.png";
        public const string HudPanel = Folders.SciFiUi + "/HudPanelClean.png";
        public const string SlotClean = Folders.SciFiUi + "/SlotClean.png";
        public const string ButtonClean = Folders.SciFiUi + "/ButtonClean.png";
        public const string ProgressClean =
            Folders.SciFiUi + "/ProgressClean.png";
        public const string PauseCard =
            Folders.SciFiUi + "/PauseCardClean.png";
        public const string LoadingDial = Folders.SciFiUi + "/LoadingDial.png";
        public const string TelemetryBackdrop =
            Folders.SciFiUi + "/TelemetryBackdrop.jpg";
        public const string AstrocraftTitle =
            Folders.SciFiUi + "/AstrocraftTitle.png";
        public const string RuntimeFont =
            "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
    }

    public static class Shaders
    {
        public const string PortalExampleSurface =
            Folders.PortalExampleShaders + "/PortalExampleSurface.shader";
        public const string PortalExampleClippedLit =
            Folders.PortalExampleShaders
            + "/PortalExampleClippedLit.shader";
        public const string SoftFalloffLit =
            Folders.LightingMaterials + "/SoftFalloffLit.shader";
        public const string SoftFalloffAttenuation =
            Folders.LightingMaterials + "/SoftFalloffAttenuation.hlsl";
        public const string SoftFalloffLitForwardPass =
            Folders.LightingMaterials + "/SoftFalloffLitForwardPass.hlsl";
        public const string CaveGrassBlade =
            Folders.VegetationShaders + "/CaveGrassBlade.shader";
        public const string CaveGrassBladeInput =
            Folders.VegetationShaders + "/CaveGrassBladeInput.hlsl";
        public const string CaveGrassBladePasses =
            Folders.VegetationShaders + "/CaveGrassBladePasses.hlsl";
        public const string CaveGrassTurfLayer =
            Folders.TerrainShaders + "/CaveBiomeSurfaceLit.shader";
        public const string CaveGrassTurfLayerForwardPass =
            Folders.TerrainShaders
            + "/CaveBiomeSurfaceLitForwardPass.hlsl";
        public const string MagnetEnergyRibbon =
            Folders.EffectShaders + "/MagnetEnergyRibbon.shader";
    }

    public static class Materials
    {
        public const string PortalExampleBlue =
            Folders.PortalExampleMaterials + "/PortalBlue.mat";
        public const string PortalExampleOrange =
            Folders.PortalExampleMaterials + "/PortalOrange.mat";
        public const string PortalExampleWhitePanel =
            Folders.PortalExampleMaterials + "/WhitePanel.mat";
        public const string PortalExampleDarkPanel =
            Folders.PortalExampleMaterials + "/DarkPanel.mat";
        public const string PortalExampleMetal =
            Folders.PortalExampleMaterials + "/Metal.mat";
        public const string PortalExampleButton =
            Folders.PortalExampleMaterials + "/Button.mat";
        public const string PortalExampleGoal =
            Folders.PortalExampleMaterials + "/Goal.mat";
        public const string CaveTerrainPhysics =
            Folders.PhysicsMaterials + "/CaveTerrain.physicMaterial";
        // Voxel materials each live in their own folder alongside their textures.
        public const string Ore = Folders.Materials + "/Voxels/Ore/Ore.mat";
        public const string RecoveredOre =
            Folders.Materials + "/Voxels/Ore/RecoveredOre.mat";
        public const string Bedrock =
            Folders.Materials + "/Voxels/Bedrock/Bedrock.mat";
        public const string Marble =
            Folders.Materials + "/Voxels/Marble/Marble.mat";
        public const string Stone =
            Folders.Materials + "/Voxels/Stone/Stone.mat";
        public const string Bricks =
            Folders.Materials + "/Voxels/Bricks/Bricks.mat";
        public const string Dirt = Folders.Materials + "/Voxels/Dirt/Dirt.mat";
        public const string RustyMetal =
            Folders.Materials + "/Voxels/RustyMetal/RustyMetal.mat";
        public const string TigerRock =
            Folders.Materials + "/Voxels/TigerRock/Bricks.mat";
        public const string WornBrick =
            Folders.Materials + "/Voxels/WornBrick/WornBrick.mat";
        public const string CaveGrassBlade =
            Folders.VegetationMaterials + "/CaveGrassBlade.mat";
        public const string CaveGrassTurfLayer =
            Folders.TerrainMaterials + "/CaveGrassTurfLayer.mat";
        public const string GrassSurfacePlaceholder =
            Folders.SurfaceContentMaterials + "/GrassPlaceholder.mat";
        public const string VineSurfacePlaceholder =
            Folders.SurfaceContentMaterials + "/VinePlaceholder.mat";
        public const string FlashlightGlow =
            Folders.ToolMaterials + "/FlashlightGlow.mat";
        public const string FlashlightBody =
            Folders.ToolMaterials + "/FlashlightBody.mat";
        public const string BombBody =
            Folders.ToolMaterials + "/BombBody.mat";
        public const string ProjectileTracer =
            Folders.ToolMaterials + "/ProjectileTracer.mat";
        public const string SolidGunBody =
            Folders.ToolMaterials + "/SolidGunBody.mat";
        public const string PortalGunBody =
            Folders.ToolMaterials + "/PortalGunBody.mat";
        public const string SolidPlatform =
            Folders.ToolMaterials + "/SolidPlatform.mat";
        public const string MissionCellConsole =
            Folders.Materials + "/Prototypes/SolidStone.mat";
        public const string MuzzleFlashFlame =
            Folders.MuzzleFlashMaterials + "/Flame1.mat";
        public const string MuzzleFlashCore =
            Folders.MuzzleFlashMaterials + "/MuzzleFlash1.mat";
        public const string MuzzleFlashSecondary =
            Folders.MuzzleFlashMaterials + "/MuzzleFlash2.mat";
        public const string MuzzleFlashSmoke =
            Folders.MuzzleFlashMaterials + "/Smoke.mat";
        public const string MuzzleFlashDistortion =
            Folders.MuzzleFlashMaterials + "/Distortion.mat";
        public const string MagnetEnergyRibbon =
            Folders.EffectMaterials + "/MagnetEnergyRibbon.mat";
        public const string CollisionDust =
            Folders.EffectMaterials + "/CollisionDust.mat";
        public const string ShopGeometryWireframeShader =
            Folders.ShopMaterials + "/ShopGeometryWireframe.shader";
        public const string ShopGeometryWireframe =
            Folders.ShopMaterials + "/ShopGeometryWireframe.mat";
    }

    public static class Textures
    {
        public const string SolidGunBaseColor =
            Folders.ToolTextures + "/SolidGunBodyBaseColor.png";
        public const string SolidGunNormal =
            Folders.ToolTextures + "/SolidGunBodyNormal.png";
        public const string SolidGunHeight =
            Folders.ToolTextures + "/SolidGunBodyHeight.png";
        public const string SolidGunMetallicSmoothness =
            Folders.ToolTextures + "/SolidGunBodyMetallicSmoothness.png";
        public const string PortalGunBaseColor =
            Folders.ToolTextures + "/PortalGunBodyBaseColor.png";
        public const string PortalGunNormal =
            Folders.ToolTextures + "/PortalGunBodyNormal.png";
        public const string PortalGunHeight =
            Folders.ToolTextures + "/PortalGunBodyHeight.png";
        public const string PortalGunMetallicSmoothness =
            Folders.ToolTextures + "/PortalGunBodyMetallicSmoothness.png";
    }

    public static class Models
    {
        public const string PortalExampleRing =
            Folders.PortalExampleModels + "/PortalRing.asset";
        public const string CheckpointDisk =
            Folders.Models + "/Disk/source/Disk.prefab";
        public const string GrassSurfacePlaceholder =
            Folders.SurfaceContentModels + "/GrassPlaceholder.asset";
        public const string CaveGrassBladeLod0 =
            Folders.VegetationModels + "/CaveGrassBladeLod0.asset";
        public const string CaveGrassBladeLod1 =
            Folders.VegetationModels + "/CaveGrassBladeLod1.asset";
        public const string CaveGrassBladeLod2 =
            Folders.VegetationModels + "/CaveGrassBladeLod2.asset";
    }

    public static class Structures
    {
        public const string SpawnShelter =
            Folders.StructureConfigs + "/SpawnShelter.asset";
        public const string TrialChamberTemplate =
            Folders.Structures + "/TrialChamberTemplate.asset";
    }

    public static class Scenes
    {
        public const string VoxelIntegrityExperiment =
            Folders.VoxelIntegrityExperimentScenes
            + "/VoxelIntegrityExperiment.scene";
        public const string PortalExample =
            Folders.PortalExampleScenes + "/PortalExample.scene";
        // Lives beside the other playable scenes rather than under Scene/, which
        // only holds the Portal example.
        public const string SpawnShelterStoneTest =
            Folders.Scenes + "/SpawnShelterStoneTest.scene";
        public const string Home = Folders.Scenes + "/Home.scene";
        public const string InfiniteCaves =
            Folders.Scenes + "/InfiniteCaves.scene";
        public const string CombatTest = Folders.Scenes + "/CombatTest.scene";
        public const string VoxelStructureEditor =
            Folders.Scenes + "/VoxelStructureEditor.scene";
        public const string PausePortraitPreview =
            Folders.Scenes + "/PausePortraitPreview.unity";
        public const string CaveGallery =
            Folders.Scenes + "/MinecraftCaveGallery.scene";
        public const string InfiniteWorldDemo =
            Folders.Scenes + "/MinecraftCaveInfiniteWorld.scene";

        public const string WorldGenerationPreview =
            Folders.Scenes + "/WorldGenerationPreview.scene";
        public const string JigsawSuperflat =
            Folders.Scenes + "/JigsawSuperflat.scene";
        public const string DenseJigsawRegion =
            Folders.Scenes + "/DenseJigsawRegion.scene";
    }

    public static class Screenshots
    {
        public const string MiningFront =
            Folders.Screenshots + "/mining_aki_composed_front.png";
        public const string MiningAngle =
            Folders.Screenshots + "/mining_aki_composed_45.png";
        public const string ChoppingKeyPoses =
            Folders.Screenshots + "/P05_Chop_Reauthored_KeyPoses.png";
        public const string ChoppingSourcePrefix =
            Folders.Screenshots + "/ChopCompare_Source_";
        public const string ChoppingPlayerPrefix =
            Folders.Screenshots + "/ChopCompare_P05_";
    }

    public static class ThirdParty
    {
        public const string MuryotaisuController =
            "Assets/3rd/Mryotaisu/Animators/Muryotaisu.controller";
        public const string LowerBodyMask =
            "Assets/3rd/Mryotaisu/Animators/LowerBodyMask.mask";
        public const string HoverDemo =
            "Assets/3rd/P05_Aki & Mika/Anim_demo/HoverDemo.anim";
        public const string WaitAnimation =
            "Assets/3rd/P05_Aki & Mika/Anim_demo/movetest_WAIT01.anim";
        public const string PlainP05Prefab =
            "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/NoPhysics_Plain/"
            + "P05_ASTRO_Aki_Plain Variant.prefab";
        public const string PhysicsP05Folder =
            "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/Physics_MagicaCloth2";
        public const string PhysicsP05Prefab =
            PhysicsP05Folder + "/P05_ASTRO_Aki Variant.prefab";
        public const string BackPuckVfx =
            "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/VFX/BackPuck_VFX.prefab";
        public const string ArmsAnimation =
            "Assets/3rd/Sketchfab/Arms_Animation_a.fbx";
        public const string SuriyunPoseAngle =
            "Assets/3rd/Suriyun/Animations/Anim@Angpose.fbx";
        public const string SuriyunPoseCast =
            "Assets/3rd/Suriyun/Animations/Anim@CastspellC.fbx";
        /// <summary>Looping two-handed cast pose used as the magnet hold animation.</summary>
        public const string SuriyunMagnetHold =
            "Assets/3rd/Suriyun/Animations/Anim@CastspellB.fbx";
        public const string SuriyunPoseAttack =
            "Assets/3rd/Suriyun/Animations/Anim@Atk4.fbx";
        public const string SuriyunPoseThinking =
            "Assets/3rd/Suriyun/Animations/Anim@Thinking.fbx";
        public const string SuriyunIdle =
            "Assets/3rd/Suriyun/Animations/Anim@Idle_A.fbx";
        public const string StylizedToolsFolder = "Assets/3rd/Stylized 3D Tools";
        public const string StylizedPickaxeModel =
            StylizedToolsFolder + "/Models/pickaxe01.obj";
        public const string StylizedPickaxeMaterial =
            StylizedToolsFolder + "/Materials/Pickaxe01.mat";
        public const string StylizedPickaxePrefab =
            StylizedToolsFolder + "/Prefabs/pickaxe01.prefab";
        public const string StylizedPickaxeAnimation =
            StylizedToolsFolder + "/Prefabs/pickaxe01.anim";
        public const string StylizedPickaxeController =
            StylizedToolsFolder + "/Prefabs/pickaxe01.controller";
    }

    public static class LookupNames
    {
        public const string HomeScene = "Home";
        public const string SpawnShelterStoneTestScene = "SpawnShelterStoneTest";
        public const string MissionCell = "Cell";
        public const string PausePoseState = "Base Layer.PausePose";
        public const string PlayerCameraRig = "CameraRig";
        public const string JetpackMount = "P05_BackPack";
        public const string JetpackMain = "BackPack_Main";
        public const string JetpackVfx = "BackPuck_VFX";
        public const string HomeShopRoot = "Shop";
    }
}
