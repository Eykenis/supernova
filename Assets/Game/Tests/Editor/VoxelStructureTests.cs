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
