using NUnit.Framework;
using Supernova.Effects;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MagnetEffectShaderTests
    {
        [Test]
        public void EnergyRibbonShader_IsSupportedAndHasNoCompileErrors()
        {
            Shader shader = Shader.Find(MagnetEffectShaderNames.EnergyRibbon);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);
        }

        [Test]
        public void EnergyRibbonMaterial_UsesRegisteredShader()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.MagnetEnergyRibbon);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo(MagnetEffectShaderNames.EnergyRibbon));
        }

        [Test]
        public void PlayerPrefab_AssignsMagnetEnergyMaterial()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);
            Assert.That(player, Is.Not.Null);
            MagnetAttractionBeam beam =
                player.GetComponent<MagnetAttractionBeam>();
            Assert.That(beam, Is.Not.Null);

            var serializedBeam = new SerializedObject(beam);
            Material assignedMaterial = serializedBeam
                .FindProperty("beamMaterial")
                .objectReferenceValue as Material;

            Assert.That(assignedMaterial, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(assignedMaterial),
                Is.EqualTo(ProjectAssetPaths.Materials.MagnetEnergyRibbon));
        }
    }
}
