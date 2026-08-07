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
            Assert.That(catalog.Definitions, Has.All.Not.Null);

            string[] paths = catalog.Definitions
                .Select(AssetDatabase.GetAssetPath)
                .ToArray();
            Assert.That(
                paths.Distinct().ToArray(),
                Has.Length.EqualTo(paths.Length),
                "Every definition must be a different asset.");
            Assert.That(paths, Has.All.StartsWith(DefinitionFolder));

            // Every TypeId must be unique; duplicates cause silent material
            // swaps because Find() returns the first match in linear order.
            var seen = new HashSet<ushort>();
            foreach (VoxelTypeDefinition definition in catalog.Definitions)
            {
                Assert.That(
                    seen.Add(definition.TypeId.Value),
                    $"Duplicate voxel type id {definition.TypeId.Value} on "
                    + $"'{definition.name}'.");
            }

            // Every type must round-trip through the catalog lookup so the
            // mesher and mining logic always resolve the correct material.
            foreach (VoxelTypeDefinition definition in catalog.Definitions)
            {
                Assert.That(
                    catalog.Find(definition.TypeId),
                    Is.SameAs(definition),
                    $"'{definition.name}' is not resolvable by its TypeId.");
            }

            // Every catalogued type must resolve to a real material so the
            // mesher never has to fall back for a voxel the world actually
            // writes. Default is exempt: it is the untyped fill and has no
            // palette of its own.
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
