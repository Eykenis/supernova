using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Math = System.Math;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class JigsawStructureGeneratorTests
    {
        private static readonly VoxelTypeId Stone = new VoxelTypeId(2);
        private static readonly VoxelTypeId StructureBrick = new VoxelTypeId(5);
        private static readonly VoxelTypeId FortressBrick = new VoxelTypeId(6);

        [Test]
        public void DefaultWorld_ReferencesTheAuthoredJigsawStructureSet()
        {
            MinecraftWorldGenerationConfiguration world =
                AssetDatabase.LoadAssetAtPath<MinecraftWorldGenerationConfiguration>(
                    ProjectAssetPaths.Config.WorldGeneration);

            Assert.That(world, Is.Not.Null);
            Assert.That(
                world.JigsawStructures.Select(item => item.StableId),
                Is.EquivalentTo(new[]
                {
                    "abandoned_mineshaft",
                    "stronghold",
                    "nether_fortress",
                    "ancient_city",
                    "cave_village",
                    "ancient_prison",
                    "cactus_grotto",
                }));
        }

        [Test]
        public void AuthoredStructures_AllPassGraphValidation()
        {
            foreach (string path in new[]
            {
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw,
                ProjectAssetPaths.Config.StrongholdJigsaw,
                ProjectAssetPaths.Config.NetherFortressJigsaw,
                ProjectAssetPaths.Config.AncientCityJigsaw,
                ProjectAssetPaths.Config.CaveVillageJigsaw,
                ProjectAssetPaths.Config.AncientPrisonJigsaw,
                ProjectAssetPaths.Config.CactusGrottoJigsaw,
            })
            {
                JigsawStructureFeatureSettings settings = LoadSettings(path);
                string[] errors = JigsawStructureValidator.Validate(settings)
                    .Where(issue => issue.Severity
                        == JigsawStructureValidator.Severity.Error)
                    .Select(issue => issue.Message)
                    .ToArray();
                Assert.That(errors, Is.Empty, path);
            }
        }

        [Test]
        public void AuthoredStructures_KeepPrimaryAndAccentInOneVoxelGroup()
        {
            VoxelTypeCatalog catalog =
                AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(
                    ProjectAssetPaths.Config.VoxelCatalog);
            Assert.That(catalog, Is.Not.Null);
            VoxelGroupMap groupMap =
                VoxelGroupMap.FromDefinitions(catalog.Definitions);

            foreach (string path in new[]
            {
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw,
                ProjectAssetPaths.Config.StrongholdJigsaw,
                ProjectAssetPaths.Config.NetherFortressJigsaw,
                ProjectAssetPaths.Config.AncientCityJigsaw,
                ProjectAssetPaths.Config.CaveVillageJigsaw,
                ProjectAssetPaths.Config.AncientPrisonJigsaw,
                ProjectAssetPaths.Config.CactusGrottoJigsaw,
            })
            {
                JigsawStructureFeatureSettings settings = LoadSettings(path);
                // A structure whose two palettes sit in different groups would be
                // meshed as two surfaces and show inset seams inside its own walls.
                Assert.That(
                    groupMap.GetGroupKey(settings.PrimaryType),
                    Is.EqualTo(groupMap.GetGroupKey(settings.AccentType)),
                    $"{path} mixes voxel groups between its primary and accent types.");
            }
        }

        [Test]
        public void AuthoredStructures_CarryOnlyTreasureSpawnMarkers()
        {
            foreach (string path in new[]
            {
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw,
                ProjectAssetPaths.Config.StrongholdJigsaw,
                ProjectAssetPaths.Config.NetherFortressJigsaw,
                ProjectAssetPaths.Config.AncientCityJigsaw,
                ProjectAssetPaths.Config.CaveVillageJigsaw,
                ProjectAssetPaths.Config.AncientPrisonJigsaw,
                ProjectAssetPaths.Config.CactusGrottoJigsaw,
            })
            {
                JigsawStructureFeatureSettings settings = LoadSettings(path);
                StructureSpawnMarkerSettings[] markers = settings.Pieces
                    .SelectMany(piece => piece.SpawnMarkers)
                    .ToArray();
                Assert.That(markers, Is.Not.Empty, path);
                Assert.That(
                    markers.All(marker => marker.IsConfigured),
                    Is.True,
                    $"{path} has a marker with no prefab assigned.");
                Assert.That(
                    markers.All(marker => marker.Kind
                        == StructureSpawnMarkerDefinition.Kind.Treasure),
                    Is.True,
                    $"{path} has a non-treasure spawn marker.");
            }
        }

        [Test]
        public void Definitions_ExposeEditableMineshaftAndStrongholdModules()
        {
            JigsawStructureFeatureSettings mineshaft = LoadSettings(
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
            JigsawStructureFeatureSettings fortress = LoadSettings(
                ProjectAssetPaths.Config.StrongholdJigsaw);

            Assert.That(
                mineshaft.Pieces.Select(piece => piece.StableId),
                Is.SupersetOf(new[]
                {
                    "mineshaft_room",
                    "mineshaft_corridor",
                    "mineshaft_crossing",
                    "mineshaft_stairs",
                    "mineshaft_storage",
                    "mineshaft_dead_end",
                }));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("stronghold_hall"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("stronghold_lobby"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("stronghold_library"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("stronghold_portal_room"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("stronghold_prison"));
            Assert.That(
                mineshaft.Pieces.Sum(piece => piece.Connectors.Count),
                Is.GreaterThanOrEqualTo(12));
            Assert.That(
                fortress.Pieces.Sum(piece => piece.Connectors.Count),
                Is.GreaterThanOrEqualTo(12));
        }

        [Test]
        public void MineshaftLayout_UsesConfiguredRoomAndCorridorModules()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                FindLayoutContaining(settings, 114514, "mineshaft_corridor");
            JigsawStructureGenerator.Piece room = layout[0];
            JigsawStructureGenerator.Piece corridor = layout.First(piece =>
                piece.ModuleId == "mineshaft_corridor");
            JigsawPieceSettings corridorModule = settings.Pieces.First(piece =>
                piece.StableId == corridor.ModuleId);

            Assert.That(room.ModuleId, Is.EqualTo("mineshaft_room"));
            // Read the expected extents from the asset: designers retune these for
            // player clearance, and the layout only has to honour what is authored.
            JigsawPieceSettings roomModule = settings.GetPiece(room.ModuleIndex);
            Assert.That(
                room.Bounds.MaxX - room.Bounds.MinX + 1,
                Is.InRange(roomModule.MinimumWidth, roomModule.MaximumWidth));
            Assert.That(
                corridor.Length,
                Is.InRange(
                    corridorModule.MinimumLength,
                    corridorModule.MaximumLength));
            int corridorWidth = (corridor.Direction & 1) == 0
                ? corridor.Bounds.MaxX - corridor.Bounds.MinX + 1
                : corridor.Bounds.MaxZ - corridor.Bounds.MinZ + 1;
            Assert.That(corridorWidth, Is.EqualTo(corridorModule.Width));
        }

        [Test]
        public void FortressLayout_CanComposeLobbyHallAndLibrary()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.StrongholdJigsaw);
            IReadOnlyList<JigsawStructureGenerator.Piece> matching = null;
            var placements = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings, 114514, -400_000, -400_000, 400_000, 400_000, placements);
            foreach (JigsawStructureGenerator.Placement placement in placements)
            {
                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(
                        settings,
                        114514,
                        placement);
                HashSet<string> ids = layout
                    .Select(piece => piece.ModuleId)
                    .ToHashSet();
                if (ids.Contains("stronghold_lobby")
                    && ids.Contains("stronghold_hall")
                    && ids.Contains("stronghold_library"))
                {
                    matching = layout;
                    break;
                }
            }

            Assert.That(matching, Is.Not.Null);
            Assert.That(matching[0].ModuleId, Is.EqualTo("stronghold_lobby"));
        }

        [Test]
        public void BuildLayout_SameSeedProducesIdenticalModuleGraph()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.StrongholdJigsaw);
            JigsawStructureGenerator.Placement placement =
                FindAnyPlacement(settings, 18731);
            IReadOnlyList<JigsawStructureGenerator.Piece> first =
                JigsawStructureGenerator.BuildLayout(settings, 18731, placement);
            IReadOnlyList<JigsawStructureGenerator.Piece> second =
                JigsawStructureGenerator.BuildLayout(settings, 18731, placement);

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].ModuleId, Is.EqualTo(first[i].ModuleId));
                Assert.That(second[i].Origin, Is.EqualTo(first[i].Origin));
                Assert.That(second[i].Length, Is.EqualTo(first[i].Length));
                Assert.That(second[i].ConnectorMask,
                    Is.EqualTo(first[i].ConnectorMask));
                Assert.That(second[i].Bounds.MinX,
                    Is.EqualTo(first[i].Bounds.MinX));
                Assert.That(second[i].Bounds.MinY,
                    Is.EqualTo(first[i].Bounds.MinY));
                Assert.That(second[i].Bounds.MinZ,
                    Is.EqualTo(first[i].Bounds.MinZ));
                Assert.That(second[i].Bounds.MaxX,
                    Is.EqualTo(first[i].Bounds.MaxX));
                Assert.That(second[i].Bounds.MaxY,
                    Is.EqualTo(first[i].Bounds.MaxY));
                Assert.That(second[i].Bounds.MaxZ,
                    Is.EqualTo(first[i].Bounds.MaxZ));
            }
        }

        [Test]
        public void SampledLayouts_SatisfyRequiredCountsAndHardMaximums()
        {
            string[] paths =
            {
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw,
                ProjectAssetPaths.Config.NetherFortressJigsaw,
            };
            foreach (string path in paths)
            {
                JigsawStructureFeatureSettings settings = LoadSettings(path);
                for (int regionZ = -2; regionZ <= 2; regionZ++)
                {
                    for (int regionX = -2; regionX <= 2; regionX++)
                    {
                        if (!JigsawStructureGenerator.TryGetPlacement(
                            settings,
                            666,
                            regionX,
                            regionZ,
                            out JigsawStructureGenerator.Placement placement))
                        {
                            continue;
                        }
                        IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                            JigsawStructureGenerator.BuildLayout(
                                settings,
                                666,
                                placement);
                        int[] counts = new int[settings.Pieces.Count];
                        foreach (JigsawStructureGenerator.Piece piece in layout)
                        {
                            counts[piece.ModuleIndex]++;
                        }
                        for (int i = 0; i < counts.Length; i++)
                        {
                            JigsawPieceSettings module = settings.GetPiece(i);
                            Assert.That(
                                counts[i],
                                Is.GreaterThanOrEqualTo(module.MinimumCount),
                                $"{settings.StableId}/{module.StableId} in region {regionX},{regionZ}");
                            if (module.MaximumCount > 0)
                            {
                                Assert.That(
                                    counts[i],
                                    Is.LessThanOrEqualTo(module.MaximumCount),
                                    $"{settings.StableId}/{module.StableId} in region {regionX},{regionZ}");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void BuildLayout_ReusesThreadSafeRegionCache()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.NetherFortressJigsaw);
            JigsawStructureGenerator.TryGetPlacement(
                settings,
                18731,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            JigsawStructureGenerator.ClearLayoutCache();

            IReadOnlyList<JigsawStructureGenerator.Piece> first =
                JigsawStructureGenerator.BuildLayout(settings, 18731, placement);
            IReadOnlyList<JigsawStructureGenerator.Piece> second =
                JigsawStructureGenerator.BuildLayout(settings, 18731, placement);

            Assert.That(first, Is.SameAs(second));
            Assert.That(JigsawStructureGenerator.LayoutBuildCount, Is.EqualTo(1));
            Assert.That(JigsawStructureGenerator.CachedLayoutCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildLayout_ConcurrentRequestsBuildRegionOnlyOnce()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.NetherFortressJigsaw);
            JigsawStructureGenerator.TryGetPlacement(
                settings,
                99173,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            JigsawStructureGenerator.ClearLayoutCache();
            var layouts = new ConcurrentBag<
                IReadOnlyList<JigsawStructureGenerator.Piece>>();

            Parallel.For(
                0,
                32,
                _ => layouts.Add(JigsawStructureGenerator.BuildLayout(
                    settings,
                    99173,
                    placement)));

            IReadOnlyList<JigsawStructureGenerator.Piece> first = layouts.First();
            Assert.That(
                layouts.All(layout => object.ReferenceEquals(layout, first)),
                Is.True);
            Assert.That(JigsawStructureGenerator.LayoutBuildCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildLayout_CacheHasBoundedMemoryFootprint()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.NetherFortressJigsaw);
            JigsawStructureGenerator.ClearLayoutCache();
            int created = JigsawStructureGenerator.LayoutCacheCapacity + 32;
            for (int regionX = 0; regionX < created; regionX++)
            {
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    331,
                    regionX,
                    0,
                    out JigsawStructureGenerator.Placement placement);
                JigsawStructureGenerator.BuildLayout(settings, 331, placement);
            }

            Assert.That(
                JigsawStructureGenerator.CachedLayoutCount,
                Is.LessThanOrEqualTo(JigsawStructureGenerator.LayoutCacheCapacity));
        }

        [Test]
        public void Definitions_PassSocketAndPoolGraphValidation()
        {
            string[] paths =
            {
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw,
                ProjectAssetPaths.Config.StrongholdJigsaw,
            };
            foreach (string path in paths)
            {
                JigsawStructureFeatureSettings settings = LoadSettings(path);
                Assert.That(
                    JigsawStructureValidator.Validate(settings),
                    Is.Empty,
                    settings.StableId);
            }
        }

        [Test]
        public void NetherFortress_ElevatorCorridorsUseLowProbabilityDoorPools()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.NetherFortressJigsaw);
            JigsawPieceSettings junction = settings.Pieces.First(piece =>
                piece.StableId == "fortress_junction");
            JigsawConnectorSettings[] liftOutputs = junction.Connectors
                .Where(connector => connector.CanEmitOutput
                    && (connector.TargetPoolId == "vertical_up"
                        || connector.TargetPoolId == "vertical_down"))
                .ToArray();
            JigsawConnectorSettings[] ordinaryOutputs = junction.Connectors
                .Where(connector => connector.CanEmitOutput
                    && connector.TargetPoolId != "vertical_up"
                    && connector.TargetPoolId != "vertical_down")
                .ToArray();

            Assert.That(
                junction.Decoration,
                Is.EqualTo(JigsawPieceDefinition.Decoration.None));
            Assert.That(liftOutputs, Has.Length.EqualTo(2));
            Assert.That(
                liftOutputs.Max(connector => connector.ActivationChance),
                Is.LessThan(ordinaryOutputs.Min(
                    connector => connector.ActivationChance)));
            Assert.That(
                liftOutputs.Select(connector => connector.TargetPoolId),
                Is.EquivalentTo(new[] { "vertical_up", "vertical_down" }));
            Assert.That(
                liftOutputs.All(connector =>
                    connector.Face == JigsawConnectorDefinition.Face.Forward
                    && connector.VerticalOffset == 1
                    && connector.OpeningWidth == 3
                    && connector.OpeningHeight == 5),
                Is.True,
                "The source room must use ordinary wall doors, not floor holes.");
            Assert.That(
                liftOutputs.Select(connector => connector.LateralOffset),
                Is.EquivalentTo(new[] { -9, 9 }));

            JigsawPieceSettings[] shafts = settings.Pieces
                .Where(piece => piece.StableId
                    == "fortress_vertical_up_shaft"
                    || piece.StableId
                        == "fortress_vertical_down_shaft")
                .ToArray();
            Assert.That(shafts, Has.Length.EqualTo(2));
            Assert.That(
                shafts.All(piece =>
                    piece.Shape
                        == JigsawPieceDefinition.Shape.VerticalShaft
                    && piece.Decoration
                        == JigsawPieceDefinition.Decoration.None),
                Is.True);
            Assert.That(
                shafts.All(piece =>
                    piece.MinimumWidth == 7
                    && piece.MaximumWidth == 7
                    && piece.MinimumDepth == 7
                    && piece.MaximumDepth == 7
                    && piece.MinimumHeight == 16
                    && piece.MaximumHeight == 16),
                Is.True,
                "Elevator corridors use a compact, empty seven-by-seven shaft.");
            Assert.That(
                shafts.SelectMany(piece => piece.Connectors)
                    .All(connector =>
                        connector.Face != JigsawConnectorDefinition.Face.Up
                        && connector.Face != JigsawConnectorDefinition.Face.Down
                        && connector.OpeningWidth == 3
                        && connector.OpeningHeight == 5),
                Is.True,
                "The shaft may only expose three-by-five wall doors.");
            foreach (JigsawPieceSettings shaft in shafts)
            {
                JigsawConnectorSettings input = shaft.Connectors.Single(
                    connector => connector.Role
                        == JigsawConnectorDefinition.Role.Input);
                JigsawConnectorSettings output = shaft.Connectors.Single(
                    connector => connector.Role
                        == JigsawConnectorDefinition.Role.Output);
                Assert.That(input.Face, Is.EqualTo(
                    JigsawConnectorDefinition.Face.Back));
                Assert.That(output.Face, Is.EqualTo(
                    JigsawConnectorDefinition.Face.Forward));
                Assert.That(
                    Math.Abs(output.VerticalOffset - input.VerticalOffset),
                    Is.EqualTo(10),
                    "The two wall doors must connect rooms ten voxels apart.");
            }

            JigsawPieceSettings landing = settings.Pieces.First(piece =>
                piece.StableId == "fortress_vertical_landing");
            Assert.That(landing.PoolId, Is.EqualTo("vertical_landing"));
            Assert.That(
                landing.Decoration,
                Is.EqualTo(JigsawPieceDefinition.Decoration.None));
            Assert.That(
                landing.MinimumWidth == 9
                && landing.MaximumWidth == 9
                && landing.MinimumDepth == 9
                && landing.MaximumDepth == 9
                && landing.MinimumHeight == 7
                && landing.MaximumHeight == 7,
                Is.True,
                "The far side only needs a compact landing room.");
            Assert.That(
                landing.Connectors.Where(connector => connector.CanAcceptInput)
                    .All(connector =>
                        connector.Face
                            == JigsawConnectorDefinition.Face.Back
                        && connector.VerticalOffset == 1),
                Is.True);
            Assert.That(
                junction.Connectors.Concat(shafts.SelectMany(
                        piece => piece.Connectors))
                    .Concat(landing.Connectors)
                    .All(connector =>
                        connector.Face != JigsawConnectorDefinition.Face.Up
                        && connector.Face
                            != JigsawConnectorDefinition.Face.Down),
                Is.True,
                "NetherFortress elevator chains must never carve floors or ceilings.");
        }

        [Test]
        public void NetherFortress_ElevatorCorridorsKeepFloorsAndConnectRooms()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.NetherFortressJigsaw);
            int upCorridors = 0;
            int downCorridors = 0;

            for (int seed = 0; seed < 512; seed++)
            {
                if (!JigsawStructureGenerator.TryGetPlacement(
                        settings,
                        seed,
                        0,
                        0,
                        out JigsawStructureGenerator.Placement placement))
                {
                    continue;
                }

                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(
                        settings,
                        seed,
                        placement);
                for (int pieceIndex = 0;
                    pieceIndex < layout.Count;
                    pieceIndex++)
                {
                    JigsawStructureGenerator.Piece shaft =
                        layout[pieceIndex];
                    bool rising = shaft.ModuleId
                        == "fortress_vertical_up_shaft";
                    bool descending = shaft.ModuleId
                        == "fortress_vertical_down_shaft";
                    if (!rising && !descending)
                    {
                        continue;
                    }

                    if (rising) upCorridors++;
                    if (descending) downCorridors++;
                    Assert.That(shaft.ParentIndex, Is.GreaterThanOrEqualTo(0));
                    JigsawStructureGenerator.Piece sourceRoom =
                        layout[shaft.ParentIndex];
                    Assert.That(
                        sourceRoom.ModuleId,
                        Is.EqualTo("fortress_junction"));

                    int[] landingIndices = Enumerable.Range(0, layout.Count)
                        .Where(index => layout[index].ParentIndex == pieceIndex
                            && layout[index].ModuleId
                                == "fortress_vertical_landing")
                        .ToArray();
                    Assert.That(
                        landingIndices,
                        Has.Length.EqualTo(1),
                        $"Seed {seed} corridor '{shaft.ModuleId}' must have "
                            + "one far-side landing.");
                    JigsawStructureGenerator.Piece landing =
                        layout[landingIndices[0]];
                    int expectedFloorDelta = rising ? 10 : -10;
                    Assert.That(
                        landing.Bounds.MinY - sourceRoom.Bounds.MinY,
                        Is.EqualTo(expectedFloorDelta));

                    Assert.That(shaft.Openings.Count, Is.EqualTo(2));
                    foreach (JigsawStructureGenerator.Opening opening
                        in shaft.Openings)
                    {
                        Assert.That(
                            opening.Direction,
                            Is.LessThan((int)JigsawConnectorDefinition.Face.Up));
                        Assert.That(
                            GenerateAndGetType(settings, seed, opening.Boundary)
                                .IsAir,
                            Is.True,
                            $"Seed {seed} wall door at {opening.Boundary} "
                                + "must be open.");
                    }

                    Vector3Int sourceFloor = sourceRoom.Origin;
                    sourceFloor.y = sourceRoom.Bounds.MinY;
                    Vector3Int shaftFloor = shaft.Origin;
                    shaftFloor.y = shaft.Bounds.MinY;
                    Vector3Int shaftCeiling = shaft.Origin;
                    shaftCeiling.y = shaft.Bounds.MaxY;
                    Vector3Int landingFloor = landing.Origin;
                    landingFloor.y = landing.Bounds.MinY;
                    Assert.That(
                        GenerateAndGetType(settings, seed, sourceFloor).IsAir,
                        Is.False,
                        "The source-room floor must remain intact.");
                    Assert.That(
                        GenerateAndGetType(settings, seed, shaftFloor).IsAir,
                        Is.False,
                        "The elevator corridor needs a solid bottom floor.");
                    Assert.That(
                        GenerateAndGetType(settings, seed, shaftCeiling).IsAir,
                        Is.False,
                        "The elevator corridor needs a solid top ceiling.");
                    Assert.That(
                        GenerateAndGetType(settings, seed, landingFloor).IsAir,
                        Is.False,
                        "The far-side room floor must remain intact.");
                }
            }

            Assert.That(upCorridors, Is.GreaterThan(0));
            Assert.That(downCorridors, Is.GreaterThan(0));
        }

        [Test]
        public void SpiralShaft_FitsPlayerAndConnectsBothFloors()
        {
            JigsawStructureFeatureSettings settings = BuildSpiralShaftFixture();
            const int seed = 73193;
            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(settings, seed, placement);

            Assert.That(layout.Count, Is.EqualTo(3));
            JigsawStructureGenerator.Piece lower = layout[0];
            JigsawStructureGenerator.Piece shaft = layout.Single(piece =>
                piece.ModuleId == "spiral_shaft");
            JigsawStructureGenerator.Piece upper = layout.Single(piece =>
                piece.ModuleId == "spiral_upper");
            Assert.That(
                shaft.Bounds.MinY,
                Is.EqualTo(lower.Bounds.MaxY + 1));
            Assert.That(
                upper.Bounds.MinY,
                Is.EqualTo(shaft.Bounds.MaxY + 1));

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);
            MinecraftWorldGenerationConfiguration worldConfiguration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    ProjectAssetPaths.Config.WorldGeneration);
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(worldConfiguration, Is.Not.Null);
            CharacterController controller =
                playerPrefab.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null);

            float requiredWidth = controller.radius * 2f
                + controller.skinWidth * 2f;
            Assert.That(
                3f * worldConfiguration.VoxelSize,
                Is.GreaterThan(requiredWidth),
                "The authored three-voxel ramp must be wider than the player's capsule.");
            Assert.That(
                0.5f * worldConfiguration.VoxelSize,
                Is.LessThanOrEqualTo(controller.stepOffset),
                "The final half-voxel transition onto the room floor must fit the player's step offset.");
            Assert.That(
                SpiralRampFitsCharacterController(
                    settings,
                    seed,
                    shaft,
                    upper,
                    controller,
                    worldConfiguration.VoxelSize,
                    out string clearanceError),
                Is.True,
                "Shaft-to-room ramp: " + clearanceError);
            Assert.That(
                SpiralRampFitsCharacterController(
                    settings,
                    seed,
                    lower,
                    shaft,
                    controller,
                    worldConfiguration.VoxelSize,
                    out string landingClearanceError),
                Is.True,
                "Landing-to-shaft ramp: " + landingClearanceError);
        }

        [Test]
        public void DenseJigsawWorld_ElevatorCorridorsKeepFloorsAndDoorConnections()
        {
            DenseJigsawWorldConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<DenseJigsawWorldConfiguration>(
                    ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.PreventStructureIntersections, Is.True);
            Assert.That(
                DenseJigsawFeatureMixer.TryBuild(
                    configuration,
                    out DenseJigsawFeature feature,
                    out string error),
                Is.True,
                error);

            int seed = configuration.InfiniteCavesLevelSource.WorldSeed;
            int selectionRadius = configuration.LayoutRadius * 2;
            int testedCorridors = 0;

            for (int regionX = -4; regionX <= 4; regionX++)
            {
                for (int regionZ = -4; regionZ <= 4; regionZ++)
                {
                    if (!JigsawStructureGenerator.TryGetPlacement(
                            feature.Settings,
                            seed,
                            regionX,
                            regionZ,
                            out JigsawStructureGenerator.Placement placement))
                    {
                        continue;
                    }

                    JigsawPlacementSelection selection =
                        JigsawPlacementSelection.CreateNonIntersecting(
                            new[] { feature.Settings },
                            seed,
                            placement.Centre.x - selectionRadius,
                            placement.Centre.z - selectionRadius,
                            placement.Centre.x + selectionRadius,
                            placement.Centre.z + selectionRadius);
                    if (!selection.Allows(feature.Settings, placement))
                    {
                        continue;
                    }

                    IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                        JigsawStructureGenerator.BuildLayout(
                            feature.Settings,
                            seed,
                            placement);
                    var generatedColumns =
                        new Dictionary<Vector3Int, GeneratedColumnSamples>();
                    for (int pieceIndex = 0;
                        pieceIndex < layout.Count;
                        pieceIndex++)
                    {
                        JigsawStructureGenerator.Piece shaft =
                            layout[pieceIndex];
                        JigsawPieceSettings module =
                            feature.Settings.GetPiece(shaft.ModuleIndex);
                        bool elevatorShaft = shaft.ModuleId
                                == "nether_fortress__fortress_vertical_up_shaft"
                            || shaft.ModuleId
                                == "nether_fortress__fortress_vertical_down_shaft";
                        if (!elevatorShaft)
                        {
                            continue;
                        }

                        testedCorridors++;
                        Assert.That(
                            module.Decoration,
                            Is.EqualTo(JigsawPieceDefinition.Decoration.None));
                        Assert.That(shaft.Openings.Count, Is.EqualTo(2));
                        Assert.That(
                            shaft.Openings.All(opening =>
                                opening.Direction
                                    < (int)JigsawConnectorDefinition.Face.Up),
                            Is.True,
                            "Elevator corridors may only use wall doors.");

                        int[] landingIndices = Enumerable.Range(0, layout.Count)
                            .Where(index =>
                                layout[index].ParentIndex == pieceIndex
                                && layout[index].ModuleId
                                    == "nether_fortress__fortress_vertical_landing")
                            .ToArray();
                        Assert.That(
                            landingIndices,
                            Has.Length.EqualTo(1),
                            $"Dense layout ({regionX}, {regionZ}) shaft "
                                + $"'{shaft.ModuleId}' needs one landing.");
                        JigsawStructureGenerator.Piece landing =
                            layout[landingIndices[0]];
                        JigsawStructureGenerator.Piece sourceRoom =
                            layout[shaft.ParentIndex];
                        Assert.That(
                            Math.Abs(
                                landing.Bounds.MinY
                                    - sourceRoom.Bounds.MinY),
                            Is.EqualTo(10));

                        foreach (JigsawStructureGenerator.Opening opening
                            in shaft.Openings)
                        {
                            GetGeneratedSample(
                                feature.Settings,
                                seed,
                                opening.Boundary,
                                generatedColumns,
                                selection,
                                out float doorDensity,
                                out VoxelTypeId doorType);
                            Assert.That(
                                doorDensity < 0f || doorType.IsAir,
                                Is.True,
                                $"Dense elevator door {opening.Boundary} is sealed.");
                        }

                        Vector3Int[] solidSamples =
                        {
                            new Vector3Int(
                                sourceRoom.Origin.x,
                                sourceRoom.Bounds.MinY,
                                sourceRoom.Origin.z),
                            new Vector3Int(
                                shaft.Origin.x,
                                shaft.Bounds.MinY,
                                shaft.Origin.z),
                            new Vector3Int(
                                shaft.Origin.x,
                                shaft.Bounds.MaxY,
                                shaft.Origin.z),
                            new Vector3Int(
                                landing.Origin.x,
                                landing.Bounds.MinY,
                                landing.Origin.z),
                        };
                        for (int sampleIndex = 0;
                            sampleIndex < solidSamples.Length;
                            sampleIndex++)
                        {
                            GetGeneratedSample(
                                feature.Settings,
                                seed,
                                solidSamples[sampleIndex],
                                generatedColumns,
                                selection,
                                out float density,
                                out VoxelTypeId type);
                            Assert.That(
                                density >= 0f && !type.IsAir,
                                Is.True,
                                $"Dense elevator solid surface "
                                    + $"{solidSamples[sampleIndex]} is missing.");
                        }
                    }
                }
            }

            Assert.That(
                testedCorridors,
                Is.GreaterThan(0),
                "The production DenseJigsaw seed produced no elevator corridors.");
        }

        [Test]
        public void UnconnectedVerticalOutput_DoesNotOpenSourceRoom()
        {
            JigsawStructureFeatureSettings settings =
                BuildSpiralShaftFixture(maximumPieces: 2);
            const int seed = 73193;
            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(settings, seed, placement);

            Assert.That(layout.Count, Is.EqualTo(1));
            JigsawStructureGenerator.Piece room = layout.Single();
            Assert.That(room.ModuleId, Is.EqualTo("spiral_lower"));
            Assert.That(
                room.Openings.Any(opening =>
                    opening.Direction
                        == (int)JigsawConnectorDefinition.Face.Up),
                Is.False,
                "A shaft without budget for its far-side room must be rejected "
                    + "before the source opening is committed.");

            Vector3Int ceilingCentre = room.Origin;
            ceilingCentre.y = room.Bounds.MaxY;
            Assert.That(
                GenerateAndGetType(settings, seed, ceilingCentre).IsAir,
                Is.False,
                "A rejected vertical branch must retain the source-room ceiling.");
        }

        [Test]
        public void VerticalSocket_StacksPiecesAndCarvesBothSurfaces()
        {
            JigsawStructureFeatureSettings settings =
                BuildVerticalSocketFixture();
            const int seed = 73191;
            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(settings, seed, placement);

            Assert.That(layout.Count, Is.EqualTo(2));
            JigsawStructureGenerator.Piece lower = layout[0];
            JigsawStructureGenerator.Piece upper = layout[1];
            Assert.That(upper.ParentIndex, Is.EqualTo(0));
            Assert.That(
                upper.Direction,
                Is.EqualTo(lower.Direction),
                "Vertical children must preserve the parent's yaw so authored "
                    + "ramp lanes meet without crossing inside the aperture.");
            Assert.That(
                upper.Bounds.MinY,
                Is.EqualTo(lower.Bounds.MaxY + 1));

            JigsawStructureGenerator.Opening ceiling =
                lower.Openings.Single(opening =>
                    opening.Direction
                        == (int)JigsawConnectorDefinition.Face.Up);
            JigsawStructureGenerator.Opening floor =
                upper.Openings.Single(opening =>
                    opening.Direction
                        == (int)JigsawConnectorDefinition.Face.Down);
            Assert.That(ceiling.Boundary.y, Is.EqualTo(lower.Bounds.MaxY));
            Assert.That(floor.Boundary.y, Is.EqualTo(upper.Bounds.MinY));
            Assert.That(
                GenerateAndGetType(settings, seed, ceiling.Boundary),
                Is.EqualTo(VoxelTypeId.Air));
            Assert.That(
                GenerateAndGetType(settings, seed, floor.Boundary),
                Is.EqualTo(VoxelTypeId.Air));

            GetAxes(lower.Direction, out _, out Vector3Int right);
            Assert.That(
                GenerateAndGetType(
                    settings,
                    seed,
                    ceiling.Boundary + right * 2),
                Is.EqualTo(StructureBrick),
                "Only the authored 3-wide ceiling aperture should be carved.");
        }

        [Test]
        public void DownwardVerticalSocket_StacksChildBelowParent()
        {
            JigsawStructureFeatureSettings settings =
                BuildVerticalSocketFixture(downward: true);
            const int seed = 73192;
            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    seed,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(settings, seed, placement);

            Assert.That(layout.Count, Is.EqualTo(2));
            JigsawStructureGenerator.Piece parent = layout[0];
            JigsawStructureGenerator.Piece child = layout[1];
            Assert.That(
                child.Bounds.MaxY,
                Is.EqualTo(parent.Bounds.MinY - 1));
            Assert.That(
                parent.Openings.Any(opening =>
                    opening.Direction
                        == (int)JigsawConnectorDefinition.Face.Down),
                Is.True);
            Assert.That(
                child.Openings.Any(opening =>
                    opening.Direction
                        == (int)JigsawConnectorDefinition.Face.Up),
                Is.True);
        }

        [Test]
        public void FortressSideSocket_CarvesConfiguredMasonryOpening()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.StrongholdJigsaw);
            const int seed = 666;
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                FindLayoutContaining(settings, seed, "stronghold_hall");
            JigsawStructureGenerator.Piece hall = layout.First(piece =>
                piece.ModuleId == "stronghold_hall"
                && piece.Openings.Count >= 3);
            JigsawStructureGenerator.Opening sideOpening = hall.Openings.First(
                opening => opening.Direction != hall.Direction
                    && opening.Direction != ((hall.Direction + 2) & 3));

            Assert.That(
                GenerateAndGetType(settings, seed, sideOpening.Boundary),
                Is.EqualTo(VoxelTypeId.Air));
            Assert.That(
                GenerateAndGetType(
                    settings,
                    seed,
                    sideOpening.Boundary + Vector3Int.up * 2),
                Is.EqualTo(VoxelTypeId.Air));
        }

        [Test]
        public void TemplatePiece_RotatesAndWritesAuthoredPaletteAndAir()
        {
            var template = ScriptableObject.CreateInstance<VoxelStructureAsset>();
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var size = new Vector3Int(5, 4, 7);
                var anchor = new Vector3Int(2, 0, 3);
                int sampleCount = size.x * size.y * size.z;
                float[] templateDensities = Enumerable.Repeat(
                    -1f,
                    sampleCount).ToArray();
                ushort[] templateTypes = new ushort[sampleCount];
                int solidIndex = 0 + size.x * (0 + size.y * 0);
                templateDensities[solidIndex] = 0.75f;
                templateTypes[solidIndex] = 9;
                template.SetData(
                    size,
                    anchor,
                    Vector3.zero,
                    templateDensities,
                    templateTypes);

                var start = new JigsawPieceDefinition();
                start.ConfigureBox(
                    "template_start",
                    "Template Start",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    7,
                    7,
                    7,
                    7,
                    5,
                    5);
                start.ConfigureTemplate(template, true);
                var output = new JigsawConnectorDefinition();
                output.Configure(
                    "exit",
                    JigsawConnectorDefinition.Role.Output,
                    JigsawConnectorDefinition.Face.Forward,
                    "template_branch",
                    "template_entry",
                    "child",
                    -1,
                    0,
                    1,
                    3,
                    3);
                start.AddConnector(output);

                var child = new JigsawPieceDefinition();
                child.ConfigurePassage(
                    "template_child",
                    "Template Child",
                    JigsawPieceDefinition.Shape.Corridor,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    1,
                    1,
                    1,
                    3,
                    3,
                    3,
                    4,
                    1,
                    0f,
                    0.5f,
                    3,
                    "child",
                    "child");
                var input = new JigsawConnectorDefinition();
                input.Configure(
                    "entrance",
                    JigsawConnectorDefinition.Role.Input,
                    JigsawConnectorDefinition.Face.Back,
                    "template_entry",
                    "template_branch",
                    "child",
                    -1,
                    0,
                    1,
                    3,
                    3);
                child.AddConnector(input);

                VoxelTypeDefinition primary =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel);
                definition.Configure(
                    true,
                    "template_test",
                    primary,
                    primary,
                    7127,
                    4,
                    1f,
                    64,
                    64,
                    2,
                    1,
                    16,
                    string.Empty,
                    new[] { start, child });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    42,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement);
                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(settings, 42, placement);
                JigsawStructureGenerator.Piece templatePiece = layout[0];
                GetAxes(
                    templatePiece.Direction,
                    out Vector3Int forward,
                    out Vector3Int right);
                Vector3Int solidWorld = templatePiece.Origin
                    + right * (0 - anchor.x)
                    + forward * (0 - anchor.z);
                Vector3Int airWorld = templatePiece.Origin + Vector3Int.up;

                Assert.That(
                    GenerateAndGetType(settings, 42, solidWorld),
                    Is.EqualTo(new VoxelTypeId(9)));
                Assert.That(
                    GenerateAndGetType(settings, 42, airWorld),
                    Is.EqualTo(VoxelTypeId.Air));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void MineshaftSupportFrame_LeavesConfiguredThreeVoxelOpening()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
            const int seed = 114514;
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                FindLayoutContaining(settings, seed, "mineshaft_corridor");
            JigsawStructureGenerator.Piece corridor = layout.First(piece =>
                piece.ModuleId == "mineshaft_corridor");
            JigsawPieceSettings module = settings.GetPiece(corridor.ModuleIndex);
            GetAxes(corridor.Direction, out Vector3Int forward, out Vector3Int right);
            Vector3Int frameCentre = corridor.Origin
                + forward * module.DecorationSpacing
                + Vector3Int.up;
            Vector3Int[] samples =
            {
                frameCentre,
                frameCentre + right,
                frameCentre - right,
                frameCentre + right * 2,
                frameCentre - right * 2,
            };
            VoxelTypeId[] actual = samples
                .Select(sample => GenerateAndGetType(settings, seed, sample))
                .ToArray();
            // Support posts are an accent decoration, so read the palette from the
            // asset rather than pinning a type id the designer may retune.
            Assert.That(
                actual,
                Is.EqualTo(new[]
                {
                    VoxelTypeId.Air,
                    VoxelTypeId.Air,
                    VoxelTypeId.Air,
                    settings.AccentType,
                    settings.AccentType,
                }),
                $"Corridor {corridor.Origin} direction {corridor.Direction}; "
                + $"samples: {string.Join(", ", samples)}");
        }

        [Test]
        public void FortressGeneration_BuildsLobbyShellAndLibraryShelves()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.StrongholdJigsaw);
            const int seed = 114514;
            JigsawStructureGenerator.Placement placement = default;
            JigsawStructureGenerator.Piece library = default;
            bool found = false;
            var placements = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                -400_000,
                -400_000,
                400_000,
                400_000,
                placements);
            foreach (JigsawStructureGenerator.Placement candidatePlacement
                in placements)
            {
                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(
                        settings,
                        seed,
                        candidatePlacement);
                JigsawStructureGenerator.Piece? candidate = layout
                    .Where(piece => piece.ModuleId == "stronghold_library")
                    .Cast<JigsawStructureGenerator.Piece?>()
                    .FirstOrDefault();
                if (candidate.HasValue)
                {
                    placement = candidatePlacement;
                    library = candidate.Value;
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True);
            Vector3Int lobbyFloor = placement.Centre;
            Vector3Int lobbyInterior = placement.Centre + Vector3Int.up;
            Vector3Int libraryShelf = new Vector3Int(
                library.Bounds.MinX + 1,
                library.Bounds.MinY + 1,
                library.Bounds.MinZ + 2);
            Vector3Int libraryInterior = new Vector3Int(
                library.Bounds.MinX + 2,
                library.Bounds.MinY + 1,
                library.Bounds.MinZ + 2);

            Assert.That(GenerateAndGetType(settings, seed, lobbyFloor),
                Is.EqualTo(settings.PrimaryType));
            Assert.That(GenerateAndGetType(settings, seed, lobbyInterior),
                Is.EqualTo(VoxelTypeId.Air));
            // Shelves are an accent decoration, so they use the accent palette
            // rather than the shell palette.
            Assert.That(GenerateAndGetType(settings, seed, libraryShelf),
                Is.EqualTo(settings.AccentType));
            Assert.That(GenerateAndGetType(settings, seed, libraryInterior),
                Is.EqualTo(VoxelTypeId.Air));
        }

        [Test]
        public void SupportProcessor_FillsGapDownToTerrainAndStopsThere()
        {
            JigsawStructureFeatureSettings settings = BuildProcessorFixture(
                JigsawProcessorDefinition.Kind.SupportToGround,
                24,
                1f,
                JigsawProcessorDefinition.Palette.Primary,
                perimeterOnly: false,
                out int floorY);
            Vector3Int column = GetProcessorColumn(settings, floorY);
            int terrainTopY = floorY - 6;
            GenerateColumnOverTerrain(
                settings,
                ProcessorSeed,
                column,
                terrainTopY,
                out float[] densities,
                out VoxelTypeId[] types);
            Vector3Int centre = GetProcessorCentre(settings, floorY);

            // Every voxel between the piece floor and the terrain top becomes a
            // support column of the structure palette.
            for (int y = floorY - 1; y > terrainTopY; y--)
            {
                Assert.That(
                    GetWorldType(
                        types,
                        column,
                        new Vector3Int(centre.x, y, centre.z)),
                    Is.EqualTo(StructureBrick),
                    $"support voxel at y={y}");
            }
            // The column stops at the surface instead of boring into terrain.
            Assert.That(
                GetWorldType(
                    types,
                    column,
                    new Vector3Int(centre.x, terrainTopY, centre.z)),
                Is.EqualTo(Stone));
        }

        [Test]
        public void FoundationProcessor_WritesFixedSlabWithoutReachingTerrain()
        {
            const int depth = 3;
            JigsawStructureFeatureSettings settings = BuildProcessorFixture(
                JigsawProcessorDefinition.Kind.FoundationFill,
                depth,
                1f,
                JigsawProcessorDefinition.Palette.Primary,
                perimeterOnly: false,
                out int floorY);
            Vector3Int column = GetProcessorColumn(settings, floorY);
            GenerateColumnOverTerrain(
                settings,
                ProcessorSeed,
                column,
                floorY - 20,
                out float[] densities,
                out VoxelTypeId[] types);
            Vector3Int centre = GetProcessorCentre(settings, floorY);

            for (int step = 1; step <= depth; step++)            {
                Assert.That(
                    GetWorldType(
                        types,
                        column,
                        new Vector3Int(centre.x, floorY - step, centre.z)),
                    Is.EqualTo(StructureBrick),
                    $"foundation voxel at depth {step}");
            }
            Assert.That(
                GetWorldType(
                    types,
                    column,
                    new Vector3Int(centre.x, floorY - depth - 1, centre.z)),
                Is.EqualTo(VoxelTypeId.Air),
                "foundation must not exceed its configured depth");
        }

        [Test]
        public void ClearAboveProcessor_CarvesHeadroomOverThePieceCeiling()
        {
            const int headroom = 4;
            JigsawStructureFeatureSettings settings = BuildProcessorFixture(
                JigsawProcessorDefinition.Kind.ClearAbove,
                headroom,
                1f,
                JigsawProcessorDefinition.Palette.Primary,
                perimeterOnly: false,
                out int floorY);
            Vector3Int column = GetProcessorColumn(settings, floorY);
            GenerateColumnOverTerrain(
                settings,
                ProcessorSeed,
                column,
                VoxelColumnChunkData.Height - 2,
                out float[] densities,
                out VoxelTypeId[] types);
            Vector3Int centre = GetProcessorCentre(settings, floorY);
            JigsawStructureGenerator.Piece start = GetProcessorStartPiece(
                settings,
                floorY);

            for (int step = 1; step <= headroom; step++)
            {
                Assert.That(
                    GetWorldType(
                        types,
                        column,
                        new Vector3Int(
                            centre.x,
                            start.Bounds.MaxY + step,
                            centre.z)),
                    Is.EqualTo(VoxelTypeId.Air),
                    $"headroom voxel {step} above the ceiling");
            }
            Assert.That(
                GetWorldType(
                    types,
                    column,
                    new Vector3Int(
                        centre.x,
                        start.Bounds.MaxY + headroom + 1,
                        centre.z)),
                Is.EqualTo(Stone),
                "clearing must not exceed its configured distance");
        }

        [Test]
        public void WeatheringProcessor_IsDeterministicAndOnlyTouchesStructureVoxels()
        {
            JigsawStructureFeatureSettings settings = BuildProcessorFixture(
                JigsawProcessorDefinition.Kind.Weathering,
                1,
                0.5f,
                JigsawProcessorDefinition.Palette.Accent,
                perimeterOnly: false,
                out int floorY);
            Vector3Int column = GetProcessorColumn(settings, floorY);
            GenerateColumnOverTerrain(
                settings,
                ProcessorSeed,
                column,
                floorY - 20,
                out float[] first,
                out VoxelTypeId[] firstTypes);
            GenerateColumnOverTerrain(
                settings,
                ProcessorSeed,
                column,
                floorY - 20,
                out float[] second,
                out VoxelTypeId[] secondTypes);

            Assert.That(secondTypes, Is.EqualTo(firstTypes));
            JigsawStructureGenerator.Piece start = GetProcessorStartPiece(
                settings,
                floorY);
            int weathered = 0;
            int shell = 0;
            for (int y = start.Bounds.MinY; y <= start.Bounds.MaxY; y++)
            {
                for (int z = start.Bounds.MinZ; z <= start.Bounds.MaxZ; z++)
                {
                    for (int x = start.Bounds.MinX; x <= start.Bounds.MaxX; x++)
                    {
                        var world = new Vector3Int(x, y, z);
                        if (!IsInsideColumn(column, world))
                        {
                            continue;
                        }
                        VoxelTypeId type = GetWorldType(
                            firstTypes,
                            column,
                            world);
                        if (type == FortressBrick) weathered++;
                        else if (type == StructureBrick) shell++;
                    }
                }
            }

            Assert.That(weathered, Is.GreaterThan(0), "weathering never applied");
            Assert.That(shell, Is.GreaterThan(0), "weathering replaced everything");
            // Space below the piece is untouched: weathering only recolours
            // voxels this structure wrote, never surrounding terrain or air.
            Assert.That(
                GetWorldType(
                    firstTypes,
                    column,
                    new Vector3Int(
                        start.Origin.x,
                        start.Bounds.MinY - 2,
                        start.Origin.z)),
                Is.EqualTo(VoxelTypeId.Air));
        }

        [Test]
        public void Processors_DoNotAffectLayoutCollisionDecisions()
        {
            JigsawStructureFeatureSettings without = LoadSettings(
                ProjectAssetPaths.Config.NetherFortressJigsaw);
            JigsawStructureFeatureDefinition definition =
                AssetDatabase.LoadAssetAtPath<JigsawStructureFeatureDefinition>(
                    ProjectAssetPaths.Config.NetherFortressJigsaw);
            Assert.That(
                definition.Pieces.Any(piece => piece.Processors.Count > 0),
                Is.True,
                "the structure should ship with authored processors");

            const int seed = 99001;
            JigsawStructureGenerator.TryGetPlacement(
                without,
                seed,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(without, seed, placement);

            // A support reaching far below a piece must never inflate the bounds
            // used for collision, otherwise deep pillars would reject neighbours.
            foreach (JigsawStructureGenerator.Piece piece in layout)
            {
                JigsawPieceSettings module = without.GetPiece(piece.ModuleIndex);
                if (module.ProcessorDownwardReach <= 0)
                {
                    continue;
                }
                Assert.That(
                    piece.Bounds.MinY,
                    Is.GreaterThanOrEqualTo(placement.Centre.y
                        - without.MaxDepth * module.VerticalDelta),
                    $"piece '{piece.ModuleId}' bounds grew with its processor reach");
            }
        }

        private const int ProcessorSeed = 20260804;

        private static JigsawStructureFeatureSettings BuildSpiralShaftFixture(
            int maximumPieces = 3)
        {
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var lower = new JigsawPieceDefinition();
                lower.ConfigureBox(
                    "spiral_lower",
                    "Spiral Lower",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.SpiralStairs,
                    true,
                    0,
                    0,
                    0,
                    21,
                    21,
                    21,
                    21,
                    9,
                    9);
                var lowerOutput = new JigsawConnectorDefinition();
                lowerOutput.Configure(
                    "to_shaft",
                    JigsawConnectorDefinition.Role.Output,
                    JigsawConnectorDefinition.Face.Up,
                    "shaft_up",
                    "shaft_down",
                    "shaft",
                    -1,
                    0,
                    0,
                    7,
                    7);
                lower.AddConnector(lowerOutput);

                var shaft = new JigsawPieceDefinition();
                shaft.ConfigureBox(
                    "spiral_shaft",
                    "Spiral Shaft",
                    JigsawPieceDefinition.Shape.VerticalShaft,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.SpiralStairs,
                    false,
                    1,
                    1,
                    1,
                    21,
                    21,
                    21,
                    21,
                    12,
                    12,
                    "shaft",
                    "landing");
                var shaftInput = new JigsawConnectorDefinition();
                shaftInput.Configure(
                    "from_lower",
                    JigsawConnectorDefinition.Role.Input,
                    JigsawConnectorDefinition.Face.Down,
                    "shaft_down",
                    "shaft_up",
                    "shaft",
                    -1,
                    0,
                    0,
                    7,
                    7);
                shaft.AddConnector(shaftInput);
                var shaftOutput = new JigsawConnectorDefinition();
                shaftOutput.Configure(
                    "to_upper",
                    JigsawConnectorDefinition.Role.Output,
                    JigsawConnectorDefinition.Face.Up,
                    "landing_up",
                    "landing_down",
                    "landing",
                    -1,
                    0,
                    0,
                    7,
                    7);
                shaft.AddConnector(shaftOutput);

                var upper = new JigsawPieceDefinition();
                upper.ConfigureBox(
                    "spiral_upper",
                    "Spiral Upper",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    false,
                    1,
                    2,
                    2,
                    21,
                    21,
                    21,
                    21,
                    7,
                    7,
                    "landing",
                    "landing");
                var upperInput = new JigsawConnectorDefinition();
                upperInput.Configure(
                    "from_shaft",
                    JigsawConnectorDefinition.Role.Input,
                    JigsawConnectorDefinition.Face.Down,
                    "landing_down",
                    "landing_up",
                    "landing",
                    -1,
                    0,
                    0,
                    7,
                    7);
                upper.AddConnector(upperInput);
                var upperFoundation = new JigsawProcessorDefinition();
                upperFoundation.Configure(
                    "upper_foundation",
                    JigsawProcessorDefinition.Kind.FoundationFill,
                    2,
                    1f,
                    JigsawProcessorDefinition.Palette.Primary,
                    1,
                    false);
                upper.AddProcessor(upperFoundation);

                definition.Configure(
                    true,
                    "spiral_shaft_fixture",
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel),
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.FortressBrickVoxel),
                    73193,
                    4,
                    1f,
                    80,
                    80,
                    maximumPieces,
                    2,
                    16,
                    string.Empty,
                    new[] { lower, shaft, upper });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                return settings;
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static JigsawStructureFeatureSettings BuildVerticalSocketFixture(
            bool downward = false)
        {
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var lower = new JigsawPieceDefinition();
                lower.ConfigureBox(
                    "vertical_lower",
                    "Vertical Lower",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    9,
                    9,
                    9,
                    9,
                    7,
                    7);
                JigsawConnectorDefinition.Face outputFace = downward
                    ? JigsawConnectorDefinition.Face.Down
                    : JigsawConnectorDefinition.Face.Up;
                JigsawConnectorDefinition.Face inputFace = downward
                    ? JigsawConnectorDefinition.Face.Up
                    : JigsawConnectorDefinition.Face.Down;
                string outputName = downward
                    ? "vertical_down"
                    : "vertical_up";
                string inputName = downward
                    ? "vertical_up"
                    : "vertical_down";
                var output = new JigsawConnectorDefinition();
                output.Configure(
                    "vertical_output",
                    JigsawConnectorDefinition.Role.Output,
                    outputFace,
                    outputName,
                    inputName,
                    "vertical",
                    -1,
                    0,
                    0,
                    3,
                    3);
                lower.AddConnector(output);

                var upper = new JigsawPieceDefinition();
                upper.ConfigureBox(
                    "vertical_upper",
                    "Vertical Upper",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    false,
                    1,
                    1,
                    1,
                    7,
                    7,
                    7,
                    7,
                    5,
                    5,
                    "vertical",
                    "vertical");
                var input = new JigsawConnectorDefinition();
                input.Configure(
                    "vertical_input",
                    JigsawConnectorDefinition.Role.Input,
                    inputFace,
                    inputName,
                    outputName,
                    "vertical",
                    -1,
                    0,
                    0,
                    3,
                    3);
                upper.AddConnector(input);

                VoxelTypeDefinition brick =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel);
                definition.Configure(
                    true,
                    "vertical_socket_fixture",
                    brick,
                    brick,
                    73191,
                    4,
                    1f,
                    96,
                    96,
                    2,
                    1,
                    16,
                    string.Empty,
                    new[] { lower, upper });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                return settings;
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// Builds a single-piece structure whose only job is to exercise one
        /// processor kind at a known location.
        /// </summary>
        private static JigsawStructureFeatureSettings BuildProcessorFixture(
            JigsawProcessorDefinition.Kind kind,
            int distance,
            float chance,
            JigsawProcessorDefinition.Palette palette,
            bool perimeterOnly,
            out int floorY)
        {
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var start = new JigsawPieceDefinition();
                start.ConfigureBox(
                    "processor_start",
                    "Processor Start",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    7,
                    7,
                    7,
                    7,
                    5,
                    5);
                var processor = new JigsawProcessorDefinition();
                processor.Configure(
                    "fixture",
                    kind,
                    distance,
                    chance,
                    palette,
                    0,
                    perimeterOnly);
                start.AddProcessor(processor);

                floorY = 120;
                definition.Configure(
                    true,
                    "processor_fixture",
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel),
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.FortressBrickVoxel),
                    31337,
                    4,
                    1f,
                    floorY,
                    floorY,
                    2,
                    1,
                    16,
                    string.Empty,
                    new[] { start });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                return settings;
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static JigsawStructureGenerator.Piece GetProcessorStartPiece(
            JigsawStructureFeatureSettings settings,
            int floorY)
        {
            JigsawStructureGenerator.TryGetPlacement(
                settings,
                ProcessorSeed,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            return JigsawStructureGenerator.BuildLayout(
                settings,
                ProcessorSeed,
                placement)[0];
        }

        private static Vector3Int GetProcessorCentre(
            JigsawStructureFeatureSettings settings,
            int floorY)
        {
            JigsawStructureGenerator.Piece start = GetProcessorStartPiece(
                settings,
                floorY);
            return new Vector3Int(
                start.Origin.x,
                start.Bounds.MinY,
                start.Origin.z);
        }

        /// <summary>
        /// The voxel column that contains the fixture piece. Placement picks a
        /// randomised centre inside its region, so tests must not assume (0, 0).
        /// </summary>
        private static Vector3Int GetProcessorColumn(
            JigsawStructureFeatureSettings settings,
            int floorY)
        {
            Vector3Int centre = GetProcessorCentre(settings, floorY);
            return InfiniteVoxelWorld.WorldToChunk(centre.x, centre.y, centre.z);
        }

        /// <summary>
        /// True when a world position lies inside the given streamed column, so
        /// assertions can skip voxels that belong to a neighbouring column.
        /// </summary>
        private static bool IsInsideColumn(Vector3Int column, Vector3Int world)
        {
            Vector3Int owner = InfiniteVoxelWorld.WorldToChunk(
                world.x,
                world.y,
                world.z);
            return owner.x == column.x && owner.z == column.z;
        }

        [Test]
        public void RandomSpread_CollectionMatchesDirectRegionQuery()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
            const int seed = 5150;
            const int minX = -2048;
            const int minZ = -2048;
            const int maxX = 2048;
            const int maxZ = 2048;
            var collected = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                minX,
                minZ,
                maxX,
                maxZ,
                collected);

            // Sweeping regions by hand must produce the same candidate set the
            // service reports, otherwise generation and tooling would disagree.
            int regionSize = settings.RegionSizeInChunks
                * VoxelColumnChunkData.Width;
            var expected = new List<Vector3Int>();
            for (int regionZ = minZ / regionSize - 2;
                regionZ <= maxZ / regionSize + 2;
                regionZ++)
            {
                for (int regionX = minX / regionSize - 2;
                    regionX <= maxX / regionSize + 2;
                    regionX++)
                {
                    if (!JigsawStructureGenerator.TryGetPlacement(
                        settings,
                        seed,
                        regionX,
                        regionZ,
                        out JigsawStructureGenerator.Placement placement))
                    {
                        continue;
                    }
                    if (placement.Centre.x < minX - settings.MaxHorizontalDistance
                        || placement.Centre.x > maxX + settings.MaxHorizontalDistance
                        || placement.Centre.z < minZ - settings.MaxHorizontalDistance
                        || placement.Centre.z > maxZ + settings.MaxHorizontalDistance)
                    {
                        continue;
                    }
                    expected.Add(placement.Centre);
                }
            }

            Assert.That(collected, Is.Not.Empty);
            Assert.That(
                collected.Select(item => item.Centre),
                Is.SupersetOf(expected));
        }

        [Test]
        public void ConcentricRings_ProduceDeterministicSpreadAroundOrigin()
        {
            JigsawStructureFeatureSettings settings = BuildRingFixture(
                structureCount: 48,
                rings: 4);
            const int seed = 7321;
            const int extent = 200_000;
            var first = new List<JigsawStructureGenerator.Placement>();
            var second = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                -extent,
                -extent,
                extent,
                extent,
                first);
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                -extent,
                -extent,
                extent,
                extent,
                second);

            Assert.That(first, Has.Count.EqualTo(48));
            Assert.That(
                second.Select(item => item.Centre),
                Is.EqualTo(first.Select(item => item.Centre)));

            // Candidates must occupy several distinct radius bands rather than
            // clustering at one distance from the origin.
            int ringStep = settings.RingDistanceInChunks
                * VoxelColumnChunkData.Width;
            var bands = new HashSet<int>();
            foreach (JigsawStructureGenerator.Placement placement in first)
            {
                double radius = Mathf.Sqrt(
                    placement.Centre.x * (float)placement.Centre.x
                    + placement.Centre.z * (float)placement.Centre.z);
                bands.Add((int)(radius / ringStep));
            }
            Assert.That(bands.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void ConcentricRings_OnlyReportCandidatesInsideTheQueryWindow()
        {
            JigsawStructureFeatureSettings settings = BuildRingFixture(
                structureCount: 48,
                rings: 4);
            const int seed = 7321;
            var all = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                -200_000,
                -200_000,
                200_000,
                200_000,
                all);
            JigsawStructureGenerator.Placement target = all[0];

            var window = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                target.Centre.x - 1,
                target.Centre.z - 1,
                target.Centre.x + 1,
                target.Centre.z + 1,
                window);

            Assert.That(
                window.Select(item => item.Centre),
                Contains.Item(target.Centre));
            Assert.That(window.Count, Is.LessThan(all.Count));
        }

        [Test]
        public void StructureSet_PicksExactlyOneCompetitorPerCandidateCell()
        {
            JigsawStructureFeatureSettings bridges = BuildSetFixture(
                "nether_bridges",
                weight: 2,
                salt: 4001);
            JigsawStructureFeatureSettings bastion = BuildSetFixture(
                "nether_bastion",
                weight: 3,
                salt: 4001);
            var features = new[] { bridges, bastion };
            const int seed = 8642;
            int bridgeWins = 0;
            int bastionWins = 0;

            for (int regionZ = 0; regionZ < 24; regionZ++)
            {
                for (int regionX = 0; regionX < 24; regionX++)
                {
                    bool first = JigsawPlacementService.WinsStructureSet(
                        features,
                        0,
                        seed,
                        regionX,
                        regionZ);
                    bool second = JigsawPlacementService.WinsStructureSet(
                        features,
                        1,
                        seed,
                        regionX,
                        regionZ);

                    // Exactly one member of a structure set may claim a cell.
                    Assert.That(first && second, Is.False);
                    Assert.That(first || second, Is.True);
                    if (first) bridgeWins++;
                    if (second) bastionWins++;
                }
            }

            Assert.That(bridgeWins, Is.GreaterThan(0));
            Assert.That(bastionWins, Is.GreaterThan(0));
            // Weight 3 should beat weight 2 over a large sample.
            Assert.That(bastionWins, Is.GreaterThan(bridgeWins));
        }

        [Test]
        public void FeaturesOutsideAnyStructureSet_NeverCompete()
        {
            JigsawStructureFeatureSettings mineshaft = LoadSettings(
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
            JigsawStructureFeatureSettings fortress = LoadSettings(
                ProjectAssetPaths.Config.StrongholdJigsaw);
            var features = new[] { mineshaft, fortress };

            for (int region = 0; region < 16; region++)
            {
                Assert.That(
                    JigsawPlacementService.WinsStructureSet(
                        features,
                        0,
                        4242,
                        region,
                        region),
                    Is.True);
                Assert.That(
                    JigsawPlacementService.WinsStructureSet(
                        features,
                        1,
                        4242,
                        region,
                        region),
                    Is.True);
            }
        }

        private static JigsawStructureFeatureSettings BuildRingFixture(
            int structureCount,
            int rings)
        {
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                ConfigureSinglePieceStructure(definition, "ring_fixture", 90210);
                definition.ConfigurePlacementStrategy(
                    JigsawPlacementStrategy.ConcentricRings,
                    structureCount,
                    rings,
                    32,
                    3);
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                return settings;
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static JigsawStructureFeatureSettings BuildSetFixture(
            string stableId,
            int weight,
            int salt)
        {
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                ConfigureSinglePieceStructure(definition, stableId, salt);
                definition.ConfigureStructureSet("nether_complexes", weight);
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                return settings;
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static void ConfigureSinglePieceStructure(
            JigsawStructureFeatureDefinition definition,
            string stableId,
            int salt)
        {
            var start = new JigsawPieceDefinition();
            start.ConfigureBox(
                stableId + "_start",
                "Start",
                JigsawPieceDefinition.Shape.Room,
                JigsawPieceDefinition.BuildStyle.Masonry,
                JigsawPieceDefinition.ConnectorPattern.FourWay,
                JigsawPieceDefinition.Decoration.None,
                true,
                0,
                0,
                0,
                7,
                7,
                7,
                7,
                5,
                5);
            definition.Configure(
                true,
                stableId,
                AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                    ProjectAssetPaths.Config.StructureBrickVoxel),
                AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                    ProjectAssetPaths.Config.FortressBrickVoxel),
                salt,
                4,
                1f,
                100,
                120,
                2,
                1,
                16,
                string.Empty,
                new[] { start });
        }

        [Test]
        public void TemplateSockets_AreInheritedByPiecesThatAuthorNone()
        {
            var template = ScriptableObject.CreateInstance<VoxelStructureAsset>();
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var size = new Vector3Int(7, 6, 9);
                var anchor = new Vector3Int(3, 0, 4);
                BuildSolidTemplate(template, size, anchor);
                var marker = new VoxelStructureSocket();
                var markerPosition = new Vector3Int(3, 1, size.z - 1);
                marker.Configure(
                    "template_exit",
                    markerPosition,
                    JigsawConnectorDefinition.Face.Forward,
                    JigsawConnectorDefinition.Role.Output,
                    "tpl_branch",
                    "tpl_entry",
                    "child",
                    3,
                    3);
                template.AddSocket(marker);

                var start = new JigsawPieceDefinition();
                start.ConfigureBox(
                    "tpl_start",
                    "Template Start",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    7,
                    7,
                    7,
                    7,
                    5,
                    5);
                // Deliberately author no connectors on the piece: the socket must
                // come from the template alone.
                start.ConfigureTemplate(template, true);

                var child = new JigsawPieceDefinition();
                child.ConfigurePassage(
                    "tpl_child",
                    "Template Child",
                    JigsawPieceDefinition.Shape.Corridor,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    1,
                    1,
                    1,
                    5,
                    5,
                    3,
                    4,
                    1,
                    0f,
                    0.5f,
                    3,
                    "child",
                    "child");
                var entrance = new JigsawConnectorDefinition();
                entrance.Configure(
                    "entrance",
                    JigsawConnectorDefinition.Role.Input,
                    JigsawConnectorDefinition.Face.Back,
                    "tpl_entry",
                    "tpl_branch",
                    "child",
                    -1,
                    0,
                    1,
                    3,
                    3);
                child.AddConnector(entrance);

                VoxelTypeDefinition brick =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel);
                definition.Configure(
                    true,
                    "template_socket_test",
                    brick,
                    brick,
                    9977,
                    4,
                    1f,
                    100,
                    100,
                    4,
                    2,
                    16,
                    string.Empty,
                    new[] { start, child });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);

                JigsawPieceSettings startModule = settings.GetPiece(
                    settings.StartPieceIndex);
                Assert.That(startModule.HasExplicitConnectors, Is.True);
                Assert.That(startModule.Connectors.Count, Is.EqualTo(1));
                Assert.That(
                    startModule.Connectors[0].StableId,
                    Is.EqualTo("template_exit"));
                Assert.That(
                    startModule.Connectors[0].HasTemplatePosition,
                    Is.True);
                Assert.That(
                    startModule.Connectors[0].TemplatePosition,
                    Is.EqualTo(markerPosition));

                // The graph validator must accept a template piece whose sockets
                // live in the template rather than on the piece.
                Assert.That(
                    JigsawStructureValidator.Validate(settings)
                        .Where(issue => issue.Severity
                            == JigsawStructureValidator.Severity.Error)
                        .Select(issue => issue.Message),
                    Is.Empty);

                // The socket must actually attach a child, and the child has to
                // sit on the rotated marker rather than the template centre.
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    31,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement);
                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(settings, 31, placement);
                Assert.That(layout.Count, Is.GreaterThan(1));
                JigsawStructureGenerator.Piece parent = layout[0];
                JigsawStructureGenerator.Piece attached = layout[1];
                Assert.That(attached.ModuleId, Is.EqualTo("tpl_child"));

                GetAxes(
                    parent.Direction,
                    out Vector3Int forward,
                    out Vector3Int right);
                Vector3Int expectedBoundary = new Vector3Int(
                    parent.Origin.x
                        + right.x * (markerPosition.x - anchor.x)
                        + forward.x * (markerPosition.z - anchor.z),
                    parent.Origin.y + markerPosition.y - anchor.y,
                    parent.Origin.z
                        + right.z * (markerPosition.x - anchor.x)
                        + forward.z * (markerPosition.z - anchor.z));
                Assert.That(
                    parent.Openings.Select(opening => opening.Boundary),
                    Contains.Item(expectedBoundary));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(template);
            }
        }

        [Test]
        public void TemplateSockets_ChangeTheFeatureContentHash()
        {
            var template = ScriptableObject.CreateInstance<VoxelStructureAsset>();
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var size = new Vector3Int(7, 6, 9);
                var anchor = new Vector3Int(3, 0, 4);
                BuildSolidTemplate(template, size, anchor);

                var start = new JigsawPieceDefinition();
                start.ConfigureBox(
                    "hash_start",
                    "Hash Start",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    7,
                    7,
                    7,
                    7,
                    5,
                    5);
                start.ConfigureTemplate(template, true);
                VoxelTypeDefinition brick =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel);
                definition.Configure(
                    true,
                    "template_hash_test",
                    brick,
                    brick,
                    9978,
                    4,
                    1f,
                    100,
                    100,
                    2,
                    1,
                    16,
                    string.Empty,
                    new[] { start });

                definition.TryCreateSettings(
                    out JigsawStructureFeatureSettings before,
                    out _);

                var marker = new VoxelStructureSocket();
                marker.Configure(
                    "late_socket",
                    new Vector3Int(3, 1, size.z - 1),
                    JigsawConnectorDefinition.Face.Forward,
                    JigsawConnectorDefinition.Role.Output,
                    "*",
                    "*",
                    "main");
                template.AddSocket(marker);
                definition.TryCreateSettings(
                    out JigsawStructureFeatureSettings after,
                    out _);

                // Editing markers must invalidate cached layouts, otherwise a
                // stale graph would survive an authoring change.
                Assert.That(after.ContentHash, Is.Not.EqualTo(before.ContentHash));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(template);
            }
        }

        private static void BuildSolidTemplate(
            VoxelStructureAsset template,
            Vector3Int size,
            Vector3Int anchor)
        {
            int sampleCount = size.x * size.y * size.z;
            var densities = new float[sampleCount];
            var types = new ushort[sampleCount];
            for (int z = 0; z < size.z; z++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        bool shell = x == 0 || x == size.x - 1
                            || y == 0 || y == size.y - 1
                            || z == 0 || z == size.z - 1;
                        int index = x + size.x * (y + size.y * z);
                        densities[index] = shell ? 1f : -1f;
                        types[index] = shell ? StructureBrick.Value : (ushort)0;
                    }
                }
            }
            template.SetData(size, anchor, Vector3.zero, densities, types);
        }

        [Test]
        public void SpawnMarkers_ResolveDeterministicallyAndRotateWithThePiece()
        {
            JigsawStructureFeatureSettings settings = BuildMarkerFixture(
                StructureSpawnMarkerDefinition.Kind.Treasure,
                new Vector3Int(2, 1, 3),
                chance: 1f,
                count: 1,
                out int floorY);
            JigsawStructureGenerator.Piece start = GetMarkerStartPiece(settings);
            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                start.Origin.x,
                start.Origin.y,
                start.Origin.z);

            var first = new List<StructureSpawnRequest>();
            var second = new List<StructureSpawnRequest>();
            JigsawStructureGenerator.CollectSpawnRequests(
                column,
                MarkerSeed,
                new[] { settings },
                first);
            JigsawStructureGenerator.CollectSpawnRequests(
                column,
                MarkerSeed,
                new[] { settings },
                second);

            Assert.That(first, Is.Not.Empty);
            Assert.That(
                second.Select(item => item.VoxelPosition),
                Is.EqualTo(first.Select(item => item.VoxelPosition)));

            // The authored offset lives in the piece's own axes, so it must rotate
            // with the piece rather than always pointing at world north.
            GetAxes(start.Direction, out Vector3Int forward, out Vector3Int right);
            Vector3Int expected = new Vector3Int(
                start.Origin.x + right.x * 2 + forward.x * 3,
                start.Origin.y + 1,
                start.Origin.z + right.z * 2 + forward.z * 3);
            Assert.That(
                first.Select(item => item.VoxelPosition),
                Contains.Item(expected));
            Assert.That(
                first[0].Kind,
                Is.EqualTo(StructureSpawnMarkerDefinition.Kind.Treasure));
        }

        [Test]
        public void SpawnMarkers_AreOnlyReportedByTheColumnThatOwnsThem()
        {
            JigsawStructureFeatureSettings settings = BuildMarkerFixture(
                StructureSpawnMarkerDefinition.Kind.Treasure,
                new Vector3Int(0, 1, 0),
                chance: 1f,
                count: 1,
                out int floorY);
            JigsawStructureGenerator.Piece start = GetMarkerStartPiece(settings);
            Vector3Int owner = InfiniteVoxelWorld.WorldToChunk(
                start.Origin.x,
                start.Origin.y,
                start.Origin.z);

            var ownerRequests = new List<StructureSpawnRequest>();
            JigsawStructureGenerator.CollectSpawnRequests(
                owner,
                MarkerSeed,
                new[] { settings },
                ownerRequests);
            Assert.That(ownerRequests, Is.Not.Empty);

            // A far-away column must not report the same spawn, otherwise the
            // world would instantiate one marker many times over.
            var distantRequests = new List<StructureSpawnRequest>();
            JigsawStructureGenerator.CollectSpawnRequests(
                new Vector3Int(owner.x + 40, 0, owner.z + 40),
                MarkerSeed,
                new[] { settings },
                distantRequests);
            Assert.That(distantRequests, Is.Empty);
        }

        [Test]
        public void SpawnMarkers_HonourZeroChanceAndInstanceCount()
        {
            JigsawStructureFeatureSettings never = BuildMarkerFixture(
                StructureSpawnMarkerDefinition.Kind.Treasure,
                new Vector3Int(0, 1, 0),
                chance: 0f,
                count: 3,
                out int neverFloor);
            JigsawStructureGenerator.Piece neverPiece =
                GetMarkerStartPiece(never);
            var neverRequests = new List<StructureSpawnRequest>();
            JigsawStructureGenerator.CollectSpawnRequests(
                InfiniteVoxelWorld.WorldToChunk(
                    neverPiece.Origin.x,
                    neverPiece.Origin.y,
                    neverPiece.Origin.z),
                MarkerSeed,
                new[] { never },
                neverRequests);
            Assert.That(neverRequests, Is.Empty);

            JigsawStructureFeatureSettings always = BuildMarkerFixture(
                StructureSpawnMarkerDefinition.Kind.Treasure,
                new Vector3Int(0, 1, 0),
                chance: 1f,
                count: 3,
                out int alwaysFloor);
            JigsawStructureGenerator.Piece alwaysPiece =
                GetMarkerStartPiece(always);
            var alwaysRequests = new List<StructureSpawnRequest>();
            JigsawStructureGenerator.CollectSpawnRequests(
                InfiniteVoxelWorld.WorldToChunk(
                    alwaysPiece.Origin.x,
                    alwaysPiece.Origin.y,
                    alwaysPiece.Origin.z),
                MarkerSeed,
                new[] { always },
                alwaysRequests);
            // Scattered instances may land in a neighbouring column, so the owning
            // column reports at least the anchor and at most the full count.
            Assert.That(alwaysRequests.Count, Is.InRange(1, 3));
            Assert.That(
                alwaysRequests,
                Has.All.Matches<StructureSpawnRequest>(
                    item => item.Kind
                        == StructureSpawnMarkerDefinition.Kind.Treasure));
        }

        [Test]
        public void SpawnMarkers_ChangeTheFeatureContentHash()
        {
            JigsawStructureFeatureSettings without = BuildMarkerFixture(
                StructureSpawnMarkerDefinition.Kind.Treasure,
                new Vector3Int(0, 1, 0),
                chance: 1f,
                count: 1,
                out _,
                includeMarker: false);
            JigsawStructureFeatureSettings with = BuildMarkerFixture(
                StructureSpawnMarkerDefinition.Kind.Treasure,
                new Vector3Int(0, 1, 0),
                chance: 1f,
                count: 1,
                out _);

            // Editing markers must invalidate cached layouts.
            Assert.That(with.ContentHash, Is.Not.EqualTo(without.ContentHash));
        }

        [Test]
        public void TemplateSpawnMarkers_AreInheritedByPiecesThatAuthorNone()
        {
            var template = ScriptableObject.CreateInstance<VoxelStructureAsset>();
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                BuildSolidTemplate(
                    template,
                    new Vector3Int(7, 6, 9),
                    new Vector3Int(3, 0, 4));
                var marker = new StructureSpawnMarkerDefinition();
                marker.Configure(
                    "template_loot",
                    StructureSpawnMarkerDefinition.Kind.Treasure,
                    new Vector3Int(0, 2, 0));
                marker.ConfigureTreasure(LoadAnyTreasure());
                template.AddSpawnMarker(marker);

                var start = new JigsawPieceDefinition();
                start.ConfigureBox(
                    "tpl_marker_start",
                    "Template Marker Start",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    7,
                    7,
                    7,
                    7,
                    5,
                    5);
                start.ConfigureTemplate(template, true);
                var exit = new JigsawConnectorDefinition();
                exit.Configure(
                    "exit",
                    JigsawConnectorDefinition.Role.Output,
                    JigsawConnectorDefinition.Face.Forward,
                    "*",
                    "*",
                    "main");
                start.AddConnector(exit);

                VoxelTypeDefinition brick =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel);
                definition.Configure(
                    true,
                    "template_marker_test",
                    brick,
                    brick,
                    5521,
                    4,
                    1f,
                    100,
                    100,
                    2,
                    1,
                    16,
                    string.Empty,
                    new[] { start });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);

                // The piece authored no markers of its own, so it must adopt the
                // template's.
                JigsawPieceSettings module = settings.GetPiece(
                    settings.StartPieceIndex);
                Assert.That(module.HasSpawnMarkers, Is.True);
                Assert.That(module.SpawnMarkers.Count, Is.EqualTo(1));
                Assert.That(
                    module.SpawnMarkers[0].StableId,
                    Is.EqualTo("template_loot"));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(template);
            }
        }

        private const int MarkerSeed = 771244;

        private static TreasureDefinition LoadAnyTreasure()
        {
            string[] guids = AssetDatabase.FindAssets("t:TreasureDefinition");
            Assert.That(guids, Is.Not.Empty, "project has no TreasureDefinition");
            return AssetDatabase.LoadAssetAtPath<TreasureDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>
        /// Builds a one-room structure carrying a single authored marker.
        /// </summary>
        private static JigsawStructureFeatureSettings BuildMarkerFixture(
            StructureSpawnMarkerDefinition.Kind kind,
            Vector3Int localOffset,
            float chance,
            int count,
            out int floorY,
            bool includeMarker = true)
        {
            var definition = ScriptableObject.CreateInstance<
                JigsawStructureFeatureDefinition>();
            try
            {
                var start = new JigsawPieceDefinition();
                start.ConfigureBox(
                    "marker_start",
                    "Marker Start",
                    JigsawPieceDefinition.Shape.Room,
                    JigsawPieceDefinition.BuildStyle.Masonry,
                    JigsawPieceDefinition.ConnectorPattern.None,
                    JigsawPieceDefinition.Decoration.None,
                    true,
                    0,
                    0,
                    0,
                    11,
                    11,
                    11,
                    11,
                    6,
                    6);
                var exit = new JigsawConnectorDefinition();
                exit.Configure(
                    "exit",
                    JigsawConnectorDefinition.Role.Output,
                    JigsawConnectorDefinition.Face.Forward,
                    "*",
                    "*",
                    "main");
                start.AddConnector(exit);
                if (includeMarker)
                {
                    var marker = new StructureSpawnMarkerDefinition();
                    marker.Configure(
                        "fixture_marker",
                        kind,
                        localOffset,
                        0f,
                        chance,
                        count,
                        2f,
                        false,
                        0);
                    marker.ConfigureTreasure(LoadAnyTreasure());
                    start.AddSpawnMarker(marker);
                }

                floorY = 110;
                VoxelTypeDefinition brick =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.StructureBrickVoxel);
                definition.Configure(
                    true,
                    "marker_fixture",
                    brick,
                    brick,
                    8812,
                    4,
                    1f,
                    floorY,
                    floorY,
                    2,
                    1,
                    16,
                    string.Empty,
                    new[] { start });
                Assert.That(
                    definition.TryCreateSettings(
                        out JigsawStructureFeatureSettings settings,
                        out string error),
                    Is.True,
                    error);
                return settings;
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static JigsawStructureGenerator.Piece GetMarkerStartPiece(
            JigsawStructureFeatureSettings settings)
        {
            JigsawStructureGenerator.TryGetPlacement(
                settings,
                MarkerSeed,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            return JigsawStructureGenerator.BuildLayout(
                settings,
                MarkerSeed,
                placement)[0];
        }

        /// <summary>
        /// Scans regions for the first layout containing a module. Tests must not
        /// assume region (0, 0) wins, because that depends on the asset's authored
        /// placement chance, which designers tune freely. Works for both placement
        /// strategies by asking the placement service rather than sweeping regions.
        /// </summary>
        private static IReadOnlyList<JigsawStructureGenerator.Piece>
            FindLayoutContaining(
                JigsawStructureFeatureSettings settings,
                int seed,
                string moduleId)
        {
            const int extent = 400_000;
            var placements = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                -extent,
                -extent,
                extent,
                extent,
                placements);
            foreach (JigsawStructureGenerator.Placement placement in placements)
            {
                IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                    JigsawStructureGenerator.BuildLayout(
                        settings,
                        seed,
                        placement);
                if (layout.Any(piece => piece.ModuleId == moduleId))
                {
                    return layout;
                }
            }
            Assert.Fail(
                $"No layout containing '{moduleId}' was found for seed {seed}.");
            return null;
        }

        /// <summary>
        /// The first candidate of a structure under whichever placement strategy it
        /// uses, so tests work for ring-placed structures too.
        /// </summary>
        private static JigsawStructureGenerator.Placement FindAnyPlacement(
            JigsawStructureFeatureSettings settings,
            int seed)
        {
            const int extent = 400_000;
            var placements = new List<JigsawStructureGenerator.Placement>();
            JigsawPlacementService.CollectPlacements(
                settings,
                seed,
                -extent,
                -extent,
                extent,
                extent,
                placements);
            Assert.That(placements, Is.Not.Empty);
            return placements[0];
        }

        private static JigsawStructureFeatureSettings LoadSettings(string path)
        {
            JigsawStructureFeatureDefinition definition =
                AssetDatabase.LoadAssetAtPath<JigsawStructureFeatureDefinition>(path);
            Assert.That(definition, Is.Not.Null, path);
            Assert.That(
                definition.TryCreateSettings(
                    out JigsawStructureFeatureSettings settings,
                    out string error),
                Is.True,
                error);
            return settings;
        }

        private static bool HasConnectedVerticalNeighbour(
            IReadOnlyList<JigsawStructureGenerator.Piece> layout,
            int pieceIndex,
            int direction)
        {
            JigsawStructureGenerator.Piece piece = layout[pieceIndex];
            for (int otherIndex = 0;
                otherIndex < layout.Count;
                otherIndex++)
            {
                if (otherIndex == pieceIndex)
                {
                    continue;
                }

                JigsawStructureGenerator.Piece other = layout[otherIndex];
                bool graphEdge = other.ParentIndex == pieceIndex
                    || piece.ParentIndex == otherIndex;
                if (!graphEdge)
                {
                    continue;
                }

                if (direction
                        == (int)JigsawConnectorDefinition.Face.Up
                    && other.Bounds.MinY == piece.Bounds.MaxY + 1)
                {
                    return true;
                }
                if (direction
                        == (int)JigsawConnectorDefinition.Face.Down
                    && other.Bounds.MaxY + 1 == piece.Bounds.MinY)
                {
                    return true;
                }
            }
            return false;
        }

        private static void GetAxes(
            int direction,
            out Vector3Int forward,
            out Vector3Int right)
        {
            switch (direction & 3)
            {
                case 1: forward = Vector3Int.right; break;
                case 2: forward = new Vector3Int(0, 0, -1); break;
                case 3: forward = Vector3Int.left; break;
                default: forward = new Vector3Int(0, 0, 1); break;
            }
            switch ((direction + 1) & 3)
            {
                case 1: right = Vector3Int.right; break;
                case 2: right = new Vector3Int(0, 0, -1); break;
                case 3: right = Vector3Int.left; break;
                default: right = new Vector3Int(0, 0, 1); break;
            }
        }

        private static VoxelTypeId GetWorldType(
            VoxelTypeId[] types,
            Vector3Int column,
            Vector3Int world)
        {
            Vector3Int local = InfiniteVoxelWorld.WorldToLocal(
                world.x,
                world.y,
                world.z,
                column);
            return types[VoxelColumnChunkData.ToIndex(
                local.x,
                local.y,
                local.z)];
        }

        private static VoxelTypeId GenerateAndGetType(
            JigsawStructureFeatureSettings settings,
            int seed,
            Vector3Int world)
        {
            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                world.x,
                world.y,
                world.z);
            float[] densities = Enumerable.Repeat(
                1f,
                VoxelColumnChunkData.VoxelCount).ToArray();
            VoxelTypeId[] types = Enumerable.Repeat(
                Stone,
                VoxelColumnChunkData.VoxelCount).ToArray();
            JigsawStructureGenerator.GenerateColumn(
                column,
                densities,
                types,
                seed,
                new[] { settings },
                1f,
                -1f);
            return GetWorldType(types, column, world);
        }

        private readonly struct RampPathSample
        {
            public RampPathSample(int along, int lateral, double expectedY)
            {
                Along = along;
                Lateral = lateral;
                ExpectedY = expectedY;
            }

            public int Along { get; }
            public int Lateral { get; }
            public double ExpectedY { get; }
        }

        private sealed class GeneratedColumnSamples
        {
            public GeneratedColumnSamples(
                float[] densities,
                VoxelTypeId[] types)
            {
                Densities = densities;
                Types = types;
            }

            public float[] Densities { get; }
            public VoxelTypeId[] Types { get; }
        }

        private static bool SpiralRampFitsCharacterController(
            JigsawStructureFeatureSettings settings,
            int seed,
            JigsawStructureGenerator.Piece shaft,
            JigsawStructureGenerator.Piece upperRoom,
            CharacterController controller,
            float voxelSize,
            out string error,
            JigsawPlacementSelection placementSelection = null)
        {
            JigsawStructureGenerator.Opening upperOpening =
                shaft.Openings.Single(opening =>
                    opening.Direction
                        == (int)JigsawConnectorDefinition.Face.Up);
            int openingHalfSpan =
                Mathf.Min(upperOpening.Width, upperOpening.Height) / 2;
            int halfAlong =
                (shaft.Bounds.MaxX - shaft.Bounds.MinX) / 2;
            int halfLateral =
                (shaft.Bounds.MaxZ - shaft.Bounds.MinZ) / 2;
            int outerRadius = Mathf.Min(halfAlong, halfLateral) - 2;
            if (openingHalfSpan < 3 || outerRadius < 8)
            {
                error = "The spiral shaft is smaller than the 7-wide aperture "
                    + "and 21-wide shell required by the player.";
                return false;
            }

            int lowerRingY = shaft.Bounds.MinY + 1;
            int upperRingY = Mathf.Max(
                lowerRingY,
                shaft.Bounds.MaxY - 7);
            int bridgeEnd = openingHalfSpan;
            int lowerLane = -Mathf.Max(1, openingHalfSpan - 1);
            int upperLane = Mathf.Max(1, openingHalfSpan - 1);
            var path = new List<RampPathSample>();

            double lowerSeamY = GetExpectedRingSurfaceY(
                -outerRadius,
                lowerLane,
                lowerRingY,
                upperRingY);
            for (int along = bridgeEnd;
                along >= -outerRadius;
                along--)
            {
                double progress = (along + outerRadius)
                    / (double)(bridgeEnd + outerRadius);
                AddRampPathSample(
                    path,
                    along,
                    lowerLane,
                    lowerSeamY
                        + (shaft.Bounds.MinY - lowerSeamY) * progress);
            }

            for (int lateral = lowerLane - 1;
                lateral >= -outerRadius;
                lateral--)
            {
                AddRingPathSample(
                    path,
                    -outerRadius,
                    lateral,
                    lowerRingY,
                    upperRingY);
            }
            for (int along = -outerRadius + 1;
                along <= outerRadius;
                along++)
            {
                AddRingPathSample(
                    path,
                    along,
                    -outerRadius,
                    lowerRingY,
                    upperRingY);
            }
            for (int lateral = -outerRadius + 1;
                lateral <= outerRadius;
                lateral++)
            {
                AddRingPathSample(
                    path,
                    outerRadius,
                    lateral,
                    lowerRingY,
                    upperRingY);
            }
            for (int along = outerRadius - 1;
                along >= -outerRadius;
                along--)
            {
                AddRingPathSample(
                    path,
                    along,
                    outerRadius,
                    lowerRingY,
                    upperRingY);
            }
            for (int lateral = outerRadius - 1;
                lateral >= upperLane;
                lateral--)
            {
                AddRingPathSample(
                    path,
                    -outerRadius,
                    lateral,
                    lowerRingY,
                    upperRingY);
            }

            double upperSeamY = GetExpectedRingSurfaceY(
                -outerRadius,
                upperLane,
                lowerRingY,
                upperRingY);
            for (int along = -outerRadius + 1;
                along <= bridgeEnd;
                along++)
            {
                double progress = (along + outerRadius)
                    / (double)(bridgeEnd + outerRadius);
                AddRampPathSample(
                    path,
                    along,
                    upperLane,
                    upperSeamY
                        + (shaft.Bounds.MaxY + 1 - upperSeamY)
                            * progress);
            }

            GetAxes(
                shaft.Direction,
                out Vector3Int forward,
                out Vector3Int right);
            var generatedColumns =
                new Dictionary<Vector3Int, GeneratedColumnSamples>();
            double previousSurface = double.NaN;
            float clearanceInVoxels = controller.height / voxelSize;
            double finalSurface = 0.0;
            for (int sampleIndex = 0;
                sampleIndex < path.Count;
                sampleIndex++)
            {
                RampPathSample sample = path[sampleIndex];
                Vector3Int world = shaft.Origin
                    + forward * sample.Along
                    + right * sample.Lateral;
                if (!TryFindTopSurfaceNear(
                        settings,
                        seed,
                        world.x,
                        world.z,
                        sample.ExpectedY,
                        generatedColumns,
                        placementSelection,
                        out double surfaceY))
                {
                    error = $"No ramp surface near local "
                        + $"({sample.Along}, {sample.Lateral}) "
                        + $"at expected Y {sample.ExpectedY:F2}.";
                    return false;
                }

                if (!double.IsNaN(previousSurface))
                {
                    double rise = Math.Abs(surfaceY - previousSurface);
                    double slope = Math.Atan2(rise, 1.0)
                        * Mathf.Rad2Deg;
                    if (slope > controller.slopeLimit + 0.5f)
                    {
                        error = $"Ramp slope {slope:F1} degrees exceeds the "
                            + $"player limit {controller.slopeLimit:F1} at "
                            + $"local ({sample.Along}, {sample.Lateral}).";
                        return false;
                    }
                }

                int firstClearY = Mathf.FloorToInt((float)surfaceY) + 1;
                int lastClearY = Mathf.CeilToInt(
                    (float)(surfaceY + clearanceInVoxels));
                for (int y = firstClearY; y <= lastClearY; y++)
                {
                    GetGeneratedSample(
                        settings,
                        seed,
                        new Vector3Int(world.x, y, world.z),
                        generatedColumns,
                        placementSelection,
                        out float density,
                        out VoxelTypeId type);
                    if (density >= 0f && !type.IsAir)
                    {
                        error = $"Player headroom is blocked at "
                            + $"({world.x}, {y}, {world.z}); "
                            + $"surface Y is {surfaceY:F2}.";
                        return false;
                    }
                }

                previousSurface = surfaceY;
                finalSurface = surfaceY;
            }

            int openFloorSamples = 0;
            for (int along = -openingHalfSpan;
                along <= openingHalfSpan;
                along++)
            {
                for (int lateral = -openingHalfSpan;
                    lateral <= openingHalfSpan;
                    lateral++)
                {
                    Vector3Int world = shaft.Origin
                        + forward * along
                        + right * lateral;
                    world.y = upperRoom.Bounds.MinY;
                    GetGeneratedSample(
                        settings,
                        seed,
                        world,
                        generatedColumns,
                        placementSelection,
                        out float density,
                        out VoxelTypeId type);
                    if (density < 0f || type.IsAir)
                    {
                        openFloorSamples++;
                    }
                }
            }
            int openingArea = upperOpening.Width * upperOpening.Height;
            if (openFloorSamples < openingArea / 2)
            {
                error = $"The upper floor aperture is sealed: only "
                    + $"{openFloorSamples}/{openingArea} samples remain open.";
                return false;
            }

            Vector3Int adjacentFloor = shaft.Origin
                + forward * (bridgeEnd + 1)
                + right * upperLane;
            if (!TryFindTopSurfaceNear(
                    settings,
                    seed,
                    adjacentFloor.x,
                    adjacentFloor.z,
                    upperRoom.Bounds.MinY + 0.5,
                    generatedColumns,
                    placementSelection,
                    out double roomFloorSurface))
            {
                error = "No walkable room floor was found beyond the upper ramp tongue.";
                return false;
            }
            float finalStepHeight =
                (float)Math.Abs(roomFloorSurface - finalSurface) * voxelSize;
            if (finalStepHeight > controller.stepOffset + 0.01f)
            {
                error = $"The ramp-to-room transition is "
                    + $"{finalStepHeight:F3}m, above the player step offset "
                    + $"{controller.stepOffset:F3}m.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static void AddRingPathSample(
            List<RampPathSample> path,
            int along,
            int lateral,
            int lowerY,
            int upperY)
        {
            AddRampPathSample(
                path,
                along,
                lateral,
                GetExpectedRingSurfaceY(
                    along,
                    lateral,
                    lowerY,
                    upperY));
        }

        private static void AddRampPathSample(
            List<RampPathSample> path,
            int along,
            int lateral,
            double expectedY)
        {
            if (path.Count > 0
                && path[path.Count - 1].Along == along
                && path[path.Count - 1].Lateral == lateral)
            {
                return;
            }
            path.Add(new RampPathSample(along, lateral, expectedY));
        }

        private static double GetExpectedRingSurfaceY(
            int along,
            int lateral,
            int lowerY,
            int upperY)
        {
            double angle = Math.Atan2(lateral, along);
            double progress = (angle + Math.PI) / (Math.PI * 2.0);
            return lowerY + (upperY - lowerY) * progress;
        }

        private static bool TryFindTopSurfaceNear(
            JigsawStructureFeatureSettings settings,
            int seed,
            int worldX,
            int worldZ,
            double expectedY,
            Dictionary<Vector3Int, GeneratedColumnSamples> generatedColumns,
            JigsawPlacementSelection placementSelection,
            out double surfaceY)
        {
            surfaceY = 0.0;
            double bestDistance = double.PositiveInfinity;
            int minimumY = Mathf.Max(0, Mathf.FloorToInt((float)expectedY) - 2);
            int maximumY = Mathf.Min(
                VoxelColumnChunkData.Height - 2,
                Mathf.CeilToInt((float)expectedY) + 2);
            for (int y = minimumY; y <= maximumY; y++)
            {
                GetGeneratedSample(
                    settings,
                    seed,
                    new Vector3Int(worldX, y, worldZ),
                    generatedColumns,
                    placementSelection,
                    out float lowerDensity,
                    out VoxelTypeId lowerType);
                GetGeneratedSample(
                    settings,
                    seed,
                    new Vector3Int(worldX, y + 1, worldZ),
                    generatedColumns,
                    placementSelection,
                    out float upperDensity,
                    out VoxelTypeId upperType);
                bool lowerSolid = lowerDensity >= 0f && !lowerType.IsAir;
                bool upperSolid = upperDensity >= 0f && !upperType.IsAir;
                if (!lowerSolid || upperSolid)
                {
                    continue;
                }

                double denominator = lowerDensity - upperDensity;
                double candidate = Math.Abs(denominator) < 0.000001
                    ? y + 0.5
                    : y + lowerDensity / denominator;
                double distance = Math.Abs(candidate - expectedY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    surfaceY = candidate;
                }
            }
            return !double.IsPositiveInfinity(bestDistance)
                && bestDistance <= 0.75;
        }

        private static void GetGeneratedSample(
            JigsawStructureFeatureSettings settings,
            int seed,
            Vector3Int world,
            Dictionary<Vector3Int, GeneratedColumnSamples> generatedColumns,
            JigsawPlacementSelection placementSelection,
            out float density,
            out VoxelTypeId type)
        {
            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                world.x,
                world.y,
                world.z);
            if (!generatedColumns.TryGetValue(
                    column,
                    out GeneratedColumnSamples samples))
            {
                float[] densities = Enumerable.Repeat(
                    1f,
                    VoxelColumnChunkData.VoxelCount).ToArray();
                VoxelTypeId[] types = Enumerable.Repeat(
                    Stone,
                    VoxelColumnChunkData.VoxelCount).ToArray();
                JigsawStructureGenerator.GenerateColumn(
                    column,
                    densities,
                    types,
                    seed,
                    new[] { settings },
                    1f,
                    -1f,
                    default,
                    placementSelection);
                samples = new GeneratedColumnSamples(densities, types);
                generatedColumns.Add(column, samples);
            }

            Vector3Int local = InfiniteVoxelWorld.WorldToLocal(
                world.x,
                world.y,
                world.z,
                column);
            int index = VoxelColumnChunkData.ToIndex(
                local.x,
                local.y,
                local.z);
            density = samples.Densities[index];
            type = samples.Types[index];
        }

        /// <summary>
        /// Generates one column over a terrain profile that is solid below
        /// <paramref name="terrainTopY"/> and air above it, so downward
        /// processors have empty space to travel through.
        /// </summary>
        private static void GenerateColumnOverTerrain(
            JigsawStructureFeatureSettings settings,
            int seed,
            Vector3Int column,
            int terrainTopY,
            out float[] densities,
            out VoxelTypeId[] types)
        {
            densities = new float[VoxelColumnChunkData.VoxelCount];
            types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
            for (int y = 0; y < VoxelColumnChunkData.Height; y++)
            {
                bool solid = y <= terrainTopY;
                for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        int index = VoxelColumnChunkData.ToIndex(x, y, z);
                        densities[index] = solid ? 1f : -1f;
                        types[index] = solid ? Stone : VoxelTypeId.Air;
                    }
                }
            }
            JigsawStructureGenerator.GenerateColumn(
                column,
                densities,
                types,
                seed,
                new[] { settings },
                1f,
                -1f);
        }
    }
}
