using System.Collections.Generic;
using NUnit.Framework;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class VoxelStructureTests
    {
        private readonly List<VoxelTypeDefinition> definitions =
            new List<VoxelTypeDefinition>();

        [TearDown]
        public void TearDown()
        {
            for (int i = definitions.Count - 1; i >= 0; i--)
            {
                if (definitions[i] != null) Object.DestroyImmediate(definitions[i]);
            }
            definitions.Clear();
        }

        [Test]
        public void StructureAsset_AppliesEntireFixedFieldRelativeToAnchor()
        {
            VoxelStructureAsset structure =
                ScriptableObject.CreateInstance<VoxelStructureAsset>();
            try
            {
                var size = new Vector3Int(2, 2, 2);
                float[] densities =
                {
                    1f, -1f,
                    -1f, -1f,
                    -1f, -1f,
                    -1f, 1f,
                };
                ushort[] types =
                {
                    3, 0,
                    0, 0,
                    0, 0,
                    0, 4,
                };
                structure.SetData(
                    size,
                    new Vector3Int(1, 0, 1),
                    Vector3.up,
                    densities,
                    types);

                var world = new InfiniteVoxelWorld();
                Vector3Int spawn = new Vector3Int(10, 20, 30);
                Vector3Int offset = new Vector3Int(2, 0, -3);
                Vector3Int origin = structure.GetWorldOrigin(spawn, offset);
                world.SetVoxel(origin.x + 1, origin.y, origin.z, 1f, new VoxelTypeId(9));

                Assert.That(structure.Apply(world, spawn, offset), Is.EqualTo(8));
                Assert.That(
                    world.GetSampleOrDefault(origin.x, origin.y, origin.z).Type,
                    Is.EqualTo(new VoxelTypeId(3)));
                Assert.That(
                    world.GetSampleOrDefault(origin.x + 1, origin.y, origin.z).IsSolid(),
                    Is.False,
                    "Air samples in a fixed structure must carve procedural terrain.");
                Assert.That(
                    world.GetSampleOrDefault(origin.x + 1, origin.y + 1, origin.z + 1).Type,
                    Is.EqualTo(new VoxelTypeId(4)));
            }
            finally
            {
                Object.DestroyImmediate(structure);
            }
        }

        [Test]
        public void SpawnPointRule_CollectsChunksAndPlacesAtSpawnOnly()
        {
            VoxelStructureAsset structure =
                ScriptableObject.CreateInstance<VoxelStructureAsset>();
            try
            {
                structure.SetData(
                    Vector3Int.one,
                    Vector3Int.zero,
                    new Vector3(0f, 1.5f, 0f),
                    new[] { 1f },
                    new ushort[] { 2 });
                var rule = new SpawnPointStructureRule();
                rule.Configure(structure, new Vector3Int(3, 0, 0));
                var chunks = new HashSet<Vector3Int>();
                var spawn = new Vector3Int(31, 4, 4);

                rule.CollectRequiredChunks(spawn, chunks);

                Assert.That(chunks, Does.Contain(new Vector3Int(1, 0, 0)));
                Assert.That(
                    rule.GetPlayerSpawnVoxel(spawn),
                    Is.EqualTo(new Vector3(34f, 5.5f, 4f)));
                var world = new InfiniteVoxelWorld();
                Assert.That(rule.Apply(world, spawn), Is.EqualTo(1));
                Assert.That(
                    world.GetSampleOrDefault(34, 4, 4).Type,
                    Is.EqualTo(new VoxelTypeId(2)));
            }
            finally
            {
                Object.DestroyImmediate(structure);
            }
        }

        [Test]
        public void TypeCatalog_StoresExternallyConfigurableDefinitions()
        {
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                catalog.SetDefinitions(new[]
                {
                    CreateDefinition(2, 5, "Stone"),
                    CreateDefinition(7, 11, "Crystal"),
                });

                Assert.That(catalog.Find(new VoxelTypeId(7)).Durability, Is.EqualTo(11));
                Assert.That(
                    catalog.Find(new VoxelTypeId(7)).DisplayName,
                    Is.EqualTo("Crystal"));
                Assert.That(catalog.Definitions, Has.Count.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void RuntimeAuthoring_CreatesRemovesAndSavesPersistentCells()
        {
            VoxelStructureAsset structure =
                ScriptableObject.CreateInstance<VoxelStructureAsset>();
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            var root = new GameObject("Runtime Structure Authoring");
            try
            {
                structure.SetData(
                    new Vector3Int(3, 3, 3),
                    Vector3Int.one,
                    Vector3.up,
                    new float[27],
                    new ushort[27]);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(2, 4, "Stone") });
                VoxelStructureAuthoring authoring =
                    root.AddComponent<VoxelStructureAuthoring>();
                authoring.Configure(
                    structure,
                    catalog,
                    structure.Size,
                    structure.Anchor,
                    structure.PlayerSpawnOffset);

                Assert.That(
                    authoring.TryCreatePaintCell(new Vector3Int(2, 1, 1), out var cell),
                    Is.True);
                Assert.That(authoring.TrySaveAssignedAsset(out string saveError), Is.True, saveError);
                Assert.That(structure.GetSample(2, 1, 1).IsSolid(), Is.True);

                Assert.That(authoring.TryRemoveCell(cell), Is.True);
                Assert.That(authoring.TrySaveAssignedAsset(out saveError), Is.True, saveError);
                Assert.That(structure.GetSample(2, 1, 1).IsSolid(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(structure);
            }
        }

        [Test]
        public void RuntimeAuthoring_OffsetsSelectedCellsAtomically()
        {
            var root = new GameObject("Offset Structure Authoring");
            try
            {
                VoxelStructureAuthoring authoring =
                    root.AddComponent<VoxelStructureAuthoring>();
                authoring.Configure(
                    null,
                    null,
                    new Vector3Int(4, 3, 3),
                    new Vector3Int(1, 1, 1),
                    Vector3.zero);
                Assert.That(
                    authoring.TryCreatePaintCell(
                        new Vector3Int(0, 1, 1),
                        out VoxelStructureCellAuthoring selected),
                    Is.True);
                Assert.That(
                    authoring.TryCreatePaintCell(
                        new Vector3Int(2, 1, 1),
                        out VoxelStructureCellAuthoring stationary),
                    Is.True);

                Assert.That(
                    authoring.TryOffsetCells(
                        new[] { selected },
                        Vector3Int.right,
                        out string offsetError),
                    Is.True,
                    offsetError);
                Assert.That(
                    selected.transform.localPosition,
                    Is.EqualTo(new Vector3(1f, 1f, 1f)));

                Assert.That(
                    authoring.TryOffsetCells(
                        new[] { selected },
                        Vector3Int.right,
                        out offsetError),
                    Is.False);
                Assert.That(offsetError, Does.Contain("occupied"));
                Assert.That(
                    selected.transform.localPosition,
                    Is.EqualTo(new Vector3(1f, 1f, 1f)),
                    "A rejected group move must not mutate any selected cell.");
                Assert.That(
                    stationary.transform.localPosition,
                    Is.EqualTo(new Vector3(2f, 1f, 1f)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeAuthoring_FillsRepaintsAndClearsInclusiveBox()
        {
            var root = new GameObject("Fill Structure Authoring");
            try
            {
                VoxelStructureAuthoring authoring =
                    root.AddComponent<VoxelStructureAuthoring>();
                authoring.Configure(
                    null,
                    null,
                    new Vector3Int(4, 3, 2),
                    Vector3Int.zero,
                    Vector3.zero);
                Assert.That(
                    authoring.TryCreatePaintCell(
                        new Vector3Int(2, 1, 1),
                        out VoxelStructureCellAuthoring existing),
                    Is.True);
                existing.Configure(0.25f, new VoxelTypeId(9));

                Assert.That(
                    authoring.TryFillPaintBox(
                        new Vector3Int(2, 1, 1),
                        new Vector3Int(1, 0, 0),
                        out int changed),
                    Is.True);
                Assert.That(changed, Is.EqualTo(8));
                for (int z = 0; z <= 1; z++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int x = 1; x <= 2; x++)
                        {
                            VoxelStructureCellAuthoring cell =
                                authoring.FindCell(new Vector3Int(x, y, z));
                            Assert.That(cell, Is.Not.Null);
                            Assert.That(cell.Type, Is.EqualTo(VoxelTypeId.Default));
                            Assert.That(cell.Density, Is.EqualTo(1f));
                        }
                    }
                }
                Assert.That(authoring.FindCell(Vector3Int.zero), Is.Null);

                Assert.That(
                    authoring.TryFillPaintBox(
                        new Vector3Int(1, 0, 0),
                        new Vector3Int(2, 1, 1),
                        out changed),
                    Is.True);
                Assert.That(changed, Is.Zero);

                Assert.That(
                    authoring.TryClearBox(
                        new Vector3Int(2, 1, 0),
                        new Vector3Int(1, 0, 0),
                        out int removed),
                    Is.True);
                Assert.That(removed, Is.EqualTo(4));
                for (int y = 0; y <= 1; y++)
                {
                    for (int x = 1; x <= 2; x++)
                    {
                        Assert.That(
                            authoring.FindCell(new Vector3Int(x, y, 0)),
                            Is.Null);
                        Assert.That(
                            authoring.FindCell(new Vector3Int(x, y, 1)),
                            Is.Not.Null);
                    }
                }

                Assert.That(
                    authoring.TryClearBox(
                        new Vector3Int(1, 0, 0),
                        new Vector3Int(2, 1, 0),
                        out removed),
                    Is.True);
                Assert.That(removed, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeAuthoring_OffsetsWholeStructureByMovingAnchorOnly()
        {
            var root = new GameObject("Anchor Offset Structure Authoring");
            try
            {
                VoxelStructureAuthoring authoring =
                    root.AddComponent<VoxelStructureAuthoring>();
                authoring.Configure(
                    null,
                    null,
                    new Vector3Int(5, 5, 5),
                    new Vector3Int(2, 2, 2),
                    Vector3.zero);

                Assert.That(
                    authoring.TryOffsetWholeStructure(
                        new Vector3Int(1, -1, 0),
                        out string offsetError),
                    Is.True,
                    offsetError);
                Assert.That(
                    authoring.Anchor,
                    Is.EqualTo(new Vector3Int(1, 3, 2)));

                Assert.That(
                    authoring.TryOffsetWholeStructure(
                        new Vector3Int(2, 0, 0),
                        out offsetError),
                    Is.False);
                Assert.That(offsetError, Does.Contain("outside"));
                Assert.That(
                    authoring.Anchor,
                    Is.EqualTo(new Vector3Int(1, 3, 2)),
                    "A rejected fast offset must preserve the previous Anchor.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void StoneTestWorld_UsesMinimumChunkAndSectionEnvelope()
        {
            Assert.That(
                VoxelStructureStoneTestWorld.CalculateMinimumChunkGrid(
                    new Vector3Int(19, 20, 40)),
                Is.EqualTo(new Vector3Int(1, 1, 2)));
            Assert.That(
                VoxelStructureStoneTestWorld.GetWorldVoxelSize(
                    new Vector3Int(1, 1, 2)),
                Is.EqualTo(new Vector3Int(32, 32, 64)));
        }

        [Test]
        public void StoneTestWorld_FillsPaddingAndAppliesAuthoredAir()
        {
            VoxelStructureAsset structure =
                ScriptableObject.CreateInstance<VoxelStructureAsset>();
            try
            {
                var size = new Vector3Int(19, 20, 40);
                int sampleCount = size.x * size.y * size.z;
                float[] densities = new float[sampleCount];
                ushort[] types = new ushort[sampleCount];
                System.Array.Fill(densities, -1f);
                structure.SetData(
                    size,
                    Vector3Int.zero,
                    Vector3.up,
                    densities,
                    types);

                InfiniteVoxelWorld world = VoxelStructureStoneTestWorld.BuildWorld(
                    structure,
                    new VoxelTypeId(2),
                    out Vector3Int chunkGrid);

                Assert.That(chunkGrid, Is.EqualTo(new Vector3Int(1, 1, 2)));
                Assert.That(world.ChunkCount, Is.EqualTo(2));
                Assert.That(world.GetSampleOrDefault(0, 0, 0).IsSolid(), Is.False);
                Assert.That(
                    world.GetSampleOrDefault(31, 0, 0).Type,
                    Is.EqualTo(new VoxelTypeId(2)));
                Assert.That(
                    world.GetSampleOrDefault(0, 31, 0).Type,
                    Is.EqualTo(new VoxelTypeId(2)));
                Assert.That(
                    world.GetSampleOrDefault(0, 0, 63).Type,
                    Is.EqualTo(new VoxelTypeId(2)));
                Assert.That(world.GetSampleOrDefault(0, 32, 0).IsSolid(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(structure);
            }
        }


        private VoxelTypeDefinition CreateDefinition(
            ushort type,
            int durability,
            string displayName)
        {
            VoxelTypeDefinition definition =
                ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(type, displayName, durability);
            definitions.Add(definition);
            return definition;
        }
    }
}
