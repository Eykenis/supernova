using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Supernova.Voxels;
using UnityEditor;

namespace Supernova.Tests
{
    public sealed class VoxelTypeConfigurationAssetTests
    {
        private const string CatalogPath =
            ProjectAssetPaths.Config.VoxelCatalog;
        private const string DefinitionFolder =
            ProjectAssetPaths.Folders.VoxelTypes + "/";

        [Test]
        public void MinecraftCatalog_ReferencesOneIndependentAssetPerVoxelType()
        {
            VoxelTypeCatalog catalog =
                AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(CatalogPath);

            Assert.That(catalog, Is.Not.Null);
            var expected = new Dictionary<
                ushort,
                (string Name, int Durability, VoxelGroup Group)>
            {
                { 1, ("Default", 1, VoxelGroup.Structure) },
                { 2, ("Stone", 1, VoxelGroup.Stone) },
                { 3, ("Ore", 5, VoxelGroup.Ore) },
                { 4, ("Bedrock", 9999, VoxelGroup.Stone) },
                { 5, ("Structure Brick", 3, VoxelGroup.Structure) },
                { 6, ("Fortress Brick", 3, VoxelGroup.Structure) },
                { 7, ("Packed Dirt", 2, VoxelGroup.Stone) },
                { 8, ("Rusted Iron", 6, VoxelGroup.Structure) },
                { 9, ("Tiger Rock", 4, VoxelGroup.Structure) },
                { 10, ("Worn Brick", 3, VoxelGroup.Structure) },
            };
            Assert.That(catalog.Definitions, Has.Count.EqualTo(expected.Count));
            Assert.That(catalog.Definitions, Has.All.Not.Null);

            string[] paths = catalog.Definitions
                .Select(AssetDatabase.GetAssetPath)
                .ToArray();
            Assert.That(
                paths.Distinct().ToArray(),
                Has.Length.EqualTo(paths.Length));
            Assert.That(paths, Has.All.StartsWith(DefinitionFolder));

            foreach (VoxelTypeDefinition definition in catalog.Definitions)
            {
                Assert.That(expected, Contains.Key(definition.TypeId.Value));
                (string name, int durability, VoxelGroup group) =
                    expected[definition.TypeId.Value];
                Assert.That(definition.DisplayName, Is.EqualTo(name));
                Assert.That(definition.Durability, Is.EqualTo(durability));
                // Group drives mesh continuity, so a wrong assignment shows up as
                // seams between voxels that should read as one solid.
                Assert.That(definition.Group, Is.EqualTo(group));
                Assert.That(catalog.Find(definition.TypeId), Is.SameAs(definition));
            }

            // Every catalogued type must resolve to a real material so the mesher
            // never has to fall back for a voxel the world actually writes. Default
            // is exempt: it is the untyped fill and has no palette of its own.
            foreach (VoxelTypeDefinition definition in catalog.Definitions)
            {
                if (definition.TypeId.Value == 1)
                {
                    continue;
                }
                Assert.That(
                    definition.Material,
                    Is.Not.Null,
                    $"{definition.name} has no material.");
                Assert.That(
                    AssetDatabase.GetAssetPath(definition.Material),
                    Does.StartWith("Assets/Game/Materials/Voxels/"),
                    definition.name);
            }
        }
    }
}
