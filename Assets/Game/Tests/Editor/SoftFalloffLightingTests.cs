using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Supernova.MinecraftCaves.Tests
{
    public sealed class SoftFalloffLightingTests
    {
        private const string ShaderName =
            "Supernova/Lighting/Soft Falloff Lit";

        [Test]
        public void SoftFalloffLitShader_IsSupportedAndHasNoCompileErrors()
        {
            Shader shader = Shader.Find(ShaderName);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            Assert.That(
                ShaderUtil.GetShaderMessages(shader),
                Is.Empty);
        }

        [TestCase("Assets/Game/Materials/Voxels/Ore.mat")]
        [TestCase("Assets/Game/Materials/Voxels/Bedrock.mat")]
        public void CaveVoxelMaterial_UsesSoftFalloffLitShader(
            string materialPath)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo(ShaderName));
        }

        [Test]
        public void SoftFalloffCurve_BrightensFarFieldAndCapsNearField()
        {
            const float falloffPower = 0.55f;
            const float attenuationLimit = 1.5f;
            const float farAttenuation = 0.04f;
            const float nearAttenuation = 4f;

            float softenedFar =
                Mathf.Pow(farAttenuation, falloffPower);
            float softenedNear = Mathf.Min(
                Mathf.Pow(nearAttenuation, falloffPower),
                attenuationLimit);

            Assert.That(softenedFar, Is.GreaterThan(farAttenuation));
            Assert.That(softenedNear, Is.LessThan(nearAttenuation));
            Assert.That(softenedNear, Is.LessThanOrEqualTo(attenuationLimit));
        }
    }
}
