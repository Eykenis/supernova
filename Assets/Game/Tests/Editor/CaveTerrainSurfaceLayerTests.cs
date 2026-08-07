using System.Collections.Generic;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class CaveTerrainSurfaceLayerTests
    {
        [Test]
        public void Build_CreatesLiftedTransparentLayerAboveNaturalFloor()
        {
            VoxelTypeDefinition stone = CreateDefinition(VoxelGroup.Stone);
            CaveBiomeDefinition grassy = CreateBiome(
                new Color(0.16f, 0.5f, 0.1f, 0.92f));
            CaveBiomeCatalog catalog = CreateCatalog(grassy);
            InfiniteVoxelWorld world = CreateHorizontalSurface(stone.TypeId);
            VoxelMeshData source = MarchingCubesMesher.BuildColumnSection(
                world,
                Vector3Int.zero,
                0,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                0f,
                1f,
                MarchingCubesVertexPlacement.EdgeMidpoint,
                stone.TypeId,
                stone.TypeId);
            Mesh layer = null;

            try
            {
                layer = CaveTerrainSurfaceLayerBuilder.Build(
                    source,
                    Vector3Int.zero,
                    0,
                    1f,
                    42,
                    catalog,
                    new[] { stone });

                Assert.That(layer, Is.Not.Null);
                Assert.That(layer.vertexCount, Is.GreaterThan(0));
                Assert.That(layer.triangles.Length, Is.GreaterThan(0));
                Assert.That(layer.colors32, Has.Length.EqualTo(layer.vertexCount));
                Assert.That(layer.bounds.max.y, Is.GreaterThan(12.5f));

                byte maximumAlpha = 0;
                foreach (Color32 color in layer.colors32)
                {
                    maximumAlpha = (byte)Mathf.Max(maximumAlpha, color.a);
                }
                Assert.That(maximumAlpha, Is.GreaterThan(200));
            }
            finally
            {
                if (layer != null) Object.DestroyImmediate(layer);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(grassy);
                Object.DestroyImmediate(stone);
            }
        }

        [Test]
        public void Build_DoesNotCoverConstructedVoxelGroups()
        {
            VoxelTypeDefinition structure = CreateDefinition(
                VoxelGroup.Structure);
            CaveBiomeDefinition grassy = CreateBiome(Color.green);
            CaveBiomeCatalog catalog = CreateCatalog(grassy);
            InfiniteVoxelWorld world = CreateHorizontalSurface(
                structure.TypeId);
            VoxelMeshData source = MarchingCubesMesher.BuildColumnSection(
                world,
                Vector3Int.zero,
                0,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                0f,
                1f,
                MarchingCubesVertexPlacement.EdgeMidpoint,
                structure.TypeId,
                structure.TypeId);

            try
            {
                Mesh layer = CaveTerrainSurfaceLayerBuilder.Build(
                    source,
                    Vector3Int.zero,
                    0,
                    1f,
                    42,
                    catalog,
                    new[] { structure });

                Assert.That(layer, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(grassy);
                Object.DestroyImmediate(structure);
            }
        }

        [Test]
        public void Build_CarvedVoxelSuppressesNewlyExposedTurf()
        {
            VoxelTypeDefinition stone = CreateDefinition(VoxelGroup.Stone);
            CaveBiomeDefinition grassy = CreateBiome(
                new Color(0.16f, 0.5f, 0.1f, 0.92f));
            CaveBiomeCatalog catalog = CreateCatalog(grassy);
            InfiniteVoxelWorld world = CreateHorizontalSurface(stone.TypeId);
            var carvedVoxel = new Vector3Int(8, 12, 8);
            world.SetVoxel(
                carvedVoxel.x,
                carvedVoxel.y,
                carvedVoxel.z,
                -1f,
                VoxelTypeId.Air);
            VoxelMeshData source = MarchingCubesMesher.BuildColumnSection(
                world,
                Vector3Int.zero,
                0,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                0f,
                1f,
                MarchingCubesVertexPlacement.EdgeMidpoint,
                stone.TypeId,
                stone.TypeId);
            var carvedVoxels = new HashSet<Vector3Int> { carvedVoxel };
            Mesh unfiltered = null;
            Mesh filtered = null;

            try
            {
                unfiltered = CaveTerrainSurfaceLayerBuilder.Build(
                    source,
                    Vector3Int.zero,
                    0,
                    1f,
                    42,
                    catalog,
                    new[] { stone });
                filtered = CaveTerrainSurfaceLayerBuilder.Build(
                    source,
                    Vector3Int.zero,
                    0,
                    1f,
                    42,
                    catalog,
                    new[] { stone },
                    carvedVoxels);

                Assert.That(unfiltered, Is.Not.Null);
                Assert.That(filtered, Is.Not.Null);
                Assert.That(
                    ContainsTriangleNearCarvedVoxel(
                        unfiltered,
                        carvedVoxels),
                    Is.True,
                    "The fixture must contain the newly exposed cavity surface.");
                Assert.That(
                    ContainsTriangleNearCarvedVoxel(
                        filtered,
                        carvedVoxels),
                    Is.False);
            }
            finally
            {
                if (filtered != null) Object.DestroyImmediate(filtered);
                if (unfiltered != null) Object.DestroyImmediate(unfiltered);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(grassy);
                Object.DestroyImmediate(stone);
            }
        }

        [Test]
        public void SelectionCoverage_FadesFromBoundaryToOpaqueInterior()
        {
            CaveBiomeDefinition grassy = CreateBiome(Color.green);
            var selection = new CaveBiomeSelection(grassy, 0f, 1f);

            try
            {
                Assert.That(
                    selection.EvaluateInteriorCoverage(0f, 0.06f),
                    Is.Zero);
                Assert.That(
                    selection.EvaluateInteriorCoverage(0.03f, 0.06f),
                    Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(
                    selection.EvaluateInteriorCoverage(0.08f, 0.06f),
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    selection.EvaluateInteriorCoverage(1f, 0.06f),
                    Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(grassy);
            }
        }

        private static CaveBiomeDefinition CreateBiome(Color color)
        {
            var biome = ScriptableObject.CreateInstance<CaveBiomeDefinition>();
            biome.Configure("test", "Test", null);
            biome.ConfigureTerrainSurface(color, 0.06f, 0.02f);
            return biome;
        }

        private static CaveBiomeCatalog CreateCatalog(
            CaveBiomeDefinition fallback)
        {
            var catalog = ScriptableObject.CreateInstance<CaveBiomeCatalog>();
            catalog.Configure(0.008f, 15485863, fallback, null);
            return catalog;
        }

        private static VoxelTypeDefinition CreateDefinition(VoxelGroup group)
        {
            var definition =
                ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(2, "Test Surface", 1);
            definition.ConfigureGroup(group);
            return definition;
        }

        private static InfiniteVoxelWorld CreateHorizontalSurface(
            VoxelTypeId surfaceType)
        {
            var world = new InfiniteVoxelWorld();
            VoxelColumnChunkData data = world.EnsureChunk(Vector3Int.zero).Data;
            data.Fill(-1f, VoxelTypeId.Air);
            const int surfaceY = 12;
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int y = 0; y <= surfaceY; y++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        data.SetSample(x, y, z, 1f, surfaceType);
                    }
                }
            }
            return world;
        }

        private static bool ContainsTriangleNearCarvedVoxel(
            Mesh mesh,
            ISet<Vector3Int> carvedVoxels)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int triangle = 0;
                triangle + 2 < triangles.Length;
                triangle += 3)
            {
                Vector3 centroid = (
                    vertices[triangles[triangle]]
                    + vertices[triangles[triangle + 1]]
                    + vertices[triangles[triangle + 2]]) / 3f;
                if (CaveSurfaceDisturbance.IsNearCarvedVoxel(
                    centroid,
                    carvedVoxels))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
