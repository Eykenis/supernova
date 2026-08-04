using System.Collections.Generic;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Tests
{
    public sealed class CaveSurfaceBrushTests
    {
        /// <summary>
        /// Exact x of the first placement produced by
        /// <see cref="UpwardBrush_GeneratesDeterministicAnchoredPlacements"/>'s
        /// fixture. It pins the placement random stream so an inserted draw cannot
        /// silently shift the whole distribution.
        /// </summary>
        private const float GoldenFirstPlacementX = 0.80371356f;

        [Test]
        public void UpwardBrush_GeneratesDeterministicAnchoredPlacements()
        {
            GameObject prefab = new GameObject("Grass Test Prefab");
            VoxelTypeDefinition stoneDefinition = CreateStoneDefinition();
            CaveSurfaceBrushDefinition brush = CreateBrush(
                prefab,
                stoneDefinition,
                CaveSurfaceOrientation.Upward,
                1f);
            CaveBiomeDefinition grassy = CreateBiome("grassy", brush);
            CaveBiomeCatalog catalog = CreateCatalog(grassy);
            InfiniteVoxelWorld world = CreateHorizontalSurface(
                stoneDefinition.TypeId,
                solidBelow: true);
            VoxelMeshData mesh = MarchingCubesMesher.BuildColumnSection(
                world,
                Vector3Int.zero,
                0,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                0f,
                1f,
                MarchingCubesVertexPlacement.EdgeMidpoint,
                stoneDefinition.TypeId,
                stoneDefinition.TypeId);

            try
            {
                List<CaveSurfacePlacement> first =
                    CaveSurfaceBrushGenerator.Generate(
                        mesh,
                        world,
                        Vector3Int.zero,
                        0,
                        1f,
                        0f,
                        42,
                        catalog);
                List<CaveSurfacePlacement> second =
                    CaveSurfaceBrushGenerator.Generate(
                        mesh,
                        world,
                        Vector3Int.zero,
                        0,
                        1f,
                        0f,
                        42,
                        catalog);

                Assert.That(first, Is.Not.Empty);
                Assert.That(second.Count, Is.EqualTo(first.Count));
                for (int i = 0; i < first.Count; i++)
                {
                    Assert.That(first[i].LocalPosition, Is.EqualTo(second[i].LocalPosition));
                    Assert.That(first[i].AnchorVoxel, Is.EqualTo(second[i].AnchorVoxel));
                    Assert.That(first[i].OutwardNormal.y, Is.GreaterThanOrEqualTo(0.6f));
                    Assert.That(first[i].StanceNormal, Is.EqualTo(second[i].StanceNormal));
                    Assert.That(first[i].Scale, Is.EqualTo(second[i].Scale));
                    Assert.That(first[i].Yaw, Is.EqualTo(second[i].Yaw));
                    Assert.That(
                        world.TryGetSample(
                            first[i].AnchorVoxel.x,
                            first[i].AnchorVoxel.y,
                            first[i].AnchorVoxel.z,
                            out VoxelSample sample),
                        Is.True);
                    Assert.That(sample.IsSolid(), Is.True);
                    Assert.That(sample.Type, Is.EqualTo(stoneDefinition.TypeId));
                }

                // Golden value. Run-to-run equality above cannot detect a
                // distribution that shifted wholesale because a random draw was
                // inserted ahead of the existing ones; pinning one exact position
                // does. Update deliberately if the placement stream changes.
                Assert.That(
                    first[0].LocalPosition.x,
                    Is.EqualTo(GoldenFirstPlacementX).Within(1e-4f),
                    "The placement random stream moved. Only update this value "
                    + "when that reordering is intended.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(grassy);
                Object.DestroyImmediate(brush);
                Object.DestroyImmediate(stoneDefinition);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void DownwardBrush_OnlyGeneratesOnCeilings()
        {
            GameObject prefab = new GameObject("Vine Test Prefab");
            VoxelTypeDefinition stoneDefinition = CreateStoneDefinition();
            CaveSurfaceBrushDefinition brush = CreateBrush(
                prefab,
                stoneDefinition,
                CaveSurfaceOrientation.Downward,
                1f);
            CaveBiomeDefinition grassy = CreateBiome("grassy", brush);
            CaveBiomeCatalog catalog = CreateCatalog(grassy);
            InfiniteVoxelWorld world = CreateHorizontalSurface(
                stoneDefinition.TypeId,
                solidBelow: false);
            VoxelMeshData mesh = MarchingCubesMesher.BuildColumnSection(
                world,
                Vector3Int.zero,
                0,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                0f,
                1f,
                MarchingCubesVertexPlacement.EdgeMidpoint,
                stoneDefinition.TypeId,
                stoneDefinition.TypeId);

            try
            {
                List<CaveSurfacePlacement> placements =
                    CaveSurfaceBrushGenerator.Generate(
                        mesh,
                        world,
                        Vector3Int.zero,
                        0,
                        1f,
                        0f,
                        42,
                        catalog);

                Assert.That(placements, Is.Not.Empty);
                foreach (CaveSurfacePlacement placement in placements)
                {
                    Assert.That(
                        placement.OutwardNormal.y,
                        Is.LessThanOrEqualTo(-0.6f));
                }
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(grassy);
                Object.DestroyImmediate(brush);
                Object.DestroyImmediate(stoneDefinition);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaldBiome_EmitsNoSurfaceContent()
        {
            VoxelTypeDefinition stoneDefinition = CreateStoneDefinition();
            CaveBiomeDefinition bald = CreateBiome("bald");
            CaveBiomeCatalog catalog = CreateCatalog(bald);
            InfiniteVoxelWorld world = CreateHorizontalSurface(
                stoneDefinition.TypeId,
                solidBelow: true);
            VoxelMeshData mesh = MarchingCubesMesher.BuildColumnSection(
                world,
                Vector3Int.zero,
                0,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                0f,
                1f,
                MarchingCubesVertexPlacement.EdgeMidpoint,
                stoneDefinition.TypeId,
                stoneDefinition.TypeId);

            try
            {
                List<CaveSurfacePlacement> placements =
                    CaveSurfaceBrushGenerator.Generate(
                        mesh,
                        world,
                        Vector3Int.zero,
                        0,
                        1f,
                        0f,
                        42,
                        catalog);
                Assert.That(placements, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(bald);
                Object.DestroyImmediate(stoneDefinition);
            }
        }

        [Test]
        public void DefaultAssets_ConfigureGrassyAndBaldBiomes()
        {
            MinecraftWorldGenerationConfiguration world =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    ProjectAssetPaths.Config.WorldGeneration);
            CaveBiomeCatalog catalog = AssetDatabase.LoadAssetAtPath<
                CaveBiomeCatalog>(ProjectAssetPaths.Config.CaveBiomeCatalog);
            CaveBiomeDefinition grassy = AssetDatabase.LoadAssetAtPath<
                CaveBiomeDefinition>(ProjectAssetPaths.Config.GrassyCaveBiome);
            CaveBiomeDefinition bald = AssetDatabase.LoadAssetAtPath<
                CaveBiomeDefinition>(ProjectAssetPaths.Config.BaldCaveBiome);
            CaveSurfaceBrushDefinition grass = AssetDatabase.LoadAssetAtPath<
                CaveSurfaceBrushDefinition>(
                ProjectAssetPaths.Config.GrassSurfaceBrush);
            CaveSurfaceBrushDefinition vine = AssetDatabase.LoadAssetAtPath<
                CaveSurfaceBrushDefinition>(
                ProjectAssetPaths.Config.VineSurfaceBrush);

            Assert.That(world, Is.Not.Null);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(world.CaveBiomeCatalog, Is.SameAs(catalog));
            Assert.That(catalog.FallbackBiome, Is.SameAs(bald));
            Assert.That(bald.SurfaceBrushes, Is.Empty);
            Assert.That(grassy.SurfaceBrushes, Is.EquivalentTo(new[] { grass, vine }));
            Assert.That(grass.Orientation, Is.EqualTo(CaveSurfaceOrientation.Upward));
            Assert.That(vine.Orientation, Is.EqualTo(CaveSurfaceOrientation.Downward));
            Assert.That(
                grass.RenderMode,
                Is.EqualTo(CaveSurfaceBrushRenderMode.InstancedMesh));
            Assert.That(grass.Prefab, Is.Null);
            Assert.That(grass.InstanceMesh, Is.Not.Null);
            Assert.That(grass.InstanceMaterial, Is.Not.Null);
            Assert.That(grass.InstanceMaterial.enableInstancing, Is.True);
            Assert.That(
                grass.InstanceMaterial.shader.name,
                Is.EqualTo(CaveVegetationShaderNames.CaveGrassBlade),
                "The instanced grass brush must use the stylised blade shader, "
                + "not the flat placeholder material.");

            Assert.That(grass.LodTiers, Has.Count.EqualTo(3));
            float previousDistance = 0f;
            for (int i = 0; i < grass.LodTiers.Count; i++)
            {
                CaveSurfaceLodTier tier = grass.LodTiers[i];
                Assert.That(tier.Mesh, Is.Not.Null, "LOD tier " + i);
                if (i < grass.LodTiers.Count - 1)
                {
                    // The final tier uses zero to mean "no upper bound".
                    Assert.That(tier.MaximumDistance, Is.GreaterThan(previousDistance));
                    previousDistance = tier.MaximumDistance;
                }
            }
            Assert.That(grass.MaximumDrawDistance, Is.GreaterThan(0f));
            Assert.That(
                grass.FadeBandDistance,
                Is.GreaterThan(0f),
                "Without a fade band the far tier pops out abruptly.");

            // Assets serialised before clumping and wind existed load with these
            // at zero, which would divide by zero in the blade shader.
            Assert.That(grass.ClumpHorizontalCellSize, Is.GreaterThan(0f));
            Assert.That(grass.ClumpVerticalCellSize, Is.GreaterThan(0f));
            Assert.That(grass.WindStrength, Is.GreaterThan(0f));
            Assert.That(grass.WindBendExponent, Is.GreaterThanOrEqualTo(1f));
            Assert.That(grass.UprightBias, Is.GreaterThan(0f));

            // The gradient must run dark root to lighter tip, not the reverse.
            Assert.That(
                grassy.VegetationRootColor.grayscale,
                Is.LessThan(grassy.VegetationTipColor.grayscale));

            Assert.That(
                vine.RenderMode,
                Is.EqualTo(CaveSurfaceBrushRenderMode.Prefab));
            Assert.That(vine.Prefab, Is.Not.Null);
            Assert.That(
                vine.Prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "Surface prefabs must own collider policy; the placeholders intentionally have none.");
        }

        [Test]
        public void InstancedRenderer_BatchesPlacementsWithoutChildObjects()
        {
            Mesh mesh = CreateTriangleMesh();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            var material = new Material(shader);
            var brush = ScriptableObject.CreateInstance<
                CaveSurfaceBrushDefinition>();
            brush.ConfigureInstanced(
                mesh,
                material,
                new VoxelTypeDefinition[0],
                CaveSurfaceOrientation.Upward,
                17,
                1f,
                0.6f,
                0.4f,
                0f,
                Vector2.one,
                Vector2.one,
                ShadowCastingMode.Off,
                true,
                45f);
            var root = new GameObject("Instance Renderer Test");
            CaveSurfaceInstanceRenderer renderer =
                root.AddComponent<CaveSurfaceInstanceRenderer>();
            var placements = new List<CaveSurfacePlacement>();
            for (int i = 0;
                i < CaveSurfaceInstanceRenderer.MaximumInstancesPerDrawCall + 2;
                i++)
            {
                placements.Add(new CaveSurfacePlacement(
                    brush,
                    null,
                    new Vector3(i, 0f, 0f),
                    Vector3.up,
                    Vector3.one,
                    0f,
                    new Vector3Int(i, 2, 3)));
            }

            try
            {
                renderer.Configure(placements);

                Assert.That(renderer.GroupCount, Is.EqualTo(1));
                Assert.That(renderer.BrushCount, Is.EqualTo(1));
                Assert.That(renderer.InstanceCount, Is.EqualTo(1025));
                Assert.That(renderer.DrawCallCount, Is.EqualTo(2));
                Assert.That(renderer.transform.childCount, Is.Zero);

                // Configure sorts placements for spatial coherence, so a render
                // index is not the input index. Instance identity is the anchor
                // set, which must survive the reordering intact.
                var renderedAnchors = new List<Vector3Int>();
                for (int i = 0; i < renderer.GetGroupInstanceCount(0); i++)
                {
                    renderedAnchors.Add(renderer.GetAnchorVoxel(0, i));
                }
                var expectedAnchors = new List<Vector3Int>();
                for (int i = 0; i < placements.Count; i++)
                {
                    expectedAnchors.Add(placements[i].AnchorVoxel);
                }
                Assert.That(renderedAnchors, Is.EquivalentTo(expectedAnchors));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(brush);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        private static VoxelTypeDefinition CreateStoneDefinition()
        {
            VoxelTypeDefinition definition =
                ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(2, "Stone", 1);
            return definition;
        }

        private static Mesh CreateTriangleMesh()
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static CaveSurfaceBrushDefinition CreateBrush(
            GameObject prefab,
            VoxelTypeDefinition stone,
            CaveSurfaceOrientation orientation,
            float density)
        {
            CaveSurfaceBrushDefinition brush =
                ScriptableObject.CreateInstance<CaveSurfaceBrushDefinition>();
            brush.Configure(
                prefab,
                new[] { stone },
                orientation,
                17,
                density,
                0.6f,
                0.4f,
                0f,
                Vector2.one,
                Vector2.one);
            return brush;
        }

        private static CaveBiomeDefinition CreateBiome(
            string id,
            params CaveSurfaceBrushDefinition[] brushes)
        {
            CaveBiomeDefinition biome =
                ScriptableObject.CreateInstance<CaveBiomeDefinition>();
            biome.Configure(id, id, brushes);
            return biome;
        }

        private static CaveBiomeCatalog CreateCatalog(
            CaveBiomeDefinition fallback)
        {
            CaveBiomeCatalog catalog =
                ScriptableObject.CreateInstance<CaveBiomeCatalog>();
            catalog.Configure(
                0.01f,
                31,
                fallback,
                new CaveBiomeSelection[0]);
            return catalog;
        }

        private static InfiniteVoxelWorld CreateHorizontalSurface(
            VoxelTypeId stone,
            bool solidBelow)
        {
            var world = new InfiniteVoxelWorld();
            VoxelColumnChunkData data = world.EnsureChunk(Vector3Int.zero).Data;
            data.Fill(-1f, VoxelTypeId.Air);
            const int surfaceY = 12;
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int y = 0; y < VoxelColumnChunkData.Height; y++)
                {
                    bool solid = solidBelow ? y <= surfaceY : y >= surfaceY;
                    if (!solid) continue;
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        data.SetSample(x, y, z, 1f, stone);
                    }
                }
            }
            return world;
        }
    }
}
