using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.PortalExample;
using Supernova.Voxels;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DenseJigsawRegionWorldTests
{
    [Test]
    public void ExternalLandingCell_IsJustOutsideFiniteMapForDebugging()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        MinecraftWorldGenerationConfiguration generation =
            configuration.InfiniteCavesGenerationSource;

        int columns = configuration.RegionColumnsPerSide;
        int minimumChunk = -(columns / 2);
        int maximumChunk = minimumChunk + columns - 1;
        float mapOuterEdge = (maximumChunk + 1)
            * VoxelColumnChunkData.Width
            * generation.VoxelSize;
        float landingX = configuration
            .ExternalLandingCellPlayerVoxelPosition.x
            * generation.VoxelSize;

        Assert.That(configuration.UseExternalLandingCell, Is.True);
        Assert.That(
            landingX - mapOuterEdge,
            Is.EqualTo(
                configuration.ExternalLandingCellDistanceInColumns
                * VoxelColumnChunkData.Width
                * generation.VoxelSize)
                .Within(0.001f));
        Assert.That(
            configuration.ExternalLandingCellDistanceInColumns,
            Is.InRange(1, 2));
    }

    [Test]
    public void FiniteRegion_RemainsAnchoredAtOriginWhenViewerIsInExternalCell()
    {
        var target = new GameObject("Dense fixed-origin generation test");
        try
        {
            MinecraftCaveInfiniteWorld world =
                target.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(world.ConfigureDenseRegion(LoadConfiguration()), Is.True);
            SetPrivateField(world, "world", new InfiniteVoxelWorld());
            SetPrivateField(world, "viewerChunk", new Vector3Int(99, 0, 0));
            MethodInfo refresh = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "RefreshRequiredChunks",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(refresh, Is.Not.Null);
            refresh.Invoke(world, new object[] { false });

            Assert.That(
                world.RequiredChunkCount,
                Is.EqualTo(LoadConfiguration().RegionColumnCount));
            Assert.That(
                world.ConfiguredDenseRegionStreamingOffsets,
                Has.All.Matches<Vector3Int>(world.IsWithinGenerationRadius));
            Assert.That(
                world.IsWithinGenerationRadius(new Vector3Int(99, 0, 0)),
                Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void Configuration_InheritsTheCompleteInfiniteCavesLevel()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        LevelConfiguration sourceLevel =
            AssetDatabase.LoadAssetAtPath<LevelConfiguration>(
                ProjectAssetPaths.Config.FirstLevel);

        Assert.That(configuration.InfiniteCavesLevelSource, Is.SameAs(sourceLevel));
        Assert.That(
            configuration.InfiniteCavesGenerationSource,
            Is.SameAs(sourceLevel.WorldGeneration));
        Assert.That(
            configuration.StructureFamilies,
            Is.SameAs(sourceLevel.WorldGeneration.JigsawStructures));
        Assert.That(
            configuration.StoneType,
            Is.SameAs(sourceLevel.WorldGeneration.BaseSolidVoxelType));
        Assert.That(
            sourceLevel.WorldGeneration.GenerationMode,
            Is.EqualTo(MinecraftWorldGenerationMode.InfiniteCaves));
    }

    [Test]
    public void MixedJigsaw_UsesEveryInheritedFamilyAtGuaranteedPlacementChance()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();

        Assert.That(
            DenseJigsawFeatureMixer.TryBuild(
                configuration,
                out DenseJigsawFeature feature,
                out string error),
            Is.True,
            error);
        Assert.That(feature.Settings.PlacementChance, Is.EqualTo(1f));
        Assert.That(feature.Settings.AllowLayoutOutsidePlacementRegion, Is.False);
        Assert.That(
            feature.Settings.RegionSizeInChunks,
            Is.EqualTo(configuration.StructureRegionSizeInColumns));
        Assert.That(
            feature.ModuleFamilies.Distinct().Count(),
            Is.EqualTo(configuration.StructureFamilies.Count));
        Assert.That(feature.Settings.Pieces.Count, Is.GreaterThan(1));

        for (int pieceIndex = 0;
            pieceIndex < feature.Settings.Pieces.Count;
            pieceIndex++)
        {
            JigsawPieceSettings piece = feature.Settings.GetPiece(pieceIndex);
            for (int connectorIndex = 0;
                connectorIndex < piece.Connectors.Count;
                connectorIndex++)
            {
                JigsawConnectorSettings connector =
                    piece.Connectors[connectorIndex];
                Assert.That(connector.SocketName, Is.EqualTo("*"));
                Assert.That(connector.TargetName, Is.EqualTo("*"));
                Assert.That(connector.TargetPoolId, Is.EqualTo("dense_mixed"));
                Assert.That(connector.ActivationChance, Is.EqualTo(1f));
            }
        }
    }

    [Test]
    public void Configuration_IntersectionSwitchPreservesBothModes()
    {
        DenseJigsawWorldConfiguration source = LoadConfiguration();
        DenseJigsawWorldConfiguration configuration =
            ScriptableObject.CreateInstance<DenseJigsawWorldConfiguration>();
        try
        {
            configuration.Configure(source.InfiniteCavesLevelSource);
            Assert.That(configuration.PreventStructureIntersections, Is.False);

            configuration.ConfigureStructureIntersections(true);
            Assert.That(configuration.PreventStructureIntersections, Is.True);

            configuration.ConfigureStructureIntersections(false);
            Assert.That(configuration.PreventStructureIntersections, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(configuration);
        }
    }

    [Test]
    public void NonIntersectingSelection_RejectsCollisionsDeterministically()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        Assert.That(
            DenseJigsawFeatureMixer.TryBuild(
                configuration,
                out DenseJigsawFeature feature,
                out string error),
            Is.True,
            error);

        JigsawStructureFeatureSettings settings =
            CreateOverlappingRingFixture(feature.Settings);
        const int seed = 20250308;
        const int minimum = -256;
        const int maximum = 255;
        var placements =
            new List<JigsawStructureGenerator.Placement>();
        JigsawPlacementService.CollectPlacements(
            settings,
            seed,
            minimum,
            minimum,
            maximum,
            maximum,
            placements);

        JigsawPlacementSelection first =
            JigsawPlacementSelection.CreateNonIntersecting(
                new[] { settings },
                seed,
                minimum,
                minimum,
                maximum,
                maximum);
        JigsawPlacementSelection second =
            JigsawPlacementSelection.CreateNonIntersecting(
                new[] { settings },
                seed,
                minimum,
                minimum,
                maximum,
                maximum);

        Assert.That(first.AcceptedPlacementCount, Is.GreaterThan(0));
        Assert.That(
            first.AcceptedPlacementCount,
            Is.LessThan(placements.Count),
            "The fixture must contain overlapping candidates to exercise rejection.");
        var acceptedLayouts =
            new List<IReadOnlyList<JigsawStructureGenerator.Piece>>();
        for (int placementIndex = 0;
            placementIndex < placements.Count;
            placementIndex++)
        {
            JigsawStructureGenerator.Placement placement =
                placements[placementIndex];
            Assert.That(
                second.Allows(settings, placement),
                Is.EqualTo(first.Allows(settings, placement)),
                "Strict selection must be deterministic.");
            if (!first.Allows(settings, placement))
            {
                continue;
            }

            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(
                    settings,
                    seed,
                    placement);
            AssertLayoutHasNoInternalIntersections(layout);
            for (int acceptedIndex = 0;
                acceptedIndex < acceptedLayouts.Count;
                acceptedIndex++)
            {
                AssertLayoutsDoNotIntersect(
                    layout,
                    acceptedLayouts[acceptedIndex]);
            }
            acceptedLayouts.Add(layout);
        }
    }

    [Test]
    public void StrictSwitch_BuildsSelectionBeforeColumnGeneration()
    {
        DenseJigsawWorldConfiguration source = LoadConfiguration();
        DenseJigsawWorldConfiguration configuration =
            ScriptableObject.CreateInstance<DenseJigsawWorldConfiguration>();
        var worldObject = new GameObject("Dense strict world test");
        var viewerObject = new GameObject("Dense strict viewer test");
        try
        {
            configuration.Configure(source.InfiniteCavesLevelSource);
            configuration.ConfigureStructureIntersections(true);
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(
                world.ConfigureDenseRegion(
                    configuration,
                    viewerObject.transform),
                Is.True);
            Assert.That(
                world.ApplyLevelConfiguration(
                    configuration.InfiniteCavesLevelSource),
                Is.True);

            world.InitializeWorld();

            Assert.That(
                world.RequiredChunkCount,
                Is.EqualTo(9),
                "Strict placement selection should still allow a local initial load.");
            Assert.That(
                world.DenseAcceptedJigsawPlacementCount,
                Is.GreaterThan(0));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
            UnityEngine.Object.DestroyImmediate(viewerObject);
            UnityEngine.Object.DestroyImmediate(configuration);
        }
    }

    [Test]
    public void FiniteRegion_InitialLoadCompletesBeforeBackgroundExpansion()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        var worldObject = new GameObject("Dense staged loading test");
        var viewerObject = new GameObject("Dense staged loading viewer");
        try
        {
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(
                world.ConfigureDenseRegion(configuration, viewerObject.transform),
                Is.True);
            Assert.That(
                world.ApplyLevelConfiguration(
                    configuration.InfiniteCavesLevelSource),
                Is.True);

            world.InitializeWorld();

            Vector3Int authoredSpawnVoxel = world.WorldPositionToVoxel(
                world.AuthoredSpawnWorldPosition);
            Vector3Int initialCenter = InfiniteVoxelWorld.WorldToChunk(
                authoredSpawnVoxel.x,
                authoredSpawnVoxel.y,
                authoredSpawnVoxel.z);
            HashSet<Vector3Int> requiredChunks =
                (HashSet<Vector3Int>)GetPrivateField(
                    world,
                    "requiredChunks");
            Assert.That(requiredChunks, Has.Count.EqualTo(9));
            Assert.That(
                requiredChunks,
                Has.All.Matches<Vector3Int>(
                    column => Mathf.Abs(column.x - initialCenter.x) <= 1
                        && Mathf.Abs(column.z - initialCenter.z) <= 1));

            HashSet<Vector3Int> builtMeshes =
                (HashSet<Vector3Int>)GetPrivateField(world, "builtMeshes");
            foreach (Vector3Int column in requiredChunks)
            {
                world.World.EnsureChunk(column);
                for (int section = 0;
                    section < world.EffectiveMeshSectionsPerColumn;
                    section++)
                {
                    builtMeshes.Add(new Vector3Int(
                        column.x,
                        section,
                        column.z));
                }
            }
            SetPrivateField(
                world,
                "generationStage",
                MinecraftCaveGenerationStage.Meshes);
            SetPrivateField(world, "structurePassApplied", true);

            MethodInfo reportReady = typeof(MinecraftCaveInfiniteWorld)
                .GetMethod(
                    "ReportReadyState",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(reportReady, Is.Not.Null);
            reportReady.Invoke(world, null);

            Assert.That(world.IsInitialLoadComplete, Is.True);
            Assert.That(world.InitialLoadProgress, Is.EqualTo(1f));
            Assert.That(
                world.RequiredChunkCount,
                Is.EqualTo(configuration.RegionColumnCount));
            Assert.That(
                world.QueuedChunkCount,
                Is.EqualTo(configuration.RegionColumnCount - 9));
            Assert.That(
                world.GenerationStage,
                Is.EqualTo(MinecraftCaveGenerationStage.Terrain),
                "The remaining finite region should continue through the existing "
                + "asynchronous generation pipeline after loading ends.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
            UnityEngine.Object.DestroyImmediate(viewerObject);
        }
    }

    [Test]
    public void FiniteRegion_PartialLoadDoesNotSealInteriorColumnsWithBedrock()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        var target = new GameObject("Dense partial boundary test");
        try
        {
            MinecraftCaveInfiniteWorld component =
                target.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(component.ConfigureDenseRegion(configuration), Is.True);
            Assert.That(
                component.ApplyLevelConfiguration(
                    configuration.InfiniteCavesLevelSource),
                Is.True);

            var voxelWorld = new InfiniteVoxelWorld();
            InfiniteVoxelChunk interiorChunk = voxelWorld.EnsureChunk(
                Vector3Int.zero);
            interiorChunk.Data.Fill(-1f, VoxelTypeId.Air);
            SetPrivateField(component, "world", voxelWorld);
            SetPrivateField(
                component,
                "bedrockType",
                configuration.InfiniteCavesGenerationSource
                    .BedrockVoxelType.TypeId);
            HashSet<Vector3Int> requiredChunks =
                (HashSet<Vector3Int>)GetPrivateField(
                    component,
                    "requiredChunks");
            requiredChunks.Add(Vector3Int.zero);

            MethodInfo restore = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "RestoreBoundaryBedrock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(restore, Is.Not.Null);
            restore.Invoke(component, null);

            int middleY = configuration.WorldHeight / 2;
            Assert.That(
                interiorChunk.Data.GetSample(0, middleY, 8).Type,
                Is.EqualTo(VoxelTypeId.Air));
            Assert.That(
                interiorChunk.Data.GetSample(15, middleY, 8).Type,
                Is.EqualTo(VoxelTypeId.Air));
            Assert.That(
                interiorChunk.Data.GetSample(8, middleY, 0).Type,
                Is.EqualTo(VoxelTypeId.Air));
            Assert.That(
                interiorChunk.Data.GetSample(8, middleY, 15).Type,
                Is.EqualTo(VoxelTypeId.Air));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void FiniteRegion_ContainsExactlySixBySixColumns()
    {
        IReadOnlyList<Vector3Int> offsets =
            MinecraftCaveInfiniteWorld.DenseRegionStreamingOffsets;

        Assert.That(
            offsets.Count,
            Is.EqualTo(
                DenseJigsawWorldConfiguration.DefaultRegionColumnsPerSide
                * DenseJigsawWorldConfiguration.DefaultRegionColumnsPerSide));
        Assert.That(offsets.Distinct().Count(), Is.EqualTo(offsets.Count));
        Assert.That(offsets.Select(item => item.x).Distinct().Count(), Is.EqualTo(6));
        Assert.That(offsets.Select(item => item.z).Distinct().Count(), Is.EqualTo(6));
        Assert.That(offsets, Does.Contain(Vector3Int.zero));
    }

    [Test]
    public void Configuration_VolumeAndDensityDriveTheSharedRuntime()
    {
        DenseJigsawWorldConfiguration source = LoadConfiguration();
        DenseJigsawWorldConfiguration configuration =
            ScriptableObject.CreateInstance<DenseJigsawWorldConfiguration>();
        var worldObject = new GameObject("Configured dense world test");
        try
        {
            configuration.Configure(source.InfiniteCavesLevelSource);
            configuration.ConfigureGenerationVolume(5, 8, 4f);
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(world.ConfigureDenseRegion(configuration), Is.True);

            Assert.That(configuration.WorldSectionCount, Is.EqualTo(5));
            Assert.That(configuration.WorldHeight, Is.EqualTo(160));
            Assert.That(configuration.RegionColumnsPerSide, Is.EqualTo(8));
            Assert.That(configuration.RegionColumnCount, Is.EqualTo(64));
            Assert.That(configuration.StructureDensity, Is.EqualTo(4f));
            Assert.That(
                world.ConfiguredDenseRegionStreamingOffsets.Count,
                Is.EqualTo(64));
            Assert.That(world.EffectiveWorldHeight, Is.EqualTo(160));
            Assert.That(world.EffectiveMeshSectionsPerColumn, Is.EqualTo(5));

            Assert.That(
                DenseJigsawFeatureMixer.TryBuild(
                    configuration,
                    out DenseJigsawFeature denserFeature,
                    out string denserError),
                Is.True,
                denserError);
            Assert.That(denserFeature.Settings.RegionSizeInChunks, Is.EqualTo(2));
            Assert.That(denserFeature.Settings.PlacementChance, Is.EqualTo(1f));
            Assert.That(
                denserFeature.Settings.AllowLayoutOutsidePlacementRegion,
                Is.True);

            configuration.ConfigureGenerationVolume(2, 4, 0.5f);
            Assert.That(world.EffectiveWorldHeight, Is.EqualTo(64));
            Assert.That(world.EffectiveMeshSectionsPerColumn, Is.EqualTo(2));
            Assert.That(
                world.ConfiguredDenseRegionStreamingOffsets.Count,
                Is.EqualTo(16));
            Assert.That(
                DenseJigsawFeatureMixer.TryBuild(
                    configuration,
                    out DenseJigsawFeature sparseFeature,
                    out string sparseError),
                Is.True,
                sparseError);
            Assert.That(sparseFeature.Settings.RegionSizeInChunks, Is.EqualTo(4));
            Assert.That(sparseFeature.Settings.PlacementChance, Is.EqualTo(0.5f));
            Assert.That(sparseFeature.Settings.WorldHeight, Is.EqualTo(64));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
            UnityEngine.Object.DestroyImmediate(configuration);
        }
    }

    [Test]
    public void DenseHeight_UsesThreeSectionsWithoutChangingNormalWorlds()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        var denseObject = new GameObject("Dense height test");
        var normalObject = new GameObject("Normal height test");
        try
        {
            MinecraftCaveInfiniteWorld dense =
                denseObject.AddComponent<MinecraftCaveInfiniteWorld>();
            MinecraftCaveInfiniteWorld normal =
                normalObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(dense.ConfigureDenseRegion(configuration), Is.True);

            Assert.That(
                dense.EffectiveWorldHeight,
                Is.EqualTo(configuration.WorldHeight));
            Assert.That(dense.EffectiveWorldHeight, Is.EqualTo(96));
            Assert.That(dense.EffectiveMeshSectionsPerColumn, Is.EqualTo(3));
            Assert.That(
                normal.EffectiveWorldHeight,
                Is.EqualTo(VoxelColumnChunkData.Height));
            Assert.That(
                normal.EffectiveMeshSectionsPerColumn,
                Is.EqualTo(MinecraftCaveInfiniteWorld.MeshSectionsPerColumn));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(denseObject);
            UnityEngine.Object.DestroyImmediate(normalObject);
        }
    }

    [Test]
    public void MixedJigsaw_AllFamiliesFitInsideNinetySixVoxels()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        Assert.That(
            DenseJigsawFeatureMixer.TryBuild(
                configuration,
                out DenseJigsawFeature feature,
                out string error),
            Is.True,
            error);

        Assert.That(
            feature.Settings.WorldHeight,
            Is.EqualTo(configuration.WorldHeight));
        var generatedFamilies = new HashSet<string>(StringComparer.Ordinal);
        const int seed = 20250308;
        for (int regionZ = -3; regionZ <= 3; regionZ++)
        {
            for (int regionX = -3; regionX <= 3; regionX++)
            {
                Assert.That(
                    JigsawStructureGenerator.TryGetPlacement(
                        feature.Settings,
                        seed,
                        regionX,
                        regionZ,
                        out JigsawStructureGenerator.Placement placement),
                    Is.True);
                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(
                        feature.Settings,
                        seed,
                        placement);
                for (int pieceIndex = 0;
                    pieceIndex < layout.Count;
                    pieceIndex++)
                {
                    JigsawStructureGenerator.Piece piece = layout[pieceIndex];
                    Assert.That(piece.Bounds.MinY, Is.GreaterThan(1));
                    Assert.That(
                        piece.Bounds.MaxY,
                        Is.LessThan(configuration.WorldHeight - 1));
                    int separator = piece.ModuleId.IndexOf(
                        "__",
                        StringComparison.Ordinal);
                    generatedFamilies.Add(separator >= 0
                        ? piece.ModuleId.Substring(0, separator)
                        : piece.ModuleId);
                }
            }
        }

        CollectionAssert.AreEquivalent(
            configuration.StructureFamilies
                .Where(item => item != null)
                .Select(item => item.StableId),
            generatedFamilies);
    }

    [Test]
    public void Scene_UsesInfiniteCavesRuntimeDirectly()
    {
        Scene scene = EditorSceneManager.OpenScene(
            ProjectAssetPaths.Scenes.DenseJigsawRegion,
            OpenSceneMode.Single);
        try
        {
            MinecraftCaveInfiniteWorld world =
                UnityEngine.Object.FindObjectOfType<MinecraftCaveInfiniteWorld>();
            VoxelPlayerController player =
                UnityEngine.Object.FindObjectOfType<VoxelPlayerController>();

            Assert.That(scene.IsValid(), Is.True);
            Assert.That(world, Is.Not.Null);
            Assert.That(world, Is.InstanceOf<IVoxelTerrain>());
            Assert.That(world.IsFiniteDenseRegion, Is.True);
            Assert.That(world.DenseRegionConfiguration, Is.SameAs(LoadConfiguration()));
            Assert.That(player, Is.Not.Null);
            SpawnPointSceneStructure landingCell =
                UnityEngine.Object.FindObjectOfType<SpawnPointSceneStructure>();
            DenseJigsawPortalBridge bridge =
                UnityEngine.Object.FindObjectOfType<DenseJigsawPortalBridge>();
            PortalExampleTriggerRelay[] triggerRelays = bridge != null
                ? bridge.GetComponentsInChildren<PortalExampleTriggerRelay>(true)
                : Array.Empty<PortalExampleTriggerRelay>();
            Assert.That(landingCell, Is.Not.Null);
            Assert.That(world.UsesExternalDenseLandingCell, Is.True);
            Assert.That(bridge, Is.Not.Null);
            Assert.That(bridge.World, Is.SameAs(world));
            Assert.That(bridge.LandingCell, Is.SameAs(landingCell));
            Assert.That(bridge.LandingCellGate, Is.Not.Null);
            Assert.That(bridge.CheckpointGate, Is.Not.Null);
            Assert.That(bridge.LandingCellGate.LinkedGate,
                Is.SameAs(bridge.CheckpointGate));
            Assert.That(bridge.CheckpointGate.LinkedGate,
                Is.SameAs(bridge.LandingCellGate));
            Assert.That(triggerRelays, Has.Length.EqualTo(2));
            Assert.That(
                triggerRelays.Select(relay => relay.Gate),
                Is.EquivalentTo(new[]
                {
                    bridge.LandingCellGate,
                    bridge.CheckpointGate
                }));
            Assert.That(
                player.GetComponent<PortalExampleTraveller>(),
                Is.Not.Null);
        }
        finally
        {
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
        }
    }

    [Test]
    public void SharedRuntime_InheritsOreMeshAndMiningConfiguration()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        MinecraftWorldGenerationConfiguration source =
            configuration.InfiniteCavesGenerationSource;
        var target = new GameObject("Dense shared runtime test");
        try
        {
            MinecraftCaveInfiniteWorld world =
                target.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(world.ConfigureDenseRegion(configuration), Is.True);
            Assert.That(
                world.ApplyLevelConfiguration(
                    configuration.InfiniteCavesLevelSource),
                Is.True);

            Assert.That(world.WorldGenerationConfiguration, Is.SameAs(source));
            CollectionAssert.AreEqual(source.OreFeatures, world.OreFeatures);
            Assert.That(world.VertexPlacement, Is.EqualTo(source.VertexPlacement));
            Assert.That(world.VoxelTypeCatalog, Is.SameAs(source.VoxelTypeCatalog));
            Assert.That(
                world.BaseSolidVoxelType,
                Is.SameAs(source.BaseSolidVoxelType));
            Assert.That(
                world.TerrainPhysicsMaterial,
                Is.SameAs(source.TerrainPhysicsMaterial));
            Assert.That(
                typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "HarvestConnectedOreVein",
                    BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Not.Null,
                "Dense must use the same ore-to-rigidbody extraction path.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void SharedRuntime_MinedOreCreatesTheOriginalPhysicsDrop()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        var target = new GameObject("Dense ore extraction test");
        try
        {
            MinecraftCaveInfiniteWorld world =
                target.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(world.ConfigureDenseRegion(configuration), Is.True);
            Assert.That(
                world.ApplyLevelConfiguration(
                    configuration.InfiniteCavesLevelSource),
                Is.True);
            world.InitializeWorld();

            Assert.That(world.OreFeatures, Is.Not.Empty);
            var oreSettings = (MinecraftOreFeatureSettings[])GetPrivateField(
                world,
                "oreFeatureSettings");
            Assert.That(oreSettings, Is.Not.Empty);
            Assert.That(
                oreSettings,
                Has.All.Matches<MinecraftOreFeatureSettings>(
                    item => item.MinHeight >= 1
                        && item.MaxHeight
                            < configuration.WorldHeight - 1));
            VoxelOreFeatureDefinition oreFeature = world.OreFeatures[0];
            Assert.That(oreFeature.ResultVoxelType, Is.Not.Null);

            InfiniteVoxelChunk chunk = world.World.EnsureChunk(Vector3Int.zero);
            chunk.Data.Fill(1f, world.BaseSolidVoxelType.TypeId);
            int oreY = configuration.WorldHeight - 16;
            var first = new Vector3Int(8, oreY, 8);
            var second = new Vector3Int(9, oreY, 8);
            world.World.SetVoxel(
                first.x,
                first.y,
                first.z,
                1f,
                oreFeature.ResultVoxelType.TypeId);
            world.World.SetVoxel(
                second.x,
                second.y,
                second.z,
                1f,
                oreFeature.ResultVoxelType.TypeId);

            for (int hit = 0;
                hit < 128 && world.ActiveOreDrops.Count == 0;
                hit++)
            {
                Assert.That(world.TryMineVoxel(first, out _), Is.True);
            }

            Assert.That(world.ActiveOreDrops, Has.Count.EqualTo(1));
            MinedOreDrop drop = world.ActiveOreDrops[0];
            Assert.That(drop.VoxelType, Is.EqualTo(oreFeature.ResultVoxelType.TypeId));
            Assert.That(drop.VoxelCount, Is.EqualTo(2));
            Assert.That(drop.Mesh, Is.Not.Null);
            Assert.That(drop.Mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(drop.Body, Is.Not.Null);
            Assert.That(drop.Body.isKinematic, Is.False);
            Assert.That(drop.GetComponent<Collider>(), Is.Not.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void FiniteRegion_RestoresBedrockOnAllSixFaces()
    {
        DenseJigsawWorldConfiguration configuration = LoadConfiguration();
        var target = new GameObject("Dense boundary test");
        try
        {
            MinecraftCaveInfiniteWorld component =
                target.AddComponent<MinecraftCaveInfiniteWorld>();
            Assert.That(component.ConfigureDenseRegion(configuration), Is.True);
            Assert.That(
                component.ApplyLevelConfiguration(
                    configuration.InfiniteCavesLevelSource),
                Is.True);

            var voxelWorld = new InfiniteVoxelWorld();
            var required = new HashSet<Vector3Int>();
            foreach (Vector3Int coordinate
                in component.ConfiguredDenseRegionStreamingOffsets)
            {
                required.Add(coordinate);
                voxelWorld.EnsureChunk(coordinate).Data.Fill(
                    -1f,
                    VoxelTypeId.Air);
            }

            SetPrivateField(component, "world", voxelWorld);
            SetPrivateField(
                component,
                "bedrockType",
                configuration.InfiniteCavesGenerationSource
                    .BedrockVoxelType.TypeId);
            HashSet<Vector3Int> requiredChunks =
                (HashSet<Vector3Int>)GetPrivateField(
                    component,
                    "requiredChunks");
            requiredChunks.UnionWith(required);
            MethodInfo restore = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "RestoreBoundaryBedrock",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(restore, Is.Not.Null);
            Assert.That((int)restore.Invoke(component, null), Is.GreaterThan(0));

            VoxelTypeId bedrock = configuration.InfiniteCavesGenerationSource
                .BedrockVoxelType.TypeId;
            int minimumChunkX = required.Min(item => item.x);
            int maximumChunkX = required.Max(item => item.x);
            int minimumChunkZ = required.Min(item => item.z);
            int maximumChunkZ = required.Max(item => item.z);
            int middleY = configuration.WorldHeight / 2;
            AssertSampleType(voxelWorld, 0, 0, 0, 0, 16, bedrock);
            AssertSampleType(
                voxelWorld,
                0,
                0,
                0,
                configuration.WorldHeight - 1,
                16,
                bedrock);
            AssertSampleType(
                voxelWorld,
                minimumChunkX,
                0,
                0,
                middleY,
                16,
                bedrock);
            AssertSampleType(
                voxelWorld,
                maximumChunkX,
                0,
                31,
                middleY,
                16,
                bedrock);
            AssertSampleType(
                voxelWorld,
                0,
                minimumChunkZ,
                16,
                middleY,
                0,
                bedrock);
            AssertSampleType(
                voxelWorld,
                0,
                maximumChunkZ,
                16,
                middleY,
                31,
                bedrock);
            AssertSampleType(
                voxelWorld,
                0,
                0,
                1,
                middleY,
                1,
                VoxelTypeId.Air);
            AssertSampleType(
                voxelWorld,
                minimumChunkX,
                0,
                0,
                configuration.WorldHeight,
                16,
                VoxelTypeId.Air);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static DenseJigsawWorldConfiguration LoadConfiguration()
    {
        DenseJigsawWorldConfiguration configuration =
            AssetDatabase.LoadAssetAtPath<DenseJigsawWorldConfiguration>(
                ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
        Assert.That(configuration, Is.Not.Null);
        return configuration;
    }

    private static JigsawStructureFeatureSettings CreateOverlappingRingFixture(
        JigsawStructureFeatureSettings source)
    {
        return new JigsawStructureFeatureSettings(
            "dense_strict_overlap_fixture",
            source.PrimaryType,
            source.AccentType,
            source.SeedSalt,
            source.RegionSizeInChunks,
            1f,
            source.MinFloorHeight,
            source.MaxFloorHeight,
            source.MaxPieces,
            source.MaxDepth,
            source.MaxHorizontalDistance,
            source.FirstPieceId,
            source.LayoutAttempts,
            source.ConnectorPlacementAttempts,
            source.CollisionPadding,
            source.Pieces.ToArray(),
            JigsawPlacementStrategy.ConcentricRings,
            16,
            1,
            4,
            0,
            worldHeight: source.WorldHeight);
    }

    private static void AssertLayoutsDoNotIntersect(
        IReadOnlyList<JigsawStructureGenerator.Piece> left,
        IReadOnlyList<JigsawStructureGenerator.Piece> right)
    {
        for (int leftIndex = 0; leftIndex < left.Count; leftIndex++)
        {
            for (int rightIndex = 0; rightIndex < right.Count; rightIndex++)
            {
                Assert.That(
                    left[leftIndex].Bounds.Intersects(
                        right[rightIndex].Bounds),
                    Is.False,
                    $"Strict layouts intersect at {left[leftIndex].ModuleId} "
                    + $"and {right[rightIndex].ModuleId}.");
            }
        }
    }

    private static void AssertLayoutHasNoInternalIntersections(
        IReadOnlyList<JigsawStructureGenerator.Piece> layout)
    {
        for (int leftIndex = 0; leftIndex < layout.Count; leftIndex++)
        {
            for (int rightIndex = 0;
                rightIndex < leftIndex;
                rightIndex++)
            {
                Assert.That(
                    layout[leftIndex].Bounds.Intersects(
                        layout[rightIndex].Bounds),
                    Is.False,
                    $"Strict layout intersects internally at "
                    + $"{layout[leftIndex].ModuleId} and "
                    + $"{layout[rightIndex].ModuleId}.");
            }
        }
    }

    private static object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        return field.GetValue(target);
    }

    private static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }

    private static void AssertSampleType(
        InfiniteVoxelWorld world,
        int chunkX,
        int chunkZ,
        int localX,
        int localY,
        int localZ,
        VoxelTypeId expected)
    {
        Assert.That(
            world.TryGetChunk(
                new Vector3Int(chunkX, 0, chunkZ),
                out InfiniteVoxelChunk chunk),
            Is.True);
        Assert.That(
            chunk.Data.GetSample(localX, localY, localZ).Type,
            Is.EqualTo(expected));
    }
}
