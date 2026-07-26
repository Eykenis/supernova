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
            "Assets/Game/Config/MinecraftVoxelTypes.asset";
        private const string DefinitionFolder =
            "Assets/Game/Config/VoxelTypes/";

        [Test]
        public void MinecraftCatalog_ReferencesOneIndependentAssetPerVoxelType()
        {
            VoxelTypeCatalog catalog =
                AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(CatalogPath);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Definitions, Has.Count.EqualTo(3));
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
                { 2, ("Stone", 4) },
                { 3, ("Ore", 8) },
            };
            foreach (VoxelTypeDefinition definition in catalog.Definitions)
            {
                Assert.That(expected, Contains.Key(definition.TypeId.Value));
                (string name, int durability) = expected[definition.TypeId.Value];
                Assert.That(definition.DisplayName, Is.EqualTo(name));
                Assert.That(definition.Durability, Is.EqualTo(durability));
                Assert.That(catalog.Find(definition.TypeId), Is.SameAs(definition));
            }
        }
    }
}
