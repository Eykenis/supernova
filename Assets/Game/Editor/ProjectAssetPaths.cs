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
        public const string Game = "Assets/Game";
        public const string Config = Game + "/Config";
        public const string Levels = Config + "/Levels";
        public const string Equipment = Config + "/Equipment";
        public const string Tools = Config + "/Tools";
        public const string Shop = Config + "/Shop";
        public const string VoxelTypes = Config + "/VoxelTypes";
        public const string OreFeatures = Config + "/OreFeatures";
        public const string Biomes = Config + "/Biomes";
        public const string SurfaceBrushes = Config + "/SurfaceBrushes";
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
        public const string GrabHookPrefabs = ToolPrefabs + "/Grabhook";
        public const string GunPrefabs = ToolPrefabs + "/Guns";
        public const string ProjectilePrefabs = GunPrefabs + "/Projectiles";
        public const string EffectPrefabs = Prefabs + "/Effects";
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
        public const string SurfaceContentMaterials =
            Materials + "/SurfaceContent";

        public const string PortalMaterials = Materials + "/Portals";
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

        public const string PortalShaders = Shaders + "/Portals";
        public const string EffectShaders = Shaders + "/Effects";
        public const string MuzzleFlashShaders =
            EffectShaders + "/MuzzleFlashes";
        public const string Textures = Game + "/Textures";
        public const string UiTextures = Textures + "/UI";
        public const string EquipmentIconTextures =
            UiTextures + "/EquipmentIcons";
        public const string EffectTextures = Textures + "/Effects";
        public const string MuzzleFlashTextures =
            EffectTextures + "/MuzzleFlashes";
        public const string PhysicsMaterials = Materials + "/Physics";
        public const string ShopMaterials = Materials + "/Shop";
        public const string Scenes = "Assets/Scenes";
        public const string Ui = "Assets/UI";
        public const string UiViews = Ui + "/UI";
        public const string SciFiUi = UiViews + "/SciFi";
        public const string PausePose = Ui + "/PausePose";
        public const string Screenshots = "Assets/Screenshots";
    }

    public static class Config
    {
        public const string GameAssetCatalog =
            Folders.Config + "/GameAssetCatalog.asset";
        public const string FirstLevel = Folders.Levels + "/FirstLevel.asset";
        public const string CombatTestLevel =
            Folders.Levels + "/CombatTestLevel.asset";
        public const string WorldGeneration =
            Folders.Worlds + "/DefaultWorldGeneration.asset";
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
        public const string StoneVoxel = Folders.VoxelTypes + "/Stone.asset";
        public const string StructureBrickVoxel =
            Folders.VoxelTypes + "/StructureBrick.asset";
        public const string FortressBrickVoxel =
            Folders.VoxelTypes + "/FortressBrick.asset";
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
        public const string PickaxeTool = Folders.Tools + "/PickaxeTool.asset";
        public const string MagnetTool = Folders.Tools + "/MagnetTool.asset";
        public const string FlashlightTool =
            Folders.Tools + "/FlashlightTool.asset";
        public const string RifleTool = Folders.Tools + "/RifleTool.asset";
        public const string SmgTool = Folders.Tools + "/SMGTool.asset";
        public const string SolidGunTool =
            Folders.Tools + "/SolidGunTool.asset";
        public const string CartTool = Folders.Tools + "/CartTool.asset";
        public const string GrabHookTool =
            Folders.Tools + "/GrabHookTool.asset";
        public const string GunProduct =
            Folders.Shop + "/GunProduct.asset";
        public const string SmgProduct =
            Folders.Shop + "/SMGProduct.asset";
        public const string FlashlightProduct =
            Folders.Shop + "/FlashlightProduct.asset";
        public const string SolidGunProduct =
            Folders.Shop + "/SolidGunProduct.asset";
        public const string AttractionModuleProduct =
            Folders.Shop + "/AttractionModuleProduct.asset";
        public const string CartProduct =
            Folders.Shop + "/CartProduct.asset";
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
        public const string Mining = Folders.PlayerAnimations + "/mining_aki.anim";
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
        public const string Jetpack =
            Folders.EquipmentPrefabs + "/Jetpack.prefab";
        public const string FlashlightProjectile =
            Folders.FlashlightPrefabs + "/FlashlightProjectile.prefab";
        public const string Smg = Folders.GunPrefabs + "/SMG.prefab";
        public const string SolidGun =
            Folders.GunPrefabs + "/SolidGun.prefab";
        public const string AttractionModuleDisplay =
            Folders.ToolPrefabs + "/AttractionModuleDisplay.prefab";
        public const string GrabHook =
            Folders.GrabHookPrefabs + "/GrabHook.prefab";
        public const string GrabHookSourceModel =
            Folders.GrabHookPrefabs + "/hook_exported.fbx";
        public const string RifleProjectile =
            Folders.ProjectilePrefabs + "/RifleProjectile.prefab";
        public const string SolidVoxelProjectile =
            Folders.ProjectilePrefabs + "/SolidVoxelProjectile.prefab";
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
        public const string RuntimeFont =
            "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
    }

    public static class Shaders
    {
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
    }

    public static class Materials
    {
        public const string CaveTerrainPhysics =
            Folders.PhysicsMaterials + "/CaveTerrain.physicMaterial";
        // Voxel materials each live in their own folder alongside their textures.
        public const string Ore = Folders.Materials + "/Voxels/Ore/Ore.mat";
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
        public const string GrassSurfacePlaceholder =
            Folders.SurfaceContentMaterials + "/GrassPlaceholder.mat";
        public const string VineSurfacePlaceholder =
            Folders.SurfaceContentMaterials + "/VinePlaceholder.mat";
        public const string FlashlightGlow =
            Folders.ToolMaterials + "/FlashlightGlow.mat";
        public const string RifleProjectile =
            Folders.ToolMaterials + "/RifleProjectile.mat";
        public const string SolidPlatform =
            Folders.ToolMaterials + "/SolidPlatform.mat";
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
        public const string ShopGeometryWireframeShader =
            Folders.ShopMaterials + "/ShopGeometryWireframe.shader";
        public const string ShopGeometryWireframe =
            Folders.ShopMaterials + "/ShopGeometryWireframe.mat";
    }

    public static class Models
    {
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
            Folders.Structures + "/SpawnShelter.asset";
        public const string TrialChamberTemplate =
            Folders.Structures + "/TrialChamberTemplate.asset";
    }

    public static class Scenes
    {
        public const string Home = Folders.Scenes + "/Home.scene";
        public const string InfiniteCaves =
            Folders.Scenes + "/InfiniteCaves.scene";
        public const string MainMenu = Folders.Scenes + "/MainMenu.unity";
        public const string CombatTest = Folders.Scenes + "/CombatTest.scene";
        public const string VoxelStructureEditor =
            Folders.Scenes + "/VoxelStructureEditor.scene";
        public const string PausePortraitPreview =
            Folders.Scenes + "/PausePortraitPreview.unity";
        public const string CaveGallery =
            Folders.Scenes + "/MinecraftCaveGallery.scene";
        public const string InfiniteWorldDemo =
            Folders.Scenes + "/MinecraftCaveInfiniteWorld.scene";

        public const string Portal = Folders.Scenes + "/Portal.scene";
        public const string WorldGenerationPreview =
            Folders.Scenes + "/WorldGenerationPreview.scene";
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
        public const string EmptyCart = "Assets/3rd/EmptyCart.prefab";
        public const string RifleAnimationFolder =
            "Assets/3rd/FPS/Rifle_01_v25/FBX/Animation";
        public const string RifleIdle = RifleAnimationFolder
            + "/MIL2_M3_W2_Stand_Aim_Idle.fbx";
        public const string RifleMove = RifleAnimationFolder
            + "/MIL2_M3_W2_Jog_Aim_F_Loop.fbx";
        public const string RifleFire = RifleAnimationFolder
            + "/MIL2_M3_W2_Stand_Fire_Single.fbx";
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
        public const string SuriyunPoseAttack =
            "Assets/3rd/Suriyun/Animations/Anim@Atk4.fbx";
        public const string SuriyunPoseThinking =
            "Assets/3rd/Suriyun/Animations/Anim@Thinking.fbx";
        public const string SuriyunIdle =
            "Assets/3rd/Suriyun/Animations/Anim@Idle_A.fbx";
    }

    public static class LookupNames
    {
        public const string MainMenuScene = "MainMenu";
        public const string MissionCell = "Cell";
        public const string AuthoredCart = "EmptyCart";
        public const string PausePoseState = "Base Layer.PausePose";
        public const string PlayerCameraRig = "CameraRig";
        public const string JetpackMount = "P05_BackPack";
        public const string JetpackMain = "BackPack_Main";
        public const string JetpackVfx = "BackPuck_VFX";
        public const string HomeShopRoot = "Shop";
    }
}
