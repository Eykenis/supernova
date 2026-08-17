using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests.Editor
{
    public sealed class CrystalOreLightingTests
    {
        private const string ShaderName =
            "Supernova/Lighting/Crystal Ore Lit";
        private const string DiamondShaderName =
            "Supernova/Voxels/DiamondCrystal";

        [Test]
        public void CrystalOreLitShader_IsSupportedAndHasNoCompileErrors()
        {
            Shader shader = Shader.Find(ShaderName);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);
        }

        [Test]
        public void DiamondShaderGraph_IsSupportedAndHasNoCompileErrors()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                ProjectAssetPaths.Shaders.DiamondCrystal);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo(DiamondShaderName));
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);
        }

        [TestCase(ProjectAssetPaths.Materials.YellowIron)]
        [TestCase(ProjectAssetPaths.Materials.DiamondOre)]
        [TestCase(ProjectAssetPaths.Materials.Amethyst)]
        [TestCase(ProjectAssetPaths.Materials.Copper)]
        [TestCase(ProjectAssetPaths.Materials.Obsidian)]
        [TestCase(ProjectAssetPaths.Materials.RecoveredOre)]
        public void OreMaterial_UsesOpaqueCrystalShader(string materialPath)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            Assert.That(material, Is.Not.Null, materialPath);
            Assert.That(material.shader, Is.Not.Null, materialPath);
            Assert.That(material.shader.name, Is.EqualTo(ShaderName));
            Assert.That(material.GetFloat("_Surface"), Is.EqualTo(0f));
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(1f));
            Assert.That(material.GetFloat("_ClearCoatMask"), Is.GreaterThan(0f));
        }

        [Test]
        public void DiamondMaterial_UsesTransparentDiamondShader()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.Diamond);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(DiamondShaderName));
            Assert.That(material.GetTag("RenderType", false),
                Is.EqualTo("Transparent"));
            Assert.That(material.renderQueue, Is.GreaterThanOrEqualTo(3000));
        }

        [Test]
        public void MineralVoxelTypes_ReferenceIndependentCrystalMaterials()
        {
            string[] voxelGuids = AssetDatabase.FindAssets(
                "t:VoxelTypeDefinition",
                new[] { ProjectAssetPaths.Folders.MineralVoxelTypes });
            var materialPaths = new HashSet<string>();

            foreach (string guid in voxelGuids)
            {
                VoxelTypeDefinition definition =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Material, Is.Not.Null, definition.name);
                string materialPath =
                    AssetDatabase.GetAssetPath(definition.Material);
                Assert.That(
                    definition.Material.shader.name,
                    Is.EqualTo(ShaderName),
                    definition.name);
                materialPaths.Add(materialPath);
            }

            string[] expectedPaths =
            {
                ProjectAssetPaths.Materials.YellowIron,
                ProjectAssetPaths.Materials.DiamondOre,
                ProjectAssetPaths.Materials.Amethyst,
                ProjectAssetPaths.Materials.Copper,
                ProjectAssetPaths.Materials.Obsidian,
            };
            Assert.That(voxelGuids, Has.Length.EqualTo(expectedPaths.Length));
            CollectionAssert.AreEquivalent(expectedPaths, materialPaths.ToArray());
        }
    }
}
