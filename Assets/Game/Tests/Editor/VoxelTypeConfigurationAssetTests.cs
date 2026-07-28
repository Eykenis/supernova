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
            Assert.That(catalog.Definitions, Has.Count.EqualTo(4));
            Assert.That(catalog.Definitions, Has.All.Not.Null);

            string[] paths = catalog.Definitions
                .Select(AssetDatabase.GetAssetPath)
                .ToArray();
            Assert.That(
                paths.Distinct().ToArray(),
                Has.Length.EqualTo(paths.Length));
            Assert.That(paths, Has.All.StartsWith(DefinitionFolder));

            var expected = new Dictionary<ushort, (string Name, int Durability)>
            {
                { 1, ("Default", 1) },
                { 2, ("Stone", 1) },
                { 3, ("Ore", 8) },
                { 4, ("Bedrock", 9999) },
            };
            foreach (VoxelTypeDefinition definition in catalog.Definitions)
            {
                Assert.That(expected, Contains.Key(definition.TypeId.Value));
                (string name, int durability) = expected[definition.TypeId.Value];
                Assert.That(definition.DisplayName, Is.EqualTo(name));
                Assert.That(definition.Durability, Is.EqualTo(durability));
                Assert.That(catalog.Find(definition.TypeId), Is.SameAs(definition));
            }

            VoxelTypeDefinition bedrock = catalog.Definitions.Single(
                definition => definition.TypeId.Value == 4);
            Assert.That(bedrock.Material, Is.Not.Null);
            Assert.That(
                bedrock.Material.GetColor("_BaseColor"),
                Is.EqualTo(UnityEngine.Color.black));
        }
    }
}
