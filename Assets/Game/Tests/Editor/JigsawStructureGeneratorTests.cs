using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class JigsawStructureGeneratorTests
    {
        private static readonly VoxelTypeId Stone = new VoxelTypeId(2);
        private static readonly VoxelTypeId StructureBrick = new VoxelTypeId(5);

        [Test]
        public void DefaultWorld_ReferencesMineshaftAndFortressAsJigsawAssets()
        {
            MinecraftWorldGenerationConfiguration world =
                AssetDatabase.LoadAssetAtPath<MinecraftWorldGenerationConfiguration>(
                    ProjectAssetPaths.Config.WorldGeneration);

            Assert.That(world, Is.Not.Null);
            Assert.That(world.JigsawStructures, Has.Count.EqualTo(2));
            Assert.That(
                world.JigsawStructures.Select(item => item.StableId),
                Is.EquivalentTo(new[] { "abandoned_mineshaft", "fortress" }));
        }

        [Test]
        public void Definitions_ExposeEditableMineshaftAndFortressModules()
        {
            JigsawStructureFeatureSettings mineshaft = LoadSettings(
                ProjectAssetPaths.Config.AbandonedMineshaftJigsaw);
            JigsawStructureFeatureSettings fortress = LoadSettings(
                ProjectAssetPaths.Config.FortressJigsaw);

            Assert.That(
                mineshaft.Pieces.Select(piece => piece.StableId),
                Is.EquivalentTo(new[]
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
                Does.Contain("fortress_hall"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("fortress_lobby"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("fortress_library"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("fortress_portal_room"));
            Assert.That(
                fortress.Pieces.Select(piece => piece.StableId),
                Does.Contain("fortress_prison"));
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
            Assert.That(
                JigsawStructureGenerator.TryGetPlacement(
                    settings,
                    114514,
                    0,
                    0,
                    out JigsawStructureGenerator.Placement placement),
                Is.True);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(
                    settings,
                    114514,
                    placement);
            JigsawStructureGenerator.Piece room = layout[0];
            JigsawStructureGenerator.Piece corridor = layout.First(piece =>
                piece.ModuleId == "mineshaft_corridor");
            JigsawPieceSettings corridorModule = settings.Pieces.First(piece =>
                piece.StableId == corridor.ModuleId);

            Assert.That(room.ModuleId, Is.EqualTo("mineshaft_room"));
            Assert.That(
                room.Bounds.MaxX - room.Bounds.MinX + 1,
                Is.InRange(13, 19));
            Assert.That(corridor.Length, Is.InRange(10, 24));
            int corridorWidth = (corridor.Direction & 1) == 0
                ? corridor.Bounds.MaxX - corridor.Bounds.MinX + 1
                : corridor.Bounds.MaxZ - corridor.Bounds.MinZ + 1;
            Assert.That(corridorWidth, Is.EqualTo(corridorModule.Width));
        }

        [Test]
        public void FortressLayout_CanComposeLobbyHallAndLibrary()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.FortressJigsaw);
            IReadOnlyList<JigsawStructureGenerator.Piece> matching = null;
            for (int regionZ = -6; regionZ <= 6 && matching == null; regionZ++)
            {
                for (int regionX = -6; regionX <= 6 && matching == null; regionX++)
                {
                    if (!JigsawStructureGenerator.TryGetPlacement(
                        settings,
                        114514,
                        regionX,
                        regionZ,
                        out JigsawStructureGenerator.Placement placement))
                    {
                        continue;
                    }
                    IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                        JigsawStructureGenerator.BuildLayout(
                            settings,
                            114514,
                            placement);
                    HashSet<string> ids = layout
                        .Select(piece => piece.ModuleId)
                        .ToHashSet();
                    if (ids.Contains("fortress_lobby")
                        && ids.Contains("fortress_hall")
                        && ids.Contains("fortress_library"))
                    {
                        matching = layout;
                    }
                }
            }

            Assert.That(matching, Is.Not.Null);
            Assert.That(matching[0].ModuleId, Is.EqualTo("fortress_lobby"));
        }

        [Test]
        public void BuildLayout_SameSeedProducesIdenticalModuleGraph()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.FortressJigsaw);
            JigsawStructureGenerator.Placement placement = default;
            bool found = false;
            for (int regionZ = -3; regionZ <= 3 && !found; regionZ++)
            {
                for (int regionX = -3; regionX <= 3 && !found; regionX++)
                {
                    found = JigsawStructureGenerator.TryGetPlacement(
                        settings,
                        18731,
                        regionX,
                        regionZ,
                        out placement);
                }
            }
            Assert.That(found, Is.True);
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
                ProjectAssetPaths.Config.FortressJigsaw,
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
                ProjectAssetPaths.Config.FortressJigsaw);
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
                ProjectAssetPaths.Config.FortressJigsaw);
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
                ProjectAssetPaths.Config.FortressJigsaw);
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
                ProjectAssetPaths.Config.FortressJigsaw,
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
        public void FortressSideSocket_CarvesConfiguredMasonryOpening()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.FortressJigsaw);
            const int seed = 666;
            JigsawStructureGenerator.TryGetPlacement(
                settings,
                seed,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(settings, seed, placement);
            JigsawStructureGenerator.Piece hall = layout.First(piece =>
                piece.ModuleId == "fortress_hall"
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
            JigsawStructureGenerator.TryGetPlacement(
                settings,
                seed,
                0,
                0,
                out JigsawStructureGenerator.Placement placement);
            IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                JigsawStructureGenerator.BuildLayout(settings, seed, placement);
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
            Assert.That(
                actual,
                Is.EqualTo(new[]
                {
                    VoxelTypeId.Air,
                    VoxelTypeId.Air,
                    VoxelTypeId.Air,
                    StructureBrick,
                    StructureBrick,
                }),
                $"Corridor {corridor.Origin} direction {corridor.Direction}; "
                + $"samples: {string.Join(", ", samples)}");
        }

        [Test]
        public void FortressGeneration_BuildsLobbyShellAndLibraryShelves()
        {
            JigsawStructureFeatureSettings settings = LoadSettings(
                ProjectAssetPaths.Config.FortressJigsaw);
            const int seed = 114514;
            JigsawStructureGenerator.Placement placement = default;
            JigsawStructureGenerator.Piece library = default;
            bool found = false;
            for (int regionZ = -6; regionZ <= 6 && !found; regionZ++)
            {
                for (int regionX = -6; regionX <= 6 && !found; regionX++)
                {
                    if (!JigsawStructureGenerator.TryGetPlacement(
                        settings,
                        seed,
                        regionX,
                        regionZ,
                        out placement))
                    {
                        continue;
                    }
                    IReadOnlyList<JigsawStructureGenerator.Piece> layout =
                        JigsawStructureGenerator.BuildLayout(
                            settings,
                            seed,
                            placement);
                    JigsawStructureGenerator.Piece? candidate = layout
                        .Where(piece => piece.ModuleId == "fortress_library")
                        .Cast<JigsawStructureGenerator.Piece?>()
                        .FirstOrDefault();
                    if (candidate.HasValue)
                    {
                        library = candidate.Value;
                        found = true;
                    }
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
                Is.EqualTo(StructureBrick));
            Assert.That(GenerateAndGetType(settings, seed, lobbyInterior),
                Is.EqualTo(VoxelTypeId.Air));
            Assert.That(GenerateAndGetType(settings, seed, libraryShelf),
                Is.EqualTo(StructureBrick));
            Assert.That(GenerateAndGetType(settings, seed, libraryInterior),
                Is.EqualTo(VoxelTypeId.Air));
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
    }
}
